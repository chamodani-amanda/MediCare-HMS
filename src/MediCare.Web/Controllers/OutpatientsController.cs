using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Receptionist,Doctor")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class OutpatientsController(MediCareDbContext db,TimeProvider clock):HospitalCareControllerBase(db,clock)
{
    private IQueryable<OutpatientVisit> Scope()=>Db.OutpatientVisits.Where(v=>Reception||(v.Doctor.IsActive&&v.Doctor.IdentityUserId==Account));
    private static IQueryable<CareSummary> Summaries(IQueryable<OutpatientVisit> query)=>query.Select(v=>new CareSummary{Id=v.Id,PatientId=v.PatientId,PatientLabel=v.Patient.PatientNumber+" — "+v.Patient.FirstName+" "+v.Patient.LastName,DoctorLabel=v.Doctor.FirstName+" "+v.Doctor.LastName,Department=v.Doctor.Department.Name,State=v.Status.ToString(),StartedAtUtc=v.RegisteredAtUtc,ClosedAtUtc=v.CompletedAtUtc??v.CancelledAtUtc,Version=v.Version});
    public async Task<IActionResult> Index(string? search,string? state,Guid? doctorId,DateOnly? date,int page=1)
    {
        search=(search??"").Trim();if(!ModelState.IsValid||search.Length>100||(state!=null&&!Enum.TryParse<OutpatientVisitStatus>(state,out _))||(date.HasValue&&date.Value.Year is <1900 or >9998))return BadRequest();
        var q=Scope().AsNoTracking();if(state!=null){var status=Enum.Parse<OutpatientVisitStatus>(state);q=q.Where(v=>v.Status==status);}
        if(search.Length>0){var term=search.ToUpperInvariant();q=q.Where(v=>v.Patient.PatientNumber.ToUpper().Contains(term)||(v.Patient.FirstName+" "+v.Patient.LastName).ToUpper().Contains(term));}
        if(doctorId.HasValue)q=q.Where(v=>v.DoctorId==doctorId);if(date.HasValue){var from=HospitalTime.ToUtc(date.Value,TimeOnly.MinValue);var until=from.AddDays(1);q=q.Where(v=>v.RegisteredAtUtc>=from&&v.RegisteredAtUtc<until);}
        var model=await Paginate(Summaries(q.OrderByDescending(v=>v.RegisteredAtUtc).ThenBy(v=>v.Id)),page);model.Search=search;model.State=state;model.Date=date;model.DoctorId=doctorId;model.Doctors=await DoctorFilters();Audit(Guid.Empty,"OutpatientsViewed");await Db.SaveChangesAsync();return View("~/Views/HospitalCare/Index.cshtml",model);
    }
    public async Task<IActionResult> Details(Guid id)
    {
        var row=await Summaries(Scope().AsNoTracking().Where(v=>v.Id==id)).SingleOrDefaultAsync();if(row==null)return NotFound();
        var v=await Scope().AsNoTracking().SingleAsync(v=>v.Id==id);var clinical=User.IsInRole("Doctor")&&await OwnDoctor(v.DoctorId);
        var model=new CareDetails{Row=row,AppointmentId=v.AppointmentId,CanClinical=clinical,History=await Summaries(Scope().Where(h=>h.PatientId==v.PatientId).OrderByDescending(h=>h.RegisteredAtUtc)).Take(50).ToListAsync()};
        if(clinical){model.OutpatientClinical=await Db.OutpatientClinicalNotes.AsNoTracking().SingleOrDefaultAsync(n=>n.OutpatientVisitId==id);model.VisibleEmrRevisionId=await VisibleEmr(model.OutpatientClinical?.MedicalRecordId);}
        Audit(id,clinical?"OutpatientClinicalViewed":"OutpatientAdminViewed");await Db.SaveChangesAsync();return View("~/Views/HospitalCare/Details.cshtml",model);
    }
    [Authorize(Roles="Receptionist")]
    public Task<IActionResult> Create(string? search)=>Registration(new CareRegistrationForm{Token=Guid.NewGuid()},false,search);
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Receptionist")]
    public async Task<IActionResult> Create(CareRegistrationForm model)
    {
        var existing=await Db.OutpatientVisits.AsNoTracking().SingleOrDefaultAsync(v=>v.CreationToken==model.Token);
        if(existing!=null)return existing.PatientId==model.PatientId&&existing.DoctorId==model.DoctorId&&existing.AppointmentId==model.AppointmentId?RedirectToAction(nameof(Details),new{id=existing.Id}):BadRequest("Submission token belongs to a different visit.");
        await ValidateRegistration(model);
        if(model.AppointmentId.HasValue&&(!await Db.Appointments.AnyAsync(a=>a.Id==model.AppointmentId&&a.PatientId==model.PatientId&&a.DoctorId==model.DoctorId&&(a.Status==AppointmentStatus.Scheduled||a.Status==AppointmentStatus.Completed))||await Db.OutpatientVisits.AnyAsync(v=>v.AppointmentId==model.AppointmentId)))ModelState.AddModelError("","Choose a matching Scheduled/Completed appointment that has no outpatient visit.");
        if(!ModelState.IsValid)return await Registration(model,false);
        var visit=new OutpatientVisit{CreationToken=model.Token,PatientId=model.PatientId,DoctorId=model.DoctorId,AppointmentId=model.AppointmentId,RegisteredAtUtc=Now,RegisteredByUserId=Account};Db.OutpatientVisits.Add(visit);await TouchRegistration(model.PatientId,model.DoctorId);
        if(model.AppointmentId.HasValue)(await Db.Appointments.SingleAsync(a=>a.Id==model.AppointmentId)).Version=Guid.NewGuid();
        Audit(visit.Id,"OutpatientRegistered");return await SaveAsync()?RedirectToAction(nameof(Details),new{id=visit.Id}):await Registration(model,false);
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Doctor")]
    public async Task<IActionResult> Progress(Guid id,Guid version,string command)
    {
        var v=await Db.OutpatientVisits.SingleOrDefaultAsync(v=>v.Id==id);if(v==null||!await OwnDoctor(v.DoctorId))return NotFound();
        if(!CheckVersion(v.Version,version))return Conflict("Visit changed. Reload.");
        if(command=="Start"&&v.Status==OutpatientVisitStatus.Registered){if(await Db.Patients.AnyAsync(p=>p.Id==v.PatientId&&p.IsArchived))return Conflict("An archived patient cannot start a new consultation.");v.Status=OutpatientVisitStatus.InConsultation;v.StartedAtUtc=Now;v.StartedByUserId=Account;}
        else if(command=="Complete"&&v.Status==OutpatientVisitStatus.InConsultation){v.Status=OutpatientVisitStatus.Completed;v.CompletedAtUtc=Now;v.CompletedByUserId=Account;}
        else return BadRequest("Invalid visit transition.");
        v.Version=Guid.NewGuid();(await Db.Doctors.SingleAsync(d=>d.Id==v.DoctorId)).Version=Guid.NewGuid();Audit(id,command=="Start"?"OutpatientConsultationStarted":"OutpatientCompleted");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Visit changed. Reload.");
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Receptionist")]
    public async Task<IActionResult> Cancel(Guid id,Guid version,string? reason)
    {
        var v=await Db.OutpatientVisits.SingleOrDefaultAsync(v=>v.Id==id);if(v==null)return NotFound();
        if(v.Status!=OutpatientVisitStatus.Registered||!CheckVersion(v.Version,version)||string.IsNullOrWhiteSpace(reason)||reason.Trim().Length>500)return BadRequest("Only a current Registered visit can be cancelled with a reason.");
        v.Status=OutpatientVisitStatus.Cancelled;v.CancelledAtUtc=Now;v.CancelledByUserId=Account;v.CancellationReason=reason.Trim();v.Version=Guid.NewGuid();Audit(id,"OutpatientCancelled");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Visit changed. Reload.");
    }
    [Authorize(Roles="Doctor")]
    public async Task<IActionResult> Clinical(Guid id)
    {
        var v=await Db.OutpatientVisits.AsNoTracking().SingleOrDefaultAsync(v=>v.Id==id);if(v==null||v.Status!=OutpatientVisitStatus.InConsultation||!await OwnDoctor(v.DoctorId))return NotFound();
        var n=await Db.OutpatientClinicalNotes.AsNoTracking().SingleOrDefaultAsync(n=>n.OutpatientVisitId==id);var form=new ClinicalStayForm{Id=id,ParentVersion=v.Version,Version=n?.Version??Guid.Empty,MedicalRecordId=n?.MedicalRecordId,FollowUpInformation=n?.FollowUpInformation,FollowUpOn=n?.FollowUpOn};await ClinicalReferences(form,v.PatientId);
        Audit(id,"OutpatientClinicalDraftViewed");await Db.SaveChangesAsync();return View("~/Views/HospitalCare/Clinical.cshtml",form);
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Doctor")]
    public async Task<IActionResult> Clinical(Guid id,ClinicalStayForm model)
    {
        model.Id=id;model.Inpatient=false;var v=await Db.OutpatientVisits.SingleOrDefaultAsync(v=>v.Id==id);if(v==null||v.Status!=OutpatientVisitStatus.InConsultation||!await OwnDoctor(v.DoctorId))return NotFound();
        CheckVersion(v.Version,model.ParentVersion);await ClinicalReferences(model,v.PatientId);if(model.FollowUpOn.HasValue&&model.FollowUpOn<Today)ModelState.AddModelError("","Follow-up date cannot be in the past.");
        var n=await Db.OutpatientClinicalNotes.SingleOrDefaultAsync(n=>n.OutpatientVisitId==id);if(n!=null)CheckVersion(n.Version,model.Version);else if(model.Version!=Guid.Empty)ModelState.AddModelError("","Reload the clinical form.");
        if(!ModelState.IsValid)return View("~/Views/HospitalCare/Clinical.cshtml",model);
        if(n==null){n=new OutpatientClinical{OutpatientVisitId=id,AuthorUserId=Account};Db.OutpatientClinicalNotes.Add(n);}
        n.MedicalRecordId=model.MedicalRecordId;n.FollowUpInformation=Optional(model.FollowUpInformation);n.FollowUpOn=model.FollowUpOn;n.UpdatedAtUtc=Now;n.Version=Guid.NewGuid();v.Version=Guid.NewGuid();(await Db.Doctors.SingleAsync(d=>d.Id==v.DoctorId)).Version=Guid.NewGuid();Audit(id,"OutpatientClinicalUpdated");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):View("~/Views/HospitalCare/Clinical.cshtml",model);
    }
}
