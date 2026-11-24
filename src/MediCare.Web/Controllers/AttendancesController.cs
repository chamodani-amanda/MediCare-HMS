using MediCare.Web.Services;
using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class AttendancesController : Controller
{
    private readonly MediCareDbContext _db;
private readonly SecurityAuditService _audit;

public AttendancesController(
    MediCareDbContext db,
    SecurityAuditService audit)
{
    _db = db;
    _audit = audit;
}
    // Display attendance history and employee selection.
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var employees = await _db.Employees
            .AsNoTracking()
            .Where(e => e.IsActive)
            .OrderBy(e => e.FirstName)
            .ThenBy(e => e.LastName)
            .Select(e => new
            {
                e.Id,
                e.EmployeeNumber,
                e.FirstName,
                e.LastName
            })
            .ToListAsync();

        ViewBag.EmployeeOptions = employees
            .Select(e => new SelectListItem
            {
                Value = e.Id.ToString(),
                Text = $"{e.EmployeeNumber} - {e.FirstName} {e.LastName}"
            })
            .ToList();

        var records = await _db.Attendances
            .AsNoTracking()
            .Include(a => a.Employee)
            .OrderByDescending(a => a.WorkDate)
            .ThenByDescending(a => a.CheckInAtUtc)
            .Take(50)
            .ToListAsync();

        return View(records);
    }

    // Record an employee's check-in.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(
        Guid employeeId,
        string? notes)
    {
        if (employeeId == Guid.Empty)
            return BadRequest();

        bool employeeExists = await _db.Employees
            .AnyAsync(e => e.Id == employeeId && e.IsActive);

        if (!employeeExists)
        {
            TempData["ErrorMessage"] =
                "Select a valid active employee.";

            return RedirectToAction(nameof(Index));
        }

        if (notes?.Length > 500)
        {
            TempData["ErrorMessage"] =
                "Notes cannot exceed 500 characters.";

            return RedirectToAction(nameof(Index));
        }

        var nowUtc = DateTime.UtcNow;

        var sriLanka = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        var localNow = TimeZoneInfo
            .ConvertTimeFromUtc(nowUtc, sriLanka);

        var workDate = DateOnly.FromDateTime(localNow);

        bool alreadyCheckedIn = await _db.Attendances
            .AnyAsync(a =>
                a.EmployeeId == employeeId &&
                a.WorkDate == workDate);

        if (alreadyCheckedIn)
        {
            TempData["ErrorMessage"] =
                "Attendance has already been recorded for today.";

            return RedirectToAction(nameof(Index));
        }

        bool hasOpenAttendance = await _db.Attendances
            .AnyAsync(a =>
                a.EmployeeId == employeeId &&
                a.CheckOutAtUtc == null);

        if (hasOpenAttendance)
        {
            TempData["ErrorMessage"] =
                "This employee has an unfinished check-in.";

            return RedirectToAction(nameof(Index));
        }

        var attendance = new Attendance
        {
            Id = Guid.NewGuid(),
            EmployeeId = employeeId,
            WorkDate = workDate,
            CheckInAtUtc = nowUtc,
            CheckOutAtUtc = null,
            Notes = string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim(),
            RecordedByUserId =
                User.FindFirstValue(ClaimTypes.NameIdentifier),
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

_db.Attendances.Add(attendance);

_audit.QueueRecordSuccess(
    actorId,
    "Attendance",
    attendance.Id,
    "AttendanceCheckIn");

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException pg &&
                  pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            TempData["ErrorMessage"] =
    "Attendance already exists for today, or this " +
    "employee has an unfinished check-in.";

            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] =
            "Employee checked in successfully.";

        return RedirectToAction(nameof(Index));
    }

    // Record check-out for an existing attendance record.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckOut(
        Guid id,
        Guid version)
    {
        if (id == Guid.Empty || version == Guid.Empty)
            return BadRequest();

        var attendance = await _db.Attendances
            .FirstOrDefaultAsync(a => a.Id == id);

        if (attendance == null)
            return NotFound();

        if (attendance.Version != version)
        {
            TempData["ErrorMessage"] =
                "Attendance was updated by another user. Refresh the page.";

            return RedirectToAction(nameof(Index));
        }

        if (attendance.CheckOutAtUtc != null)
        {
            TempData["ErrorMessage"] =
                "This employee has already checked out.";

            return RedirectToAction(nameof(Index));
        }

        var nowUtc = DateTime.UtcNow;

        if (nowUtc <= attendance.CheckInAtUtc)
        {
            TempData["ErrorMessage"] =
                "Check-out must be after check-in.";

            return RedirectToAction(nameof(Index));
        }

        attendance.CheckOutAtUtc = nowUtc;
        attendance.UpdatedAtUtc = nowUtc;
        attendance.Version = Guid.NewGuid();
        
        var actorId = User.FindFirstValue(
    ClaimTypes.NameIdentifier);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Forbid();
}

_audit.QueueRecordSuccess(
    actorId,
    "Attendance",
    attendance.Id,
    "AttendanceCheckOut");
    
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["ErrorMessage"] =
                "Attendance changed during checkout. Please refresh.";

            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] =
            "Employee checked out successfully.";

        return RedirectToAction(nameof(Index));
    }
}