using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class DepartmentsController(MediCareDbContext db) : ClinicalAdministrationControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        var total = await Db.Departments.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / 20d));
        page = Math.Clamp(page, 1, pages);
        ViewBag.Page = page; ViewBag.TotalPages = pages;
        return View(await Db.Departments.AsNoTracking().OrderBy(d => d.Name).ThenBy(d => d.Id)
            .Skip((page - 1) * 20).Take(20).ToListAsync());
    }

    [HttpGet]
    public IActionResult Create() => View("Form", new DepartmentFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DepartmentFormViewModel model)
    {
        model.Id = Guid.Empty;
        await ValidateAsync(model);
        if (!ModelState.IsValid) return View("Form", model);
        var department = new Department { Name = model.Name.Trim(), NormalizedName = model.Name.Trim().ToUpperInvariant(), Description = Optional(model.Description), IsActive = model.IsActive };
        Db.Departments.Add(department); Audit(department.Id, "DepartmentCreated");
        return await SaveAsync() ? RedirectToAction(nameof(Index)) : View("Form", model);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var department = await Db.Departments.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
        return department == null ? NotFound() : View("Form", new DepartmentFormViewModel
        { Id = department.Id, Version = department.Version, Name = department.Name, Description = department.Description, IsActive = department.IsActive });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DepartmentFormViewModel model)
    {
        var department = await Db.Departments.SingleOrDefaultAsync(d => d.Id == model.Id);
        if (department == null) return NotFound();
        await ValidateAsync(model);
        CheckVersion(department.Version, model.Version);
        if (!model.IsActive && await Db.Doctors.AnyAsync(d => d.DepartmentId == model.Id && d.IsActive))
            ModelState.AddModelError(nameof(model.IsActive), "Move or deactivate active doctors before deactivating this department.");
        if (!ModelState.IsValid) return View("Form", model);
        department.Name = model.Name.Trim(); department.NormalizedName = department.Name.ToUpperInvariant();
        department.Description = Optional(model.Description); department.IsActive = model.IsActive;
        department.Version = Guid.NewGuid(); Audit(department.Id, "DepartmentUpdated");
        return await SaveAsync() ? RedirectToAction(nameof(Index)) : View("Form", model);
    }

    private async Task ValidateAsync(DepartmentFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Name)) { ModelState.AddModelError(nameof(model.Name), "Department name is required."); return; }
        var key = model.Name.Trim().ToUpperInvariant();
        if (await Db.Departments.AnyAsync(d => d.Id != model.Id && d.NormalizedName == key))
            ModelState.AddModelError(nameof(model.Name), "A department with this name already exists.");
    }
}
