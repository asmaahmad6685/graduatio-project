using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace graduatio_project.DTOs.Appointments;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RejectionReasonDto
{
    private string? _rejectionReason;

    [StringLength(500)]
    public string? RejectionReason
    {
        get => _rejectionReason;
        set => _rejectionReason = value?.Trim();
    }
}
