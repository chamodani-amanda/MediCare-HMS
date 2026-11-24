using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Receptionist,Doctor")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class AdmissionsController(MediCareDbContext db,TimeProvider clock):HospitalCareControllerBase(db,clock)
{
    private IQueryable<Admission> Scope()=>Db.Admissions.Where(a=>Reception||(a.Doctor.IsActive&&a.Doctor.IdentityUserId==Account));
    private IQueryable<CareSummary> Summaries(IQueryable<Admission> query)=>query.Select(a=>new CareSummary {
        Id=a.Id,PatientId=a.PatientId,PatientLabel=a.Patient.PatientNumber+" — "+a.Patient.FirstName+" "+a.Patient.LastName,DoctorLabel=a.Doctor.FirstName+" "+a.Doctor.LastName,Department=a.Department.Name,
        State=a.Status.ToString(),StartedAtUtc=a.AdmittedAtUtc,ClosedAtUtc=a.DischargedAtUtc,Version=a.Version,
        BedLabel=Db.BedOccupancies.Where(o=>o.AdmissionId==a.Id&&o.EndedAtUtc==null).Select(o=>o.Bed.Ward.Code+" / "+o.Bed.Number).FirstOrDefault() });
    public async Task<IActionResult> Index(string? search,string? state,Guid? doctorId,int page=1)
    {
        search=(search??"").Trim(); if(!ModelState.IsValid||search.Length>100||(state!=null&&!Enum.TryParse<AdmissionStatus>(state,out _)))return BadRequest();
        var q=Scope().AsNoTracking();if(state==null)q=q.Where(a=>a.Status!=AdmissionStatus.Discharged);else { var status=Enum.Parse<AdmissionStatus>(state);q=q.Where(a=>a.Status==status); }
        if(search.Length>0){var term=search.ToUpperInvariant();q=q.Where(a=>a.Patient.PatientNumber.ToUpper().Contains(term)||(a.Patient.FirstName+" "+a.Patient.LastName).ToUpper().Contains(term));}
        if(doctorId.HasValue)q=q.Where(a=>a.DoctorId==doctorId);
        var result=await Paginate(Summaries(q.OrderByDescending(a=>a.AdmittedAtUtc).ThenBy(a=>a.Id)),page);result.Search=search;result.State=state;result.DoctorId=doctorId;
        result.Doctors=await DoctorFilters();Audit(Guid.Empty,"AdmissionsViewed");await Db.SaveChangesAsync();return View("~/Views/HospitalCare/Index.cshtml",result);
    }
    public async Task<IActionResult> Details(Guid id)
    {
        var row=await Summaries(Scope().AsNoTracking().Where(a=>a.Id==id)).SingleOrDefaultAsync();if(row==null)return NotFound();
        var a=await Scope().AsNoTracking().SingleAsync(a=>a.Id==id);var clinical=User.IsInRole("Doctor")&&await OwnDoctor(a.DoctorId);
        var model=new CareDetails{Row=row,Inpatient=true,CanClinical=clinical,
            Transfers=await Db.BedTransfers.AsNoTracking().Include(t=>t.FromBed).ThenInclude(b=>b!.Ward).Include(t=>t.ToBed).ThenInclude(b=>b!.Ward).Where(t=>t.AdmissionId==id).OrderBy(t=>t.OccurredAtUtc).ThenBy(t=>t.Id).ToListAsync(),
            History=await Summaries(Scope().AsNoTracking().Where(h=>h.PatientId==a.PatientId).OrderByDescending(h=>h.AdmittedAtUtc)).Take(50).ToListAsync()};
        if(clinical){model.AdmissionClinical=await Db.AdmissionClinicalNotes.AsNoTracking().SingleOrDefaultAsync(n=>n.AdmissionId==id);model.VisibleEmrRevisionId=await VisibleEmr(model.AdmissionClinical?.MedicalRecordId);}
        if(Reception&&a.Status==AdmissionStatus.Admitted)model.AvailableBeds=await AvailableBeds(a.DepartmentId).OrderBy(b=>b.Ward.Code).ThenBy(b=>b.Number).ToListAsync();
        Audit(id,clinical?"AdmissionClinicalViewed":"AdmissionAdminViewed");await Db.SaveChangesAsync();return View("~/Views/HospitalCare/Details.cshtml",model);
    }
    [Authorize(Roles="Receptionist")]
    public Task<IActionResult> Create(string? search)=>Registration(new CareRegistrationForm{Token=Guid.NewGuid()},true,search);
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Receptionist")]
    public async Task<IActionResult> Create(CareRegistrationForm model)
    {
        var existing=await Db.Admissions.AsNoTracking().SingleOrDefaultAsync(a=>a.CreationToken==model.Token);
        if(existing!=null)return existing.PatientId==model.PatientId&&existing.DoctorId==model.DoctorId&&await Db.BedTransfers.AnyAsync(t=>t.AdmissionId==existing.Id&&t.FromBedId==null&&t.ToBedId==model.BedId)?RedirectToAction(nameof(Details),new{id=existing.Id}):BadRequest("Submission token belongs to a different registration.");
        await ValidateRegistration(model);var doctor=await Db.Doctors.SingleOrDefaultAsync(d=>d.Id==model.DoctorId);
        if(doctor!=null&&!await AvailableBeds(doctor.DepartmentId).AnyAsync(b=>b.Id==model.BedId))ModelState.AddModelError("","Choose an available operational bed in the treating department.");
        if(await Db.Admissions.AnyAsync(a=>a.PatientId==model.PatientId&&a.Status!=AdmissionStatus.Discharged))ModelState.AddModelError("","This patient already has an active admission.");
        if(!ModelState.IsValid)return await Registration(model,true);
        var now=Now;var admission=new Admission{CreationToken=model.Token,PatientId=model.PatientId,DoctorId=model.DoctorId,DepartmentId=doctor!.DepartmentId,AdmittedAtUtc=now,AdmittedByUserId=Account};Db.Admissions.Add(admission);
        Db.BedOccupancies.Add(new BedOccupancy{Admission=admission,BedId=model.BedId,StartedAtUtc=now});
        Db.BedTransfers.Add(new BedTransfer{Admission=admission,Token=model.Token,ToBedId=model.BedId,OccurredAtUtc=now,ActorUserId=Account,Reason="Initial bed assignment"});
        await TouchRegistration(model.PatientId,model.DoctorId);Audit(admission.Id,"PatientAdmitted");
        if(!await SaveAsync())return await Registration(model,true);return RedirectToAction(nameof(Details),new{id=admission.Id});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Receptionist")]
    public async Task<IActionResult> Transfer(Guid id,Guid version,Guid bedId,Guid token,string? reason)
    {
        var a=await Db.Admissions.SingleOrDefaultAsync(a=>a.Id==id);if(a==null)return NotFound();
        var repeated=await Db.BedTransfers.AsNoTracking().SingleOrDefaultAsync(t=>t.Token==token);if(repeated!=null)return repeated.AdmissionId==id&&repeated.ToBedId==bedId?RedirectToAction(nameof(Details),new{id}):BadRequest();
        if(a.Status!=AdmissionStatus.Admitted||!CheckVersion(a.Version,version)||token==Guid.Empty||string.IsNullOrWhiteSpace(reason)||reason.Trim().Length>1000)return Conflict("Reload the admission and provide a transfer reason.");
        if(!await AvailableBeds(a.DepartmentId).AnyAsync(b=>b.Id==bedId))return Conflict("Bed unavailable or outside the treating department.");
        var old=await Db.BedOccupancies.SingleAsync(o=>o.AdmissionId==id&&o.EndedAtUtc==null);var now=Now;
        // One transaction and ordered close-before-open avoid immediate partial unique-index collisions.
        await using var tx=await Db.Database.BeginTransactionAsync();
        old.EndedAtUtc=now;a.Version=Guid.NewGuid();if(!await SaveAsync())return Conflict("Admission changed. Reload.");
        Db.BedOccupancies.Add(new BedOccupancy{AdmissionId=id,BedId=bedId,StartedAtUtc=now});Db.BedTransfers.Add(new BedTransfer{AdmissionId=id,Token=token,FromBedId=old.BedId,ToBedId=bedId,OccurredAtUtc=now,ActorUserId=Account,Reason=reason.Trim()});Audit(id,"InpatientBedTransferred");
        if(!await SaveAsync())return Conflict("Transfer conflicted with another operation. Reload.");
        try{await tx.CommitAsync();}catch(Npgsql.PostgresException ex)when(ex.SqlState is "23514" or "23P01" or "23505" or "40P01" or "40001"){return Conflict("Transfer conflicted with another operation. Reload.");}
        return RedirectToAction(nameof(Details),new{id});
    }
    [Authorize(Roles="Doctor")]
    public async Task<IActionResult> Clinical(Guid id)
    {
        var a=await Db.Admissions.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id);if(a==null||a.Status!=AdmissionStatus.Admitted||!await OwnDoctor(a.DoctorId))return NotFound();
        var n=await Db.AdmissionClinicalNotes.AsNoTracking().SingleOrDefaultAsync(n=>n.AdmissionId==id);
        var form=new ClinicalStayForm{Id=id,ParentVersion=a.Version,Version=n?.Version??Guid.Empty,AdmissionReason=n?.AdmissionReason,DischargeSummary=n?.DischargeSummary,MedicalRecordId=n?.MedicalRecordId,Inpatient=true};await ClinicalReferences(form,a.PatientId);
        Audit(id,"AdmissionClinicalDraftViewed");await Db.SaveChangesAsync();return View("~/Views/HospitalCare/Clinical.cshtml",form);
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Doctor")]
    public async Task<IActionResult> Clinical(Guid id,ClinicalStayForm model)
    {
        model.Id=id;model.Inpatient=true;var a=await Db.Admissions.SingleOrDefaultAsync(a=>a.Id==id);if(a==null||a.Status!=AdmissionStatus.Admitted||!await OwnDoctor(a.DoctorId))return NotFound();
        CheckVersion(a.Version,model.ParentVersion);await ClinicalReferences(model,a.PatientId);
        if(string.IsNullOrWhiteSpace(model.AdmissionReason))ModelState.AddModelError("","A clinical admission reason is required.");
        var n=await Db.AdmissionClinicalNotes.SingleOrDefaultAsync(n=>n.AdmissionId==id);if(n!=null)CheckVersion(n.Version,model.Version);else if(model.Version!=Guid.Empty)ModelState.AddModelError("","Reload the clinical form.");
        if(!ModelState.IsValid)return View("~/Views/HospitalCare/Clinical.cshtml",model);
        if(n==null){n=new AdmissionClinical{AdmissionId=id,AuthorUserId=Account};Db.AdmissionClinicalNotes.Add(n);}
        n.AdmissionReason=model.AdmissionReason!.Trim();n.DischargeSummary=Optional(model.DischargeSummary);n.MedicalRecordId=model.MedicalRecordId;n.UpdatedAtUtc=Now;n.Version=Guid.NewGuid();a.Version=Guid.NewGuid();
        (await Db.Doctors.SingleAsync(d=>d.Id==a.DoctorId)).Version=Guid.NewGuid();Audit(id,"AdmissionClinicalUpdated");
        if(!await SaveAsync())return View("~/Views/HospitalCare/Clinical.cshtml",model);return RedirectToAction(nameof(Details),new{id});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Doctor")]
    public async Task<IActionResult> ClearDischarge(Guid id,Guid version)
    {
        var a=await Db.Admissions.SingleOrDefaultAsync(a=>a.Id==id);if(a==null||!await OwnDoctor(a.DoctorId))return NotFound();
        if(a.Status!=AdmissionStatus.Admitted||!CheckVersion(a.Version,version))return Conflict("Admission changed. Reload.");
        var note=await Db.AdmissionClinicalNotes.SingleOrDefaultAsync(n=>n.AdmissionId==id);if(note==null||string.IsNullOrWhiteSpace(note.AdmissionReason)||string.IsNullOrWhiteSpace(note.DischargeSummary))return BadRequest("Save a clinical admission reason and discharge summary before clearance.");
        a.Status=AdmissionStatus.DischargeReady;a.ClearedAtUtc=Now;a.ClearedByUserId=Account;a.Version=Guid.NewGuid();(await Db.Doctors.SingleAsync(d=>d.Id==a.DoctorId)).Version=Guid.NewGuid();Audit(id,"DischargeCleared");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Admission changed. Reload.");
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Receptionist")]
    public async Task<IActionResult> Discharge(Guid id,Guid version,Guid token)
    {
        var a=await Db.Admissions.SingleOrDefaultAsync(a=>a.Id==id);if(a==null)return NotFound();
        if(await Db.BedTransfers.AnyAsync(t=>t.Token==token&&t.AdmissionId==id&&t.ToBedId==null))return RedirectToAction(nameof(Details),new{id});
        if(a.Status!=AdmissionStatus.DischargeReady||!CheckVersion(a.Version,version)||token==Guid.Empty)return Conflict("Only a current doctor-cleared admission can be checked out.");
        var occupancy=await Db.BedOccupancies.SingleAsync(o=>o.AdmissionId==id&&o.EndedAtUtc==null);var now=Now;occupancy.EndedAtUtc=now;a.Status=AdmissionStatus.Discharged;a.DischargedAtUtc=now;a.DischargedByUserId=Account;a.Version=Guid.NewGuid();
        Db.BedTransfers.Add(new BedTransfer{Token=token,AdmissionId=id,FromBedId=occupancy.BedId,OccurredAtUtc=now,ActorUserId=Account,Reason="Doctor-cleared discharge checkout"});Audit(id,"PatientDischarged");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Discharge conflicted with another operation. Reload.");
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Doctor")]
    public async Task<IActionResult> Clearance(Guid id,Guid version)
    {
        var a=await Db.Admissions.SingleOrDefaultAsync(a=>a.Id==id);if(a==null||!await OwnDoctor(a.DoctorId))return NotFound();
        if(a.Status!=AdmissionStatus.Admitted||!CheckVersion(a.Version,version))return Conflict("Only a current Admitted record can be cleared.");
        var n=await Db.AdmissionClinicalNotes.SingleOrDefaultAsync(n=>n.AdmissionId==id);
        if(n==null||string.IsNullOrWhiteSpace(n.AdmissionReason)||string.IsNullOrWhiteSpace(n.DischargeSummary))return BadRequest("Save the clinical admission reason and discharge summary before clearance.");
        a.Status=AdmissionStatus.DischargeReady;a.ClearedAtUtc=Now;a.ClearedByUserId=Account;a.Version=Guid.NewGuid();(await Db.Doctors.SingleAsync(d=>d.Id==a.DoctorId)).Version=Guid.NewGuid();Audit(id,"AdmissionDischargeCleared");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Admission changed. Reload.");
    }
}
