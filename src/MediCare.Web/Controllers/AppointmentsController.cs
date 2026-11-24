using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator,Receptionist,Doctor")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AppointmentsController(MediCareDbContext db, TimeProvider clock) : ClinicalAdministrationControllerBase(db)
{
    private bool IsManager => User.IsInRole("Administrator") || User.IsInRole("Receptionist");
    private IQueryable<Appointment> Authorized()
    {
        var query = Db.Appointments.AsNoTracking();
        if (!IsManager)
        {
            var account = User.FindFirstValue(ClaimTypes.NameIdentifier);
            query = query.Where(a => account != null && a.Doctor.IdentityUserId == account);
        }
        return query;
    }
    private IQueryable<AppointmentRowViewModel> Rows(IQueryable<Appointment> query)
    {
        var manager = IsManager;
        return query.Select(a => new AppointmentRowViewModel { Id = a.Id, PatientId = a.PatientId, DoctorId = a.DoctorId,
            PatientName = a.Patient.FirstName + " " + a.Patient.LastName, PatientNumber = a.Patient.PatientNumber,
            DoctorName = a.Doctor.FirstName + " " + a.Doctor.LastName, StartAtUtc = a.StartAtUtc, EndAtUtc = a.EndAtUtc,
            DurationMinutes = a.DurationMinutes, Status = a.Status, Version = a.Version,
            CancellationReason = manager ? a.CancellationReason : null });
    }
    private static bool SafeDate(DateOnly date) => date.Year is >= 1900 and <= 9998;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, Guid? doctorId, DateOnly? date, AppointmentStatus? status, int page = 1)
    {
        search = (search ?? "").Trim();
        if (!ModelState.IsValid || search.Length > 100 || (date.HasValue && !SafeDate(date.Value)) || (status.HasValue && !Enum.IsDefined(status.Value))) return BadRequest("Invalid appointment filter.");
        var query = Authorized();
        if (doctorId.HasValue) query = query.Where(a => a.DoctorId == doctorId);
        if (date.HasValue)
        {
            var from = HospitalTime.ToUtc(date.Value, TimeOnly.MinValue); var until = from.AddDays(1);
            query = query.Where(a => a.StartAtUtc >= from && a.StartAtUtc < until);
        }
        if (status.HasValue) query = query.Where(a => a.Status == status.Value);
        if (search.Length > 0)
        {
            var term = search.ToUpperInvariant();
            query = query.Where(a => a.Patient.PatientNumber.ToUpper().Contains(term) ||
                (a.Patient.FirstName + " " + a.Patient.LastName).ToUpper().Contains(term) ||
                (a.Doctor.FirstName + " " + a.Doctor.LastName).ToUpper().Contains(term));
        }
        var count = await query.CountAsync(); var pages = Math.Max(1, (int)Math.Ceiling(count / 20d)); page = Math.Clamp(page, 1, pages);
        return View(new AppointmentListViewModel { Search = search, DoctorId = doctorId, Date = date, Status = status,
            Page = page, TotalPages = pages, TotalAppointments = count, Doctors = await DoctorChoices(false),
            Appointments = await Rows(query.OrderByDescending(a => a.StartAtUtc).ThenBy(a => a.Id)).Skip((page-1)*20).Take(20).ToListAsync() });
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var appointment = await Rows(Authorized().Where(a => a.Id == id)).SingleOrDefaultAsync();
        return appointment == null ? NotFound() : View(appointment);
    }

    [HttpGet]
    public async Task<IActionResult> Calendar(DateOnly? date, Guid? doctorId)
    {
        var fromDate = date ?? HospitalTime.Today(clock);
        if (!ModelState.IsValid || !SafeDate(fromDate) || fromDate > new DateOnly(9998,12,20)) return BadRequest("Invalid calendar date.");
        var from = HospitalTime.ToUtc(fromDate, TimeOnly.MinValue); var until = from.AddDays(7);
        var query = Authorized().Where(a => a.StartAtUtc >= from && a.StartAtUtc < until);
        if (doctorId.HasValue) query = query.Where(a => a.DoctorId == doctorId);
        // Bounded weekly agenda rather than an unbounded export of patient data.
        var rows = await Rows(query.OrderBy(a => a.StartAtUtc).ThenBy(a => a.Id)).Take(1001).ToListAsync();
        if (rows.Count > 1000) return BadRequest("More than 1000 appointments in this week. Filter by doctor or use the paginated list.");
        return View(new AppointmentCalendarViewModel { Date = fromDate, DoctorId = doctorId, Doctors = await DoctorChoices(false), Appointments = rows });
    }

    [HttpGet, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> Create(Guid? patientId, Guid? doctorId, DateOnly? date, TimeOnly? startTime, int durationMinutes = 30, string? patientSearch = null)
    {
        return View("Form", await Choices(new AppointmentFormViewModel { BookingToken = Guid.NewGuid(), PatientId = patientId ?? Guid.Empty,
            DoctorId = doctorId ?? Guid.Empty, Date = date ?? HospitalTime.Today(clock), StartTime = startTime,
            DurationMinutes = durationMinutes, PatientSearch = patientSearch }));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> Create(AppointmentFormViewModel model)
    {
        model.Id = Guid.Empty;
        if (model.BookingToken == Guid.Empty) ModelState.AddModelError("", "Reload the booking form before submitting.");
        var existing = await Db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.BookingToken == model.BookingToken);
        if (existing != null)
        {
            if (model.Date.HasValue && model.StartTime.HasValue && SafeDate(model.Date.Value) && existing.PatientId == model.PatientId &&
                existing.DoctorId == model.DoctorId && existing.StartAtUtc == HospitalTime.ToUtc(model.Date.Value, model.StartTime.Value) && existing.DurationMinutes == model.DurationMinutes)
                return RedirectToAction(nameof(Details), new { id = existing.Id });
            ModelState.AddModelError("", "This booking form has already been submitted. Open a new booking form.");
        }
        var times = await ValidateBooking(model);
        if (!ModelState.IsValid || times == null) return View("Form", await Choices(model));
        var appointment = new Appointment { BookingToken = model.BookingToken, PatientId = model.PatientId, DoctorId = model.DoctorId,
            StartAtUtc = times.Value.From, EndAtUtc = times.Value.Until, DurationMinutes = model.DurationMinutes };
        Db.Appointments.Add(appointment); Audit(appointment.Id, "AppointmentBooked");
        if (!await SaveBooking()) return View("Form", await Choices(model));
        return RedirectToAction(nameof(Details), new { id = appointment.Id });
    }

    [HttpGet, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> Reschedule(Guid id)
    {
        var appointment = await Db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.Status == AppointmentStatus.Scheduled);
        if (appointment == null) return NotFound();
        var local = HospitalTime.Local(appointment.StartAtUtc);
        return View("Form", await Choices(new AppointmentFormViewModel { Id = appointment.Id, Version = appointment.Version,
            PatientId = appointment.PatientId, DoctorId = appointment.DoctorId, Date = DateOnly.FromDateTime(local),
            StartTime = TimeOnly.FromDateTime(local), DurationMinutes = appointment.DurationMinutes }));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> Reschedule(AppointmentFormViewModel model)
    {
        var appointment = await Db.Appointments.SingleOrDefaultAsync(a => a.Id == model.Id);
        if (appointment == null) return NotFound();
        CheckVersion(appointment.Version, model.Version);
        if (appointment.Status != AppointmentStatus.Scheduled) ModelState.AddModelError("", "Only Scheduled appointments can be rescheduled.");
        // Rescheduling cannot silently move an appointment to another patient.
        if (model.PatientId != appointment.PatientId) ModelState.AddModelError(nameof(model.PatientId), "The patient cannot be changed when rescheduling.");
        var times = await ValidateBooking(model);
        if (!ModelState.IsValid || times == null) return View("Form", await Choices(model));
        appointment.DoctorId = model.DoctorId; appointment.StartAtUtc = times.Value.From; appointment.EndAtUtc = times.Value.Until;
        appointment.DurationMinutes = model.DurationMinutes; appointment.Version = Guid.NewGuid(); appointment.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        Audit(appointment.Id, "AppointmentRescheduled");
        if (!await SaveBooking()) return View("Form", await Choices(model));
        return RedirectToAction(nameof(Details), new { id = appointment.Id });
    }

    [HttpGet, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> ChangeStatus(Guid id)
    {
        var appointment = await Rows(Authorized().Where(a => a.Id == id)).SingleOrDefaultAsync();
        return appointment == null ? NotFound() : View(new AppointmentChangeViewModel { Id = id, Version = appointment.Version, Status = AppointmentStatus.Cancelled, Appointment = appointment });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> ChangeStatus(AppointmentChangeViewModel model)
    {
        var appointment = await Db.Appointments.SingleOrDefaultAsync(a => a.Id == model.Id);
        if (appointment == null) return NotFound();
        CheckVersion(appointment.Version, model.Version);
        if (!Enum.IsDefined(model.Status) || !AppointmentRules.MayChangeStatus(appointment.Status, model.Status, appointment.EndAtUtc, clock.GetUtcNow().UtcDateTime))
            ModelState.AddModelError(nameof(model.Status), "Only Scheduled appointments can change status. Completed and NoShow require the appointment to have ended; all other statuses are terminal.");
        if (!ModelState.IsValid)
        {
            model.Appointment = await Rows(Authorized().Where(a => a.Id == model.Id)).SingleAsync(); return View(model);
        }
        appointment.Status = model.Status; appointment.Version = Guid.NewGuid(); appointment.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        appointment.CancelledAtUtc = model.Status == AppointmentStatus.Cancelled ? clock.GetUtcNow().UtcDateTime : null;
        appointment.CancellationReason = model.Status == AppointmentStatus.Cancelled ? Optional(model.CancellationReason) : null;
        Audit(appointment.Id, model.Status == AppointmentStatus.Cancelled ? "AppointmentCancelled" : "AppointmentStatusChanged");
        if (!await SaveBooking()) { model.Appointment = await Rows(Authorized().Where(a => a.Id == model.Id)).SingleAsync(); return View(model); }
        return RedirectToAction(nameof(Details), new { id = appointment.Id });
    }

    [HttpGet, Authorize(Roles = "Administrator,Receptionist")]
    public async Task<IActionResult> Availability(Guid doctorId, DateOnly? date, int durationMinutes = 30)
    {
        var model = new AppointmentAvailabilityViewModel { DoctorId = doctorId, Date = date ?? HospitalTime.Today(clock), DurationMinutes = durationMinutes, Doctors = await DoctorChoices(true) };
        if (!ModelState.IsValid || !SafeDate(model.Date.Value) || !AppointmentRules.ValidDuration(durationMinutes)) return BadRequest("Choose a valid date and duration (15, 30, 45 or 60 minutes).");
        if (doctorId == Guid.Empty) return View(model);
        var doctor = await Db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == doctorId && d.IsActive && d.Department.IsActive);
        if (doctor == null) return NotFound();
        model.DoctorName = doctor.FirstName + " " + doctor.LastName;
        var shifts = await Db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == doctorId && s.IsActive && s.DayOfWeek == model.Date.Value.DayOfWeek).ToListAsync();
        var from = HospitalTime.ToUtc(model.Date.Value, TimeOnly.MinValue); var until = from.AddDays(1);
        var booked = await Db.Appointments.AsNoTracking().Where(a => a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled && a.StartAtUtc < until && a.EndAtUtc > from)
            .Select(a => new { a.StartAtUtc, a.EndAtUtc }).ToListAsync();
        for (var minute = 0; minute + durationMinutes < 24*60; minute += 15)
        {
            var start = new TimeOnly(minute/60, minute%60); var end = start.AddMinutes(durationMinutes); var utc = HospitalTime.ToUtc(model.Date.Value, start);
            if (utc > clock.GetUtcNow().UtcDateTime && AppointmentRules.Covers(shifts, model.Date.Value.DayOfWeek, start, end) &&
                !booked.Any(a => a.StartAtUtc < utc.AddMinutes(durationMinutes) && a.EndAtUtc > utc)) model.AvailableStarts.Add(start);
        }
        return View(model);
    }

    private async Task<(DateTime From,DateTime Until)?> ValidateBooking(AppointmentFormViewModel model)
    {
        var patient = await Db.Patients.SingleOrDefaultAsync(p => p.Id == model.PatientId && !p.IsArchived);
        var doctor = await Db.Doctors.SingleOrDefaultAsync(d => d.Id == model.DoctorId && d.IsActive && d.Department.IsActive);
        if (patient == null) ModelState.AddModelError(nameof(model.PatientId), "Select an active patient.");
        if (doctor == null) ModelState.AddModelError(nameof(model.DoctorId), "Select an active doctor in an active department.");
        if (!model.Date.HasValue || !SafeDate(model.Date.Value) || !model.StartTime.HasValue || !AppointmentRules.ValidDuration(model.DurationMinutes) || !AppointmentRules.ValidStart(model.StartTime.Value))
        { ModelState.AddModelError("", "Choose a valid date, a 15-minute start interval and a duration of 15, 30, 45 or 60 minutes."); return null; }
        var from = HospitalTime.ToUtc(model.Date.Value, model.StartTime.Value); var until = from.AddMinutes(model.DurationMinutes);
        var localEnd = HospitalTime.Local(until);
        if (from <= clock.GetUtcNow().UtcDateTime) ModelState.AddModelError(nameof(model.Date), "Only future appointments can be booked or rescheduled.");
        var shifts = await Db.DoctorSchedules.AsNoTracking().Where(s => s.DoctorId == model.DoctorId && s.IsActive && s.DayOfWeek == model.Date.Value.DayOfWeek).ToListAsync();
        if (DateOnly.FromDateTime(localEnd) != model.Date.Value || !AppointmentRules.Covers(shifts, model.Date.Value.DayOfWeek, model.StartTime.Value, TimeOnly.FromDateTime(localEnd)))
            ModelState.AddModelError("", "The whole appointment must fit within the doctor's working hours on that day.");
        if (await Db.Appointments.AnyAsync(a => a.Id != model.Id && a.DoctorId == model.DoctorId && a.Status != AppointmentStatus.Cancelled && a.StartAtUtc < until && a.EndAtUtc > from))
            ModelState.AddModelError("", "This time overlaps another appointment. Choose another available time.");
        if (ModelState.IsValid && patient != null && doctor != null)
        {
            // Coordinate with patient archiving, doctor deactivation and schedule edits.
            patient.Version = Guid.NewGuid(); doctor.Version = Guid.NewGuid();
        }
        return (from, until);
    }

    private async Task<bool> SaveBooking()
    {
        try { return await SaveAsync(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23P01" or "40P01" or "40001" })
        { ModelState.AddModelError("", "Another booking or schedule change conflicted with this request. Refresh availability and try again."); return false; }
    }

    private async Task<List<SelectListItem>> DoctorChoices(bool active)
    {
        var query = Db.Doctors.AsNoTracking();
        if (!IsManager) { var account = User.FindFirstValue(ClaimTypes.NameIdentifier); query = query.Where(d => account != null && d.IdentityUserId == account); }
        if (active) query = query.Where(d => d.IsActive && d.Department.IsActive);
        return await query.OrderBy(d => d.LastName).ThenBy(d => d.FirstName).Select(d => new SelectListItem(d.FirstName + " " + d.LastName + " (" + d.RegistrationNumber + ")", d.Id.ToString())).ToListAsync();
    }
    private async Task<AppointmentFormViewModel> Choices(AppointmentFormViewModel model)
    {
        model.PatientSearch = (model.PatientSearch ?? "").Trim();
        if (model.PatientSearch.Length > 100) model.PatientSearch = model.PatientSearch[..100];
        var patients = Db.Patients.AsNoTracking().Where(p => !p.IsArchived);
        if (model.PatientSearch.Length > 0)
        { var term = model.PatientSearch.ToUpperInvariant(); patients = patients.Where(p => p.PatientNumber.ToUpper().Contains(term) || (p.FirstName + " " + p.LastName).ToUpper().Contains(term) || p.Id == model.PatientId); }
        model.Patients = await patients.OrderByDescending(p => p.Id == model.PatientId).ThenBy(p => p.LastName).ThenBy(p => p.Id).Take(50)
            .Select(p => new SelectListItem(p.PatientNumber + " - " + p.FirstName + " " + p.LastName, p.Id.ToString())).ToListAsync();
        model.Doctors = await DoctorChoices(true); return model;
    }
}
