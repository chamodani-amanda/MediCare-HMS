using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class EmployeeCreateViewModel
{
    [Required(ErrorMessage = "Employee number is required.")]
    [StringLength(30, MinimumLength = 3)]
    [Display(Name = "Employee Number")]
    public string EmployeeNumber { get; set; }
        = string.Empty;

    [Required(ErrorMessage = "First name is required.")]
    [StringLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; }
        = string.Empty;

    [Required(ErrorMessage = "Last name is required.")]
    [StringLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; }
        = string.Empty;

    [StringLength(100)]
    [Display(Name = "Job Title")]
    public string? JobTitle { get; set; }

    [Phone]
    [StringLength(30)]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    [StringLength(256)]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Hire date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Hire Date")]
    public DateOnly? HireDate { get; set; }

    [Display(Name = "Department")]
    public Guid? DepartmentId { get; set; }
}