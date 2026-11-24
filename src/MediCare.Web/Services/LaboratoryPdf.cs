using System.Globalization;
using MediCare.Web.ViewModels;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;

namespace MediCare.Web.Services;

public static class LaboratoryPdf
{
    private static IEnumerable<(string Title,string Text)> Sections(LabReport m)
    {
        var q=m.Request;var r=m.Result;
        yield return ("Patient",q.PatientLabel);
        yield return ("Requesting doctor","Dr. "+q.DoctorName);
        yield return ("Test",q.TestCode+" — "+q.TestName+" ("+q.Category+")");
        yield return ("Sample",(q.SampleIdentifier??"Not recorded")+" — "+q.SampleType);
        yield return ("Result",r.NumericValue?.ToString("G29",CultureInfo.InvariantCulture)??r.TextValue??"");
        yield return ("Unit",q.Unit??"Not configured");
        yield return ("Configured reference range",q.ReferenceRange??"Not configured");
        yield return ("Comments",r.Comments??"None");
        if(r.CorrectionReason!=null)yield return ("Correction reason",r.CorrectionReason);
        yield return ("Result author",r.AuthorUserId);
        yield return ("Collected",Time(q.CollectedAtUtc)+" / "+q.CollectedByUserId);
        yield return ("Received",Time(q.ReceivedAtUtc)+" / "+q.ReceivedByUserId);
        yield return ("Processing started",Time(q.ProcessingAtUtc)+" / "+q.ProcessingByUserId);
        yield return ("Finalized",Time(r.FinalizedAtUtc));
        yield return ("Revision",r.Number.ToString(CultureInfo.InvariantCulture)+" / "+r.Id);
        static string Time(DateTime? utc)=>utc.HasValue?HospitalTime.Local(utc.Value).ToString("yyyy-MM-dd HH:mm")+" Asia/Colombo":"Not recorded";
    }
    public static bool Supports(LabReport model)=>Sections(model).All(s=>MedicalRecordPdf.SupportsText(s.Text));
    public static byte[] Render(LabReport model)
    {
        if(!Supports(model))throw new ArgumentException("Use the authorized printable report for unsupported PDF scripts.");
        MedicalRecordPdf.EnsureFont();var document=new Document();document.Info.Title="MediCare confidential laboratory report";
        document.Styles[StyleNames.Normal]!.Font.Name="MediCareReport";document.Styles[StyleNames.Normal]!.Font.Size=10;
        var section=document.AddSection();section.AddParagraph("MediCare — Laboratory Report","Heading1");section.AddParagraph("FINALIZED","Heading2");
        foreach(var item in Sections(model)){section.AddParagraph(item.Title,"Heading2");section.AddParagraph(item.Text);}
        var footer=section.Footers.Primary.AddParagraph("Confidential — authorized use only. Page ");footer.AddPageField();
        var renderer=new PdfDocumentRenderer {Document=document};renderer.RenderDocument();using var stream=new MemoryStream();renderer.PdfDocument.Save(stream,false);return stream.ToArray();
    }
}
