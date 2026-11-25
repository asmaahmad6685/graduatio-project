using System.Security.Claims;
using graduatio_project.Services;
using Microsoft.AspNetCore.Mvc;

namespace graduatio_project.Controllers;

public abstract class AppointmentControllerBase : ControllerBase
{
    protected async Task<IActionResult> ExecuteAsync<T>(Func<int, Task<T>> action,
        Func<T, IActionResult>? success = null)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized(new { message = "Invalid token identity." });

        try
        {
            var result = await action(userId);
            return success == null ? Ok(result) : success(result);
        }
        catch (AppointmentAccessException exception)
        {
            return StatusCode(exception.StatusCode, new { message = exception.Message });
        }
        catch (AppointmentValidationException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Appointment"] = [exception.Message]
            }) { Status = StatusCodes.Status400BadRequest, Title = "Invalid appointment request." });
        }
        catch (AppointmentConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (SchedulingConflictException exception)
        {
            return Conflict(new { message = exception.Message });
        }
    }
}
