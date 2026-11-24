using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;

namespace MediCare.Web.ViewModels;

public class BillingServiceForm
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }
    [Required,StringLength(40)] public string Code { get; set; } = "";
    [Required,StringLength(160)] public string Name { get; set; } = "";
    [Required,StringLength(80)] public string Category { get; set; } = "";
    [StringLength(1000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}
public class BillingPriceForm
{
    public Guid ServiceId { get; set; }
    public decimal UnitPrice { get; set; }
    public DateOnly? EffectiveDate { get; set; }
    public TimeOnly? EffectiveTime { get; set; }
}
public class BillingChoice
{
    public Guid Id { get; set; }
    public string Label { get; set; } = "";
    public decimal? UnitPrice { get; set; }
}
public class ChargeForm
{
    public Guid Token { get; set; }
    public Guid PatientId { get; set; }
    public Guid ServiceId { get; set; }
    public decimal Quantity { get; set; } = 1;
    public string? SourceKind { get; set; }
    public Guid? SourceId { get; set; }
    public List<BillingChoice> Patients { get; set; } = new();
    public List<BillingChoice> Services { get; set; } = new();
    public List<BillingSourceChoice> Sources { get; set; } = new();
}
public class BillingSourceChoice : BillingChoice
{
    public string Kind { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}
public class InvoiceForm
{
    public Guid Id { get; set; }
    public Guid Token { get; set; }
    public Guid PatientId { get; set; }
    public Guid Version { get; set; }
    public List<Guid> ChargeIds { get; set; } = new();
    public List<BillingChoice> Patients { get; set; } = new();
    public List<PatientCharge> Charges { get; set; } = new();
}
public class PaymentForm
{
    public Guid InvoiceId { get; set; }
    public Guid Token { get; set; }
    public Guid Version { get; set; }
    public decimal Amount { get; set; }
    public string Method { get; set; } = "Cash";
    [StringLength(100)] public string? BankReference { get; set; }
}
public class BillingRow
{
    public BillingInvoice Invoice { get; set; } = null!;
    public string PatientNumber { get; set; } = "";
    public string PatientName { get; set; } = "";
    public decimal Paid { get; set; }
    public decimal Balance => Invoice.State==InvoiceState.Issued?Invoice.Total-Paid:0;
}
public class InvoiceDetails : BillingRow { public List<BillingPayment> Payments { get; set; } = new(); }
public class BillingPage<T>
{
    public List<T> Items { get; set; } = new();
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public string? Search { get; set; }
    public string? Status { get; set; }
}
public class ChargeRow { public PatientCharge Charge { get; set; }=null!;public string PatientName { get; set; }=""; }
public class BillingDashboard
{
    public int OutstandingInvoices { get; set; }
    public decimal Outstanding { get; set; }
    public decimal RecordedPayments { get; set; }
    public decimal Reversals { get; set; }
    public List<BillingPayment> Payments { get; set; } = new();
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
