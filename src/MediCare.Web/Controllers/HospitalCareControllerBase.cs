using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

public abstract class HospitalCareControllerBase(MediCareDbContext db, TimeProvider clock) : ClinicalAdministrationControllerBase(db)
{
    protected string Account => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    protected DateTime Now => clock.GetUtcNow().UtcDateTime;
    protected DateOnly Today => HospitalTime.Today(clock);
    protected bool Reception => User.IsInRole("Receptionist");
    protected Task<bool> OwnDoctor(Guid id) => Db.Doctors.AnyAsync(d => d.Id == id && d.IsActive && d.IdentityUserId == Account);
    protected IQueryable<Bed> AvailableBeds(Guid? department = null) => Db.Beds.AsNoTracking().Include(b => b.Ward).Where(b => b.IsOperational && b.Ward.IsActive && b.Ward.Department.IsActive && (!department.HasValue || b.Ward.DepartmentId == department) && !Db.BedOccupancies.Any(o => o.BedId == b.Id && o.EndedAtUtc == null));
    protected async Task<CarePage<T>> Paginate<T>(IQueryable<T> query, int page)
    {
        var count = await query.CountAsync(); var pages = Math.Max(1,(int)Math.Ceiling(count/20d)); page = Math.Clamp(page,1,pages);
        return new() { Rows = await query.Skip((page-1)*20).Take(20).ToListAsync(), Total = count, Pages = pages, Page = page };
    }
    protected async Task<IActionResult> Registration(CareRegistrationForm model, bool inpatient, string? search = null)
    {
        model.Inpatient = inpatient;
        var patients = Db.Patients.AsNoTracking().Where(p => !p.IsArchived);
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToUpperInvariant(); if(term.Length>100)return BadRequest(); patients = patients.Where(p => p.PatientNumber.ToUpper().Contains(term) || (p.FirstName+" "+p.LastName).ToUpper().Contains(term)); }
        // Keep administrative lookup projections minimal, even though registration selects existing patients.
        model.Patients = await patients.OrderBy(p => p.PatientNumber).Take(50).Select(p => new Patient { Id=p.Id,PatientNumber=p.PatientNumber,FirstName=p.FirstName,LastName=p.LastName }).ToListAsync();
        model.Doctors = await Db.Doctors.AsNoTracking().Where(d => d.IsActive && d.Department.IsActive && d.IdentityUserId != null).OrderBy(d => d.LastName).Select(d => new Doctor {Id=d.Id,FirstName=d.FirstName,LastName=d.LastName,DepartmentId=d.DepartmentId}).ToListAsync();
        if(inpatient) model.Beds = await AvailableBeds().OrderBy(b=>b.Ward.Code).ThenBy(b=>b.Number).ToListAsync();
        else
        {
            var appointments=Db.Appointments.AsNoTracking().Where(a=>(a.Status==AppointmentStatus.Scheduled||a.Status==AppointmentStatus.Completed)&&!a.Patient.IsArchived&&a.Doctor.IsActive&&!Db.OutpatientVisits.Any(v=>v.AppointmentId==a.Id));
            if(!string.IsNullOrWhiteSpace(search)){var term=search.Trim().ToUpperInvariant();appointments=appointments.Where(a=>a.Patient.PatientNumber.ToUpper().Contains(term)||(a.Patient.FirstName+" "+a.Patient.LastName).ToUpper().Contains(term));}
            model.Appointments=await appointments.OrderByDescending(a=>a.StartAtUtc).Take(100).Select(a=>new Appointment{Id=a.Id,StartAtUtc=a.StartAtUtc,PatientId=a.PatientId,DoctorId=a.DoctorId,Patient=new Patient{PatientNumber=a.Patient.PatientNumber,FirstName=a.Patient.FirstName,LastName=a.Patient.LastName},Doctor=new Doctor{FirstName=a.Doctor.FirstName,LastName=a.Doctor.LastName}}).ToListAsync();
        }
        return View("~/Views/HospitalCare/Register.cshtml",model);
    }
    protected async Task<bool> ValidateRegistration(CareRegistrationForm m)
    {
        if(m.Token==Guid.Empty)ModelState.AddModelError("","Reload the registration form.");
        if(!await Db.Patients.AnyAsync(p=>p.Id==m.PatientId&&!p.IsArchived))ModelState.AddModelError(nameof(m.PatientId),"Choose an active patient.");
        if(!await Db.Doctors.AnyAsync(d=>d.Id==m.DoctorId&&d.IsActive&&d.Department.IsActive&&d.IdentityUserId!=null))ModelState.AddModelError(nameof(m.DoctorId),"Choose an active, account-linked doctor in an active department.");
        return ModelState.IsValid;
    }
    protected async Task TouchRegistration(Guid patientId,Guid doctorId)
    {
        (await Db.Patients.SingleAsync(p=>p.Id==patientId)).Version=Guid.NewGuid();
        (await Db.Doctors.SingleAsync(d=>d.Id==doctorId)).Version=Guid.NewGuid();
    }
    protected async Task ClinicalReferences(ClinicalStayForm model,Guid patientId)
    {
        model.Records=await MedicalRecordAccess.Visible(Db,Account).AsNoTracking().Where(r=>r.MedicalRecord.PatientId==patientId&&r.FinalizedAtUtc!=null)
            .OrderByDescending(r=>r.Number).Take(100).Select(r=>new MedicalRecordSummary{Id=r.MedicalRecordId,Number=r.Number,CreatedAtUtc=r.CreatedAtUtc,DoctorName=r.MedicalRecord.Doctor.FirstName+" "+r.MedicalRecord.Doctor.LastName,FinalizedAtUtc=r.FinalizedAtUtc}).ToListAsync();
        if(model.MedicalRecordId.HasValue&&!await MedicalRecordAccess.Visible(Db,Account).AnyAsync(r=>r.MedicalRecordId==model.MedicalRecordId&&r.MedicalRecord.PatientId==patientId&&r.FinalizedAtUtc!=null))ModelState.AddModelError("","Choose a finalized EMR record authorized by your existing care relationship.");
    }
    protected Task<Guid?> VisibleEmr(Guid? record) => MedicalRecordAccess.Visible(Db,Account).Where(r=>r.MedicalRecordId==record&&r.FinalizedAtUtc!=null).OrderByDescending(r=>r.Number).Select(r=>(Guid?)r.Id).FirstOrDefaultAsync();
    protected Task<List<Doctor>> DoctorFilters()=>Db.Doctors.AsNoTracking().Where(d=>Reception||(d.IsActive&&d.IdentityUserId==Account)).OrderBy(d=>d.LastName).Select(d=>new Doctor{Id=d.Id,FirstName=d.FirstName,LastName=d.LastName}).ToListAsync();
}
