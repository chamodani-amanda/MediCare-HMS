using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

// An encounter is stable; every finalized revision remains part of its history.
public class MedicalRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    [MaxLength(450)] public string AuthorUserId { get; set; } = "";
    public Guid CreationToken { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; }
    public List<MedicalRecordRevision> Revisions { get; set; } = new();
}

public class MedicalRecordRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalRecordId { get; set; }
    public MedicalRecord MedicalRecord { get; set; } = null!;
    public int Number { get; set; }
    [MaxLength(450)] public string AuthorUserId { get; set; } = "";
    [MaxLength(4000)] public string Symptoms { get; set; } = "";
    [MaxLength(8000)] public string Observations { get; set; } = "";
    [MaxLength(4000)] public string Diagnosis { get; set; } = "";
    [MaxLength(8000)] public string Treatment { get; set; } = "";
    [MaxLength(1000)] public string? AmendmentReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<MedicalPrescription> Prescriptions { get; set; } = new();
}

public class MedicalPrescription
{
    public Guid? MedicineId { get; set; }
    public Medicine? Medicine { get; set; }
    public Guid? LineageId { get; set; }
    public decimal? AuthorizedQuantity { get; set; }
    [MaxLength(60)] public string? DispensingUnit { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicalRecordRevisionId { get; set; }
    public MedicalRecordRevision MedicalRecordRevision { get; set; } = null!;
    public int Position { get; set; }
    [MaxLength(200)] public string Medication { get; set; } = "";
    [MaxLength(100)] public string Dosage { get; set; } = "";
    [MaxLength(100)] public string Route { get; set; } = "";
    [MaxLength(200)] public string Frequency { get; set; } = "";
    [MaxLength(200)] public string Duration { get; set; } = "";
    [MaxLength(1000)] public string? Instructions { get; set; }
}
