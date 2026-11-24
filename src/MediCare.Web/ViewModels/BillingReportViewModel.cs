
using MediCare.Web.Models;

namespace MediCare.Web.ViewModels;

public class BillingReportRowViewModel
{
    public Guid Id { get; set; }
    public long Number { get; set; }

    public string DisplayNumber => $"MC-I-{Number:D8}";

    public string PatientNumber { get; set; } = "";
    public string PatientName { get; set; } = "";

    public InvoiceState State { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public decimal Total { get; set; }
    public decimal NetPaid { get; set; }

    public decimal? Balance =>
        State == InvoiceState.Issued
            ? Total - NetPaid
            : null;

    public string PaymentStatus =>
        State switch
        {
            InvoiceState.Draft => "Draft",
            InvoiceState.Voided => "Voided",
            _ when NetPaid <= 0m => "Unpaid",
            _ when NetPaid < Total => "Partially Paid",
            _ => "Paid"
        };
}

public class BillingReportViewModel
{
    public List<BillingReportRowViewModel> Invoices
        { get; set; } = new();

    public string Search { get; set; } = "";
    public string Status { get; set; } = "All";

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int TotalInvoices { get; set; }
    public int DraftInvoices { get; set; }
    public int IssuedInvoices { get; set; }
    public int VoidedInvoices { get; set; }

    public decimal IssuedAmount { get; set; }
    public decimal NetReceivedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }

    public int MatchingInvoices { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}
