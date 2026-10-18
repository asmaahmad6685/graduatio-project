using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PhysioGuide.Appointments.Tests;

public sealed class RescheduleAndCompletionTests : IAsyncLifetime
{
    private readonly AppointmentApiFactory _factory = new();
    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task RescheduleChangesTherapistAndTimeWhilePreservingIdentityServicePriceNotesAndHistory()
    {
        using var client = _factory.Client();
        using var created = await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(notes: "Keep notes"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await AppointmentApiFactory.JsonAsync(created)).GetProperty("appointmentId").GetInt32();
        var before = await _factory.WithDbAsync(db => db.Appointments.AsNoTracking().SingleAsync());
        await _factory.MutateAsync(db => db.Services.Single(s => s.ServiceId == 601).Price = 800m);
        using var response = await client.PatchAsJsonAsync($"/api/Appointments/{id}/reschedule", new { therapistId = 303, startDateTime = "2026-10-19T11:00:00" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var json = await AppointmentApiFactory.JsonAsync(response);
        Assert.Equal(id, json.GetProperty("appointmentId").GetInt32());
        Assert.Equal(303, json.GetProperty("therapist").GetProperty("therapistId").GetInt32());
        Assert.Equal("Pending", json.GetProperty("status").GetString());
        Assert.Equal(new DateTime(2026, 10, 19, 11, 50, 0), json.GetProperty("endDateTime").GetDateTime());
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Appointments.SingleAsync();
            Assert.Equal(before.AppointmentId, row.AppointmentId);
            Assert.Equal(before.PatientId, row.PatientId);
            Assert.Equal(before.CenterId, row.CenterId);
            Assert.Equal(before.ServiceId, row.ServiceId);
            Assert.Equal(before.PriceAtBooking, row.PriceAtBooking);
            Assert.Equal(before.PatientNotes, row.PatientNotes);
            Assert.Equal(before.CreatedAt, row.CreatedAt);
            Assert.Equal("Pending", row.Status);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, row.UpdatedAt);
            Assert.Single(await db.AppointmentStatusHistories.ToArrayAsync());
            Assert.Empty(await db.Notifications.ToArrayAsync());
            return true;
        });
        // The original occupied interval has been released.
        using var anotherPatient = _factory.Client("Patient", "122");
        await AppointmentApiFactory.AssertStatusAsync(await anotherPatient.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), HttpStatusCode.Created);
    }

    [Fact]
    public async Task RescheduleExcludesCurrentAppointmentFromTherapistAndPatientConflicts()
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/reschedule", new { therapistId = 301, startDateTime = "2026-10-18T10:00:00" }), HttpStatusCode.OK);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.Appointments.CountAsync()));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
    }

    [Theory]
    [InlineData("wrongCenter")]
    [InlineData("therapistInactive")]
    [InlineData("therapistUnavailable")]
    [InlineData("serviceInactive")]
    [InlineData("serviceWrongCenter")]
    [InlineData("assignmentMissing")]
    [InlineData("centerInactive")]
    [InlineData("scheduleDisabled")]
    [InlineData("centerClosed")]
    [InlineData("nonSlot")]
    [InlineData("therapistConflict")]
    [InlineData("patientConflict")]
    public async Task RescheduleRevalidatesAllCurrentEligibilityAndConflictsAtomically(string condition)
    {
        var id = await _factory.AddAppointmentAsync();
        var start = "2026-10-18T11:00:00";
        var therapist = condition == "wrongCenter" ? 302 : 303;
        if (condition == "therapistConflict") await _factory.AddAppointmentAsync(start: new DateTime(2026, 10, 18, 11, 0, 0), patient: 402, therapist: 303, service: 602);
        if (condition == "patientConflict") await _factory.AddAppointmentAsync(start: new DateTime(2026, 10, 18, 11, 0, 0), therapist: 302, service: 604, center: 202);
        if (condition == "nonSlot") start = "2026-10-18T11:15:00";
        await _factory.MutateAsync(db =>
        {
            if (condition == "therapistInactive") db.Therapists.Single(t => t.TherapistId == 303).IsActive = false;
            if (condition == "therapistUnavailable") db.Therapists.Single(t => t.TherapistId == 303).IsAvailable = false;
            if (condition == "serviceInactive") db.Services.Single(s => s.ServiceId == 601).IsActive = false;
            if (condition == "serviceWrongCenter") db.Services.Single(s => s.ServiceId == 601).CenterId = 202;
            if (condition == "assignmentMissing") db.TherapistServices.Remove(db.TherapistServices.Single(t => t.TherapistId == 303 && t.ServiceId == 601));
            if (condition == "centerInactive") db.Centers.Single(c => c.CenterId == 201).Status = "Suspended";
            if (condition == "scheduleDisabled") db.TherapistSchedules.Single(s => s.TherapistId == 303 && s.DayOfWeek == DayOfWeek.Sunday).IsAvailable = false;
            if (condition == "centerClosed") db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday).IsClosed = true;
        });
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/reschedule", new { therapistId = therapist, startDateTime = start }), HttpStatusCode.Conflict);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Appointments.FindAsync(id);
            Assert.Equal(301, row!.TherapistId);
            Assert.Equal(new DateTime(2026, 10, 18, 10, 0, 0), row.StartDateTime);
            Assert.Equal(77m, row.PriceAtBooking);
            Assert.Null(row.UpdatedAt);
            Assert.Empty(await db.AppointmentStatusHistories.ToArrayAsync());
            return true;
        });
    }

    [Theory]
    [InlineData("2026-10-16T11:00:00", HttpStatusCode.BadRequest)]
    [InlineData("2026-10-17T09:00:00", HttpStatusCode.Conflict)]
    [InlineData("2026-11-18T11:00:00", HttpStatusCode.BadRequest)]
    [InlineData("2026-10-18T11:00:00Z", HttpStatusCode.BadRequest)]
    [InlineData("2026-10-18T11:00:00+03:00", HttpStatusCode.BadRequest)]
    public async Task RescheduleEnforcesLocalTimeAndBookingHorizon(string start, HttpStatusCode status)
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/reschedule", new { therapistId = 303, startDateTime = start }), status);
    }

    [Theory]
    [InlineData("centerId", 202)]
    [InlineData("serviceId", 602)]
    [InlineData("patientId", 402)]
    [InlineData("priceAtBooking", 1)]
    public async Task RescheduleCannotAlterRetainedServerOwnedFields(string field, int value)
    {
        var id = await _factory.AddAppointmentAsync();
        var request = new Dictionary<string, object> { ["therapistId"] = 303, ["startDateTime"] = "2026-10-18T11:00:00", [field] = value };
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/reschedule", request), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Patient", false)]
    [InlineData("Patient", true)]
    [InlineData("Center", false)]
    [InlineData("Center", true)]
    public async Task ReadsCompleteExpiredConfirmedAppointmentsWithAutomaticHistoryOnlyInCurrentScope(string role, bool detail)
    {
        var expired = new DateTime(2026, 10, 17, 8, 0, 0);
        var own = await _factory.AddAppointmentAsync("Confirmed", expired);
        var foreign = await _factory.AddAppointmentAsync("Confirmed", expired, patient: 402, therapist: 302, service: 604, center: 202);
        using var client = _factory.Client(role);
        var route = role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments";
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync(detail ? $"{route}/{own}" : route), HttpStatusCode.OK);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync(detail ? $"{route}/{own}" : route), HttpStatusCode.OK);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("Completed", (await db.Appointments.FindAsync(own))!.Status);
            Assert.Equal("Confirmed", (await db.Appointments.FindAsync(foreign))!.Status);
            var history = await db.AppointmentStatusHistories.SingleAsync();
            Assert.Equal(own, history.AppointmentId);
            Assert.Equal("Confirmed", history.OldStatus);
            Assert.Equal("Completed", history.NewStatus);
            Assert.Null(history.ChangedByUserId);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, history.ChangedAt);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, (await db.Appointments.FindAsync(own))!.UpdatedAt);
            Assert.Empty(await db.Notifications.ToArrayAsync());
            return true;
        });
    }

    [Theory]
    [InlineData(-1, "Completed")]
    [InlineData(0, "Confirmed")]
    [InlineData(1, "Confirmed")]
    public async Task CompletionOccursOnlyAfterTreatmentEndNotAtEquality(long offsetTicks, string expected)
    {
        var end = new DateTime(2026, 10, 17, 9, 30, 0).AddTicks(offsetTicks);
        var id = await _factory.AddAppointmentAsync("Confirmed", end.AddMinutes(-50), end);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync($"/api/Appointments/{id}"), HttpStatusCode.OK);
        Assert.Equal(expected, await _factory.WithDbAsync(async db => (await db.Appointments.FindAsync(id))!.Status));
        Assert.Equal(expected == "Completed" ? 1 : 0, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
    }

    [Fact]
    public async Task DetailRequestCannotCompleteForeignAppointment()
    {
        var id = await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 17, 8, 0, 0), patient: 402);
        using var patient = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await patient.GetAsync($"/api/Appointments/{id}"), HttpStatusCode.NotFound);
        Assert.Equal("Confirmed", await _factory.WithDbAsync(async db => (await db.Appointments.FindAsync(id))!.Status));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
    }

    [Fact]
    public async Task CompletionReleasesHistoricalConfirmedBlockAndDoesNotWaitForReservedBreak()
    {
        // At09:30, treatment ended09:29 while reserved break lasts until09:39.
        var id = await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 17, 8, 39, 0), new DateTime(2026, 10, 17, 9, 29, 0));
        using var patient = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await patient.GetAsync($"/api/Appointments/{id}"), HttpStatusCode.OK);
        Assert.Equal("Completed", await _factory.WithDbAsync(async db => (await db.Appointments.FindAsync(id))!.Status));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.Appointments.CountAsync(a => a.Status == "Confirmed")));
    }

    [Fact]
    public async Task ConfirmingExpiredPendingCompletesThroughBothApprovedTransitions()
    {
        var id = await _factory.AddAppointmentAsync(start: new DateTime(2026, 10, 17, 8, 0, 0));
        using var center = _factory.Client("Center");
        await AppointmentApiFactory.AssertStatusAsync(await center.PatchAsJsonAsync($"/api/CenterAppointments/{id}/confirm", new { }), HttpStatusCode.OK);
        await _factory.WithDbAsync(async db =>
        {
            Assert.Equal("Completed", (await db.Appointments.FindAsync(id))!.Status);
            var histories = await db.AppointmentStatusHistories.OrderBy(h => h.AppointmentStatusHistoryId).ToArrayAsync();
            Assert.Equal(2, histories.Length);
            Assert.Equal(("Pending", "Confirmed", (int?)101), (histories[0].OldStatus, histories[0].NewStatus, histories[0].ChangedByUserId));
            Assert.Equal(("Confirmed", "Completed", (int?)null), (histories[1].OldStatus, histories[1].NewStatus, histories[1].ChangedByUserId));
            return true;
        });
    }

    [Fact]
    public async Task CenterCancelRechecksExpiryUnderWriteLockAndCommitsCompletionBeforeReturningConflict()
    {
        var end = new DateTime(2026, 10, 17, 9, 31, 0);
        var id = await _factory.AddAppointmentAsync("Confirmed", end.AddMinutes(-50), end);
        var beforeEnd = _factory.Clock.UtcNow;
        var afterEnd = beforeEnd.AddMinutes(2);
        var reads = 0;
        // Deterministically advance between the preliminary completion scan and the
        // expiry recheck inside the write lock, as when the request waits for that lock.
        // No timers, sleeps, or production hooks are needed.
        _factory.Clock.ReadUtcNow = () => Interlocked.Increment(ref reads) == 1 ? beforeEnd : afterEnd;
        using var center = _factory.Client("Center");
        await AppointmentApiFactory.AssertStatusAsync(await center.PatchAsJsonAsync($"/api/CenterAppointments/{id}/cancel",
            new { cancellationReason = "Must not cancel an already completed treatment" }), HttpStatusCode.Conflict);
        Assert.True(reads >= 2);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Appointments.SingleAsync();
            Assert.Equal("Completed", row.Status);
            Assert.Null(row.CancellationReason);
            Assert.Equal(afterEnd.UtcDateTime, row.UpdatedAt);
            var history = await db.AppointmentStatusHistories.SingleAsync();
            Assert.Equal(("Confirmed", "Completed", (int?)null),
                (history.OldStatus, history.NewStatus, history.ChangedByUserId));
            Assert.Equal(afterEnd.UtcDateTime, history.ChangedAt);
            return true;
        });
    }
}
