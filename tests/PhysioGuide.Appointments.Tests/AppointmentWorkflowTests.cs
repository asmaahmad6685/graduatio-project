using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PhysioGuide.Appointments.Tests;

public sealed class AppointmentWorkflowTests : IAsyncLifetime
{
    private readonly AppointmentApiFactory _factory = new();
    public Task InitializeAsync() => _factory.InitializeDatabaseAsync();
    public Task DisposeAsync() => _factory.DisposeAsync().AsTask();

    [Fact]
    public async Task PatientAndCenterListsAndDetailsOnlyExposeOwnedAppointments()
    {
        var own = await _factory.AddAppointmentAsync();
        var otherPatient = await _factory.AddAppointmentAsync(patient: 402, therapist: 303);
        var otherCenter = await _factory.AddAppointmentAsync(patient: 402, therapist: 302, service: 604, center: 202);
        using var patient = _factory.Client();
        using var center = _factory.Client("Center");
        using var patientList = await patient.GetAsync("/api/Appointments");
        using var centerList = await center.GetAsync("/api/CenterAppointments");
        Assert.Equal(HttpStatusCode.OK, patientList.StatusCode);
        Assert.Equal(HttpStatusCode.OK, centerList.StatusCode);
        Assert.Equal(own, Assert.Single((await AppointmentApiFactory.JsonAsync(patientList)).EnumerateArray()).GetProperty("appointmentId").GetInt32());
        Assert.Equal(new[] { own, otherPatient }.Order(), (await AppointmentApiFactory.JsonAsync(centerList)).EnumerateArray().Select(x => x.GetProperty("appointmentId").GetInt32()).Order());
        await AppointmentApiFactory.AssertStatusAsync(await patient.GetAsync($"/api/Appointments/{own}"), HttpStatusCode.OK);
        await AppointmentApiFactory.AssertStatusAsync(await patient.GetAsync($"/api/Appointments/{otherPatient}"), HttpStatusCode.NotFound);
        await AppointmentApiFactory.AssertStatusAsync(await center.GetAsync($"/api/CenterAppointments/{own}"), HttpStatusCode.OK);
        await AppointmentApiFactory.AssertStatusAsync(await center.GetAsync($"/api/CenterAppointments/{otherCenter}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Patient", "122", "cancel")]
    [InlineData("Patient", "122", "reschedule")]
    [InlineData("Center", "102", "confirm")]
    [InlineData("Center", "102", "reject")]
    [InlineData("Center", "102", "cancel")]
    public async Task ForeignActionsAreIndistinguishableFromMissingResources(string role, string userId, string action)
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client(role, userId);
        var route = role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments";
        var request = action == "reschedule" ? (object)new { therapistId = 303, startDateTime = "2026-10-18T11:00:00" } : new { };
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"{route}/{id}/{action}", request), HttpStatusCode.NotFound);
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"{route}/99999/{action}", request), HttpStatusCode.NotFound);
        Assert.Equal("Pending", await _factory.WithDbAsync(async db => (await db.Appointments.FindAsync(id))!.Status));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Therapist", HttpStatusCode.Forbidden)]
    [InlineData("Admin", HttpStatusCode.Forbidden)]
    public async Task PatientAndCenterEndpointsEnforceTheirRole(string? role, HttpStatusCode expected)
    {
        using var client = _factory.Client(role);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync("/api/Appointments"), expected);
        await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), expected);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync("/api/Appointments/1"), expected);
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync("/api/Appointments/1/cancel", new { }), expected);
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync("/api/Appointments/1/reschedule", new { therapistId = 301, startDateTime = "2026-10-18T11:00:00" }), expected);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync("/api/CenterAppointments"), expected);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync("/api/CenterAppointments/1"), expected);
        foreach (var action in new[] { "confirm", "reject", "cancel" })
            await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/CenterAppointments/1/{action}", new { }), expected);
    }

    [Fact]
    public async Task PatientAndCenterCannotUseEachOthersManagementEndpoints()
    {
        using var patient = _factory.Client();
        using var center = _factory.Client("Center");
        await AppointmentApiFactory.AssertStatusAsync(await patient.GetAsync("/api/CenterAppointments"), HttpStatusCode.Forbidden);
        await AppointmentApiFactory.AssertStatusAsync(await patient.PatchAsJsonAsync("/api/CenterAppointments/1/confirm", new { }), HttpStatusCode.Forbidden);
        await AppointmentApiFactory.AssertStatusAsync(await center.GetAsync("/api/Appointments"), HttpStatusCode.Forbidden);
        await AppointmentApiFactory.AssertStatusAsync(await center.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Patient", "bad")]
    [InlineData("Patient", "0")]
    [InlineData("Center", "-1")]
    public async Task InvalidJwtIdentityIsUnauthorized(string role, string userId)
    {
        using var client = _factory.Client(role, userId);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync(role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments"), HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Patient", "123")]
    [InlineData("Center", "103")]
    public async Task MissingDomainProfileReturnsNotFound(string role, string userId)
    {
        using var client = _factory.Client(role, userId);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync(role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("Patient", 121)]
    [InlineData("Center", 101)]
    public async Task InactiveAuthenticatedUserCannotReadOrPerformActions(string role, int userId)
    {
        var id = await _factory.AddAppointmentAsync();
        await _factory.MutateAsync(db => db.Users.Single(u => u.UserId == userId).IsActive = false);
        using var client = _factory.Client(role, userId.ToString());
        var route = role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments";
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync(route), HttpStatusCode.Forbidden);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync($"{route}/{id}"), HttpStatusCode.Forbidden);
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"{route}/{id}/cancel", new { }), HttpStatusCode.Forbidden);
        if (role == "Patient")
            await AppointmentApiFactory.AssertStatusAsync(await client.PostAsJsonAsync(route, AppointmentApiFactory.Booking()), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("ACTIVE")]
    [InlineData("Pending")]
    [InlineData("Rejected")]
    [InlineData("Suspended")]
    public async Task IneligibleCenterCannotReadOrManageAppointments(string status)
    {
        var id = await _factory.AddAppointmentAsync();
        await _factory.MutateAsync(db => db.Centers.Single(c => c.CenterId == 201).Status = status);
        using var client = _factory.Client("Center");
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync("/api/CenterAppointments"), HttpStatusCode.Forbidden);
        await AppointmentApiFactory.AssertStatusAsync(await client.GetAsync($"/api/CenterAppointments/{id}"), HttpStatusCode.Forbidden);
        foreach (var action in new[] { "confirm", "reject", "cancel" })
            await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/CenterAppointments/{id}/{action}", new { }), HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  Travel changed  ")]
    public async Task PatientCancelsOnlyOwnPendingFutureAppointmentWithOptionalTrimmedReason(string? reason)
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/cancel", new { cancellationReason = reason }), HttpStatusCode.OK);
        await _factory.WithDbAsync(async db =>
        {
            var row = await db.Appointments.FindAsync(id);
            Assert.Equal("Cancelled", row!.Status);
            Assert.Equal(reason?.Trim(), row.CancellationReason);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, row.UpdatedAt);
            var history = await db.AppointmentStatusHistories.SingleAsync();
            Assert.Equal("Pending", history.OldStatus);
            Assert.Equal("Cancelled", history.NewStatus);
            Assert.Equal(121, history.ChangedByUserId);
            return true;
        });
        // No deletion: another patient can now book the released therapist slot.
        using var second = _factory.Client("Patient", "122");
        await AppointmentApiFactory.AssertStatusAsync(await second.PostAsJsonAsync("/api/Appointments", AppointmentApiFactory.Booking()), HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("Confirmed", "2026-10-18T10:00:00")]
    [InlineData("Cancelled", "2026-10-18T10:00:00")]
    [InlineData("Rejected", "2026-10-18T10:00:00")]
    [InlineData("Completed", "2026-10-18T10:00:00")]
    [InlineData("Pending", "2026-10-17T09:00:00")]
    [InlineData("Pending", "2026-10-17T09:30:00")]
    public async Task PatientCannotCancelOrRescheduleStartedOrNonPendingAppointment(string status, string start)
    {
        var id = await _factory.AddAppointmentAsync(status, DateTime.Parse(start));
        using var client = _factory.Client();
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/cancel", new { }), HttpStatusCode.Conflict);
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/Appointments/{id}/reschedule", new { therapistId = 303, startDateTime = "2026-10-18T11:00:00" }), HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("confirm", "Pending", "Confirmed", null)]
    [InlineData("reject", "Pending", "Rejected", null)]
    [InlineData("reject", "Pending", "Rejected", "  Unavailable  ")]
    [InlineData("cancel", "Pending", "Cancelled", null)]
    [InlineData("cancel", "Confirmed", "Cancelled", "  Center closed  ")]
    public async Task CenterApprovedTransitionsWriteHistoryAndUtcAudit(string action, string from, string to, string? reason)
    {
        var id = await _factory.AddAppointmentAsync(from);
        using var client = _factory.Client("Center");
        object body = action == "reject" ? new { rejectionReason = reason } : new { cancellationReason = reason };
        if (action == "confirm") body = new { };
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/CenterAppointments/{id}/{action}", body), HttpStatusCode.OK);
        await _factory.WithDbAsync(async db =>
        {
            var appointment = await db.Appointments.SingleAsync();
            Assert.Equal(to, appointment.Status);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, appointment.UpdatedAt);
            if (action == "reject") Assert.Equal(reason?.Trim(), appointment.RejectionReason);
            if (action == "cancel") Assert.Equal(reason?.Trim(), appointment.CancellationReason);
            var history = await db.AppointmentStatusHistories.SingleAsync();
            Assert.Equal(from, history.OldStatus);
            Assert.Equal(to, history.NewStatus);
            Assert.Equal(101, history.ChangedByUserId);
            Assert.Equal(_factory.Clock.UtcNow.UtcDateTime, history.ChangedAt);
            Assert.Empty(await db.Notifications.ToArrayAsync());
            return true;
        });
    }

    [Theory]
    [InlineData("confirm", "Confirmed")]
    [InlineData("confirm", "Cancelled")]
    [InlineData("reject", "Confirmed")]
    [InlineData("reject", "Rejected")]
    [InlineData("cancel", "Rejected")]
    [InlineData("cancel", "Cancelled")]
    [InlineData("cancel", "Completed")]
    public async Task CenterInvalidTransitionsAreRejectedWithoutHistory(string action, string status)
    {
        var id = await _factory.AddAppointmentAsync(status);
        using var client = _factory.Client("Center");
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"/api/CenterAppointments/{id}/{action}", new { }), HttpStatusCode.Conflict);
        Assert.Equal(status, await _factory.WithDbAsync(async db => (await db.Appointments.FindAsync(id))!.Status));
        Assert.Equal(0, await _factory.WithDbAsync(db => db.AppointmentStatusHistories.CountAsync()));
    }

    [Theory]
    [InlineData("Patient", "cancel", 500, true)]
    [InlineData("Patient", "cancel", 501, false)]
    [InlineData("Center", "cancel", 500, true)]
    [InlineData("Center", "cancel", 501, false)]
    [InlineData("Center", "reject", 500, true)]
    [InlineData("Center", "reject", 501, false)]
    public async Task ReasonLengthMatchesExistingModel(string role, string action, int length, bool valid)
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client(role);
        object body = action == "reject" ? new { rejectionReason = new string('r', length) } : new { cancellationReason = new string('r', length) };
        var route = role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments";
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsJsonAsync($"{route}/{id}/{action}", body), valid ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("Patient", "cancel")]
    [InlineData("Center", "cancel")]
    [InlineData("Center", "reject")]
    public async Task OptionalReasonsMayBeOmittedWithNoBody(string role, string action)
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client(role);
        var route = role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments";
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsync($"{route}/{id}/{action}", null), HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("Patient", "cancel")]
    [InlineData("Center", "cancel")]
    [InlineData("Center", "reject")]
    public async Task ActionBodiesRejectUnexpectedOwnershipOrStatusFields(string role, string action)
    {
        var id = await _factory.AddAppointmentAsync();
        using var client = _factory.Client(role);
        var route = role == "Patient" ? "/api/Appointments" : "/api/CenterAppointments";
        await AppointmentApiFactory.AssertStatusAsync(await client.PatchAsync($"{route}/{id}/{action}", new StringContent("{\"status\":\"Completed\",\"patientId\":402}", Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
    }
}
