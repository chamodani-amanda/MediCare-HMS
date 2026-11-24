using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

internal static class LaboratoryMapping
{
    public static void ConfigureLaboratory(this ModelBuilder model)
    {
        model.Entity<LabTest>(e =>
        {
            e.ToTable("lab_tests", t =>
            {
                t.HasCheckConstraint("CK_lab_tests_kind", "\"ResultKind\" IN ('Numeric','Text')");
                t.HasCheckConstraint("CK_lab_tests_required", "\"Code\" ~ '^[A-Z0-9][A-Z0-9_.-]{0,39}$' AND length(btrim(\"Name\")) > 0 AND length(btrim(\"Category\")) > 0 AND length(btrim(\"SampleType\")) > 0");
            });
            e.Property(t => t.ResultKind).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Version).IsConcurrencyToken();
            e.HasIndex(t => t.Code).IsUnique();
            e.HasIndex(t => new { t.IsActive, t.Category, t.Name });
        });
        model.Entity<LabRequest>(e =>
        {
            e.ToTable("lab_requests", t =>
            {
                t.HasCheckConstraint("CK_lab_requests_status", "\"Status\" IN ('Requested','SampleCollected','InProgress','Completed','Cancelled')");
                t.HasCheckConstraint("CK_lab_requests_kind", "\"ResultKind\" IN ('Numeric','Text')");
                t.HasCheckConstraint("CK_lab_requests_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                t.HasCheckConstraint("CK_lab_requests_sample", "(\"SampleIdentifier\" IS NULL AND \"CollectedAtUtc\" IS NULL AND \"CollectedByUserId\" IS NULL) OR (\"SampleIdentifier\" IS NOT NULL AND \"SampleIdentifier\" ~ '^LAB-[A-F0-9]{32}$' AND \"CollectedAtUtc\" >= \"RequestedAtUtc\" AND \"CollectedAtUtc\" IS NOT NULL AND \"CollectedByUserId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_lab_requests_receipt", "(\"ReceivedAtUtc\" IS NULL AND \"ReceivedByUserId\" IS NULL) OR (\"ReceivedAtUtc\" IS NOT NULL AND \"ReceivedByUserId\" IS NOT NULL AND \"CollectedAtUtc\" IS NOT NULL AND \"ReceivedAtUtc\" >= \"CollectedAtUtc\")");
                t.HasCheckConstraint("CK_lab_requests_processing", "(\"ProcessingAtUtc\" IS NULL AND \"ProcessingByUserId\" IS NULL) OR (\"ProcessingAtUtc\" IS NOT NULL AND \"ProcessingByUserId\" IS NOT NULL AND \"ReceivedAtUtc\" IS NOT NULL AND \"ProcessingAtUtc\" >= \"ReceivedAtUtc\")");
                t.HasCheckConstraint("CK_lab_requests_completion", "(\"CompletedAtUtc\" IS NULL AND \"Status\" <> 'Completed') OR (\"CompletedAtUtc\" IS NOT NULL AND \"Status\" = 'Completed' AND \"ProcessingAtUtc\" IS NOT NULL AND \"CompletedAtUtc\" >= \"ProcessingAtUtc\")");
                t.HasCheckConstraint("CK_lab_requests_cancellation", "(\"Status\" <> 'Cancelled' AND \"CancelledAtUtc\" IS NULL AND \"CancelledByUserId\" IS NULL AND \"CancellationReason\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL AND \"CancelledAtUtc\" >= \"RequestedAtUtc\" AND \"CancelledByUserId\" IS NOT NULL AND length(btrim(\"CancellationReason\")) > 0 AND \"CancellationReason\" IS NOT NULL)");
                t.HasCheckConstraint("CK_lab_requests_stage", "(\"Status\" IN ('Requested','Cancelled') OR \"CollectedAtUtc\" IS NOT NULL) AND (\"Status\" NOT IN ('InProgress','Completed') OR \"ProcessingAtUtc\" IS NOT NULL) AND (\"Status\" <> 'Requested' OR \"CollectedAtUtc\" IS NULL) AND (\"Status\" <> 'SampleCollected' OR \"ProcessingAtUtc\" IS NULL)");
            });
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(24);
            e.Property(r => r.ResultKind).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => r.CreationToken).IsUnique(); e.HasIndex(r => r.SampleIdentifier).IsUnique();
            e.HasIndex(r => new { r.Status, r.RequestedAtUtc }); e.HasIndex(r => new { r.MedicalRecordId, r.RequestedAtUtc });
            e.HasOne(r => r.MedicalRecord).WithMany().HasForeignKey(r => r.MedicalRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.LabTest).WithMany().HasForeignKey(r => r.LabTestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.CollectedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.ReceivedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.ProcessingByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<LabResultRevision>(e =>
        {
            e.ToTable("lab_result_revisions", t =>
            {
                t.HasCheckConstraint("CK_lab_result_revisions_number", "\"Number\" >= 1 AND ((\"Number\" = 1 AND \"CorrectionReason\" IS NULL) OR (\"Number\" > 1 AND \"CorrectionReason\" IS NOT NULL AND length(btrim(\"CorrectionReason\")) > 0))");
                t.HasCheckConstraint("CK_lab_result_revisions_token", "\"CreationToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                t.HasCheckConstraint("CK_lab_result_revisions_value", "(\"NumericValue\" IS NULL OR \"NumericValue\" <> 'NaN'::numeric) AND NOT (\"NumericValue\" IS NOT NULL AND \"TextValue\" IS NOT NULL) AND (\"FinalizedAtUtc\" IS NULL OR (\"FinalizedAtUtc\" >= \"CreatedAtUtc\" AND (\"NumericValue\" IS NOT NULL OR (\"TextValue\" IS NOT NULL AND length(btrim(\"TextValue\")) > 0))))");
            });
            e.Property(r => r.NumericValue).HasPrecision(28, 8);
            e.Property(r => r.Version).IsConcurrencyToken();
            e.HasIndex(r => r.CreationToken).IsUnique();
            e.HasIndex(r => new { r.LabRequestId, r.Number }).IsUnique();
            e.HasIndex(r => r.LabRequestId).IsUnique().HasFilter("\"FinalizedAtUtc\" IS NULL");
            e.HasOne(r => r.LabRequest).WithMany(r => r.Results).HasForeignKey(r => r.LabRequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(r => r.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
