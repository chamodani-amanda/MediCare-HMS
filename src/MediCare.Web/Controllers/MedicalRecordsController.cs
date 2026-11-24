using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Doctor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class MedicalRecordsController(MediCareDbContext db, TimeProvider clock) : ClinicalAdministrationControllerBase(db)
{
    private string Account => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private IQueryable<MedicalRecordRevision> Visible() => MedicalRecordAccess.Visible(Db, Account).AsNoTracking();
    private IQueryable<MedicalRecordRevision> Graph(IQueryable<MedicalRecordRevision> query) => query
        .Include(r => r.MedicalRecord).ThenInclude(r => r.Patient)
        .Include(r => r.MedicalRecord).ThenInclude(r => r.Doctor)
        .Include(r => r.Prescriptions).ThenInclude(p => p.Medicine);
    private static IQueryable<MedicalRecordSummary> Summaries(IQueryable<MedicalRecordRevision> query) => query.Select(r => new MedicalRecordSummary
    {
        Id = r.Id, PatientId = r.MedicalRecord.PatientId, PatientLabel = r.MedicalRecord.Patient.PatientNumber + " — " + r.MedicalRecord.Patient.FirstName + " " + r.MedicalRecord.Patient.LastName,
        DoctorName = r.MedicalRecord.Doctor.FirstName + " " + r.MedicalRecord.Doctor.LastName, Number = r.Number, CreatedAtUtc = r.CreatedAtUtc, FinalizedAtUtc = r.FinalizedAtUtc
    });

    [HttpGet]
    public async Task<IActionResult> Index(Guid? patientId, string? search, DateOnly? date, string? state, int page = 1)
    {
        search = (search ?? "").Trim();
        if (!ModelState.IsValid || search.Length > 100 || (date.HasValue && date.Value.Year is < 1900 or > 9998) || (state != null && state != "Draft" && state != "Finalized")) return BadRequest("Invalid record filter.");
        if (patientId.HasValue && !await MedicalRecordAccess.CareAppointments(Db, Account).AnyAsync(a => a.PatientId == patientId)) return NotFound();
        var query = Visible();
        if (patientId.HasValue) query = query.Where(r => r.MedicalRecord.PatientId == patientId);
        if (search.Length > 0)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(r => r.MedicalRecord.Patient.PatientNumber.ToUpper().Contains(term) ||
                (r.MedicalRecord.Patient.FirstName + " " + r.MedicalRecord.Patient.LastName).ToUpper().Contains(term));
        }
        if (date.HasValue)
        {
            var from = HospitalTime.ToUtc(date.Value, TimeOnly.MinValue); var until = from.AddDays(1);
            query = query.Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < until);
        }
        if (state != null) query = query.Where(r => (r.FinalizedAtUtc != null) == (state == "Finalized"));
        var count = await query.CountAsync(); var pages = Math.Max(1, (int)Math.Ceiling(count / 20d)); page = Math.Clamp(page, 1, pages);
        var result = new MedicalRecordList { PatientId = patientId, Search = search, Date = date, State = state, Page = page, TotalPages = pages, Total = count,
            Records = await Summaries(query.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Number).ThenBy(r => r.Id)).Skip((page-1)*20).Take(20).ToListAsync() };
        Audit(patientId ?? Guid.Empty, "MedicalHistoryViewed"); await Db.SaveChangesAsync();
        return View(result);
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id, bool print = false)
    {
        var revision = await Graph(Visible()).SingleOrDefaultAsync(r => r.Id == id);
        if (revision == null) return NotFound();
        Audit(id, print ? "MedicalReportPrinted" : "MedicalRecordViewed"); await Db.SaveChangesAsync();
        var record = revision.MedicalRecord;
        return View(print ? "Print" : "Details", new MedicalRecordDetails { Revision = revision,
            CanWrite = await CanWrite(record), IsLatest = !await Db.MedicalRecordRevisions.AnyAsync(r => r.MedicalRecordId == record.Id && r.Number > revision.Number),
            History = await Summaries(Visible().Where(r => r.MedicalRecordId == record.Id).OrderBy(r => r.Number)).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> Pdf(Guid id)
    {
        var revision = await Graph(Visible()).SingleOrDefaultAsync(r => r.Id == id);
        if (revision == null) return NotFound();
        if (!MedicalRecordPdf.SupportsText(revision)) return UnprocessableEntity("This PDF renderer supports Latin-script text. Use Printable Report and your browser's Save as PDF for other scripts.");
        var bytes = MedicalRecordPdf.Render(revision);
        Audit(id, "MedicalReportExported"); await Db.SaveChangesAsync();
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(bytes, "application/pdf", $"medical-report-{revision.Id:N}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid appointmentId)
    {
        var appointment = await EligibleAppointment(appointmentId);
        if (appointment == null) return NotFound();
        var existing = await Db.MedicalRecordRevisions.Where(r => r.MedicalRecord.AppointmentId == appointmentId && r.AuthorUserId == Account).OrderByDescending(r => r.Number).FirstOrDefaultAsync();
        if (existing != null) return RedirectToAction(nameof(Details), new { id = existing.Id });
        return View("Form", Prepare(new MedicalRecordForm { AppointmentId = appointmentId, CreationToken = Guid.NewGuid() }, appointment.Patient));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(MedicalRecordForm model)
    {
        var appointment = await EligibleAppointment(model.AppointmentId);
        if (appointment == null) return NotFound();
        if (model.CreationToken == Guid.Empty) ModelState.AddModelError("", "Reload the consultation form.");
        var existing = await Db.MedicalRecordRevisions.Where(r => r.MedicalRecord.AppointmentId == model.AppointmentId).OrderByDescending(r => r.Number).FirstOrDefaultAsync();
        if (existing != null)
        {
            if (existing.AuthorUserId != Account) return NotFound();
            return RedirectToAction(nameof(Details), new { id = existing.Id });
        }
        if (await Db.MedicalRecords.AnyAsync(r => r.CreationToken == model.CreationToken)) ModelState.AddModelError("", "This form has already been used. Reload the consultation form.");
        ValidateForm(model, false); await ValidatePrescribing(model);
        if (!ModelState.IsValid) return View("Form", Prepare(model, appointment.Patient));
        var now = clock.GetUtcNow().UtcDateTime;
        var record = new MedicalRecord { AppointmentId = appointment.Id, PatientId = appointment.PatientId, DoctorId = appointment.DoctorId,
            AuthorUserId = Account, CreationToken = model.CreationToken, CreatedAtUtc = now };
        var revision = new MedicalRecordRevision { MedicalRecord = record, Number = 1, AuthorUserId = Account, CreatedAtUtc = now };
        Copy(model, revision); Db.MedicalRecords.Add(record); Db.MedicalRecordRevisions.Add(revision);
        TouchCare(appointment); appointment.Patient.Version = Guid.NewGuid(); Audit(record.Id, "MedicalRecordCreated");
        if (!await SaveAsync()) return View("Form", Prepare(model, appointment.Patient));
        return RedirectToAction(nameof(Details), new { id = revision.Id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var revision = await WritableDraft(id);
        return revision == null ? NotFound() : View("Form", Prepare(ToForm(revision), revision.MedicalRecord.Patient));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, MedicalRecordForm model)
    {
        var revision = await WritableDraft(id);
        if (revision == null) return NotFound();
        model.Id = id; model.IsAmendment = revision.Number > 1; model.AppointmentId = revision.MedicalRecord.AppointmentId;
        CheckVersion(revision.Version, model.Version); ValidateForm(model, model.IsAmendment); await ValidatePrescribing(model);
        if (!ModelState.IsValid) return View("Form", Prepare(model, revision.MedicalRecord.Patient));
        Db.MedicalPrescriptions.RemoveRange(revision.Prescriptions); revision.Prescriptions.Clear();
        Copy(model, revision); Db.MedicalPrescriptions.AddRange(revision.Prescriptions);
        revision.Version = Guid.NewGuid(); revision.MedicalRecord.Version = Guid.NewGuid();
        await TouchRecordCare(revision.MedicalRecord); Audit(id, "MedicalDraftUpdated");
        if (!await SaveAsync()) return View("Form", Prepare(model, revision.MedicalRecord.Patient));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(Guid id, Guid version)
    {
        var revision = await WritableDraft(id);
        if (revision == null) return NotFound();
        var form = ToForm(revision); CheckVersion(revision.Version, version); ValidateForm(form, revision.Number > 1); await ValidatePrescribing(form);
        if (string.IsNullOrWhiteSpace(revision.Symptoms) || string.IsNullOrWhiteSpace(revision.Diagnosis) || string.IsNullOrWhiteSpace(revision.Treatment))
            ModelState.AddModelError("", "Symptoms, diagnosis and treatment are required before finalizing.");
        if (!ModelState.IsValid) return View("Form", Prepare(form, revision.MedicalRecord.Patient));
        revision.FinalizedAtUtc = clock.GetUtcNow().UtcDateTime; revision.UpdatedAtUtc = revision.FinalizedAtUtc.Value; revision.Version = Guid.NewGuid();
        revision.MedicalRecord.Version = Guid.NewGuid(); await TouchRecordCare(revision.MedicalRecord); Audit(id, "MedicalRecordFinalized");
        if (!await SaveAsync()) return View("Form", Prepare(form, revision.MedicalRecord.Patient));
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Amend(Guid id, Guid version, string? reason)
    {
        var revision = await Graph(MedicalRecordAccess.Visible(Db, Account)).SingleOrDefaultAsync(r => r.Id == id && r.AuthorUserId == Account);
        if (revision == null || !await CanWrite(revision.MedicalRecord)) return NotFound();
        if (revision.FinalizedAtUtc == null || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000) return BadRequest("A finalized revision and an amendment reason (1–1000 characters) are required.");
        if (!CheckVersion(revision.Version, version) || await Db.MedicalRecordRevisions.AnyAsync(r => r.MedicalRecordId == revision.MedicalRecordId && r.Number > revision.Number)) return Conflict("This encounter changed. Reload before starting an amendment.");
        var form = ToForm(revision); form.AmendmentReason = reason.Trim();
        var next = new MedicalRecordRevision { MedicalRecord = revision.MedicalRecord, Number = revision.Number + 1, AuthorUserId = Account, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        Copy(form, next); Db.MedicalRecordRevisions.Add(next); revision.MedicalRecord.Version = Guid.NewGuid();
        await TouchRecordCare(revision.MedicalRecord); Audit(next.Id, "MedicalAmendmentStarted");
        if (!await SaveAsync()) return Conflict("This encounter changed. Reload before starting an amendment.");
        return RedirectToAction(nameof(Edit), new { id = next.Id });
    }

    private Task<Appointment?> EligibleAppointment(Guid id) => MedicalRecordAccess.CareAppointments(Db, Account)
        .Include(a => a.Patient).Include(a => a.Doctor).SingleOrDefaultAsync(a => a.Id == id && !a.Patient.IsArchived);
    private async Task<bool> CanWrite(MedicalRecord record)
    {
        if (record.AuthorUserId != Account || !await MedicalRecordAccess.LinkedDoctors(Db, Account).AnyAsync(d => d.Id == record.DoctorId)) return false;
        // Track the authorization snapshot so concurrent cancellation/reassignment invalidates a write.
        return await MedicalRecordAccess.CareAppointments(Db, Account).Include(a => a.Doctor)
            .SingleOrDefaultAsync(a => a.Id == record.AppointmentId && a.PatientId == record.PatientId && a.DoctorId == record.DoctorId) != null;
    }
    private async Task<MedicalRecordRevision?> WritableDraft(Guid id)
    {
        var revision = await Graph(Db.MedicalRecordRevisions).SingleOrDefaultAsync(r => r.Id == id && r.FinalizedAtUtc == null && r.AuthorUserId == Account);
        return revision != null && await CanWrite(revision.MedicalRecord) ? revision : null;
    }
    private void TouchCare(Appointment appointment)
    {
        // Coordinate writes with appointment reassignment/cancellation and doctor unlink/deactivation.
        appointment.Version = Guid.NewGuid(); appointment.Doctor.Version = Guid.NewGuid();
    }
    private async Task TouchRecordCare(MedicalRecord record)
    {
        var appointment = await Db.Appointments.Include(a => a.Doctor).SingleAsync(a => a.Id == record.AppointmentId);
        TouchCare(appointment);
    }
    private void ValidateForm(MedicalRecordForm form, bool amendment)
    {
        if (form.Prescriptions.Count > 20) ModelState.AddModelError("", "At most 20 prescriptions are allowed per revision.");
        if (amendment && string.IsNullOrWhiteSpace(form.AmendmentReason)) ModelState.AddModelError(nameof(form.AmendmentReason), "An amendment reason is required.");
    }
    private void Copy(MedicalRecordForm form, MedicalRecordRevision revision)
    {
        revision.Symptoms = (form.Symptoms ?? "").Trim(); revision.Observations = (form.Observations ?? "").Trim();
        revision.Diagnosis = (form.Diagnosis ?? "").Trim(); revision.Treatment = (form.Treatment ?? "").Trim();
        revision.AmendmentReason = revision.Number > 1 ? Optional(form.AmendmentReason) : null;
        revision.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        revision.Prescriptions = form.Prescriptions.Where(p => !p.IsEmpty).Select((p, i) => new MedicalPrescription { Position = i,
            MedicineId = p.MedicineId, AuthorizedQuantity = p.AuthorizedQuantity, DispensingUnit = Optional(p.DispensingUnit), LineageId = p.LineageId,
            Medication = p.Medication!.Trim(), Dosage = p.Dosage!.Trim(), Route = p.Route!.Trim(), Frequency = p.Frequency!.Trim(), Duration = p.Duration!.Trim(), Instructions = Optional(p.Instructions) }).ToList();
    }
    private static MedicalRecordForm ToForm(MedicalRecordRevision r) => new() { Id = r.Id, AppointmentId = r.MedicalRecord.AppointmentId, Version = r.Version,
        Symptoms = r.Symptoms, Observations = r.Observations, Diagnosis = r.Diagnosis, Treatment = r.Treatment, AmendmentReason = r.AmendmentReason,
        IsAmendment = r.Number > 1, Prescriptions = r.Prescriptions.OrderBy(p => p.Position).Select(p => new PrescriptionForm {
            SourceId = p.Id, MedicineId = p.MedicineId, AuthorizedQuantity = p.AuthorizedQuantity, DispensingUnit = p.DispensingUnit, LineageId = p.LineageId,
            Medication = p.Medication, Dosage = p.Dosage, Route = p.Route, Frequency = p.Frequency, Duration = p.Duration, Instructions = p.Instructions }).ToList() };
    private MedicalRecordForm Prepare(MedicalRecordForm form, Patient patient)
    {
        form.Medicines = Db.Medicines.AsNoTracking().Where(m => m.IsActive).OrderBy(m => m.Code).ToList();
        form.PatientLabel = patient.PatientNumber + " — " + patient.FirstName + " " + patient.LastName;
        while (form.Prescriptions.Count < 20) form.Prescriptions.Add(new PrescriptionForm());
        return form;
    }

    private async Task ValidatePrescribing(MedicalRecordForm form)
    {
        var lineages = new HashSet<Guid>();
        foreach (var p in form.Prescriptions.Where(p => !p.IsEmpty))
        {
            MedicalPrescription? source = null;
            if (p.SourceId.HasValue)
            {
                source = await Db.MedicalPrescriptions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == p.SourceId && s.MedicalRecordRevision.MedicalRecord.AppointmentId == form.AppointmentId && s.MedicalRecordRevision.AuthorUserId == Account);
                if (source == null) { ModelState.AddModelError("", "Invalid prescription source. Reload the draft."); continue; }
            }
            p.LineageId = source?.LineageId;
            if (p.MedicineId == null && p.AuthorizedQuantity == null && string.IsNullOrWhiteSpace(p.DispensingUnit)) continue; // Historical/free-text orders are never dispensable.
            var medicine = await Db.Medicines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == p.MedicineId && m.IsActive);
            if (medicine == null || p.AuthorizedQuantity == null || !PharmacyRules.ValidQuantity(p.AuthorizedQuantity.Value) || p.DispensingUnit?.Trim() != medicine.DispensingUnit)
            { ModelState.AddModelError("", "Select an active exact catalogue medicine and enter an explicit positive total quantity and its exact dispensing unit."); continue; }
            if (p.Medication?.Trim() != medicine.Name) ModelState.AddModelError("", "Medication must exactly match the selected catalogue medicine name. Strength and formulation are determined by that explicit selection.");
            if (source?.MedicineId != null && (source.MedicineId != p.MedicineId || p.AuthorizedQuantity > source.AuthorizedQuantity) && !p.ConfirmAuthorizationChange)
                ModelState.AddModelError("", "Explicitly confirm a changed medicine or increased total authorization. Prior dispensing is retained.");
            if (source?.MedicineId != p.MedicineId || p.LineageId == null) p.LineageId = Guid.NewGuid();
            if (!lineages.Add(p.LineageId.Value)) ModelState.AddModelError("", "A prescription authorization cannot be duplicated in one revision.");
            var net = await Db.PharmacyDispenses.Where(d => d.LineageId == p.LineageId).SumAsync(d => (decimal?)d.Quantity) ?? 0;
            if (p.AuthorizedQuantity < net) ModelState.AddModelError("", "Authorized total cannot be lower than the quantity already dispensed.");
        }
    }
}
