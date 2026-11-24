using MediCare.Web.Models;

namespace MediCare.Web.Services;

public static class BillingRules
{
    public const decimal MaximumMoney = 999999999.99m;
    public static bool Money(decimal value, bool positive = false) => value >= (positive ? .01m : 0m) && value <= MaximumMoney && decimal.Round(value,2)==value;
    public static bool Quantity(decimal value) => value > 0 && value <= 999999m && decimal.Round(value,3)==value;
    public static decimal LineTotal(decimal quantity, decimal price) => decimal.Round(quantity*price,2,MidpointRounding.AwayFromZero);
    public static decimal NetPaid(IEnumerable<BillingPayment> payments) => payments.Sum(p=>p.ReversesId==null?p.Amount:-p.Amount);
    public static string PaymentStatus(BillingInvoice invoice, decimal paid) => invoice.State!=InvoiceState.Issued?invoice.State.ToString():paid==invoice.Total?"Paid":paid>0?"PartiallyPaid":"Unpaid";
    public static string SourceReference(PatientCharge c)=>c.AppointmentId.HasValue?"Appointment "+c.AppointmentId:c.LabRequestId.HasValue?"Laboratory request "+c.LabRequestId:c.PharmacyDispenseId.HasValue?"Dispensing entry "+c.PharmacyDispenseId:c.AdmissionId.HasValue?"Admission "+c.AdmissionId:c.OutpatientVisitId.HasValue?"Outpatient visit "+c.OutpatientVisitId:"Manual charge";
}
