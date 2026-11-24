using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class Medicine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(40)] public string Code { get; set; } = "";
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [Required, StringLength(160)] public string GenericName { get; set; } = "";
    [Required, StringLength(100)] public string Strength { get; set; } = "";
    [Required, StringLength(100)] public string Formulation { get; set; } = "";
    [Required, StringLength(60)] public string DispensingUnit { get; set; } = "";
    [Range(typeof(decimal), "0", "1000000000")] public decimal LowStockThreshold { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public string Label => $"{Code} — {Name} / {GenericName}, {Strength}, {Formulation} ({DispensingUnit})";
}

public class MedicineBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MedicineId { get; set; }
    public Medicine Medicine { get; set; } = null!;
    [Required, StringLength(80)] public string BatchNumber { get; set; } = "";
    [Required, StringLength(200)] public string Supplier { get; set; } = "";
    public DateOnly? ManufacturedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    public DateTime ReceivedAtUtc { get; set; }
    public decimal AvailableQuantity { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}

// Append-only ledger. Reversed goods are recorded as quarantined, never saleable stock.
public class InventoryMovement
{
    public Guid? ExpectedBatchVersion { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Token { get; set; }
    public Guid BatchId { get; set; }
    public MedicineBatch Batch { get; set; } = null!;
    public Guid? DispenseId { get; set; }
    public PharmacyDispense? Dispense { get; set; }
    [StringLength(30)] public string Kind { get; set; } = "";
    public decimal Quantity { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
    [StringLength(450)] public string ActorUserId { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}

public class PharmacyDispense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Token { get; set; }
    public Guid PrescriptionId { get; set; }
    public MedicalPrescription Prescription { get; set; } = null!;
    public Guid LineageId { get; set; }
    public decimal Quantity { get; set; }
    public Guid? ReversesId { get; set; }
    [StringLength(1000)] public string? Reason { get; set; }
    [StringLength(450)] public string ActorUserId { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public List<InventoryMovement> Movements { get; set; } = new();
}
