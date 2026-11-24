using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

internal static class HospitalStayMapping
{
    public static void ConfigureHospitalStays(this ModelBuilder model)
    {
        model.Entity<Admission>(e => {
            e.ToTable("admissions", t => {
                t.HasCheckConstraint("CK_admissions_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                t.HasCheckConstraint("CK_admissions_state", "(\"Status\"='Admitted' AND \"ClearedAtUtc\" IS NULL AND \"DischargedAtUtc\" IS NULL) OR (\"Status\"='DischargeReady' AND \"ClearedAtUtc\" IS NOT NULL AND \"DischargedAtUtc\" IS NULL) OR (\"Status\"='Discharged' AND \"ClearedAtUtc\" IS NOT NULL AND \"DischargedAtUtc\" IS NOT NULL)");
                t.HasCheckConstraint("CK_admissions_times", "isfinite(\"AdmittedAtUtc\") AND (\"ClearedAtUtc\" IS NULL OR (isfinite(\"ClearedAtUtc\") AND \"ClearedAtUtc\" >= \"AdmittedAtUtc\")) AND (\"DischargedAtUtc\" IS NULL OR (isfinite(\"DischargedAtUtc\") AND \"DischargedAtUtc\" >= \"ClearedAtUtc\")) AND (\"ClearedAtUtc\" IS NULL) = (\"ClearedByUserId\" IS NULL) AND (\"DischargedAtUtc\" IS NULL) = (\"DischargedByUserId\" IS NULL)");
            });
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20); e.Property(a => a.Version).IsConcurrencyToken();
            e.HasIndex(a => a.CreationToken).IsUnique(); e.HasIndex(a => a.PatientId).IsUnique().HasFilter("\"Status\" <> 'Discharged'");
            e.HasIndex(a => new { a.DoctorId, a.AdmittedAtUtc }); e.HasIndex(a => new { a.DepartmentId, a.Status });
            e.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Doctor).WithMany().HasForeignKey(a => a.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.Department).WithMany().HasForeignKey(a => a.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(a => a.AdmittedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(a => a.ClearedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(a => a.DischargedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BedOccupancy>(e => {
            e.ToTable("bed_occupancies", t => t.HasCheckConstraint("CK_bed_occupancies_times", "isfinite(\"StartedAtUtc\") AND (\"EndedAtUtc\" IS NULL OR (isfinite(\"EndedAtUtc\") AND \"EndedAtUtc\" >= \"StartedAtUtc\"))"));
            e.HasIndex(o => o.BedId).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
            e.HasIndex(o => o.AdmissionId).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL");
            e.HasOne(o => o.Admission).WithMany().HasForeignKey(o => o.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(o => o.Bed).WithMany().HasForeignKey(o => o.BedId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<BedTransfer>(e => {
            e.ToTable("bed_transfers", t => t.HasCheckConstraint("CK_bed_transfers_fields", "\"Token\" <> '00000000-0000-0000-0000-000000000000'::uuid AND isfinite(\"OccurredAtUtc\") AND length(btrim(\"Reason\")) > 0 AND (\"FromBedId\" IS NOT NULL OR \"ToBedId\" IS NOT NULL) AND \"FromBedId\" IS DISTINCT FROM \"ToBedId\""));
            e.HasIndex(t => t.Token).IsUnique(); e.HasIndex(t => new { t.AdmissionId, t.OccurredAtUtc });
            e.HasOne(t => t.Admission).WithMany().HasForeignKey(t => t.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.FromBed).WithMany().HasForeignKey(t => t.FromBedId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.ToBed).WithMany().HasForeignKey(t => t.ToBedId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(t => t.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AdmissionClinical>(e => {
            e.ToTable("admission_clinical_notes"); e.HasKey(n => n.AdmissionId); e.Property(n => n.Version).IsConcurrencyToken();
            e.HasOne(n => n.Admission).WithMany().HasForeignKey(n => n.AdmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.MedicalRecord).WithMany().HasForeignKey(n => n.MedicalRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(n => n.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OutpatientClinical>(e => {
            e.ToTable("outpatient_clinical_notes"); e.HasKey(n => n.OutpatientVisitId); e.Property(n => n.Version).IsConcurrencyToken();
            e.HasOne(n => n.OutpatientVisit).WithMany().HasForeignKey(n => n.OutpatientVisitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(n => n.MedicalRecord).WithMany().HasForeignKey(n => n.MedicalRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(n => n.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Ward>(e => {
            e.ToTable("wards", t => t.HasCheckConstraint("CK_wards_fields", "length(btrim(\"Code\")) > 0 AND \"Code\" = upper(btrim(\"Code\")) AND length(btrim(\"Name\")) > 0"));
            e.HasIndex(w => w.Code).IsUnique(); e.Property(w => w.Version).IsConcurrencyToken();
            e.HasOne(w => w.Department).WithMany().HasForeignKey(w => w.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<Bed>(e => {
            e.ToTable("beds", t => t.HasCheckConstraint("CK_beds_number", "length(btrim(\"Number\")) > 0 AND \"Number\" = upper(btrim(\"Number\"))"));
            e.HasIndex(b => new { b.WardId, b.Number }).IsUnique(); e.Property(b => b.Version).IsConcurrencyToken();
            e.HasOne(b => b.Ward).WithMany().HasForeignKey(b => b.WardId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<OutpatientVisit>(e => {
            e.ToTable("outpatient_visits", t => {
                t.HasCheckConstraint("CK_outpatient_visits_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                t.HasCheckConstraint("CK_outpatient_visits_state", "(\"Status\"='Registered' AND \"StartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\"='InConsultation' AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\"='Completed' AND \"StartedAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" IS NULL) OR (\"Status\"='Cancelled' AND \"StartedAtUtc\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"CancelledAtUtc\" IS NOT NULL)");
                t.HasCheckConstraint("CK_outpatient_visits_times", "isfinite(\"RegisteredAtUtc\") AND (\"StartedAtUtc\" IS NULL OR (isfinite(\"StartedAtUtc\") AND \"StartedAtUtc\" >= \"RegisteredAtUtc\")) AND (\"CompletedAtUtc\" IS NULL OR (isfinite(\"CompletedAtUtc\") AND \"CompletedAtUtc\" >= \"StartedAtUtc\")) AND (\"CancelledAtUtc\" IS NULL OR (isfinite(\"CancelledAtUtc\") AND \"CancelledAtUtc\" >= \"RegisteredAtUtc\"))");
                t.HasCheckConstraint("CK_outpatient_visits_actors", "(\"StartedAtUtc\" IS NULL) = (\"StartedByUserId\" IS NULL) AND (\"CompletedAtUtc\" IS NULL) = (\"CompletedByUserId\" IS NULL) AND (\"CancelledAtUtc\" IS NULL) = (\"CancelledByUserId\" IS NULL) AND ((\"Status\"='Cancelled' AND \"CancellationReason\" IS NOT NULL AND length(btrim(\"CancellationReason\")) > 0) OR (\"Status\"<>'Cancelled' AND \"CancellationReason\" IS NULL))");
            });
            e.Property(v => v.Status).HasConversion<string>().HasMaxLength(20); e.Property(v => v.Version).IsConcurrencyToken();
            e.HasIndex(v => v.CreationToken).IsUnique(); e.HasIndex(v => v.AppointmentId).IsUnique();
            e.HasIndex(v => new { v.PatientId, v.RegisteredAtUtc }); e.HasIndex(v => new { v.DoctorId, v.Status, v.RegisteredAtUtc });
            e.HasOne(v => v.Patient).WithMany().HasForeignKey(v => v.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Doctor).WithMany().HasForeignKey(v => v.DoctorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(v => v.Appointment).WithMany().HasForeignKey(v => v.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(v => v.RegisteredByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(v => v.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(v => v.CompletedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(v => v.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
