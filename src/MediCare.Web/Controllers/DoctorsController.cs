using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator,Doctor,Receptionist")]
public class DoctorsController(MediCareDbContext db) : ClinicalAdministrationControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, int page = 1, bool includeInactive = false)
    {
        search = (search ?? "").Trim();
        if (search.Length > 100) return BadRequest("Search must be 100 characters or fewer.");
        includeInactive &= User.IsInRole("Administrator");
        var query = Db.Doctors.AsNoTracking().Include(d => d.Department).Where(d => includeInactive || d.IsActive);
        if (search.Length > 0)
        {
            // Contains is translated by Npgsql; wildcards in user input remain literal.
            var term = search.ToUpperInvariant();
            query = query.Where(d => d.RegistrationNumber.Contains(term) ||
                (d.FirstName + " " + d.LastName).ToUpper().Contains(term) || d.Specialization.ToUpper().Contains(term) || d.Department.NormalizedName.Contains(term));
        }
        var count = await query.CountAsync(); var pages = Math.Max(1, (int)Math.Ceiling(count / 20d));
        page = Math.Clamp(page, 1, pages);
        return View(new DoctorDirectoryViewModel { Search = search, Page = page, TotalPages = pages, TotalDoctors = count, IncludeInactive = includeInactive,
            Doctors = await query.OrderBy(d => d.LastName).ThenBy(d => d.FirstName).ThenBy(d => d.Id).Skip((page - 1) * 20).Take(20).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var doctor = await Db.Doctors.AsNoTracking().Include(d => d.Department)
            .SingleOrDefaultAsync(d => d.Id == id && (d.IsActive || User.IsInRole("Administrator")));
        if (doctor == null) return NotFound();
        return View(new DoctorProfileViewModel { Doctor = doctor, Schedules = await Db.DoctorSchedules.AsNoTracking()
            .Where(s => s.DoctorId == id && s.IsActive).OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).ToListAsync() });
    }

    [HttpGet, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Create() => View("Form", await Choices(new DoctorFormViewModel()));

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Create(DoctorFormViewModel model)
    {
        // IDs on creation are server-owned; posted IDs cannot bypass duplicate checks.
        model.Id = Guid.Empty;
        await ValidateAsync(model);
        if (!ModelState.IsValid) return View("Form", await Choices(model));
        var doctor = new Doctor(); Apply(doctor, model);
        Db.Doctors.Add(doctor); Audit(doctor.Id, "DoctorCreated");
        if (!await SaveAsync()) return View("Form", await Choices(model));
        return RedirectToAction(nameof(Details), new { id = doctor.Id });
    }

    [HttpGet, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var doctor = await Db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id);
        if (doctor == null) return NotFound();
        return View("Form", await Choices(new DoctorFormViewModel { Id = doctor.Id, Version = doctor.Version,
            RegistrationNumber = doctor.RegistrationNumber, FirstName = doctor.FirstName, LastName = doctor.LastName,
            Qualifications = doctor.Qualifications, Specialization = doctor.Specialization, Email = doctor.Email,
            PhoneNumber = doctor.PhoneNumber, DepartmentId = doctor.DepartmentId, IdentityUserId = doctor.IdentityUserId, IsActive = doctor.IsActive }));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Edit(DoctorFormViewModel model)
    {
        var doctor = await Db.Doctors.SingleOrDefaultAsync(d => d.Id == model.Id);
        if (doctor == null) return NotFound();
        await ValidateAsync(model); CheckVersion(doctor.Version, model.Version);
        if (!ModelState.IsValid) return View("Form", await Choices(model));
        var oldLink = doctor.IdentityUserId; var oldStatus = doctor.IsActive;
        Apply(doctor, model); doctor.Version = Guid.NewGuid(); doctor.UpdatedAtUtc = DateTime.UtcNow;
        Audit(doctor.Id, "DoctorUpdated");
        if (oldLink != doctor.IdentityUserId) Audit(doctor.Id, "DoctorAccountLinkChanged");
        if (oldStatus != doctor.IsActive) Audit(doctor.Id, doctor.IsActive ? "DoctorActivated" : "DoctorDeactivated");
        if (!await SaveAsync()) return View("Form", await Choices(model));
        return RedirectToAction(nameof(Details), new { id = doctor.Id });
    }

    private async Task ValidateAsync(DoctorFormViewModel model)
    {
        var registration = (model.RegistrationNumber ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(registration)) ModelState.AddModelError(nameof(model.RegistrationNumber), "Registration number is required.");
        if (await Db.Doctors.AnyAsync(d => d.Id != model.Id && d.RegistrationNumber == registration))
            ModelState.AddModelError(nameof(model.RegistrationNumber), "This medical registration number is already registered.");
        var email = Optional(model.Email)?.ToUpperInvariant();
        if (email != null && await Db.Doctors.AnyAsync(d => d.Id != model.Id && d.NormalizedEmail == email))
            ModelState.AddModelError(nameof(model.Email), "A doctor already uses this email.");
        var department = await Db.Departments.SingleOrDefaultAsync(d => d.Id == model.DepartmentId);
        if (department == null || (model.IsActive && !department.IsActive))
            ModelState.AddModelError(nameof(model.DepartmentId), "Select an active department for an active doctor.");
        model.IdentityUserId = Optional(model.IdentityUserId);
        if (model.IdentityUserId != null)
        {
            var account = await Db.Users.SingleOrDefaultAsync(u => u.Id == model.IdentityUserId);
            var doctorRole = await (from userRole in Db.UserRoles join role in Db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == model.IdentityUserId && role.Name == "Doctor" select role.Id).AnyAsync();
            if (account == null || !doctorRole || account.LockoutEnd > DateTimeOffset.UtcNow)
                ModelState.AddModelError(nameof(model.IdentityUserId), "Select an available existing Doctor-role account.");
            if (await Db.Doctors.AnyAsync(d => d.Id != model.Id && d.IdentityUserId == model.IdentityUserId))
                ModelState.AddModelError(nameof(model.IdentityUserId), "This account is already linked to another doctor.");
        }
        // A doctor registration/edit contends with department changes so a concurrent
        // department deactivation cannot strand a newly active doctor.
        if (department != null && ModelState.IsValid) department.Version = Guid.NewGuid();
    }

    private static void Apply(Doctor doctor, DoctorFormViewModel model)
    {
        doctor.RegistrationNumber = model.RegistrationNumber.Trim().ToUpperInvariant();
        doctor.FirstName = model.FirstName.Trim(); doctor.LastName = model.LastName.Trim();
        doctor.Qualifications = model.Qualifications.Trim(); doctor.Specialization = model.Specialization.Trim();
        doctor.Email = Optional(model.Email); doctor.NormalizedEmail = doctor.Email?.ToUpperInvariant();
        doctor.PhoneNumber = Optional(model.PhoneNumber); doctor.DepartmentId = model.DepartmentId;
        doctor.IdentityUserId = model.IdentityUserId; doctor.IsActive = model.IsActive;
    }

    private async Task<DoctorFormViewModel> Choices(DoctorFormViewModel model)
    {
        model.Departments = await Db.Departments.AsNoTracking().Where(d => d.IsActive || d.Id == model.DepartmentId)
            .OrderBy(d => d.Name).Select(d => new SelectListItem(d.Name + (d.IsActive ? "" : " (inactive)"), d.Id.ToString())).ToListAsync();
        model.Accounts = await Db.Users.AsNoTracking().Where(user => user.Id == model.IdentityUserId ||
            ((user.LockoutEnd == null || user.LockoutEnd <= DateTimeOffset.UtcNow) &&
             (from userRole in Db.UserRoles join role in Db.Roles on userRole.RoleId equals role.Id
              where userRole.UserId == user.Id && role.Name == "Doctor" select role.Id).Any() &&
             !Db.Doctors.Any(d => d.IdentityUserId == user.Id && d.Id != model.Id)))
            .OrderBy(user => user.Email).Select(user => new SelectListItem(
                (user.Email ?? user.UserName ?? user.Id) + (user.LockoutEnd > DateTimeOffset.UtcNow ? " (sign-in locked)" : ""), user.Id)).ToListAsync();
        return model;
    }
}
