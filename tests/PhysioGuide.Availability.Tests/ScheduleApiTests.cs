using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PhysioGuide.Availability.Tests;

public sealed class ScheduleApiTests : IAsyncLifetime
{
    private const string Route = "/api/Schedules/therapists/301";
    private readonly AvailabilityApiFactory _factory = new();
    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task CenterCreatesReadsUpdatesAndDeletesItsTherapistsSchedule()
    {
        using var client = _factory.Client("Center");
        using var created = await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        var window = await AvailabilityApiFactory.JsonAsync(created);
        var id = window.GetProperty("scheduleId").GetInt32();
        Assert.Equal(301, window.GetProperty("therapistId").GetInt32());
        Assert.Equal(0, window.GetProperty("dayOfWeek").GetInt32());
        Assert.Equal("09:00:00", window.GetProperty("startTime").GetString());
        Assert.True(window.GetProperty("isAvailable").GetBoolean());
        using var schedule = await client.GetAsync(Route);
        Assert.Equal(HttpStatusCode.OK, schedule.StatusCode);
        Assert.Equal(id, Assert.Single((await AvailabilityApiFactory.JsonAsync(schedule)).EnumerateArray()).GetProperty("scheduleId").GetInt32());

        using var updated = await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(start: "10:00:00", end: "14:00:00"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("10:00:00", (await AvailabilityApiFactory.JsonAsync(updated)).GetProperty("startTime").GetString());
        await _factory.WithDbAsync(async db =>
        {
            var persisted = await db.TherapistSchedules.SingleAsync();
            Assert.Equal(301, persisted.TherapistId);
            Assert.Equal(new TimeOnly(10, 0), persisted.StartTime);
            return true;
        });
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{id}"), HttpStatusCode.NoContent);
        Assert.Equal(0, await _factory.WithDbAsync(db => db.TherapistSchedules.CountAsync()));
    }

    [Fact]
    public async Task OmittedIsAvailableDefaultsToTrue()
    {
        using var client = _factory.Client("Center");
        using var response = await client.PostAsJsonAsync(Route, new { dayOfWeek = 0, startTime = "09:00:00", endTime = "10:00:00" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True((await AvailabilityApiFactory.JsonAsync(response)).GetProperty("isAvailable").GetBoolean());
    }

    [Fact]
    public async Task MultipleSeparateAndAdjacentWindowsOnSameDayAreAllowed()
    {
        using var client = _factory.Client("Center");
        foreach (var (start, end) in new[] { ("09:00:00", "12:00:00"), ("17:00:00", "19:00:00"), ("12:00:00", "13:00:00") })
            await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window(start: start, end: end)), HttpStatusCode.Created);
        using var response = await client.GetAsync(Route);
        var rows = (await AvailabilityApiFactory.JsonAsync(response)).EnumerateArray().ToArray();
        Assert.Equal(3, rows.Length);
        Assert.Equal(new[] { "09:00:00", "12:00:00", "17:00:00" }, rows.Select(r => r.GetProperty("startTime").GetString()));
    }

    [Theory]
    [InlineData("09:00:00", "12:00:00")]
    [InlineData("08:00:00", "10:00:00")]
    [InlineData("11:00:00", "13:00:00")]
    [InlineData("10:00:00", "11:00:00")]
    [InlineData("08:00:00", "13:00:00")]
    public async Task AllFormsOfSameDayOverlapAreRejected(string start, string end)
    {
        await _factory.AddWindowAsync();
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window(start: start, end: end)), HttpStatusCode.Conflict);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.TherapistSchedules.CountAsync()));
    }

    [Fact]
    public async Task UpdatingWindowToOverlapAnotherWindowIsRejected()
    {
        await _factory.AddWindowAsync();
        var id = await _factory.AddWindowAsync(start: "14:00", end: "17:00");
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(start: "11:00:00", end: "15:00:00")), HttpStatusCode.Conflict);
        Assert.Equal(new TimeOnly(14, 0), await _factory.WithDbAsync(async db => (await db.TherapistSchedules.FindAsync(id))!.StartTime));
    }

    [Fact]
    public async Task SameClockTimesOnDifferentDaysDoNotOverlap()
    {
        await _factory.AddWindowAsync(day: 0);
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window(day: 1)), HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(-1, "09:00:00", "12:00:00")]
    [InlineData(7, "09:00:00", "12:00:00")]
    [InlineData(0, "12:00:00", "12:00:00")]
    [InlineData(0, "13:00:00", "12:00:00")]
    [InlineData(0, "19:00:00", "08:00:00")]
    [InlineData(0, "07:59:00", "12:00:00")]
    [InlineData(0, "17:00:00", "20:01:00")]
    public async Task InvalidDayTimesOvernightAndOutsideWorkingHoursAreRejected(int day, string start, string end)
    {
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window(day, start, end)), HttpStatusCode.BadRequest);
        Assert.Equal(0, await _factory.WithDbAsync(db => db.TherapistSchedules.CountAsync()));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("missing")]
    [InlineData("missingOpening")]
    [InlineData("reversed")]
    public async Task ClosedMissingOrInvalidWorkingHoursRejectScheduleWrites(string mode)
    {
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday);
            if (mode == "closed") hours.IsClosed = true;
            if (mode == "missing") db.CenterWorkingHours.Remove(hours);
            if (mode == "missingOpening") hours.OpeningTime = null;
            if (mode == "reversed") hours.ClosingTime = new TimeOnly(7, 0);
        });
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window()), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateAlsoValidatesCenterWorkingHoursAndClosedDays()
    {
        var id = await _factory.AddWindowAsync();
        await _factory.MutateAsync(db => db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Monday).IsClosed = true);
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(day: 1)), HttpStatusCode.BadRequest);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(end: "21:00:00")), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task CenterCannotManageAnotherCentersTherapist(string method)
    {
        var id = await _factory.AddWindowAsync(therapistId: 302);
        using var client = _factory.Client("Center");
        using var response = method switch
        {
            "GET" => await client.GetAsync("/api/Schedules/therapists/302"),
            "POST" => await client.PostAsJsonAsync("/api/Schedules/therapists/302", AvailabilityApiFactory.Window()),
            "PUT" => await client.PutAsJsonAsync($"/api/Schedules/therapists/302/{id}", AvailabilityApiFactory.Window()),
            _ => await client.DeleteAsync($"/api/Schedules/therapists/302/{id}")
        };
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(302, await _factory.WithDbAsync(async db => (await db.TherapistSchedules.SingleAsync()).TherapistId));
    }

    [Fact]
    public async Task ForeignScheduleIdCannotBeUpdatedOrDeletedThroughOwnTherapistRoute()
    {
        var foreignId = await _factory.AddWindowAsync(therapistId: 302);
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{foreignId}", AvailabilityApiFactory.Window()), HttpStatusCode.NotFound);
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{foreignId}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Confirmed")]
    [InlineData("pEnDiNg")]
    [InlineData("cOnFiRmEd")]
    public async Task BlockingAppointmentPreventsShrinkingDisablingChangingDayAndDeletingWindow(string status)
    {
        var id = await _factory.AddWindowAsync(start: "08:00", end: "15:00");
        await _factory.AddAppointmentAsync(status, new DateTime(2026, 10, 18, 14, 0, 0));
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(start: "08:00:00", end: "13:00:00")), HttpStatusCode.Conflict);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(start: "08:00:00", end: "15:00:00", available: false)), HttpStatusCode.Conflict);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(day: 1, start: "08:00:00", end: "15:00:00")), HttpStatusCode.Conflict);
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{id}"), HttpStatusCode.Conflict);
        Assert.Equal(new TimeOnly(15, 0), await _factory.WithDbAsync(async db => (await db.TherapistSchedules.SingleAsync()).EndTime));
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    [InlineData("rEjEcTeD")]
    [InlineData("cAnCeLlEd")]
    public async Task RejectedCancelledAppointmentsDoNotPreventScheduleChanges(string status)
    {
        var id = await _factory.AddWindowAsync(start: "08:00", end: "15:00");
        await _factory.AddAppointmentAsync(status, new DateTime(2026, 10, 18, 14, 0, 0));
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(end: "13:00:00")), HttpStatusCode.OK);
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{id}"), HttpStatusCode.NoContent);
        Assert.Equal(status, await _factory.WithDbAsync(async db => (await db.Appointments.SingleAsync()).Status));
    }

    [Theory]
    [InlineData(2025, 10, 19)]
    [InlineData(2027, 10, 17)]
    public async Task BlockingAppointmentsOutsideCurrentBookingHorizonRemainProtected(int year, int month, int day)
    {
        var appointmentStart = new DateTime(year, month, day, 14, 0, 0);
        var id = await _factory.AddWindowAsync(day: (int)appointmentStart.DayOfWeek, start: "08:00", end: "15:00");
        await _factory.AddAppointmentAsync("Pending", appointmentStart);
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{id}"), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ExistingAppointmentMayRemainContainedWithoutMatchingNewSlotGrid()
    {
        var id = await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 18, 10, 0, 0));
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(start: "09:30:00", end: "12:30:00")), HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("10:55:00")]
    [InlineData("10:59:59")]
    public async Task ScheduleProtectionIncludesEntirePostAppointmentBreak(string end)
    {
        var id = await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Pending", new DateTime(2026, 10, 18, 10, 0, 0));
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(end: end)), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task LegacyLongerAppointmentRequiresItsActualEndPlusBreakToRemainContained()
    {
        var id = await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Pending", new DateTime(2026, 10, 18, 10, 0, 0), new DateTime(2026, 10, 18, 11, 30, 0));
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(end: "11:30:00")), HttpStatusCode.Conflict);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(end: "11:40:00")), HttpStatusCode.OK);
    }

    [Fact]
    public async Task ScheduleOperationsNeverModifyExistingAppointmentRows()
    {
        var id = await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 18, 10, 0, 0));
        var before = await _factory.WithDbAsync(db => db.Appointments.AsNoTracking().Select(a => new { a.AppointmentId, a.PatientId, a.CenterId, a.TherapistId, a.ServiceId, a.StartDateTime, a.EndDateTime, a.Status, a.PriceAtBooking, a.PatientNotes, a.CreatedAt, a.UpdatedAt }).SingleAsync());
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window(start: "08:00:00", end: "13:00:00")), HttpStatusCode.OK);
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{id}"), HttpStatusCode.Conflict);
        var after = await _factory.WithDbAsync(db => db.Appointments.AsNoTracking().Select(a => new { a.AppointmentId, a.PatientId, a.CenterId, a.TherapistId, a.ServiceId, a.StartDateTime, a.EndDateTime, a.Status, a.PriceAtBooking, a.PatientNotes, a.CreatedAt, a.UpdatedAt }).SingleAsync());
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ConcurrentIdenticalCreatesCannotPersistOverlappingWindows()
    {
        using var client = _factory.Client("Center");
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window())));
        try
        {
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await _factory.WithDbAsync(db => db.TherapistSchedules.CountAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Patient", HttpStatusCode.Forbidden)]
    [InlineData("Therapist", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    public async Task EveryManagementVerbRequiresCenterRole(string? role, HttpStatusCode expected)
    {
        var id = await _factory.AddWindowAsync();
        using var client = _factory.Client(role);
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route), expected);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window()), expected);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/{id}", AvailabilityApiFactory.Window()), expected);
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/{id}"), expected);
    }

    [Theory]
    [InlineData("not-an-int", HttpStatusCode.Unauthorized)]
    [InlineData("0", HttpStatusCode.Unauthorized)]
    [InlineData("-1", HttpStatusCode.Unauthorized)]
    [InlineData("103", HttpStatusCode.NotFound)]
    public async Task MalformedIdentityOrMissingCenterProfileCannotManageSchedules(string userId, HttpStatusCode expected)
    {
        using var client = _factory.Client("Center", userId);
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route), expected);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(Route, AvailabilityApiFactory.Window()), expected);
    }

    [Fact]
    public async Task UnknownTherapistOrScheduleReturnsNotFound()
    {
        using var client = _factory.Client("Center");
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync("/api/Schedules/therapists/99999"), HttpStatusCode.NotFound);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Schedules/therapists/99999", AvailabilityApiFactory.Window()), HttpStatusCode.NotFound);
        await AvailabilityApiFactory.AssertStatusAsync(await client.PutAsJsonAsync($"{Route}/99999", AvailabilityApiFactory.Window()), HttpStatusCode.NotFound);
        await AvailabilityApiFactory.AssertStatusAsync(await client.DeleteAsync($"{Route}/99999"), HttpStatusCode.NotFound);
    }
}
