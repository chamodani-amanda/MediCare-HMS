using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Pharmacist")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PharmacyController(MediCareDbContext db, TimeProvider clock) : ClinicalAdministrationControllerBase(db)
{
    private string Account => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private DateOnly Today => HospitalTime.Today(clock);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private async Task<PharmacyPage<T>> Page<T>(IQueryable<T> query, int page, string? search = null, string? filter = null)
    {
        var count = await query.CountAsync(); var pages = Math.Max(1, (int)Math.Ceiling(count / 20d)); page = Math.Clamp(page, 1, pages);
        return new() { Rows = await query.Skip((page-1)*20).Take(20).ToListAsync(), Page = page, Pages = pages, Total = count, Search = search, Filter = filter };
    }
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        search = (search ?? "").Trim(); if (search.Length > 100) return BadRequest();
        var query = PharmacyRules.Eligible(Db).AsNoTracking();
        if (search.Length > 0) { var term = search.ToUpperInvariant(); query = query.Where(p => p.MedicalRecordRevision.MedicalRecord.Patient.PatientNumber.ToUpper().Contains(term) || p.Medicine!.Code.Contains(term)); }
        var result = await Page(query.OrderBy(p => p.MedicalRecordRevision.FinalizedAtUtc).ThenBy(p => p.Id).Select(p => new PharmacyPrescriptionRow {
            Id = p.Id, PatientLabel = p.MedicalRecordRevision.MedicalRecord.Patient.PatientNumber + " — " + p.MedicalRecordRevision.MedicalRecord.Patient.FirstName + " " + p.MedicalRecordRevision.MedicalRecord.Patient.LastName,
            MedicineLabel = p.Medicine!.Code + " / " + p.Medicine.Name + " / " + p.Medicine.Strength + " / " + p.Medicine.Formulation + " / " + p.DispensingUnit,
            Instructions = p.Dosage + "; " + p.Route + "; " + p.Frequency + "; " + p.Duration + "; " + p.Instructions,
            Authorized = p.AuthorizedQuantity!.Value,
            Dispensed = Db.PharmacyDispenses.Where(d => d.LineageId == p.LineageId).Sum(d => (decimal?)d.Quantity) ?? 0
        }), page, search);
        Audit(Guid.Empty, "PharmacyWorklistViewed"); await Db.SaveChangesAsync(); return View(result);
    }
    public async Task<IActionResult> Batches(string? filter, int page = 1)
    {
        if (filter is not (null or "expired" or "near")) return BadRequest();
        var query = Db.MedicineBatches.AsNoTracking().Include(b => b.Medicine).AsQueryable(); var today = Today; var soon = today.AddDays(90);
        if (filter == "expired") query = query.Where(b => b.ExpiresOn <= today && b.AvailableQuantity > 0);
        if (filter == "near") query = query.Where(b => b.ExpiresOn > today && b.ExpiresOn <= soon && b.AvailableQuantity > 0);
        return View(await Page(query.OrderBy(b => b.ExpiresOn).ThenBy(b => b.Id), page, filter: filter));
    }
    public async Task<IActionResult> LowStock(int page = 1)
    {
        var today = Today;
        var query = Db.Medicines.AsNoTracking().Where(m => m.IsActive).Select(m => new StockAlertRow { Medicine = m,
            Available = Db.MedicineBatches.Where(b => b.MedicineId == m.Id && b.ExpiresOn > today).Sum(b => (decimal?)b.AvailableQuantity) ?? 0 }).Where(r => r.Available <= r.Medicine.LowStockThreshold);
        return View(await Page(query.OrderBy(r => r.Medicine.Code), page));
    }
    public async Task<IActionResult> Receive(Guid medicineId)
    {
        if (!await Db.Medicines.AnyAsync(m => m.Id == medicineId && m.IsActive)) return NotFound();
        return View("StockForm", new StockForm { MedicineId = medicineId, Token = Guid.NewGuid() });
    }
    public async Task<IActionResult> Adjust(Guid id)
    {
        var batch = await Db.MedicineBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id); if (batch == null) return NotFound();
        return View("StockForm", new StockForm { BatchId = id, MedicineId = batch.MedicineId, Version = batch.Version, Token = Guid.NewGuid() });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(StockForm model)
    {
        if (!await Db.Medicines.AnyAsync(m => m.Id == model.MedicineId && m.IsActive)) return NotFound();
        if (await Db.InventoryMovements.AnyAsync(m => m.Token == model.Token && m.ActorUserId == Account)) return RedirectToAction(nameof(Batches));
        if (await Db.MedicineBatches.AnyAsync(b => b.MedicineId == model.MedicineId && b.BatchNumber == (model.BatchNumber ?? "").Trim().ToUpper())) ModelState.AddModelError("", "This medicine batch already exists. Use a reasoned stock adjustment for a verified additional receipt or correction.");
        if (!PharmacyRules.ValidQuantity(model.Quantity) || model.Token == Guid.Empty || string.IsNullOrWhiteSpace(model.BatchNumber) || string.IsNullOrWhiteSpace(model.Supplier) || model.ExpiresOn <= Today || model.ExpiresOn.Year > 9998 || model.ManufacturedOn > Today || model.ManufacturedOn > model.ExpiresOn)
            ModelState.AddModelError("", "Enter a positive quantity (up to three decimal places), batch, supplier and a future expiry date; manufacture cannot be in the future.");
        if (!ModelState.IsValid) return View("StockForm", model);
        var batch = new MedicineBatch { MedicineId = model.MedicineId, BatchNumber = model.BatchNumber!.Trim().ToUpperInvariant(), Supplier = model.Supplier!.Trim(), ManufacturedOn = model.ManufacturedOn, ExpiresOn = model.ExpiresOn, ReceivedAtUtc = Now };
        Db.MedicineBatches.Add(batch); Db.InventoryMovements.Add(Movement(batch.Id, model.Token, model.Quantity, "Receive", model.Reason)); Audit(batch.Id, "PharmacyStockReceived");
        return await SaveAsync() ? RedirectToAction(nameof(Batches)) : View("StockForm", model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(StockForm model)
    {
        var batch = await Db.MedicineBatches.SingleOrDefaultAsync(b => b.Id == model.BatchId); if (batch == null) return NotFound();
        if (await Db.InventoryMovements.AnyAsync(m => m.Token == model.Token && m.ActorUserId == Account)) return RedirectToAction(nameof(Batches));
        CheckVersion(batch.Version, model.Version);
        if (model.Quantity is < -1000000000m or > 1000000000m || !PharmacyRules.ValidQuantity(Math.Abs(model.Quantity)) || model.Token == Guid.Empty || batch.AvailableQuantity + model.Quantity < 0) ModelState.AddModelError("", "Adjustment would produce an invalid balance.");
        if (model.Quantity > 0 && batch.ExpiresOn <= Today) ModelState.AddModelError("", "Expired stock cannot be added to available stock.");
        if (!ModelState.IsValid) return View("StockForm", model);
        // Balance and version are changed only by the movement trigger.
        var movement = Movement(batch.Id, model.Token, model.Quantity, "Adjust", model.Reason); movement.ExpectedBatchVersion = model.Version;
        Db.InventoryMovements.Add(movement); Audit(batch.Id, "PharmacyStockAdjusted");
        return await SaveAsync() ? RedirectToAction(nameof(Batches)) : View("StockForm", model);
    }
    public async Task<IActionResult> History(Guid? batchId, int page = 1)
    {
        var query = Db.InventoryMovements.AsNoTracking().Include(m => m.Batch).ThenInclude(b => b.Medicine).AsQueryable(); if (batchId.HasValue) query = query.Where(m => m.BatchId == batchId);
        return View(await Page(query.OrderByDescending(m => m.OccurredAtUtc).ThenBy(m => m.Id), page));
    }
    public async Task<IActionResult> Dispense(Guid id)
    {
        if (!await PharmacyRules.Eligible(Db).AnyAsync(p => p.Id == id)) return NotFound();
        Audit(id, "PharmacyPrescriptionViewed"); await Db.SaveChangesAsync();
        return await DispenseView(new DispenseForm { PrescriptionId = id, Token = Guid.NewGuid() });
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Dispense(DispenseForm model)
    {
        if (await Db.PharmacyDispenses.AnyAsync(d => d.Token == model.Token && d.ActorUserId == Account)) return RedirectToAction(nameof(Dispensings));
        if (!PharmacyRules.ValidQuantity(model.Quantity) || model.Token == Guid.Empty || (model.ExceptionBatchId.HasValue && string.IsNullOrWhiteSpace(model.ExceptionReason))) ModelState.AddModelError("", "Enter a positive quantity and a reason for any FEFO exception.");
        if (!ModelState.IsValid) return await DispenseView(model);
        // PostgreSQL triggers serialize on the encounter, then batches, and recheck eligibility and remaining authorization.
        var prescription = await PharmacyRules.Eligible(Db).SingleOrDefaultAsync(p => p.Id == model.PrescriptionId); if (prescription == null) return NotFound();
        var used = await Db.PharmacyDispenses.Where(d => d.LineageId == prescription.LineageId).SumAsync(d => (decimal?)d.Quantity) ?? 0;
        if (model.Quantity > prescription.AuthorizedQuantity - used) { ModelState.AddModelError("", "Quantity exceeds remaining authorization."); return await DispenseView(model); }
        var today = Today;
        var batches = await Db.MedicineBatches.AsNoTracking().Where(b => b.MedicineId == prescription.MedicineId && b.ExpiresOn > today && b.AvailableQuantity > 0 && (!model.ExceptionBatchId.HasValue || b.Id == model.ExceptionBatchId))
            .OrderBy(b => b.ExpiresOn).ThenBy(b => b.ReceivedAtUtc).ThenBy(b => b.Id).ToListAsync();
        if (batches.Sum(b => b.AvailableQuantity) < model.Quantity) { ModelState.AddModelError("", "Insufficient eligible stock. Reload availability."); return await DispenseView(model); }
        var dispense = new PharmacyDispense { Token = model.Token, PrescriptionId = prescription.Id, LineageId = prescription.LineageId!.Value, Quantity = model.Quantity, ActorUserId = Account, OccurredAtUtc = Now, Reason = Optional(model.ExceptionReason) };
        var remaining = model.Quantity;
        foreach (var batch in batches) { var take = Math.Min(remaining, batch.AvailableQuantity); if (take <= 0) break;
            dispense.Movements.Add(Movement(batch.Id, Guid.NewGuid(), -take, "Dispense", Optional(model.ExceptionReason) ?? "FEFO dispensing")); remaining -= take; }
        Db.PharmacyDispenses.Add(dispense); Audit(dispense.Id, "PrescriptionDispensed");
        if (!await SaveAsync()) return await DispenseView(model);
        return RedirectToAction(nameof(Dispensings));
    }
    public async Task<IActionResult> Dispensings(int page = 1)
    {
        // No encounter graph or notes are returned to pharmacy staff.
        Audit(Guid.Empty, "PharmacyDispenseHistoryViewed"); await Db.SaveChangesAsync();
        return View(await Page(Db.PharmacyDispenses.AsNoTracking().OrderByDescending(d => d.OccurredAtUtc).ThenBy(d => d.Id), page));
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(Guid id, Guid token, string? reason, bool returnedToQuarantine)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 1000 || token == Guid.Empty || !returnedToQuarantine) return BadRequest("A reason and confirmation that all issued goods have been returned to quarantine are required.");
        if (await Db.PharmacyDispenses.AnyAsync(d => d.Token == token && d.ActorUserId == Account)) return RedirectToAction(nameof(Dispensings));
        var original = await Db.PharmacyDispenses.AsNoTracking().Include(d => d.Movements).SingleOrDefaultAsync(d => d.Id == id && d.ReversesId == null);
        if (original == null) return NotFound(); if (await Db.PharmacyDispenses.AnyAsync(d => d.ReversesId == id)) return Conflict("Already reversed.");
        var reversal = new PharmacyDispense { PrescriptionId = original.PrescriptionId, LineageId = original.LineageId, Token = token, ReversesId = id, Quantity = -original.Quantity, Reason = reason.Trim(), ActorUserId = Account, OccurredAtUtc = Now };
        foreach (var m in original.Movements) reversal.Movements.Add(Movement(m.BatchId, Guid.NewGuid(), -m.Quantity, "QuarantineReturn", reason.Trim()));
        Db.PharmacyDispenses.Add(reversal); Audit(id, "PharmacyDispenseReversed");
        return await SaveAsync() ? RedirectToAction(nameof(Dispensings)) : Conflict("Reversal conflicts with current information. Reload.");
    }
    private InventoryMovement Movement(Guid batch, Guid token, decimal quantity, string kind, string reason) => new() { BatchId = batch, Token = token, Quantity = quantity, Kind = kind, Reason = reason.Trim(), ActorUserId = Account, OccurredAtUtc = Now };
    private async Task<IActionResult> DispenseView(DispenseForm model)
    {
        model.Prescription = await PharmacyRules.Eligible(Db).AsNoTracking().Where(p => p.Id == model.PrescriptionId).Select(p => new PharmacyPrescriptionRow {
            Id = p.Id, PatientLabel = p.MedicalRecordRevision.MedicalRecord.Patient.PatientNumber + " — " + p.MedicalRecordRevision.MedicalRecord.Patient.FirstName + " " + p.MedicalRecordRevision.MedicalRecord.Patient.LastName,
            MedicineLabel = p.Medicine!.Code + " / " + p.Medicine.Name + " / " + p.Medicine.Strength + " / " + p.Medicine.Formulation + " / " + p.DispensingUnit,
            Instructions = p.Dosage + "; " + p.Route + "; " + p.Frequency + "; " + p.Duration + "; " + p.Instructions,
            Authorized = p.AuthorizedQuantity!.Value, Dispensed = Db.PharmacyDispenses.Where(d => d.LineageId == p.LineageId).Sum(d => (decimal?)d.Quantity) ?? 0
        }).SingleOrDefaultAsync();
        if (model.Prescription == null) return NotFound();
        var medicineId = await Db.MedicalPrescriptions.Where(p => p.Id == model.PrescriptionId).Select(p => p.MedicineId).SingleAsync(); var today = Today;
        model.Batches = await Db.MedicineBatches.AsNoTracking().Where(b => b.MedicineId == medicineId && b.ExpiresOn > today && b.AvailableQuantity > 0).OrderBy(b => b.ExpiresOn).ThenBy(b => b.ReceivedAtUtc).ThenBy(b => b.Id).ToListAsync();
        return View("Dispense", model);
    }
}
