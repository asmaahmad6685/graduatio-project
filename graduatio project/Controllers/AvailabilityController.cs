using System.Security.Claims;
using graduatio_project.DTOs.Availability;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace graduatio_project.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Patient")]
public sealed class AvailabilityController(AvailabilityService service) : ControllerBase
{
    [HttpGet("therapists/{therapistId:int}/slots")]
    public async Task<IActionResult> GetSlots(int therapistId, [FromQuery] AvailabilityQueryDto query,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            return Unauthorized(new { message = "Invalid token identity." });

        try
        {
            return Ok(await service.GetAsync(therapistId, query.ServiceId!.Value, query.Date!.Value, cancellationToken));
        }
        catch (AvailabilityValidationException exception)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["Availability"] = [exception.Message]
            }) { Status = StatusCodes.Status400BadRequest, Title = "Invalid availability request." });
        }
        catch (AvailabilityNotFoundException exception)
        {
            return NotFound(new { message = exception.Message });
        }
    }
}
