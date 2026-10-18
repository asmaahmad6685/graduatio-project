using graduatio_project.DTOs.Appointments;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace graduatio_project.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Patient")]
public sealed class AppointmentsController(AppointmentService service) : AppointmentControllerBase
{
    [HttpPost]
    public Task<IActionResult> Create(CreateAppointmentDto request, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.CreateAsync(userId, request, cancellationToken),
            appointment => CreatedAtAction(nameof(Get), new { appointmentId = appointment.AppointmentId }, appointment));

    [HttpGet]
    public Task<IActionResult> List(CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.ListPatientAsync(userId, cancellationToken));

    [HttpGet("{appointmentId:int}")]
    public Task<IActionResult> Get(int appointmentId, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.GetPatientAsync(userId, appointmentId, cancellationToken));

    [HttpPatch("{appointmentId:int}/cancel")]
    public Task<IActionResult> Cancel(int appointmentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancellationReasonDto? request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.CancelPatientAsync(userId, appointmentId,
            request ?? new CancellationReasonDto(), cancellationToken));

    [HttpPatch("{appointmentId:int}/reschedule")]
    public Task<IActionResult> Reschedule(int appointmentId, RescheduleAppointmentDto request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.RescheduleAsync(userId, appointmentId, request, cancellationToken));
}
