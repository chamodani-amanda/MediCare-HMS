using Npgsql;
using MediCare.Web.Services;
using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class LeaveRequestsController : Controller
{
    private readonly MediCareDbContext _db;
private readonly SecurityAuditService _audit;

public LeaveRequestsController(
    MediCareDbContext db,
    SecurityAuditService audit)
{
    _db = db;
    _audit = audit;
}

    // GET: LeaveRequests
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var requests = await _db.LeaveRequests
            .AsNoTracking()
            .Include(l => l.Employee)
            .OrderBy(l => l.Status == "Pending" ? 0 : 1)
            .ThenByDescending(l => l.CreatedAtUtc)
            .Take(100)
            .ToListAsync();

        return View(requests);
    }

    // GET: LeaveRequests/Create
    [HttpGet]
    public async Task<IActionResult> Create()
    {
        
        await LoadEmployeesAsync();

        return View(new LeaveRequestCreateViewModel());
    }

    // POST: LeaveRequests/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        LeaveRequestCreateViewModel model)
    {
        model.LeaveType = (model.LeaveType ?? "").Trim();

        var allowedTypes = new[]
        {
            "Annual Leave",
            "Sick Leave",
            "Casual Leave"
        };

        if (!allowedTypes.Contains(model.LeaveType))
        {
            ModelState.AddModelError(
                nameof(model.LeaveType),
                "Select a valid leave type.");
        }

        if (model.EmployeeId is not Guid employeeId ||
            employeeId == Guid.Empty)
        {
            ModelState.AddModelError(
                nameof(model.EmployeeId),
                "Select an employee.");
        }
        else
        {
            bool activeEmployee = await _db.Employees
                .AnyAsync(e => e.Id == employeeId && e.IsActive);

            if (!activeEmployee)
            {
                ModelState.AddModelError(
                    nameof(model.EmployeeId),
                    "Select a valid active employee.");
            }
        }

        if (model.StartDate.HasValue &&
            model.EndDate.HasValue &&
            model.EndDate.Value < model.StartDate.Value)
        {
            ModelState.AddModelError(
                nameof(model.EndDate),
                "End date cannot be before start date.");
        }

        if (!ModelState.IsValid)
        {
            await LoadEmployeesAsync();
            return View(model);
        }

        Guid validEmployeeId = model.EmployeeId!.Value;
        DateOnly startDate = model.StartDate!.Value;
        DateOnly endDate = model.EndDate!.Value;

        // Prevent a new request overlapping an existing
        // Pending or Approved request.
        bool overlap = await _db.LeaveRequests.AnyAsync(l =>
            l.EmployeeId == validEmployeeId &&
            l.Status != "Rejected" &&
            l.StartDate <= endDate &&
            l.EndDate >= startDate);

        if (overlap)
        {
            ModelState.AddModelError(
                "",
                "This employee already has a pending or " +
                "approved leave request for these dates.");

            await LoadEmployeesAsync();
            return View(model);
        }

        var nowUtc = DateTime.UtcNow;

        var request = new LeaveRequest
        {
            Id = Guid.NewGuid(),
            EmployeeId = validEmployeeId,
            LeaveType = model.LeaveType,
            StartDate = startDate,
            EndDate = endDate,
            Reason = string.IsNullOrWhiteSpace(model.Reason)
                ? null
                : model.Reason.Trim(),
            Status = "Pending",
            Version = Guid.NewGuid(),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };

        var actorId = User.FindFirstValue(
    ClaimTypes.NameIdentifier);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Forbid();
}

_db.LeaveRequests.Add(request);

_audit.QueueRecordSuccess(
    actorId,
    "LeaveRequest",
    request.Id,
    "LeaveRequestCreated");

try
{
    await _db.SaveChangesAsync();
}
catch (DbUpdateException ex)
    when (ex.InnerException is PostgresException pg &&
          pg.SqlState == PostgresErrorCodes.ExclusionViolation &&
          pg.ConstraintName == "EX_leave_requests_no_active_overlap")
{
    ModelState.AddModelError(
        "",
        "Another pending or approved leave request " +
        "overlaps these dates. Please choose different dates.");

    await LoadEmployeesAsync();
    return View(model);
}

        TempData["SuccessMessage"] =
            "Leave request submitted successfully.";

        return RedirectToAction(nameof(Index));
    }

    // POST: LeaveRequests/Review
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        Guid id,
        Guid version,
        string decision)
    {
        if (id == Guid.Empty || version == Guid.Empty)
            return BadRequest();

        if (decision != "Approved" &&
            decision != "Rejected")
            return BadRequest();

        var request = await _db.LeaveRequests
            .FirstOrDefaultAsync(l => l.Id == id);

        if (request == null)
            return NotFound();

        if (request.Status != "Pending")
        {
            TempData["ErrorMessage"] =
                "This leave request has already been reviewed.";

            return RedirectToAction(nameof(Index));
        }

        if (request.Version != version)
        {
            TempData["ErrorMessage"] =
                "This leave request was changed by another user. " +
                "Please refresh the page.";

            return RedirectToAction(nameof(Index));
        }

        if (decision == "Approved")
        {
            bool activeEmployee = await _db.Employees
                .AnyAsync(e =>
                    e.Id == request.EmployeeId &&
                    e.IsActive);

            if (!activeEmployee)
            {
                TempData["ErrorMessage"] =
                    "Cannot approve leave for an inactive employee.";

                return RedirectToAction(nameof(Index));
            }

            bool approvedOverlap = await _db.LeaveRequests
                .AnyAsync(l =>
                    l.Id != request.Id &&
                    l.EmployeeId == request.EmployeeId &&
                    l.Status == "Approved" &&
                    l.StartDate <= request.EndDate &&
                    l.EndDate >= request.StartDate);

            if (approvedOverlap)
            {
                TempData["ErrorMessage"] =
                    "This employee already has approved leave " +
                    "overlapping these dates.";

                return RedirectToAction(nameof(Index));
            }
        }

        var nowUtc = DateTime.UtcNow;

       var actorId = User.FindFirstValue(
    ClaimTypes.NameIdentifier);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Forbid();
}

request.Status = decision;
request.ReviewedAtUtc = nowUtc;
request.ReviewedByUserId = actorId;
request.UpdatedAtUtc = nowUtc;
request.Version = Guid.NewGuid();

string auditAction = decision == "Approved"
    ? "LeaveRequestApproved"
    : "LeaveRequestRejected";

_audit.QueueRecordSuccess(
    actorId,
    "LeaveRequest",
    request.Id,
    auditAction);

        try
{
    await _db.SaveChangesAsync();
}
catch (DbUpdateConcurrencyException)
{
    TempData["ErrorMessage"] =
        "The leave request was updated by another user.";

    return RedirectToAction(nameof(Index));
}
catch (DbUpdateException ex)
    when (ex.InnerException is PostgresException pg &&
          pg.SqlState == PostgresErrorCodes.ExclusionViolation &&
          pg.ConstraintName == "EX_leave_requests_no_active_overlap")
{
    TempData["ErrorMessage"] =
        "Cannot approve this leave request because " +
        "another pending or approved request overlaps its dates.";

    return RedirectToAction(nameof(Index));
}

        TempData["SuccessMessage"] =
            $"Leave request {decision.ToLowerInvariant()} successfully.";

        return RedirectToAction(nameof(Index));
    }

    private async Task LoadEmployeesAsync()
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.FirstName)
            .ThenBy(e => e.LastName)
            .ToListAsync();

        ViewBag.EmployeeOptions = employees
            .Select(e => new SelectListItem
            {
                Value = e.Id.ToString(),
                Text = $"{e.EmployeeNumber} - " +
                       $"{e.FirstName} {e.LastName}"
            })
            .ToList();
    }
}