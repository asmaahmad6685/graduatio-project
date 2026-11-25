using System.Net;
using System.Text.Json;
using graduatio_project.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PhysioGuide.Availability.Tests;

public sealed class AvailabilityApiTests : IAsyncLifetime
{
    private readonly AvailabilityApiFactory _factory = new();
    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();
    private static string Route(string date = "2026-10-18", int service = 601, int therapist = 301) =>
        $"/api/Availability/therapists/{therapist}/slots?serviceId={service}&date={date}";

    private async Task<JsonElement> QueryAsync(string date = "2026-10-18", int service = 601)
    {
        using var client = _factory.Client();
        using var response = await client.GetAsync(Route(date, service));
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"Unexpected {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await AvailabilityApiFactory.JsonAsync(response);
    }

    private static DateTime[] Starts(JsonElement response) => response.GetProperty("slots")
        .EnumerateArray().Select(s => s.GetProperty("startDateTime").GetDateTime()).ToArray();

    [Fact]
    public async Task WindowsProduceFixedFiftyMinuteSessionsWithTenMinuteBreaksForEveryAssignedService()
    {
        await _factory.AddWindowAsync(start: "17:00", end: "19:00");
        foreach (var service in new[] { 601, 602 })
        {
            var result = await QueryAsync(service: service);
            Assert.Equal(301, result.GetProperty("therapistId").GetInt32());
            Assert.Equal(service, result.GetProperty("serviceId").GetInt32());
            Assert.Equal("Asia/Hebron", result.GetProperty("timeZone").GetString());
            Assert.Equal("2026-10-18", result.GetProperty("date").GetString());
            Assert.Equal("2026-11-17", result.GetProperty("bookableThrough").GetString());
            Assert.Equal(50, result.GetProperty("sessionMinutes").GetInt32());
            Assert.Equal(10, result.GetProperty("breakMinutes").GetInt32());
            Assert.Equal(new[] { 17, 18 }, Starts(result).Select(s => s.Hour));
            foreach (var slot in result.GetProperty("slots").EnumerateArray())
            {
                var start = slot.GetProperty("startDateTime").GetDateTime();
                var end = slot.GetProperty("endDateTime").GetDateTime();
                var reservedUntil = slot.GetProperty("reservedUntil").GetDateTime();
                Assert.Equal(TimeSpan.FromMinutes(50), end - start);
                Assert.Equal(TimeSpan.FromMinutes(10), reservedUntil - end);
                Assert.Equal(DateTimeKind.Unspecified, start.Kind);
            }
        }
    }

    [Fact]
    public async Task MultipleWindowsProduceSlotsAnchoredAtEachWindowStartAndRepeatWeekly()
    {
        await _factory.AddWindowAsync(start: "09:15", end: "11:15");
        await _factory.AddWindowAsync(start: "17:30", end: "19:30");
        var result = await QueryAsync();
        Assert.Equal(new[] { "09:15", "10:15", "17:30", "18:30" }, Starts(result).Select(s => s.ToString("HH:mm")));
        var followingWeek = await QueryAsync("2026-10-25");
        Assert.Equal(new[] { "09:15", "10:15", "17:30", "18:30" }, Starts(followingWeek).Select(s => s.ToString("HH:mm")));
        Assert.All(Starts(followingWeek), s => Assert.Equal(new DateTime(2026, 10, 25), s.Date));
    }

    [Theory]
    [InlineData("09:00", "09:50", 0)]
    [InlineData("09:00", "09:59", 0)]
    [InlineData("09:00", "10:00", 1)]
    [InlineData("09:00", "10:59", 1)]
    [InlineData("09:15", "11:15", 2)]
    public async Task FullSixtyMinuteOccupiedBlockMustFitInsideWindow(string start, string end, int expectedCount)
    {
        await _factory.AddWindowAsync(start: start, end: end);
        Assert.Equal(expectedCount, Starts(await QueryAsync()).Length);
    }

    [Fact]
    public async Task LegacyWindowOutsideCenterHoursNeverAdvertisesOutsideHoursSlots()
    {
        await _factory.AddWindowAsync(start: "07:00", end: "13:00");
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday);
            hours.OpeningTime = new TimeOnly(8, 30);
            hours.ClosingTime = new TimeOnly(11, 50);
        });
        Assert.Equal(new[] { 9, 10 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task DisabledSchedulesAndWrongWeekdaysExposeNoSlots()
    {
        await _factory.AddWindowAsync(available: false);
        await _factory.AddWindowAsync(day: 1);
        Assert.Empty(Starts(await QueryAsync()));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Confirmed")]
    [InlineData("pEnDiNg")]
    [InlineData("cOnFiRmEd")]
    public async Task BlockingAppointmentsRemoveCorrespondingSlotsAcrossAllServices(string status)
    {
        await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync(status, new DateTime(2026, 10, 18, 10, 0, 0), serviceId: 602);
        foreach (var service in new[] { 601, 602 })
            Assert.Equal(new[] { 9, 11 }, Starts(await QueryAsync(service: service)).Select(s => s.Hour));
    }

    [Theory]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    [InlineData("rEjEcTeD")]
    [InlineData("cAnCeLlEd")]
    public async Task RejectedCancelledAppointmentsDoNotRemoveSlots(string status)
    {
        await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync(status, new DateTime(2026, 10, 18, 10, 0, 0));
        Assert.Equal(new[] { 9, 10, 11 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task AppointmentBreakAndCandidateBreakBothParticipateInOverlapDetection()
    {
        await _factory.AddWindowAsync(start: "09:00", end: "13:00");
        // This blocks09:00's break as well as the10:00 occupied block.
        await _factory.AddAppointmentAsync("Pending", new DateTime(2026, 10, 18, 9, 50, 0));
        Assert.Equal(new[] { 11, 12 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task ShortLegacyAppointmentsStillReserveAtLeastFiftyPlusTenMinutes()
    {
        await _factory.AddWindowAsync(start: "09:00", end: "13:00");
        await _factory.AddAppointmentAsync("Pending", new DateTime(2026, 10, 18, 10, 5, 0), new DateTime(2026, 10, 18, 10, 10, 0));
        Assert.Equal(new[] { 9, 12 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task LongerLegacyAppointmentsReserveActualEndPlusTenMinutes()
    {
        await _factory.AddWindowAsync(start: "09:00", end: "14:00");
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 18, 10, 0, 0), new DateTime(2026, 10, 18, 12, 5, 0));
        Assert.Equal(new[] { 9, 13 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task AppointmentOnAnotherTherapistOrDateDoesNotBlockRequestedSlots()
    {
        await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 18, 10, 0, 0), serviceId: 604, therapistId: 302);
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 25, 10, 0, 0));
        Assert.Equal(new[] { 9, 10, 11 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task AppointmentEndingWithBreakExactlyAtSlotStartDoesNotBlockThatSlot()
    {
        await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 18, 9, 0, 0));
        Assert.Equal(new[] { 10, 11 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task PreviousDayAppointmentSpilloverBlocksEarlyNextDaySlots()
    {
        await _factory.AddWindowAsync(start: "00:00", end: "03:00");
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday);
            hours.OpeningTime = new TimeOnly(0, 0);
            hours.ClosingTime = new TimeOnly(3, 0);
        });
        await _factory.AddAppointmentAsync("Confirmed", new DateTime(2026, 10, 17, 23, 40, 0), new DateTime(2026, 10, 18, 0, 30, 0));
        Assert.Equal(new[] { 1, 2 }, Starts(await QueryAsync()).Select(s => s.Hour));
    }

    [Fact]
    public async Task LegacyDuplicateAndOverlappingWindowsNeverYieldDuplicateOrCompetingSlots()
    {
        await _factory.AddWindowAsync();
        await _factory.AddWindowAsync();
        await _factory.AddWindowAsync(start: "09:30", end: "12:30");
        var slots = (await QueryAsync()).GetProperty("slots").EnumerateArray().ToArray();
        var starts = slots.Select(s => s.GetProperty("startDateTime").GetDateTime()).ToArray();
        Assert.Equal(starts.Length, starts.Distinct().Count());
        Assert.NotEmpty(slots);
        for (var i = 1; i < slots.Length; i++)
            Assert.True(slots[i].GetProperty("startDateTime").GetDateTime() >= slots[i - 1].GetProperty("reservedUntil").GetDateTime());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task InactiveOrUnavailableTherapistExposesNoSlots(bool active, bool available)
    {
        await _factory.AddWindowAsync();
        await _factory.MutateAsync(db =>
        {
            var therapist = db.Therapists.Single(t => t.TherapistId == 301);
            therapist.IsActive = active;
            therapist.IsAvailable = available;
        });
        Assert.Empty(Starts(await QueryAsync()));
    }

    [Theory]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    [InlineData("Suspended")]
    [InlineData("Approved")]
    [InlineData("Unknown")]
    public async Task EveryNonActiveCenterStatusExposesNoSlots(string status)
    {
        await _factory.AddWindowAsync();
        await _factory.MutateAsync(db => db.Centers.Single(c => c.CenterId == 201).Status = status);
        Assert.Empty(Starts(await QueryAsync()));
    }

    [Theory]
    [InlineData("Active", 3)]
    [InlineData("active", 0)]
    [InlineData("ACTIVE", 0)]
    public async Task CenterStatusMustMatchActiveExactly(string status, int expectedSlots)
    {
        await _factory.AddWindowAsync();
        await _factory.MutateAsync(db => db.Centers.Single(c => c.CenterId == 201).Status = status);
        Assert.Equal(expectedSlots, Starts(await QueryAsync()).Length);
    }

    [Fact]
    public async Task InactiveServiceExposesNoSlots()
    {
        await _factory.AddWindowAsync();
        await _factory.MutateAsync(db => db.Services.Single(s => s.ServiceId == 601).IsActive = false);
        Assert.Empty(Starts(await QueryAsync()));
    }

    [Theory]
    [InlineData(603)]
    [InlineData(604)]
    public async Task UnassignedOrForeignCenterServiceExposesNoSlots(int serviceId)
    {
        await _factory.AddWindowAsync();
        Assert.Empty(Starts(await QueryAsync(service: serviceId)));
    }

    [Fact]
    public async Task EvenAnExistingCrossCenterAssignmentCannotMakeForeignServiceBookable()
    {
        await _factory.AddWindowAsync();
        await _factory.MutateAsync(db => db.TherapistServices.Add(new graduatio_project.Models.TherapistService { TherapistId = 301, ServiceId = 604 }));
        Assert.Empty(Starts(await QueryAsync(service: 604)));
    }

    [Theory]
    [InlineData("closed")]
    [InlineData("missing")]
    [InlineData("missingClosing")]
    [InlineData("reversed")]
    public async Task ClosedMissingOrInvalidWorkingHoursExposeNoSlots(string mode)
    {
        await _factory.AddWindowAsync();
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday);
            if (mode == "closed") hours.IsClosed = true;
            if (mode == "missing") db.CenterWorkingHours.Remove(hours);
            if (mode == "missingClosing") hours.ClosingTime = null;
            if (mode == "reversed") hours.ClosingTime = new TimeOnly(7, 0);
        });
        Assert.Empty(Starts(await QueryAsync()));
    }

    [Fact]
    public async Task SameDayPastSlotsAreExcludedWhileFutureSlotsRemainAvailable()
    {
        // Fixed clock is09:30 in Asia/Hebron, despite UTC being06:30.
        await _factory.AddWindowAsync(day: 6, start: "08:00", end: "12:00");
        Assert.Equal(new[] { 10, 11 }, Starts(await QueryAsync("2026-10-17")).Select(s => s.Hour));
    }

    [Fact]
    public async Task SlotStartingExactlyAtCurrentLocalTimeRemainsAvailable()
    {
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 10, 17, 7, 0, 0, TimeSpan.Zero);
        await _factory.AddWindowAsync(day: 6, start: "09:00", end: "12:00");
        Assert.Equal(new[] { 10, 11 }, Starts(await QueryAsync("2026-10-17")).Select(s => s.Hour));
    }

    [Theory]
    [InlineData("2026-10-16")]
    [InlineData("2026-11-18")]
    public async Task PastDatesAndDatesBeyondOneCalendarMonthAreRejected(string date)
    {
        using var client = _factory.Client();
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route(date)), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExactOneMonthBookingBoundaryIsInclusive()
    {
        await _factory.AddWindowAsync(day: 2);
        var result = await QueryAsync("2026-11-17");
        Assert.Equal(3, Starts(result).Length);
        Assert.Equal("2026-11-17", result.GetProperty("bookableThrough").GetString());
    }

    [Fact]
    public async Task MonthEndUsesCalendarAddMonthsRatherThanThirtyDays()
    {
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);
        await _factory.AddWindowAsync(day: 6);
        var result = await QueryAsync("2026-02-28");
        Assert.Equal("2026-02-28", result.GetProperty("bookableThrough").GetString());
        Assert.Equal(3, Starts(result).Length);
        using var client = _factory.Client();
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route("2026-03-01")), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CurrentDateAndBookingHorizonUseHebronEvenWhenUtcDateDiffers()
    {
        _factory.Clock.UtcNow = new DateTimeOffset(2026, 1, 31, 22, 30, 0, TimeSpan.Zero); // February1 00:30 Hebron.
        await _factory.AddWindowAsync(day: 0, start: "01:00", end: "03:00");
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == DayOfWeek.Sunday);
            hours.OpeningTime = new TimeOnly(0, 0);
            hours.ClosingTime = new TimeOnly(4, 0);
        });
        var result = await QueryAsync("2026-02-01");
        Assert.Equal(new[] { 1, 2 }, Starts(result).Select(s => s.Hour));
        Assert.Equal("2026-03-01", result.GetProperty("bookableThrough").GetString());
        using var client = _factory.Client();
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route("2026-01-31")), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalDstTransitionsNeverExposeInvalidOrRepeatedClockBlocks(bool repeatedTime)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");
        DateTime? affectedLocalTime = null;
        for (var day = new DateTime(2026, 1, 1); day.Year == 2026 && !affectedLocalTime.HasValue; day = day.AddDays(1))
        {
            for (var minute = 0; minute < 6 * 60; minute += 15)
            {
                var local = day.AddMinutes(minute);
                if (repeatedTime ? zone.IsAmbiguousTime(local) : zone.IsInvalidTime(local))
                {
                    affectedLocalTime = local;
                    break;
                }
            }
        }
        Assert.True(affectedLocalTime.HasValue, "Expected an Asia/Hebron DST transition in the runtime 2026 timezone rules.");
        var affected = affectedLocalTime!.Value;
        var beforeTransitionLocalNoon = affected.Date.AddDays(-1).AddHours(12);
        _factory.Clock.UtcNow = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(beforeTransitionLocalNoon, zone));
        await _factory.AddWindowAsync(day: (int)affected.DayOfWeek, start: "00:00", end: "06:00");
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == affected.DayOfWeek);
            hours.OpeningTime = new TimeOnly(0, 0);
            hours.ClosingTime = new TimeOnly(6, 0);
        });

        var result = await QueryAsync(affected.ToString("yyyy-MM-dd"));
        var slots = result.GetProperty("slots").EnumerateArray().ToArray();
        Assert.NotEmpty(slots); // Stable times must remain usable on a transition day.
        var precedingStart = affected.AddHours(-1);
        Assert.Contains(slots, slot => slot.GetProperty("startDateTime").GetDateTime() == precedingStart);
        Assert.DoesNotContain(slots, slot => slot.GetProperty("startDateTime").GetDateTime().Hour == affected.Hour);
        Assert.Contains(slots, slot => slot.GetProperty("startDateTime").GetDateTime().Hour > affected.Hour);
        var clock = new SchedulingClock(_factory.Clock);
        var precedingTreatmentEnd = precedingStart.AddMinutes(50);
        Assert.True(clock.IsUnambiguousBlock(precedingStart, precedingTreatmentEnd, affected));
        Assert.False(clock.IsUnambiguousBlock(precedingStart, precedingTreatmentEnd, affected.AddTicks(1)));
        Assert.False(clock.IsUnambiguousBlock(precedingStart, precedingTreatmentEnd, precedingStart));
        foreach (var slot in slots)
        {
            var start = slot.GetProperty("startDateTime").GetDateTime();
            var reservedUntil = slot.GetProperty("reservedUntil").GetDateTime();
            for (var local = start; local < reservedUntil; local = local.AddMinutes(1))
            {
                Assert.False(zone.IsInvalidTime(local), $"An advertised occupied block contains nonexistent local time {local:O}.");
                Assert.False(zone.IsAmbiguousTime(local), $"An advertised occupied block contains repeated local time {local:O}.");
            }
            var lastOccupiedTick = reservedUntil.AddTicks(-1);
            Assert.False(zone.IsInvalidTime(lastOccupiedTick));
            Assert.False(zone.IsAmbiguousTime(lastOccupiedTick));
            Assert.Equal(TimeSpan.FromMinutes(60),
                TimeZoneInfo.ConvertTimeToUtc(lastOccupiedTick, zone).AddTicks(1) - TimeZoneInfo.ConvertTimeToUtc(start, zone));
        }
    }

    [Fact]
    public async Task FallTransitionKeepsMidnightSlotEndingExactlyAtRepeatedOneOClockBoundary()
    {
        var date = new DateTime(2026, 10, 24);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Hebron");
        Assert.True(zone.IsAmbiguousTime(date.AddHours(1)));
        _factory.Clock.UtcNow = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.AddDays(-1).AddHours(12), zone));
        await _factory.AddWindowAsync(day: (int)date.DayOfWeek, start: "00:00", end: "06:00");
        await _factory.MutateAsync(db =>
        {
            var hours = db.CenterWorkingHours.Single(h => h.CenterId == 201 && h.DayOfWeek == date.DayOfWeek);
            hours.OpeningTime = new TimeOnly(0, 0);
            hours.ClosingTime = new TimeOnly(6, 0);
        });

        var slots = (await QueryAsync("2026-10-24")).GetProperty("slots").EnumerateArray().ToArray();
        var midnightSlot = Assert.Single(slots, slot => slot.GetProperty("startDateTime").GetDateTime() == date);
        Assert.Equal(date.AddMinutes(50), midnightSlot.GetProperty("endDateTime").GetDateTime());
        Assert.Equal(date.AddHours(1), midnightSlot.GetProperty("reservedUntil").GetDateTime());
        Assert.DoesNotContain(slots, slot => slot.GetProperty("startDateTime").GetDateTime().Hour == 1);
    }

    [Fact]
    public async Task AvailabilityReadsNeverModifyOrCreateAppointments()
    {
        await _factory.AddWindowAsync();
        await _factory.AddAppointmentAsync("Pending", new DateTime(2026, 10, 18, 10, 0, 0));
        var before = await _factory.WithDbAsync(db => db.Appointments.AsNoTracking().Select(a => new { a.AppointmentId, a.StartDateTime, a.EndDateTime, a.Status, a.PriceAtBooking, a.PatientNotes, a.CreatedAt, a.UpdatedAt }).SingleAsync());
        await QueryAsync();
        await QueryAsync(service: 602);
        var after = await _factory.WithDbAsync(db => db.Appointments.AsNoTracking().Select(a => new { a.AppointmentId, a.StartDateTime, a.EndDateTime, a.Status, a.PriceAtBooking, a.PatientNotes, a.CreatedAt, a.UpdatedAt }).SingleAsync());
        Assert.Equal(before, after);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.Appointments.CountAsync()));
    }

    [Theory]
    [InlineData(99999, 601)]
    [InlineData(301, 99999)]
    public async Task MissingTherapistOrServiceReturnsNotFound(int therapistId, int serviceId)
    {
        using var client = _factory.Client();
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route(therapist: therapistId, service: serviceId)), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("?serviceId=0&date=2026-10-18")]
    [InlineData("?serviceId=-1&date=2026-10-18")]
    [InlineData("?date=2026-10-18")]
    [InlineData("?serviceId=601")]
    [InlineData("?serviceId=601&date=not-a-date")]
    [InlineData("?serviceId=601&date=2026-02-30")]
    public async Task MalformedOrIncompleteQueryIsRejected(string query)
    {
        using var client = _factory.Client();
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync("/api/Availability/therapists/301/slots" + query), HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Center", HttpStatusCode.Forbidden)]
    [InlineData("Therapist", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    public async Task AvailabilityRequiresPatientRole(string? role, HttpStatusCode expected)
    {
        using var client = _factory.Client(role);
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route()), expected);
    }

    [Theory]
    [InlineData("not-an-integer")]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task MalformedPatientIdentityIsUnauthorized(string userId)
    {
        using var client = _factory.Client("Patient", userId);
        await AvailabilityApiFactory.AssertStatusAsync(await client.GetAsync(Route()), HttpStatusCode.Unauthorized);
    }
}
