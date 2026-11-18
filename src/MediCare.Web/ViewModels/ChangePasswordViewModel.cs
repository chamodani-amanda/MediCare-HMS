using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class ChangePasswordViewModel
{
    [Required(ErrorMessage = "Current password is required.")]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "New password is required.")]
    [MinLength(12, ErrorMessage =
        "Password must contain at least 12 characters.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your new password.")]
    [Compare(nameof(NewPassword),
        ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}