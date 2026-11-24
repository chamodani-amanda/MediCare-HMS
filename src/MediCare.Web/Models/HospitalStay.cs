using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class Ward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(40)] public string Code { get; set; } = "";
    [Required, StringLength(120)] public string Name { get; set; } = "";
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class Bed
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WardId { get; set; }
    public Ward Ward { get; set; } = null!;
    [Required, StringLength(40)] public string Number { get; set; } = "";
    public bool IsOperational { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
}

public enum OutpatientVisitStatus { Registered, InConsultation, Completed, Cancelled }
public enum AdmissionStatus { Admitted, DischargeReady, Discharged }

public class Admission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreationToken { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public AdmissionStatus Status { get; set; } = AdmissionStatus.Admitted;
    public DateTime AdmittedAtUtc { get; set; }
    [StringLength(450)] public string AdmittedByUserId { get; set; } = "";
    public DateTime? ClearedAtUtc { get; set; }
    [StringLength(450)] public string? ClearedByUserId { get; set; }
    public DateTime? DischargedAtUtc { get; set; }
    [StringLength(450)] public string? DischargedByUserId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class BedOccupancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AdmissionId { get; set; }
    public Admission Admission { get; set; } = null!;
    public Guid BedId { get; set; }
    public Bed Bed { get; set; } = null!;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}

public class BedTransfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Token { get; set; }
    public Guid AdmissionId { get; set; }
    public Admission Admission { get; set; } = null!;
    public Guid? FromBedId { get; set; }
    public Bed? FromBed { get; set; }
    public Guid? ToBedId { get; set; }
    public Bed? ToBed { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
    [StringLength(450)] public string ActorUserId { get; set; } = "";
}

// Separate clinical tables prevent administrative projections from loading these fields.
public class AdmissionClinical
{
    public Guid AdmissionId { get; set; }
    public Admission Admission { get; set; } = null!;
    [Required, StringLength(4000)] public string AdmissionReason { get; set; } = "";
    [StringLength(8000)] public string? DischargeSummary { get; set; }
    public Guid? MedicalRecordId { get; set; }
    public MedicalRecord? MedicalRecord { get; set; }
    [StringLength(450)] public string AuthorUserId { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

public class OutpatientClinical
{
    public Guid OutpatientVisitId { get; set; }
    public OutpatientVisit OutpatientVisit { get; set; } = null!;
    public Guid? MedicalRecordId { get; set; }
    public MedicalRecord? MedicalRecord { get; set; }
    [StringLength(4000)] public string? FollowUpInformation { get; set; }
    public DateOnly? FollowUpOn { get; set; }
    [StringLength(450)] public string AuthorUserId { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

// Administrative visit identity; existing encounters, prescriptions and investigations are not duplicated.
public class OutpatientVisit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreationToken { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid? AppointmentId { get; set; }
    public Appointment? Appointment { get; set; }
    public OutpatientVisitStatus Status { get; set; } = OutpatientVisitStatus.Registered;
    [StringLength(450)] public string RegisteredByUserId { get; set; } = "";
    public DateTime RegisteredAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    [StringLength(450)] public string? StartedByUserId { get; set; }
    [StringLength(450)] public string? CompletedByUserId { get; set; }
    [StringLength(450)] public string? CancelledByUserId { get; set; }
    [StringLength(500)] public string? CancellationReason { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
