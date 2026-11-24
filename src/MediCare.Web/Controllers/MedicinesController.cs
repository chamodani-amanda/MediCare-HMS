using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator,Pharmacist")]
public class MedicinesController(MediCareDbContext db) : ClinicalAdministrationControllerBase(db)
{
    public async Task<IActionResult> Index(string? search, string? filter, string? sort, int page = 1)
    {
        search = (search ?? "").Trim();
        if (search.Length > 100 || filter is not (null or "active" or "inactive") || sort is not (null or "code" or "name")) return BadRequest();
        var query = Db.Medicines.AsNoTracking();
        if (search.Length > 0) { var term = search.ToUpperInvariant(); query = query.Where(m => m.Code.Contains(term) || m.Name.ToUpper().Contains(term) || m.GenericName.ToUpper().Contains(term)); }
        if (filter != null) query = query.Where(m => m.IsActive == (filter == "active"));
        var total = await query.CountAsync(); var pages = Math.Max(1, (int)Math.Ceiling(total / 20d)); page = Math.Clamp(page, 1, pages);
        return View(new PharmacyPage<Medicine> { Rows = await (sort == "name" ? query.OrderBy(m => m.Name).ThenBy(m => m.Id) : query.OrderBy(m => m.Code)).Skip((page-1)*20).Take(20).ToListAsync(), Page = page, Pages = pages, Total = total, Search = search, Filter = filter, Sort = sort });
    }
    public IActionResult Create() => View("Form", new Medicine());
    public async Task<IActionResult> Edit(Guid id) { var m = await Db.Medicines.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id); return m == null ? NotFound() : View("Form", m); }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Code,Name,GenericName,Strength,Formulation,DispensingUnit,LowStockThreshold,IsActive")] Medicine model)
    {
        Normalize(model);
        if (await Db.Medicines.AnyAsync(m => m.Code == model.Code)) ModelState.AddModelError(nameof(model.Code), "This medicine code already exists.");
        if (!ModelState.IsValid) return View("Form", model);
        Db.Medicines.Add(model); Audit(model.Id, "MedicineCreated");
        return await SaveAsync() ? RedirectToAction(nameof(Index)) : View("Form", model);
    }
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, [Bind("Code,Name,GenericName,Strength,Formulation,DispensingUnit,LowStockThreshold,IsActive,Version")] Medicine model)
    {
        var current = await Db.Medicines.SingleOrDefaultAsync(m => m.Id == id); if (current == null) return NotFound();
        Normalize(model); CheckVersion(current.Version, model.Version);
        // Identity is immutable once registered. Register a distinct item for a different strength/unit/form.
        if (current.Code != model.Code || current.Name != model.Name || current.GenericName != model.GenericName || current.Strength != model.Strength || current.Formulation != model.Formulation || current.DispensingUnit != model.DispensingUnit)
            ModelState.AddModelError("", "Medicine identity cannot be changed. Register a distinct item; existing prescriptions keep their exact identity.");
        if (!ModelState.IsValid) { model.Id = id; return View("Form", model); }
        current.LowStockThreshold = model.LowStockThreshold; current.IsActive = model.IsActive; current.Version = Guid.NewGuid(); Audit(id, "MedicineUpdated");
        return await SaveAsync() ? RedirectToAction(nameof(Index)) : View("Form", model);
    }
    private void Normalize(Medicine m)
    {
        m.Code = (m.Code ?? "").Trim().ToUpperInvariant(); m.Name = (m.Name ?? "").Trim(); m.GenericName = (m.GenericName ?? "").Trim();
        m.Strength = (m.Strength ?? "").Trim(); m.Formulation = (m.Formulation ?? "").Trim(); m.DispensingUnit = (m.DispensingUnit ?? "").Trim();
        if (new[] { m.Code,m.Name,m.GenericName,m.Strength,m.Formulation,m.DispensingUnit }.Any(string.IsNullOrWhiteSpace) || decimal.Round(m.LowStockThreshold,3) != m.LowStockThreshold) ModelState.AddModelError("", "All medicine identity fields are required; quantities support at most three decimal places.");
    }
}
