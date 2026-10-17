using System.Security.Claims;
using graduatio_project.DTOs.Schedules;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace graduatio_project.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Center")]
public sealed class SchedulesController : ControllerBase
{
    private readonly ScheduleService _service;

    public SchedulesController(ScheduleService service)
    {
        _service = service;
    }

    [HttpGet("therapists/{therapistId:int}")]
    public async Task<IActionResult> GetWeeklySchedule(int therapistId, CancellationToken cancellationToken)
    {
        var (centerId, error) = await ResolveCenterAsync(cancellationToken);
        if (error != null)
            return error;

        var schedule = await _service.GetAsync(centerId!.Value, therapistId, cancellationToken);
        return schedule == null ? ResourceNotFound() : Ok(schedule);
    }

    [HttpPost("therapists/{therapistId:int}")]
    public async Task<IActionResult> CreateWindow(int therapistId, ScheduleWindowRequestDto request,
        CancellationToken cancellationToken)
    {
        var (centerId, error) = await ResolveCenterAsync(cancellationToken);
        if (error != null)
            return error;

        try
        {
            var window = await _service.CreateAsync(centerId!.Value, therapistId, request, cancellationToken);
            return window == null ? ResourceNotFound()
                : CreatedAtAction(nameof(GetWeeklySchedule), new { therapistId }, window);
        }
        catch (SchedulingValidationException exception)
        {
            return InvalidSchedule(exception);
        }
        catch (SchedulingConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpPut("therapists/{therapistId:int}/{scheduleId:int}")]
    public async Task<IActionResult> UpdateWindow(int therapistId, int scheduleId, ScheduleWindowRequestDto request,
        CancellationToken cancellationToken)
    {
        var (centerId, error) = await ResolveCenterAsync(cancellationToken);
        if (error != null)
            return error;

        try
        {
            var window = await _service.UpdateAsync(centerId!.Value, therapistId, scheduleId, request, cancellationToken);
            return window == null ? ResourceNotFound() : Ok(window);
        }
        catch (SchedulingValidationException exception)
        {
            return InvalidSchedule(exception);
        }
        catch (SchedulingConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    [HttpDelete("therapists/{therapistId:int}/{scheduleId:int}")]
    public async Task<IActionResult> DeleteWindow(int therapistId, int scheduleId, CancellationToken cancellationToken)
    {
        var (centerId, error) = await ResolveCenterAsync(cancellationToken);
        if (error != null)
            return error;

        try
        {
            return await _service.DeleteAsync(centerId!.Value, therapistId, scheduleId, cancellationToken)
                ? NoContent() : ResourceNotFound();
        }
        catch (SchedulingConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }

    private async Task<(int? CenterId, IActionResult? Error)> ResolveCenterAsync(CancellationToken cancellationToken)
    {
        // The JWT identifies the User, so resolve the linked Center instead of using UserId as CenterId.
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return (null, Unauthorized(new { message = "Invalid token identity." }));

        var centerId = await _service.ResolveCenterIdAsync(userId, cancellationToken);
        return centerId == null ? (null, NotFound(new { message = "Center profile not found." })) : (centerId, null);
    }

    private BadRequestObjectResult InvalidSchedule(SchedulingValidationException exception) =>
        BadRequest(new ValidationProblemDetails(exception.Errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = exception.Message
        });

    private NotFoundObjectResult ResourceNotFound() => NotFound(new { message = "Therapist or schedule window not found." });
}
