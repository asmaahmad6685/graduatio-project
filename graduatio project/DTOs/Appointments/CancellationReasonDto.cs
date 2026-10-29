using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace graduatio_project.DTOs.Appointments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CancellationReasonDto
{
    private string? _cancellationReason;

    [StringLength(500)]
    public string? CancellationReason
    {
        get => _cancellationReason;
        set => _cancellationReason = value?.Trim();
    }
}
