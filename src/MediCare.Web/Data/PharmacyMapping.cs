using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

internal static class PharmacyMapping
{
    public static void ConfigurePharmacy(this ModelBuilder model)
    {
        model.Entity<Medicine>(e => {
            e.ToTable("medicines", t => {
                t.HasCheckConstraint("CK_medicines_fields", "length(btrim(\"Code\")) > 0 AND \"Code\" = upper(btrim(\"Code\")) AND length(btrim(\"Name\")) > 0 AND length(btrim(\"GenericName\")) > 0 AND length(btrim(\"Strength\")) > 0 AND length(btrim(\"Formulation\")) > 0 AND length(btrim(\"DispensingUnit\")) > 0");
                t.HasCheckConstraint("CK_medicines_threshold", "\"LowStockThreshold\" BETWEEN 0 AND 1000000000");
            });
            e.Ignore(m => m.Label); e.HasIndex(m => m.Code).IsUnique(); e.Property(m => m.Version).IsConcurrencyToken();
            e.Property(m => m.LowStockThreshold).HasPrecision(18,3);
        });
        model.Entity<MedicineBatch>(e => {
            e.ToTable("medicine_batches", t => {
                t.HasCheckConstraint("CK_medicine_batches_balance", "\"AvailableQuantity\" BETWEEN 0 AND 1000000000");
                t.HasCheckConstraint("CK_medicine_batches_fields", "length(btrim(\"BatchNumber\")) > 0 AND \"BatchNumber\" = upper(btrim(\"BatchNumber\")) AND length(btrim(\"Supplier\")) > 0 AND (\"ManufacturedOn\" IS NULL OR \"ManufacturedOn\" <= \"ExpiresOn\")");
            });
            e.Property(b => b.AvailableQuantity).HasPrecision(18,3); e.Property(b => b.Version).IsConcurrencyToken();
            e.HasIndex(b => new { b.MedicineId, b.BatchNumber }).IsUnique(); e.HasIndex(b => b.ExpiresOn);
            e.HasOne(b => b.Medicine).WithMany().HasForeignKey(b => b.MedicineId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PharmacyDispense>(e => {
            e.ToTable("pharmacy_dispenses", t => {
                t.HasCheckConstraint("CK_pharmacy_dispenses_quantity", "(\"ReversesId\" IS NULL AND \"Quantity\" > 0) OR (\"ReversesId\" IS NOT NULL AND \"Quantity\" < 0 AND length(btrim(\"Reason\")) > 0 AND \"Reason\" IS NOT NULL)");
                t.HasCheckConstraint("CK_pharmacy_dispenses_token", "\"Token\" <> '00000000-0000-0000-0000-000000000000'::uuid AND \"LineageId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
            });
            e.Property(d => d.Quantity).HasPrecision(18,3); e.HasIndex(d => d.Token).IsUnique(); e.HasIndex(d => d.LineageId);
            e.HasIndex(d => d.ReversesId).IsUnique();
            e.HasOne(d => d.Prescription).WithMany().HasForeignKey(d => d.PrescriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PharmacyDispense>().WithMany().HasForeignKey(d => d.ReversesId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(d => d.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<InventoryMovement>(e => {
            e.ToTable("inventory_movements", t => {
                t.HasCheckConstraint("CK_inventory_movements_kind", "(\"Kind\" = 'Receive' AND \"Quantity\" > 0 AND \"DispenseId\" IS NULL) OR (\"Kind\" = 'Adjust' AND \"Quantity\" <> 0 AND \"DispenseId\" IS NULL) OR (\"Kind\" = 'Dispense' AND \"Quantity\" < 0 AND \"DispenseId\" IS NOT NULL) OR (\"Kind\" = 'QuarantineReturn' AND \"Quantity\" > 0 AND \"DispenseId\" IS NOT NULL)");
                t.HasCheckConstraint("CK_inventory_movements_fields", "\"Token\" <> '00000000-0000-0000-0000-000000000000'::uuid AND length(btrim(\"Reason\")) > 0 AND abs(\"Quantity\") <= 1000000000");
            });
            e.Property(m => m.Quantity).HasPrecision(18,3); e.HasIndex(m => m.Token).IsUnique(); e.HasIndex(m => new { m.BatchId, m.OccurredAtUtc });
            e.HasOne(m => m.Batch).WithMany().HasForeignKey(m => m.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(m => m.Dispense).WithMany(d => d.Movements).HasForeignKey(m => m.DispenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IdentityUser>().WithMany().HasForeignKey(m => m.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MedicalPrescription>(e => {
            e.Property(p => p.AuthorizedQuantity).HasPrecision(18,3);
            e.HasOne(p => p.Medicine).WithMany().HasForeignKey(p => p.MedicineId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.LineageId);
            e.HasIndex(p => new { p.MedicalRecordRevisionId, p.LineageId }).IsUnique().HasFilter("\"LineageId\" IS NOT NULL");
            e.ToTable("medical_prescriptions", t => t.HasCheckConstraint("CK_medical_prescriptions_authorization", "(\"MedicineId\" IS NULL AND \"AuthorizedQuantity\" IS NULL AND \"DispensingUnit\" IS NULL) OR (\"MedicineId\" IS NOT NULL AND \"AuthorizedQuantity\" > 0 AND \"AuthorizedQuantity\" <= 1000000000 AND \"AuthorizedQuantity\" IS NOT NULL AND length(btrim(\"DispensingUnit\")) > 0 AND \"DispensingUnit\" IS NOT NULL AND \"LineageId\" IS NOT NULL AND \"LineageId\" <> '00000000-0000-0000-0000-000000000000'::uuid)"));
        });
    }
}
