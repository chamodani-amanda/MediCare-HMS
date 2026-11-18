using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [StringLength(20)]
    public string PatientNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [StringLength(80)]
    public string LastName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    [StringLength(30)]
    public string? Gender { get; set; }

    [Phone]
    [StringLength(20)]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    [StringLength(150)]
    public string? Email { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    [StringLength(160)]
    public string? EmergencyContactName { get; set; }

    [Phone]
    [StringLength(20)]
    public string? EmergencyContactPhone { get; set; }

    public bool IsArchived { get; set; } = false;

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;
}