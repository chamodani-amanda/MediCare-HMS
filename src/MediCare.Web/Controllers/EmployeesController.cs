using System.Security.Claims;
using MediCare.Web.Services;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class EmployeesController : Controller
{
   private readonly MediCareDbContext _db;
private readonly SecurityAuditService _audit;

public EmployeesController(
    MediCareDbContext db,
    SecurityAuditService audit)
{
    _db = db;
    _audit = audit;
}

    // GET: Employees/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        await LoadDepartmentsAsync();
        return View(new EmployeeCreateViewModel());
    }

    // POST: Employees/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        EmployeeCreateViewModel model)
    {
        string employeeNumber =
            (model.EmployeeNumber ?? "")
            .Trim()
            .ToUpperInvariant();

        if (employeeNumber.Length < 3 ||
            employeeNumber.Length > 30)
        {
            ModelState.AddModelError(
                nameof(model.EmployeeNumber),
                "Employee number must have 3 to 30 characters.");
        }

        if (model.HireDate is null)
        {
            ModelState.AddModelError(
                nameof(model.HireDate),
                "Hire date is required.");
        }

        if (model.DepartmentId.HasValue)
        {
            bool departmentExists = await _db.Departments
                .AnyAsync(d =>
                    d.Id == model.DepartmentId.Value &&
                    d.IsActive);

            if (!departmentExists)
            {
                ModelState.AddModelError(
                    nameof(model.DepartmentId),
                    "Select a valid active department.");
            }
        }

        if (!ModelState.IsValid)
        {
            await LoadDepartmentsAsync();
            return View(model);
        }

        bool duplicate = await _db.Employees
            .AnyAsync(e =>
                e.EmployeeNumber == employeeNumber);

        if (duplicate)
        {
            ModelState.AddModelError(
                nameof(model.EmployeeNumber),
                "This employee number already exists.");

            await LoadDepartmentsAsync();
            return View(model);
        }

        var employee = new Employee
        {
            Id = Guid.NewGuid(),

            EmployeeNumber = employeeNumber,

            FirstName = model.FirstName.Trim(),

            LastName = model.LastName.Trim(),

            JobTitle = string.IsNullOrWhiteSpace(model.JobTitle)
                ? null
                : model.JobTitle.Trim(),

            PhoneNumber =
                string.IsNullOrWhiteSpace(model.PhoneNumber)
                ? null
                : model.PhoneNumber.Trim(),

            Email = string.IsNullOrWhiteSpace(model.Email)
                ? null
                : model.Email.Trim(),

            HireDate = model.HireDate!.Value,

            DepartmentId = model.DepartmentId,

            IdentityUserId = null,

            IsActive = true,

            Version = Guid.NewGuid(),

            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        var actorId = User.FindFirstValue(
    ClaimTypes.NameIdentifier);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Forbid();
}

        _db.Employees.Add(employee);

        _audit.QueueRecordSuccess(
    actorId,
    "Employee",
    employee.Id,
    "EmployeeCreated");

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg &&
                  pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            ModelState.AddModelError(
                nameof(model.EmployeeNumber),
                "Employee number must be unique.");

            await LoadDepartmentsAsync();
            return View(model);
        }

        TempData["SuccessMessage"] =
            $"Employee {employee.EmployeeNumber} registered successfully.";

        return RedirectToAction(nameof(Create));
    }

    private async Task LoadDepartmentsAsync()
    {
        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .Select(d => new
            {
                d.Id,
                d.Name
            })
            .ToListAsync();

        ViewBag.DepartmentOptions = departments
            .Select(d => new SelectListItem
            {
                Text = d.Name,
                Value = d.Id.ToString()
            })
            .ToList();
    }

// GET: Employees
[HttpGet]
public async Task<IActionResult> Index(
    string? search, int page = 1)
{
    const int pageSize = 20;

    string searchTerm = (search ?? "").Trim();

    if (searchTerm.Length > 100)
    {
        return BadRequest("Search text is too long.");
    }

    var query = _db.Employees
        .AsNoTracking()
        .AsQueryable();

    if (!string.IsNullOrWhiteSpace(searchTerm))
    {
        string pattern = $"%{searchTerm}%";

        query = query.Where(e =>
            EF.Functions.ILike(e.EmployeeNumber, pattern) ||
            EF.Functions.ILike(e.FirstName, pattern) ||
            EF.Functions.ILike(e.LastName, pattern) ||
            EF.Functions.ILike(
                e.FirstName + " " + e.LastName,
                pattern));
    }

    int totalEmployees = await query.CountAsync();

    int totalPages = Math.Max(
        1,
        (int)Math.Ceiling(totalEmployees / (double)pageSize));

    page = Math.Clamp(page, 1, totalPages);

    var employees = await query
        .OrderBy(e => e.LastName)
        .ThenBy(e => e.FirstName)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(e => new EmployeeListItemViewModel
        {
            Id = e.Id,
            EmployeeNumber = e.EmployeeNumber,
            FullName = e.FirstName + " " + e.LastName,
            JobTitle = e.JobTitle,
            DepartmentName = e.Department != null
                ? e.Department.Name
                : null,
            IsActive = e.IsActive
        })
        .ToListAsync();

    return View(new EmployeeListViewModel
    {
        Employees = employees,
        SearchTerm = searchTerm,
        Page = page,
        TotalPages = totalPages,
        TotalEmployees = totalEmployees
    });
}  
// GET: Employees/Details/{id}
[HttpGet]
public async Task<IActionResult> Details(Guid id)
{
    if (id == Guid.Empty)
        return BadRequest();

    var employee = await _db.Employees
        .AsNoTracking()
        .Include(e => e.Department)
        .FirstOrDefaultAsync(e => e.Id == id);

    if (employee == null)
        return NotFound();

    return View(employee);
}  
// GET: Employees/Edit/{id}
[HttpGet]
public async Task<IActionResult> Edit(Guid id)
{
    if (id == Guid.Empty)
        return BadRequest();

    var employee = await _db.Employees
        .AsNoTracking()
        .FirstOrDefaultAsync(e => e.Id == id);

    if (employee == null)
        return NotFound();

    var model = new EmployeeEditViewModel
    {
        Id = employee.Id,
        Version = employee.Version,
        EmployeeNumber = employee.EmployeeNumber,
        FirstName = employee.FirstName,
        LastName = employee.LastName,
        JobTitle = employee.JobTitle,
        PhoneNumber = employee.PhoneNumber,
        Email = employee.Email,
        HireDate = employee.HireDate,
        DepartmentId = employee.DepartmentId
    };

    await LoadDepartmentsAsync();

    return View(model);
}

// POST: Employees/Edit/{id}
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Edit(
    Guid id,
    EmployeeEditViewModel model)
{
    if (id == Guid.Empty ||
        id != model.Id ||
        model.Version == Guid.Empty)
    {
        return BadRequest();
    }

    var employee = await _db.Employees
        .FirstOrDefaultAsync(e => e.Id == id);

    if (employee == null)
        return NotFound();

    // The employee number cannot be changed here.
    model.EmployeeNumber = employee.EmployeeNumber;

    // Check whether another administrator
    // updated the record after this form opened.
    if (employee.Version != model.Version)
    {
        ModelState.AddModelError(
            "",
            "This employee was modified by another user. " +
            "Please reload the page before editing again.");

        await LoadDepartmentsAsync();
        return View(model);
    }

    // Validate the selected department.
    if (model.DepartmentId.HasValue)
    {
        bool departmentExists = await _db.Departments
            .AnyAsync(d =>
                d.Id == model.DepartmentId.Value &&
                d.IsActive);

        if (!departmentExists)
        {
            ModelState.AddModelError(
                nameof(model.DepartmentId),
                "Select a valid active department.");
        }
    }

    if (!ModelState.IsValid)
    {
        await LoadDepartmentsAsync();
        return View(model);
    }

    if (model.HireDate is not DateOnly hireDate)
    {
        ModelState.AddModelError(
            nameof(model.HireDate),
            "Hire date is required.");

        await LoadDepartmentsAsync();
        return View(model);
    }

    // Update only permitted employee fields.
    employee.FirstName = model.FirstName.Trim();
    employee.LastName = model.LastName.Trim();

    employee.JobTitle =
        string.IsNullOrWhiteSpace(model.JobTitle)
            ? null
            : model.JobTitle.Trim();

    employee.PhoneNumber =
        string.IsNullOrWhiteSpace(model.PhoneNumber)
            ? null
            : model.PhoneNumber.Trim();

    employee.Email =
        string.IsNullOrWhiteSpace(model.Email)
            ? null
            : model.Email.Trim();

    employee.HireDate = hireDate;
    employee.DepartmentId = model.DepartmentId;

    employee.UpdatedAtUtc = DateTime.UtcNow;

    // Change the concurrency version.
    employee.Version = Guid.NewGuid();

    var actorId = User.FindFirstValue(
    ClaimTypes.NameIdentifier);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Forbid();
}

_audit.QueueRecordSuccess(
    actorId,
    "Employee",
    employee.Id,
    "EmployeeUpdated");

    try
    {
        await _db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        ModelState.AddModelError(
            "",
            "Another user has updated this employee. " +
            "Please reload the page and try again.");

        await LoadDepartmentsAsync();
        return View(model);
    }

    TempData["SuccessMessage"] =
        "Employee information updated successfully.";

    return RedirectToAction(nameof(Details),
        new { id = employee.Id });
}
}