using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class StaffPasswordResetViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessage = "Temporary password is required.")]
    [MinLength(12, ErrorMessage =
        "Password must contain at least 12 characters.")]
    [DataType(DataType.Password)]
    public string TemporaryPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm the temporary password.")]
    [Compare(nameof(TemporaryPassword),
        ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}