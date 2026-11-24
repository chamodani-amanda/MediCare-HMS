using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MediCare.Web.Services;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class DoctorSchedulesController(MediCareDbContext db, TimeProvider? clock = null) : ClinicalAdministrationControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Index(Guid doctorId)
    {
        var doctor = await Db.Doctors.AsNoTracking().Include(d => d.Department).SingleOrDefaultAsync(d => d.Id == doctorId);
        if (doctor == null) return NotFound();
        return View(new DoctorProfileViewModel { Doctor = doctor, Schedules = await Db.DoctorSchedules.AsNoTracking()
            .Where(s => s.DoctorId == doctorId).OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> Create(Guid doctorId)
    {
        var doctor = await Db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == doctorId && d.IsActive);
        return doctor == null ? NotFound() : View("Form", new DoctorScheduleFormViewModel { DoctorId = doctor.Id, DoctorVersion = doctor.Version });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DoctorScheduleFormViewModel model)
    {
        model.Id = Guid.Empty;
        return await SaveSchedule(model, null);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var schedule = await Db.DoctorSchedules.AsNoTracking().Include(s => s.Doctor).SingleOrDefaultAsync(s => s.Id == id);
        return schedule == null ? NotFound() : View("Form", new DoctorScheduleFormViewModel { Id = schedule.Id, DoctorId = schedule.DoctorId,
            Version = schedule.Version, DoctorVersion = schedule.Doctor.Version, DayOfWeek = schedule.DayOfWeek,
            StartTime = schedule.StartTime, EndTime = schedule.EndTime, IsActive = schedule.IsActive });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(DoctorScheduleFormViewModel model)
    {
        var schedule = await Db.DoctorSchedules.SingleOrDefaultAsync(s => s.Id == model.Id && s.DoctorId == model.DoctorId);
        if (schedule == null) return NotFound();
        CheckVersion(schedule.Version, model.Version);
        return await SaveSchedule(model, schedule);
    }

    private async Task<IActionResult> SaveSchedule(DoctorScheduleFormViewModel model, DoctorSchedule? schedule)
    {
        var doctor = await Db.Doctors.SingleOrDefaultAsync(d => d.Id == model.DoctorId);
        if (doctor == null) return NotFound();
        CheckVersion(doctor.Version, model.DoctorVersion);
        if (model.IsActive && !doctor.IsActive) ModelState.AddModelError("", "Reactivate the doctor before adding active availability.");
        if (!Enum.IsDefined(model.DayOfWeek)) ModelState.AddModelError(nameof(model.DayOfWeek), "Select a valid day.");
        if (!model.StartTime.HasValue || !model.EndTime.HasValue || model.StartTime >= model.EndTime)
            ModelState.AddModelError(nameof(model.EndTime), "Provide a start and later end time on the same day.");
        if (ModelState.IsValid && model.IsActive && await Db.DoctorSchedules.AnyAsync(s => s.DoctorId == model.DoctorId && s.Id != model.Id &&
            s.IsActive && s.DayOfWeek == model.DayOfWeek && s.StartTime < model.EndTime!.Value && s.EndTime > model.StartTime!.Value))
            ModelState.AddModelError("", "This schedule overlaps an existing active schedule for the doctor.");
        if (!ModelState.IsValid) return View("Form", model);
        var proposed = await Db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctor.Id && s.Id != model.Id && s.IsActive).ToListAsync();
        if (model.IsActive) proposed.Add(new DoctorSchedule { DayOfWeek = model.DayOfWeek, StartTime = model.StartTime!.Value, EndTime = model.EndTime!.Value });
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var bookings = await Db.Appointments.AsNoTracking().Where(a => a.DoctorId == doctor.Id && a.Status == AppointmentStatus.Scheduled && a.EndAtUtc > now)
            .Select(a => new { a.StartAtUtc, a.EndAtUtc }).ToListAsync();
        if (bookings.Any(a => !AppointmentRules.Covers(proposed, HospitalTime.Local(a.StartAtUtc).DayOfWeek,
            TimeOnly.FromDateTime(HospitalTime.Local(a.StartAtUtc)), TimeOnly.FromDateTime(HospitalTime.Local(a.EndAtUtc)))))
        {
            ModelState.AddModelError("", "This schedule change would leave a Scheduled appointment outside working hours. Reschedule or cancel affected appointments first.");
            return View("Form", model);
        }
        var isNew = schedule == null;
        schedule ??= new DoctorSchedule { DoctorId = doctor.Id };
        schedule.DayOfWeek = model.DayOfWeek; schedule.StartTime = model.StartTime!.Value; schedule.EndTime = model.EndTime!.Value;
        schedule.IsActive = model.IsActive; schedule.Version = Guid.NewGuid();
        if (isNew) Db.DoctorSchedules.Add(schedule);
        // Every writer of a doctor's schedules contends on this row. In PostgreSQL
        // the concurrency check and schedule/audit changes commit atomically,
        // preventing two simultaneous overlapping additions from both succeeding.
        doctor.Version = Guid.NewGuid(); doctor.UpdatedAtUtc = DateTime.UtcNow;
        Audit(schedule.Id, isNew ? "DoctorScheduleCreated" : "DoctorScheduleUpdated");
        return await SaveAsync() ? RedirectToAction(nameof(Index), new { doctorId = doctor.Id }) : View("Form", model);
    }
}
