using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MediCare.Web.ViewModels;

public class CarePage<T>
{
    public List<T> Rows { get; set; } = new();
    public string? Search { get; set; }
    public string? State { get; set; }
    public Guid? DoctorId { get; set; }
    public DateOnly? Date { get; set; }
    public int Page { get; set; }
    public int Pages { get; set; }
    public int Total { get; set; }
    public List<Doctor> Doctors { get; set; } = new();
}
public class CareRegistrationForm
{
    public Guid PatientId { get; set; }
    public Guid DoctorId { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid BedId { get; set; }
    public Guid Token { get; set; }
    [ValidateNever] public bool Inpatient { get; set; }
    [ValidateNever] public List<Patient> Patients { get; set; } = new();
    [ValidateNever] public List<Doctor> Doctors { get; set; } = new();
    [ValidateNever] public List<Bed> Beds { get; set; } = new();
    [ValidateNever] public List<Appointment> Appointments { get; set; } = new();
}
public class CareSummary
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PatientLabel { get; set; } = "";
    public string DoctorLabel { get; set; } = "";
    public string Department { get; set; } = "";
    public string State { get; set; } = "";
    public string? BedLabel { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public Guid Version { get; set; }
}
public class CareDetails
{
    public CareSummary Row { get; set; } = null!;
    public bool Inpatient { get; set; }
    public bool CanClinical { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? VisibleEmrRevisionId { get; set; }
    public AdmissionClinical? AdmissionClinical { get; set; }
    public OutpatientClinical? OutpatientClinical { get; set; }
    public List<BedTransfer> Transfers { get; set; } = new();
    public List<Bed> AvailableBeds { get; set; } = new();
    public List<CareSummary> History { get; set; } = new();
}
public class ClinicalStayForm
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    public Guid ParentVersion { get; set; }
    [StringLength(4000)] public string? AdmissionReason { get; set; }
    [StringLength(8000)] public string? DischargeSummary { get; set; }
    [StringLength(4000)] public string? FollowUpInformation { get; set; }
    public DateOnly? FollowUpOn { get; set; }
    public Guid? MedicalRecordId { get; set; }
    [ValidateNever] public bool Inpatient { get; set; }
    [ValidateNever] public List<MedicalRecordSummary> Records { get; set; } = new();
}
public class WardSummary
{
    public Ward Ward { get; set; } = null!;
    public int OperationalBeds { get; set; }
    public int OccupiedBeds { get; set; }
    public int AvailableBeds { get; set; }
}
public class FacilityForm
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    [Required,StringLength(40)] public string Code { get; set; } = "";
    [StringLength(120)] public string? Name { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid WardId { get; set; }
    public bool Active { get; set; } = true;
    [ValidateNever] public bool IsBed { get; set; }
    [ValidateNever] public List<Department> Departments { get; set; } = new();
    [ValidateNever] public List<Ward> Wards { get; set; } = new();
}
