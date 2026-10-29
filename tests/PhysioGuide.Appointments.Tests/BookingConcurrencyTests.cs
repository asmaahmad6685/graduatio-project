using System.Net;
using System.Net.Http.Json;
using graduatio_project.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PhysioGuide.Appointments.Tests;

public sealed class BookingConcurrencyTests : IAsyncLifetime
{
    private readonly AppointmentApiFactory _factory = new();
    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task ConcurrentDifferentPatientsCannotDoubleBookTherapist()
    {
        using var first = _factory.Client();
        using var second = _factory.Client("Patient", "122");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Attempt(HttpClient client)
        {
            await gate.Task;
            return await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking());
        }
        var pending = new[] { Attempt(first), Attempt(second) };
        gate.SetResult();
        var responses = await Task.WhenAll(pending);
        try
        {
            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).Order());
            Assert.Equal(1, await _factory.WithDbAsync(db => db.Appointments.CountAsync()));
            Assert.Equal(1, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ConcurrentSamePatientCannotBookOverlappingAppointmentsAcrossCentersAndTherapists()
    {
        using var first = _factory.Client();
        using var second = _factory.Client();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Attempt(HttpClient client, object booking)
        {
            await gate.Task;
            return await client.PostAsJsonAsync("/api/Appointments", booking);
        }
        var pending = new[]
        {
            Attempt(first, AppointmentApiFactory.Booking()),
            Attempt(second, AppointmentApiFactory.Booking(therapist: 302, service: 604, center: 202))
        };
        gate.SetResult();
        var responses = await Task.WhenAll(pending);
        try
        {
            Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).Order());
            Assert.Equal(1, await _factory.WithDbAsync(db => db.Appointments.CountAsync()));
            Assert.Equal(1, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ScheduleDeletionAndBookingUseCompatibleCoordinationAndCannotLeaveInvalidBooking()
    {
        var scheduleId = await _factory.WithDbAsync(db => db.TherapistSchedules.Where(s => s.TherapistId == 301 && s.DayOfWeek == DayOfWeek.Sunday).Select(s => s.ScheduleId).SingleAsync());
        using var patient = _factory.Client();
        using var center = _factory.Client("Center");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Book()
        {
            await gate.Task;
            return await patient.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking());
        }
        async Task<HttpResponseMessage> Delete()
        {
            await gate.Task;
            return await center.DeleteAsync($"/api/Schedules/therapists/301/{scheduleId}");
        }
        var book = Book();
        var delete = Delete();
        gate.SetResult();
        using var booked = await book;
        using var deleted = await delete;
        var exists = await _factory.WithDbAsync(db => db.TherapistSchedules.AnyAsync(s => s.ScheduleId == scheduleId));
        var count = await _factory.WithDbAsync(db => db.Appointments.CountAsync());
        Assert.True(
            (booked.StatusCode == HttpStatusCode.Created && deleted.StatusCode == HttpStatusCode.Conflict && exists && count == 1) ||
            (booked.StatusCode == HttpStatusCode.Conflict && deleted.StatusCode == HttpStatusCode.NoContent && !exists && count == 0),
            $"Inconsistent outcome: booking={(int)booked.StatusCode} ({await booked.Content.ReadAsStringAsync()}), deletion={(int)deleted.StatusCode}, windowExists={exists}, appointments={count}");
    }

    [Fact]
    public async Task ConcurrentRescheduleAndBookingCannotOccupySameTargetInterval()
    {
        var original = await _factory.AddAppointmentAsync(start: new DateTime(2026, 10, 18, 9, 0, 0));
        using var owner = _factory.Client();
        using var another = _factory.Client("Patient", "122");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<HttpResponseMessage> Reschedule()
        {
            await gate.Task;
            return await owner.PatchAsJsonAsync($"/api/Appointments/{original}/reschedule", new { therapistId = 303, startDateTime = "2026-10-18T11:00:00" });
        }
        async Task<HttpResponseMessage> Book()
        {
            await gate.Task;
            return await another.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking("2026-10-18T11:00:00", therapist: 303));
        }
        var moved = Reschedule(); var booked = Book();
        gate.SetResult();
        using var moveResponse = await moved;
        using var bookResponse = await booked;
        Assert.True((moveResponse.StatusCode == HttpStatusCode.OK && bookResponse.StatusCode == HttpStatusCode.Conflict) ||
                    (moveResponse.StatusCode == HttpStatusCode.Conflict && bookResponse.StatusCode == HttpStatusCode.Created));
        var target = new DateTime(2026, 10, 18, 11, 0, 0);
        Assert.Equal(1, await _factory.WithDbAsync(db => db.Appointments.CountAsync(a => a.TherapistId == 303 && a.StartDateTime == target && a.Status == "Pending")));
    }
}
