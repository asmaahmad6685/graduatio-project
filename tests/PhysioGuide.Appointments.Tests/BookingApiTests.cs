using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PhysioGuide.Appointments.Tests;

public sealed class BookingApiTests : IAsyncLifetime
{
    private readonly AppointmentApiFactory _factory = new();
    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task BookingUsesAuthenticatedPatientFixedDurationPriceSnapshotAndInitialHistoryWithoutNotifications()
    {
        using var client = _factory.Client();
        using var response = await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(notes: "  Needs support  "));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.Location);
        var json = await AppointmentApiFactory.JsonAsync(response);
        var id = json.GetProperty("appointmentId").GetInt32();
        Assert.Equal("Pending", json.GetProperty("status").GetString());
        Assert.Equal("Needs support", json.GetProperty("patientNotes").GetString());
        Assert.Equal("Asia/Hebron", json.GetProperty("timeZone").GetString());
        Assert.Equal(123.45m, json.GetProperty("priceAtBooking").GetDecimal());
        Assert.Equal(401, json.GetProperty("patient").GetProperty("patientId").GetInt32());
        Assert.Equal(201, json.GetProperty("center").GetProperty("centerId").GetInt32());
        Assert.Equal(301, json.GetProperty("therapist").GetProperty("therapistId").GetInt32());
        Assert.Equal(601, json.GetProperty("service").GetProperty("serviceId").GetInt32());
        Assert.Equal(new DateTime(2026, 10, 18, 10, 0, 0), json.GetProperty("startDateTime").GetDateTime());
        Assert.Equal(new DateTime(2026, 10, 18, 10, 50, 0), json.GetProperty("endDateTime").GetDateTime());
        Assert.Equal(new DateTime(2026, 10, 18, 11, 0, 0), json.GetProperty("reservedUntil").GetDateTime());
        Assert.Equal(DateTimeKind.Unspecified, json.GetProperty("startDateTime").GetDateTime().Kind);
        Assert.DoesNotContain("password", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await _factory.WithDbAsync(async db =>
        {
            var appointment = await db.Appointments.SingleAsync();
            Assert.Equal(id, appointment.AppointmentId);
            Assert.Equal(401, appointment.PatientId);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, appointment.CreatedAt);
            Assert.Null(appointment.UpdatedAt);
            var history = await db.AppointmentStatusHistories.SingleAsync();
            Assert.Equal(id, history.AppointmentId);
            Assert.Null(history.OldStatus);
            Assert.Equal("Pending", history.NewStatus);
            Assert.Equal(121, history.ChangedByUserId);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, history.ChangedAt);
            Assert.Empty(await db.Notifications.ToArrayAsync());
            return true;
        });
        await _factory.MutateAsync(db => db.Services.Single(s => s.ServiceId == 601).Price = 999m);
        using var details = await client.GetAsync($"/api/Appointments/{id}");
        Assert.Equal(123.45m, (await AppointmentApiFactory.JsonAsync(details)).GetProperty("priceAtBooking").GetDecimal());
    }

    [Theory]
    [InlineData("patientId", "402")]
    [InlineData("priceAtBooking", "0.01")]
    [InlineData("endDateTime", "\"2026-10-18T10:05:00\"")]
    [InlineData("status", "\"Confirmed\"")]
    public async Task ClientCannotProvideServerOwnedFields(string field, string value)
    {
        using var client = _factory.Client();
        var body = "{\"centerId\":201,\"serviceId\":601,\"therapistId\":301,\"startDateTime\":\"2026-10-18T10:00:00\",\"" + field + "\":" + value + "}";
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsync("/api/Appointments", new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
        Assert.Equal(0, await _factory.WithDbAsync(db => db.Appointments.CountAsync()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"centerId\":201,\"serviceId\":601,\"therapistId\":301}")]
    [InlineData("{\"centerId\":0,\"serviceId\":601,\"therapistId\":301,\"startDateTime\":\"2026-10-18T10:00:00\"}")]
    [InlineData("{\"centerId\":201,\"serviceId\":-1,\"therapistId\":301,\"startDateTime\":\"2026-10-18T10:00:00\"}")]
    [InlineData("{\"centerId\":201,\"serviceId\":601,\"therapistId\":0,\"startDateTime\":\"2026-10-18T10:00:00\"}")]
    [InlineData("{\"centerId\":201,\"serviceId\":601,\"therapistId\":301,\"startDateTime\":\"not-a-date\"}")]
    public async Task MalformedIncompleteOrInvalidRequestsAreRejected(string body)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsync("/api/Appointments", new StringContent(body, Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("2026-10-18T10:00:00Z")]
    [InlineData("2026-10-18T10:00:00+03:00")]
    [InlineData("2026-10-18T10:00:00-03:00")]
    public async Task SchedulingInputMustNotSupplyUtcOrOffset(string start)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(start)), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task PatientNotesRespectModelLimit(int length, bool valid)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(notes: new string('x', length))), valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("therapistInactive")]
    [InlineData("therapistUnavailable")]
    [InlineData("serviceInactive")]
    [InlineData("assignmentMissing")]
    [InlineData("closedCenter")]
    [InlineData("scheduleUnavailable")]
    [InlineData("scheduleMissing")]
    [InlineData("outsideWorkingHours")]
    public async Task IneligibleResourcesCannotBeBooked(string condition)
    {
        await _factory.MutateAsync(db =>
        {
            if (condition == "therapistInactive") db.Therapists.Single(t => t.TherapistId == 301).IsActive = false;
            if (condition == "therapistUnavailable") db.Therapists.Single(t => t.TherapistId == 301).IsAvailable = false;
            if (condition == "serviceInactive") db.Services.Single(s => s.ServiceId == 601).IsActive = false;
            if (condition == "assignmentMissing") db.TherapistServices.Remove(db.TherapistServices.Single(t => t.TherapistId == 301 && t.ServiceId == 601));
            if (condition == "closedCenter") db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday).IsClosed = true;
            if (condition == "outsideWorkingHours") db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday).OpeningTime = new TimeOnly(11, 0);
            if (condition == "scheduleUnavailable") db.TherapistSchedules.Single(s => s.TherapistId == 301 && s.DayOfWeek == DayOfWeek.Sunday).IsAvailable = false;
            if (condition == "scheduleMissing") db.TherapistSchedules.Remove(db.TherapistSchedules.Single(s => s.TherapistId == 301 && s.DayOfWeek == DayOfWeek.Sunday));
        });
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), HttpStatusCode.Conflict);
        Assert.Equal(0, await _factory.WithDbAsync(db => db.Appointments.CountAsync()));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
    }

    [Theory]
    [InlineData("Active", true)]
    [InlineData("active", false)]
    [InlineData("ACTIVE", false)]
    [InlineData("Pending", false)]
    [InlineData("Rejected", false)]
    [InlineData("Suspended", false)]
    public async Task CenterStatusMustBeExactlyActive(string status, bool valid)
    {
        await _factory.MutateAsync(db => db.Centers.Single(c => c.CenterId == 201).Status = status);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), valid ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(301, 604)]
    [InlineData(302, 601)]
    [InlineData(301, 603)]
    public async Task WrongCenterOrUnassignedResourcesCannotBeBooked(int therapist, int service)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(therapist: therapist, service: service)), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(999, 301, 601)]
    [InlineData(201, 999, 601)]
    [InlineData(201, 301, 999)]
    public async Task MissingSelectedResourceReturnsNotFound(int center, int therapist, int service)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(center: center, therapist: therapist, service: service)), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("2026-10-17T09:00:00")]
    [InlineData("2026-10-18T10:30:00")]
    [InlineData("2026-10-18T10:00:01")]
    [InlineData("2026-10-18T08:00:00")]
    [InlineData("2026-10-18T19:30:00")]
    [InlineData("2026-10-18T20:00:00")]
    public async Task PastOutOfHorizonAndNonSlotStartsCannotBeBooked(string start)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(start)), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("2026-10-16T10:00:00")]
    [InlineData("2026-11-18T10:00:00")]
    public async Task DatesOutsideCalendarHorizonAreRejectedAsInvalidRequests(string start)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(start)), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("2026-10-17T10:00:00")]
    [InlineData("2026-11-17T19:00:00")]
    public async Task SameDayFutureAndFinalHorizonDateAreBookable(string start)
    {
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(start)), HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("2027-02-28T19:00:00", true)]
    [InlineData("2027-03-01T09:00:00", false)]
    public async Task CalendarMonthHorizonClampsAtMonthEnd(string start, bool valid)
    {
        _factory.Clock.UtcNow = new DateTimeOffset(2027, 1, 31, 6, 0, 0, TimeSpan.Zero);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(start)), valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Pending", false)]
    [InlineData("Confirmed", false)]
    [InlineData("pEnDiNg", false)]
    [InlineData("cOnFiRmEd", false)]
    [InlineData("Rejected", true)]
    [InlineData("Cancelled", true)]
    [InlineData("Completed", true)]
    public async Task TherapistConflictsSpanAllServicesAndOnlyBlockingStatusesMatter(string status, bool valid)
    {
        await _factory.AddAppointmentAsync(status, patient: 402, service: 602);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), valid ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("2026-10-18T09:00:00", true)]
    [InlineData("2026-10-18T09:50:00", false)]
    [InlineData("2026-10-18T10:00:00", false)]
    [InlineData("2026-10-18T10:50:00", false)]
    [InlineData("2026-10-18T11:00:00", true)]
    public async Task TherapistOccupiedIntervalsIncludeBothBreaksAndAllowHalfOpenAdjacency(string existingStart, bool valid)
    {
        await _factory.AddAppointmentAsync(start: DateTime.Parse(existingStart), patient: 402);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), valid ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("Pending", false)]
    [InlineData("Confirmed", false)]
    [InlineData("Rejected", true)]
    [InlineData("Cancelled", true)]
    public async Task PatientConflictsSpanOtherCentersTherapistsAndServices(string status, bool valid)
    {
        await _factory.AddAppointmentAsync(status, therapist: 302, service: 604, center: 202);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), valid ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("2026-10-18T09:00:00", true)]
    [InlineData("2026-10-18T09:50:00", false)]
    [InlineData("2026-10-18T10:50:00", false)]
    [InlineData("2026-10-18T11:00:00", true)]
    public async Task PatientConflictUsesHalfOpenOccupiedIntervalsIncludingBreaks(string existingStart, bool valid)
    {
        await _factory.AddAppointmentAsync(start: DateTime.Parse(existingStart), therapist: 302, service: 604, center: 202);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), valid ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DifferentPatientCanBookDifferentTherapistAtSameTime()
    {
        await _factory.AddAppointmentAsync(patient: 402, therapist: 303);
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("2026-10-24T00:00:00", true)]
    [InlineData("2026-10-24T01:00:00", false)]
    [InlineData("2026-10-24T02:00:00", true)]
    public async Task FallDstRejectsAmbiguousOccupiedTimeButAllowsValidExclusiveEndBoundary(string start, bool valid)
    {
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Saturday);
            hours.OpeningTime = new TimeOnly(0, 0); hours.ClosingTime = new TimeOnly(4, 0);
            var schedule = db.TherapistSchedules.Single(s => s.TherapistId == 301 && s.DayOfWeek == DayOfWeek.Saturday);
            schedule.StartTime = new TimeOnly(0, 0); schedule.EndTime = new TimeOnly(4, 0);
        });
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(start)), valid ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task SpringDstRejectsNonexistentOccupiedTimeAndPreservesPrecedingBoundary()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");
        DateTime? missing = null;
        for (var day = new DateTime(2026, 1, 1); day.Year == 2026 && !missing.HasValue; day = day.AddDays(1))
            for (var hour = 0; hour < 6; hour++)
                if (zone.IsInvalidTime(day.AddHours(hour))) { missing = day.AddHours(hour); break; }
        Assert.True(missing.HasValue, "Expected a spring Asia/Hebron DST gap in runtime 2026 rules.");
        var affected = missing!.Value;
        _factory.Clock.UtcNow = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(affected.Date.AddDays(-1).AddHours(12), zone));
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == affected.DayOfWeek);
            hours.OpeningTime = new TimeOnly(0, 0); hours.ClosingTime = new TimeOnly(6, 0);
            var schedule = db.TherapistSchedules.Single(s => s.TherapistId == 301 && s.DayOfWeek == affected.DayOfWeek);
            schedule.StartTime = new TimeOnly(0, 0); schedule.EndTime = new TimeOnly(6, 0);
        });
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(affected.ToString("yyyy-MM-ddTHH:mm:ss"))), HttpStatusCode.Conflict);
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking(affected.AddHours(-1).ToString("yyyy-MM-ddTHH:mm:ss"))), HttpStatusCode.Created);
    }
}
