using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class BillingService
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [Required, StringLength(40)] public string Code { get; set; } = "";
    [Required, StringLength(160)] public string Name { get; set; } = "";
    [Required, StringLength(80)] public string Category { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
}
public class BillingPrice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ServiceId { get; set; }
    public BillingService Service { get; set; } = null!;
    public decimal UnitPrice { get; set; }
    public DateTime EffectiveAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    [Required, StringLength(450)] public string ActorUserId { get; set; } = "";
}
public class PatientCharge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Token { get; set; }
    public Guid PatientId { get; set; }
    public Guid PriceId { get; set; }
    public BillingPrice Price { get; set; } = null!;
    [Required, StringLength(240)] public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
    public Guid? AppointmentId { get; set; }
    public Guid? LabRequestId { get; set; }
    public Guid? PharmacyDispenseId { get; set; }
    public Guid? AdmissionId { get; set; }
    public Guid? OutpatientVisitId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    [Required, StringLength(450)] public string ActorUserId { get; set; } = "";
}
public enum InvoiceState { Draft, Issued, Voided }
public class BillingInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Number { get; set; }
    public Guid Token { get; set; }
    public Guid PatientId { get; set; }
    public InvoiceState State { get; set; }
    public decimal Total { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? IssuedAtUtc { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    [StringLength(1000)] public string? VoidReason { get; set; }
    [Required, StringLength(450)] public string ActorUserId { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public List<BillingInvoiceLine> Lines { get; set; } = new();
    public string DisplayNumber => $"MC-I-{Number:D8}";
}
public class BillingInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public BillingInvoice Invoice { get; set; } = null!;
    public Guid ChargeId { get; set; }
    public PatientCharge Charge { get; set; } = null!;
}
// Append-only entries; reversal is a separate positive-valued entry, never an edit.
public class BillingPayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long Number { get; set; }
    public Guid Token { get; set; }
    public Guid InvoiceId { get; set; }
    public BillingInvoice Invoice { get; set; } = null!;
    public decimal Amount { get; set; }
    [Required, StringLength(20)] public string Method { get; set; } = "Cash";
    [StringLength(100)] public string? BankReference { get; set; }
    public Guid? ReversesId { get; set; }
    [StringLength(1000)] public string? Reason { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    [Required, StringLength(450)] public string ActorUserId { get; set; } = "";
    public string DisplayNumber => $"MC-R-{Number:D8}";
}
