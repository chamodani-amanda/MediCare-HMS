using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace MediCare.Web.ViewModels;

public class PrescriptionForm : IValidatableObject
{
    public Guid? SourceId { get; set; }
    public Guid? MedicineId { get; set; }
    public decimal? AuthorizedQuantity { get; set; }
    [StringLength(60)] public string? DispensingUnit { get; set; }
    public bool ConfirmAuthorizationChange { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public Guid? LineageId { get; set; }
    [StringLength(200)] public string? Medication { get; set; }
    [StringLength(100)] public string? Dosage { get; set; }
    [StringLength(100)] public string? Route { get; set; }
    [StringLength(200)] public string? Frequency { get; set; }
    [StringLength(200)] public string? Duration { get; set; }
    [StringLength(1000)] public string? Instructions { get; set; }
    public bool IsEmpty => MedicineId == null && AuthorizedQuantity == null && string.IsNullOrWhiteSpace(DispensingUnit) && new[] { Medication, Dosage, Route, Frequency, Duration, Instructions }.All(string.IsNullOrWhiteSpace);
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!IsEmpty && new[] { Medication, Dosage, Route, Frequency, Duration }.Any(string.IsNullOrWhiteSpace))
            yield return new ValidationResult("Every prescription requires medication, dosage, route, frequency and duration.");
    }
}

public class MedicalRecordForm
{
    public Guid Id { get; set; }
    public Guid AppointmentId { get; set; }
    public Guid CreationToken { get; set; }
    public Guid Version { get; set; }
    [StringLength(4000)] public string? Symptoms { get; set; }
    [StringLength(8000)] public string? Observations { get; set; }
    [StringLength(4000)] public string? Diagnosis { get; set; }
    [StringLength(8000)] public string? Treatment { get; set; }
    [StringLength(1000)] public string? AmendmentReason { get; set; }
    public List<PrescriptionForm> Prescriptions { get; set; } = new();
    [ValidateNever] public string PatientLabel { get; set; } = "";
    [ValidateNever] public bool IsAmendment { get; set; }
    [ValidateNever] public List<Medicine> Medicines { get; set; } = new();
}

public class MedicalRecordList
{
    public Guid? PatientId { get; set; }
    public string? Search { get; set; }
    public DateOnly? Date { get; set; }
    public string? State { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
    public List<MedicalRecordSummary> Records { get; set; } = new();
}
public class MedicalRecordSummary
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string PatientLabel { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public int Number { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
}
public class MedicalRecordDetails
{
    public MedicalRecordRevision Revision { get; set; } = null!;
    public List<MedicalRecordSummary> History { get; set; } = new();
    public bool CanWrite { get; set; }
    public bool IsLatest { get; set; }
}
