using System.Globalization;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace MediCare.Web.Services;

public static class BillingPdf
{
    private static string Money(decimal amount)=>amount.ToString("N2",CultureInfo.InvariantCulture)+" LKR";
    public static IEnumerable<string> Text(InvoiceDetails detail,BillingPayment? payment)
    {
        yield return "MediCare - "+(payment==null?"Invoice":"Payment receipt");
        yield return detail.Invoice.DisplayNumber+" / "+detail.Invoice.State;
        yield return "Patient: "+detail.PatientNumber+" - "+detail.PatientName;
        if(payment==null){
            yield return "Issued: "+(detail.Invoice.IssuedAtUtc.HasValue?HospitalTime.Local(detail.Invoice.IssuedAtUtc.Value).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture):"Draft");
            foreach(var line in detail.Invoice.Lines)yield return $"{line.Charge.Description} | {line.Charge.Quantity.ToString("0.###",CultureInfo.InvariantCulture)} x {Money(line.Charge.UnitPrice)} = {Money(line.Charge.Total)}";
            yield return "Total: "+Money(detail.Invoice.Total);yield return "Net recorded payments: "+Money(detail.Paid);yield return "Outstanding: "+Money(detail.Balance);yield return "Payment status: "+BillingRules.PaymentStatus(detail.Invoice,detail.Paid);
            if(detail.Invoice.State==InvoiceState.Voided)yield return "Void reason: "+detail.Invoice.VoidReason;
        }else{
            yield return payment.DisplayNumber;yield return "Recorded: "+HospitalTime.Local(payment.RecordedAtUtc).ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)+" Asia/Colombo";
            yield return "Method: "+payment.Method;yield return "Amount: "+Money(payment.Amount);yield return "Recorded by: "+payment.ActorUserId;
            if(payment.BankReference!=null)yield return "Bank reference: "+payment.BankReference;
            if(payment.ReversesId!=null){yield return "REVERSAL - original payment "+detail.Payments.Single(p=>p.Id==payment.ReversesId).DisplayNumber;yield return "Reason: "+payment.Reason;}
            if(detail.Payments.Any(p=>p.ReversesId==payment.Id))yield return "REVERSED - original retained for history";
            yield return "This document records a manual payment entry; it does not confirm bank or card processing.";
        }
    }
    public static bool Supports(InvoiceDetails detail,BillingPayment? payment)=>Text(detail,payment).All(MedicalRecordPdf.SupportsText);
    public static byte[] Render(InvoiceDetails detail,BillingPayment? payment)
    {
        if(!Supports(detail,payment))throw new ArgumentException("Use browser printing for unsupported scripts.");MedicalRecordPdf.EnsureFont();var doc=new Document();doc.Info.Title=payment==null?"MediCare invoice":"MediCare receipt";doc.Styles[StyleNames.Normal]!.Font.Name="MediCareReport";doc.Styles[StyleNames.Normal]!.Font.Size=10;
        var section=doc.AddSection();section.PageSetup.TopMargin=Unit.FromCentimeter(2);foreach(var text in Text(detail,payment))section.AddParagraph(text);section.Footers.Primary.AddParagraph("Confidential financial document. Times: Asia/Colombo.");
        var renderer=new PdfDocumentRenderer{Document=doc};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
}
