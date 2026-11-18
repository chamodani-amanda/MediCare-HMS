using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class SecurityAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(64)]
    public string Action { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string ActorUserId { get; set; } = string.Empty;

    [Required]
    [StringLength(450)]
    public string TargetUserId { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public DateTime OccurredAtUtc { get; set; } =
        DateTime.UtcNow;
}