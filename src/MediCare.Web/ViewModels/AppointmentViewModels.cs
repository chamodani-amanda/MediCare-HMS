using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MediCare.Web.ViewModels;

public class AppointmentFormViewModel : IValidatableObject
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    public Guid BookingToken { get; set; }
    [Display(Name = "Patient")] public Guid PatientId { get; set; }
    [Display(Name = "Doctor")] public Guid DoctorId { get; set; }
    [Required, DataType(DataType.Date)] public DateOnly? Date { get; set; }
    [Required, DataType(DataType.Time), Display(Name = "Start time")] public TimeOnly? StartTime { get; set; }
    [Range(15, 60), Display(Name = "Duration (minutes)")] public int DurationMinutes { get; set; } = 30;
    [ValidateNever] public List<SelectListItem> Patients { get; set; } = new();
    [ValidateNever] public List<SelectListItem> Doctors { get; set; } = new();
    public string? PatientSearch { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!Services.AppointmentRules.ValidDuration(DurationMinutes)) yield return new ValidationResult("Choose 15, 30, 45 or 60 minutes.", new[] { nameof(DurationMinutes) });
        if (StartTime.HasValue && !Services.AppointmentRules.ValidStart(StartTime.Value)) yield return new ValidationResult("Start time must use a 15-minute interval.", new[] { nameof(StartTime) });
    }
}

public class AppointmentRowViewModel
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public string PatientName { get; set; } = "";
    public string PatientNumber { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public AppointmentStatus Status { get; set; }
    public Guid Version { get; set; }
    public string? CancellationReason { get; set; }
}

public class AppointmentListViewModel
{
    public List<AppointmentRowViewModel> Appointments { get; set; } = new();
    public string Search { get; set; } = "";
    public Guid? DoctorId { get; set; }
    public DateOnly? Date { get; set; }
    public AppointmentStatus? Status { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int TotalAppointments { get; set; }
    public List<SelectListItem> Doctors { get; set; } = new();
}

public class AppointmentChangeViewModel
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    [EnumDataType(typeof(AppointmentStatus))] public AppointmentStatus Status { get; set; }
    [StringLength(500), Display(Name = "Cancellation reason")] public string? CancellationReason { get; set; }
    [ValidateNever] public AppointmentRowViewModel Appointment { get; set; } = null!;
}

public class AppointmentAvailabilityViewModel
{
    public Guid DoctorId { get; set; }
    [Required, DataType(DataType.Date)] public DateOnly? Date { get; set; }
    [Range(15,60), Display(Name = "Duration (minutes)")] public int DurationMinutes { get; set; } = 30;
    public string DoctorName { get; set; } = "";
    public List<SelectListItem> Doctors { get; set; } = new();
    public List<TimeOnly> AvailableStarts { get; set; } = new();
}

public class AppointmentCalendarViewModel
{
    public DateOnly Date { get; set; }
    public Guid? DoctorId { get; set; }
    public List<SelectListItem> Doctors { get; set; } = new();
    public List<AppointmentRowViewModel> Appointments { get; set; } = new();
}
