using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

internal static class MedicalRecordMapping
{
    public static void ConfigureMedicalRecords(this ModelBuilder model)
    {
        model.Entity<MedicalRecord>(e =>
        {
            e.ToTable("medical_records", t => t.HasCheckConstraint("CK_medical_records_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid"));
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => r.AppointmentId).IsUnique();
            e.HasIndex(r => r.CreationToken).IsUnique();
            e.HasIndex(r => new { r.PatientId, r.CreatedAtUtc });
            e.HasIndex(r => new { r.DoctorId, r.CreatedAtUtc });
            e.HasOne(r => r.Patient).WithMany().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Doctor).WithMany().HasForeignKey(r => r.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Appointment).WithMany().HasForeignKey(r => r.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MedicalRecordRevision>(e =>
        {
            e.ToTable("medical_record_revisions", t =>
            {
                t.HasCheckConstraint("CK_medical_record_revisions_number", "\"Number\" >= 1 AND ((\"Number\" = 1 AND \"AmendmentReason\" IS NULL) OR (\"Number\" > 1 AND length(btrim(\"AmendmentReason\")) > 0 AND \"AmendmentReason\" IS NOT NULL))");
                t.HasCheckConstraint("CK_medical_record_revisions_final", "\"FinalizedAtUtc\" IS NULL OR (\"FinalizedAtUtc\" >= \"CreatedAtUtc\" AND length(btrim(\"Symptoms\")) > 0 AND length(btrim(\"Diagnosis\")) > 0 AND length(btrim(\"Treatment\")) > 0)");
            });
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => new { r.MedicalRecordId, r.Number }).IsUnique();
            e.HasIndex(r => r.MedicalRecordId).IsUnique().HasFilter("\"FinalizedAtUtc\" IS NULL");
            e.HasOne(r => r.MedicalRecord).WithMany(r => r.Revisions).HasForeignKey(r => r.MedicalRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MedicalPrescription>(e =>
        {
            e.ToTable("medical_prescriptions", t => t.HasCheckConstraint("CK_medical_prescriptions_fields", "\"Position\" BETWEEN 0 AND 19 AND length(btrim(\"Medication\")) > 0 AND length(btrim(\"Dosage\")) > 0 AND length(btrim(\"Route\")) > 0 AND length(btrim(\"Frequency\")) > 0 AND length(btrim(\"Duration\")) > 0"));
            e.HasIndex(p => new { p.MedicalRecordRevisionId, p.Position }).IsUnique();
            e.HasOne(p => p.MedicalRecordRevision).WithMany(r => r.Prescriptions).HasForeignKey(p => p.MedicalRecordRevisionId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
