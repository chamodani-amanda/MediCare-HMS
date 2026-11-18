using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class StaffAccountCreateViewModel
{
    public static IReadOnlyList<string> AssignableRoles { get; } =
        Array.AsReadOnly(new[]
        {
            "Doctor",
            "Nurse",
            "Receptionist",
            "Laboratory Staff",
            "Pharmacist",
            "Accountant"
        });

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Enter a valid email.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [MinLength(12, ErrorMessage =
        "Password must have at least 12 characters.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm your password.")]
    [Compare(nameof(Password),
        ErrorMessage = "Passwords do not match.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a hospital role.")]
    public string Role { get; set; } = string.Empty;
}