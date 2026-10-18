using graduatio_project.DTOs.Appointments;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace graduatio_project.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Center")]
public sealed class CenterAppointmentsController(AppointmentService service) : AppointmentControllerBase
{
    [HttpGet]
    public Task<IActionResult> List(CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.ListCenterAsync(userId, cancellationToken));

    [HttpGet("{appointmentId:int}")]
    public Task<IActionResult> Get(int appointmentId, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.GetCenterAsync(userId, appointmentId, cancellationToken));

    [HttpPatch("{appointmentId:int}/confirm")]
    public Task<IActionResult> Confirm(int appointmentId, CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.ConfirmAsync(userId, appointmentId, cancellationToken));

    [HttpPatch("{appointmentId:int}/reject")]
    public Task<IActionResult> Reject(int appointmentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] RejectionReasonDto? request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.RejectAsync(userId, appointmentId,
            request ?? new RejectionReasonDto(), cancellationToken));

    [HttpPatch("{appointmentId:int}/cancel")]
    public Task<IActionResult> Cancel(int appointmentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancellationReasonDto? request,
        CancellationToken cancellationToken) =>
        ExecuteAsync(userId => service.CancelCenterAsync(userId, appointmentId,
            request ?? new CancellationReasonDto(), cancellationToken));
}
