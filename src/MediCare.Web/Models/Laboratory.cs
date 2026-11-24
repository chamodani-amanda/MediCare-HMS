using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public enum LabResultKind { Numeric, Text }
public enum LabRequestStatus { Requested, SampleCollected, InProgress, Completed, Cancelled }

public class LabTest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(40)] public string Code { get; set; } = "";
    [MaxLength(160)] public string Name { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    [MaxLength(100)] public string SampleType { get; set; } = "";
    public LabResultKind ResultKind { get; set; }
    [MaxLength(60)] public string? Unit { get; set; }
    [MaxLength(300)] public string? ReferenceRange { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public class LabRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreationToken { get; set; }
    public Guid MedicalRecordId { get; set; }
    public MedicalRecord MedicalRecord { get; set; } = null!;
    public Guid LabTestId { get; set; }
    public LabTest LabTest { get; set; } = null!;
    [MaxLength(450)] public string RequestedByUserId { get; set; } = "";
    // Catalogue snapshots retain the ordered test's meaning after later catalogue edits.
    [MaxLength(40)] public string TestCode { get; set; } = "";
    [MaxLength(160)] public string TestName { get; set; } = "";
    [MaxLength(80)] public string Category { get; set; } = "";
    [MaxLength(100)] public string SampleType { get; set; } = "";
    public LabResultKind ResultKind { get; set; }
    [MaxLength(60)] public string? Unit { get; set; }
    [MaxLength(300)] public string? ReferenceRange { get; set; }
    public LabRequestStatus Status { get; set; } = LabRequestStatus.Requested;
    [MaxLength(40)] public string? SampleIdentifier { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public DateTime? CollectedAtUtc { get; set; }
    [MaxLength(450)] public string? CollectedByUserId { get; set; }
    public DateTime? ReceivedAtUtc { get; set; }
    [MaxLength(450)] public string? ReceivedByUserId { get; set; }
    public DateTime? ProcessingAtUtc { get; set; }
    [MaxLength(450)] public string? ProcessingByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    [MaxLength(450)] public string? CancelledByUserId { get; set; }
    [MaxLength(1000)] public string? CancellationReason { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<LabResultRevision> Results { get; set; } = new();
}

public class LabResultRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LabRequestId { get; set; }
    public LabRequest LabRequest { get; set; } = null!;
    public Guid CreationToken { get; set; }
    public int Number { get; set; }
    [MaxLength(450)] public string AuthorUserId { get; set; } = "";
    public decimal? NumericValue { get; set; }
    [MaxLength(4000)] public string? TextValue { get; set; }
    [MaxLength(2000)] public string? Comments { get; set; }
    [MaxLength(1000)] public string? CorrectionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
