namespace MediCare.Web.ViewModels;

public class PatientDetailsViewModel
{
    public Guid Id { get; set; }
    public bool IsArchived { get; set; }
    public Guid Version { get; set; }
    public List<MediCare.Web.Models.PatientDocument> Documents { get; set; } = new();

    public string PatientNumber { get; set; }
        = string.Empty;

    public string FullName { get; set; }
        = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? EmergencyContactName { get; set; }

    public string? EmergencyContactPhone { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
