using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MediCare.Web.ViewModels;

public class DepartmentFormViewModel
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(100)] public string Name { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class DoctorFormViewModel
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(50), Display(Name = "Medical registration number")]
    public string RegistrationNumber { get; set; } = "";
    [Required, StringLength(80), Display(Name = "First name")] public string FirstName { get; set; } = "";
    [Required, StringLength(80), Display(Name = "Last name")] public string LastName { get; set; } = "";
    [Required, StringLength(1000)] public string Qualifications { get; set; } = "";
    [Required, StringLength(120)] public string Specialization { get; set; } = "";
    [EmailAddress, StringLength(150)] public string? Email { get; set; }
    [Phone, StringLength(20), Display(Name = "Phone number")] public string? PhoneNumber { get; set; }
    [Display(Name = "Department")] public Guid DepartmentId { get; set; }
    [StringLength(450), Display(Name = "Linked Doctor account (optional)")] public string? IdentityUserId { get; set; }
    [Display(Name = "Active doctor")] public bool IsActive { get; set; } = true;
    [ValidateNever] public List<SelectListItem> Departments { get; set; } = new();
    [ValidateNever] public List<SelectListItem> Accounts { get; set; } = new();
}

public class DoctorDirectoryViewModel
{
    public List<Doctor> Doctors { get; set; } = new();
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalDoctors { get; set; }
    public bool IncludeInactive { get; set; }
}

public class DoctorProfileViewModel
{
    public Doctor Doctor { get; set; } = null!;
    public List<DoctorSchedule> Schedules { get; set; } = new();
}

public class DoctorScheduleFormViewModel : IValidatableObject
{
    public Guid Id { get; set; }
    public Guid DoctorId { get; set; }
    public Guid Version { get; set; }
    public Guid DoctorVersion { get; set; }
    [EnumDataType(typeof(DayOfWeek)), Display(Name = "Day")] public DayOfWeek DayOfWeek { get; set; }
    [Required, DataType(DataType.Time), Display(Name = "Start time")] public TimeOnly? StartTime { get; set; }
    [Required, DataType(DataType.Time), Display(Name = "End time")] public TimeOnly? EndTime { get; set; }
    [Display(Name = "Active schedule")] public bool IsActive { get; set; } = true;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (StartTime.HasValue && EndTime.HasValue && StartTime >= EndTime)
            yield return new ValidationResult("End time must be after start time. Split overnight shifts into separate days.", new[] { nameof(EndTime) });
    }
}
