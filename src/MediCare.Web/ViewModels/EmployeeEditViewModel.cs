using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class EmployeeEditViewModel
{
    public Guid Id { get; set; }

    public Guid Version { get; set; }

    public string EmployeeNumber { get; set; } = "";

    [Required]
    [StringLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = "";

    [Required]
    [StringLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = "";

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

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Hire Date")]
    public DateOnly? HireDate { get; set; }

    [Display(Name = "Department")]
    public Guid? DepartmentId { get; set; }
}