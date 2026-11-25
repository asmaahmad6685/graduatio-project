using System.Security.Claims;
using graduatio_project.DTOs.Assessment;
using graduatio_project.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace graduatio_project.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Patient")]
    public sealed class AssessmentController : ControllerBase
    {
        private readonly AssessmentService _service;

        // Wires the assessment service that scores and stores submissions.
        public AssessmentController(AssessmentService service)
        {
            _service = service;
        }

        // POST api/assessment: scores a submitted pain assessment and stores it for the patient.
        [HttpPost]
        public async Task<IActionResult> Submit(AssessmentSubmissionDto submission, CancellationToken cancellationToken)
        {
            var (patientId, identityError) = await GetPatientIdentityAsync(cancellationToken);
            if (identityError != null)
            {
                return identityError;
            }

            try
            {
                var result = await _service.SubmitAsync(patientId!.Value, submission, cancellationToken);
                return CreatedAtAction(nameof(GetDetails), new { assessmentId = result.AssessmentId }, result);
            }
            catch (AssessmentSubmissionValidationException exception)
            {
                return BadRequest(new ValidationProblemDetails(exception.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = exception.Message
                });
            }
            catch (AssessmentDefinitionConflictException exception)
            {
                return Conflict(new { message = exception.Message });
            }
        }

        // GET api/assessment/history: saved assessments of the patient, newest first.
        [HttpGet("history")]
        public async Task<IActionResult> GetHistory(CancellationToken cancellationToken)
        {
            var (patientId, identityError) = await GetPatientIdentityAsync(cancellationToken);
            if (identityError != null)
            {
                return identityError;
            }

            return Ok(await _service.GetHistoryAsync(patientId!.Value, cancellationToken));
        }

        // GET api/assessment/{assessmentId}: one saved assessment with its result and advice.
        [HttpGet("{assessmentId:int}")]
        public async Task<IActionResult> GetDetails(int assessmentId, CancellationToken cancellationToken)
        {
            var (patientId, identityError) = await GetPatientIdentityAsync(cancellationToken);
            if (identityError != null)
            {
                return identityError;
            }

            var result = await _service.GetDetailsAsync(patientId!.Value, assessmentId, cancellationToken);
            return result == null ? NotFound(new { message = "Assessment not found." }) : Ok(result);
        }

        // Patient id behind the token, or the error response the caller should return.
        private async Task<(int? PatientId, IActionResult? Error)> GetPatientIdentityAsync(CancellationToken cancellationToken)
        {
            // NameIdentifier contains UserId; it is never used as PatientId.
            if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId <= 0)
            {
                return (null, Unauthorized(new { message = "Invalid token identity." }));
            }

            var patientId = await _service.ResolvePatientIdAsync(userId, cancellationToken);
            return patientId == null
                ? (null, NotFound(new { message = "Patient profile not found." }))
                : (patientId, null);
        }
    }
}
