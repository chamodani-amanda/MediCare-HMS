using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace MediCare.Web.ViewModels;

public class LabTestForm : IValidatableObject
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    [Required, StringLength(40), RegularExpression("^[A-Za-z0-9][A-Za-z0-9_.-]{0,39}$")] public string Code { get; set; } = "";
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string Category { get; set; } = "";
    [StringLength(2000)] public string? Description { get; set; }
    [Required, StringLength(100), Display(Name="Sample type")] public string SampleType { get; set; } = "";
    [EnumDataType(typeof(LabResultKind)), Display(Name="Result format")] public LabResultKind ResultKind { get; set; }
    [StringLength(60)] public string? Unit { get; set; }
    [StringLength(300), Display(Name="Configured reference range")] public string? ReferenceRange { get; set; }
    public bool IsActive { get; set; } = true;
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (new[] { Code, Name, Category, SampleType }.Any(string.IsNullOrWhiteSpace)) yield return new ValidationResult("Code, name, category and sample type are required.");
    }
}
public class LabCatalogueList
{
    public string? Search { get; set; }
    public bool IncludeInactive { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
    public List<LabTest> Tests { get; set; } = new();
}
public class LabRequestForm
{
    public Guid MedicalRecordId { get; set; }
    public Guid LabTestId { get; set; }
    public Guid CreationToken { get; set; }
    [ValidateNever] public string PatientLabel { get; set; } = "";
    [ValidateNever] public List<SelectListItem> Tests { get; set; } = new();
}
public class LabResultForm
{
    public Guid Id { get; set; }
    public Guid LabRequestId { get; set; }
    public Guid CreationToken { get; set; }
    public Guid Version { get; set; }
    [Range(typeof(decimal),"-99999999999999999999","99999999999999999999"), Display(Name="Numeric value")] public decimal? NumericValue { get; set; }
    [StringLength(4000), Display(Name="Text result")] public string? TextValue { get; set; }
    [StringLength(2000)] public string? Comments { get; set; }
    [StringLength(1000), Display(Name="Correction reason")] public string? CorrectionReason { get; set; }
    [ValidateNever] public LabRequestRow Request { get; set; } = null!;
    [ValidateNever] public bool IsCorrection { get; set; }
}
public class LabRequestRow
{
    public Guid Id { get; set; }
    public Guid MedicalRecordId { get; set; }
    public Guid PatientId { get; set; }
    public string PatientLabel { get; set; } = "";
    public string DoctorName { get; set; } = "";
    public string RequestedByUserId { get; set; } = "";
    public string TestCode { get; set; } = "";
    public string TestName { get; set; } = "";
    public string Category { get; set; } = "";
    public string SampleType { get; set; } = "";
    public string? Unit { get; set; }
    public string? ReferenceRange { get; set; }
    public LabResultKind ResultKind { get; set; }
    public LabRequestStatus Status { get; set; }
    public string? SampleIdentifier { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? CollectedAtUtc { get; set; }
    public string? CollectedByUserId { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    public string? ReceivedByUserId { get; set; }
    public DateTime? ProcessingAtUtc { get; set; }
    public string? ProcessingByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public Guid Version { get; set; }
}
public class LabRequestList
{
    public string? Search { get; set; }
    public LabRequestStatus? Status { get; set; }
    public DateOnly? Date { get; set; }
    public Guid? PatientId { get; set; }
    public Guid? MedicalRecordId { get; set; }
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
    public bool Worklist { get; set; }
    public List<LabRequestRow> Requests { get; set; } = new();
}
public class LabRequestDetails
{
    public LabRequestRow Request { get; set; } = null!;
    public List<LabResultRevision> Results { get; set; } = new();
    public Guid? LatestRevisionId { get; set; }
    public bool LaboratoryStaff { get; set; }
    public bool CanCancel { get; set; }
    public string Account { get; set; } = "";
}
public class LabReport
{
    public LabRequestRow Request { get; set; } = null!;
    public LabResultRevision Result { get; set; } = null!;
}
