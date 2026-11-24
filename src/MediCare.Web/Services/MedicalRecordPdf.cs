using MediCare.Web.Models;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace MediCare.Web.Services;

public static class MedicalRecordPdf
{
    private static readonly object FontLock = new();
    // MigraDoc 6.2 does not shape complex scripts. Refuse an inaccurate clinical PDF;
    // the authorized browser print view supports the deployment's Unicode fonts/shaping.
    public static bool SupportsText(MedicalRecordRevision revision)
    {
        var record = revision.MedicalRecord;
        var text = string.Join("\n", new[] { record.Patient.PatientNumber, record.Patient.FirstName, record.Patient.LastName,
            record.Doctor.FirstName, record.Doctor.LastName, record.Doctor.RegistrationNumber, revision.AuthorUserId,
            revision.Symptoms, revision.Observations, revision.Diagnosis, revision.Treatment, revision.AmendmentReason }
            .Concat(revision.Prescriptions.SelectMany(p => new[] { p.Medication, p.Dosage, p.Route, p.Frequency, p.Duration, p.Instructions, p.Medicine?.Label, p.DispensingUnit })));
        return text.All(c => c is '\r' or '\n' or '\t' || c is >= ' ' and <= '\u024f' || c is >= '\u2010' and <= '\u2027' || c == '\u20ac');
    }
    public static byte[] Render(MedicalRecordRevision revision)
    {
        if (!SupportsText(revision)) throw new ArgumentException("Use the printable report for text outside the supported PDF font/script range.");
        EnsureFont();
        var record = revision.MedicalRecord;
        var document = new Document(); document.Info.Title = "MediCare confidential medical report";
        document.Styles[StyleNames.Normal]!.Font.Name = "MediCareReport";
        document.Styles[StyleNames.Normal]!.Font.Size = 10;
        var section = document.AddSection(); section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        var footer = section.Footers.Primary.AddParagraph("Confidential — authorized clinical use only. Page "); footer.AddPageField();
        section.AddParagraph("MediCare — Medical Report", "Heading1");
        section.AddParagraph(revision.FinalizedAtUtc == null ? "DRAFT — NOT FINALIZED" : "FINALIZED" , "Heading2");
        section.AddParagraph($"Patient: {record.Patient.PatientNumber} — {record.Patient.FirstName} {record.Patient.LastName}");
        section.AddParagraph($"Treating doctor: Dr. {record.Doctor.FirstName} {record.Doctor.LastName} ({record.Doctor.RegistrationNumber})");
        section.AddParagraph($"Encounter: {record.Id} | Revision: {revision.Number}");
        section.AddParagraph($"Author account: {revision.AuthorUserId}");
        section.AddParagraph($"Created: {HospitalTime.Local(revision.CreatedAtUtc):yyyy-MM-dd HH:mm} Asia/Colombo");
        section.AddParagraph($"Last updated: {HospitalTime.Local(revision.UpdatedAtUtc):yyyy-MM-dd HH:mm} Asia/Colombo");
        if (revision.FinalizedAtUtc.HasValue) section.AddParagraph($"Finalized: {HospitalTime.Local(revision.FinalizedAtUtc.Value):yyyy-MM-dd HH:mm} Asia/Colombo");
        Add("Amendment reason", revision.AmendmentReason);
        Add("Symptoms", revision.Symptoms); Add("Clinical observations", revision.Observations);
        Add("Diagnosis", revision.Diagnosis); Add("Treatment", revision.Treatment);
        section.AddParagraph("Prescriptions", "Heading2");
        foreach (var p in revision.Prescriptions.OrderBy(p => p.Position))
        {
            section.AddParagraph($"{p.Position + 1}. {p.Medication}\nDosage: {p.Dosage}; route: {p.Route}; frequency: {p.Frequency}; duration: {p.Duration}\nInstructions: {p.Instructions ?? "None"}");
            section.AddParagraph(p.Medicine == null ? "Not authorized for inventory dispensing." : $"Exact medicine: {p.Medicine.Label}\nAuthorized total (includes prior dispensing): {p.AuthorizedQuantity} {p.DispensingUnit}");
        }
        if (revision.Prescriptions.Count == 0) section.AddParagraph("No prescriptions recorded.");
        var renderer = new PdfDocumentRenderer { Document = document }; renderer.RenderDocument();
        using var output = new MemoryStream(); renderer.PdfDocument.Save(output, false); return output.ToArray();
        void Add(string heading, string? text) { section.AddParagraph(heading, "Heading2"); section.AddParagraph(string.IsNullOrWhiteSpace(text) ? "Not recorded." : text); }
    }

    internal static bool SupportsText(string text)=>text.All(c => c is '\r' or '\n' or '\t' || c is >= ' ' and <= '\u024f' || c is >= '\u2010' and <= '\u2027' || c == '\u20ac');
    internal static void EnsureFont()
    {
        lock (FontLock)
        {
            // The existing deployment is Windows. Font files stay on the server; exports embed subsets.
            if (GlobalFontSettings.FontResolver == null) GlobalFontSettings.FontResolver = new ReportFontResolver();
        }
    }

    private sealed class ReportFontResolver : IFontResolver
    {
        public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new("MediCareReport", bold, italic);
        public byte[] GetFont(string faceName)
        {
            var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
            if (!File.Exists(fontPath)) throw new InvalidOperationException("The report font is unavailable. Configure a licensed Unicode font before PDF export.");
            return File.ReadAllBytes(fontPath);
        }
    }
}
