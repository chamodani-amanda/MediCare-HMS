using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class PatientCreateViewModel : IValidatableObject
{
    [Required(ErrorMessage = "First name is required.")]
    [StringLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(80)]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Date of birth is required.")]
    [DataType(DataType.Date)]
    public DateOnly? DateOfBirth { get; set; }

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

    public IEnumerable<ValidationResult> Validate(
        ValidationContext validationContext)
    {
        if (DateOfBirth.HasValue &&
            DateOfBirth.Value > DateOnly.FromDateTime(DateTime.Today))
        {
            yield return new ValidationResult(
                "Date of birth cannot be in the future.",
                new[] { nameof(DateOfBirth) });
        }
    }
}