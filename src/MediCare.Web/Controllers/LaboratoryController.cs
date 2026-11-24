using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Doctor,Laboratory Staff")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class LaboratoryController(MediCareDbContext db,TimeProvider clock) : ClinicalAdministrationControllerBase(db)
{
    private string Account=>User.FindFirstValue(ClaimTypes.NameIdentifier)??"";
    private bool Staff=>User.IsInRole("Laboratory Staff");
    private IQueryable<LabRequest> Authorized()=>LaboratoryAccess.Visible(Db,Account,Staff);
    // The worklist deliberately projects no DOB, contacts, symptoms, diagnoses or EMR revisions.
    internal static IQueryable<LabRequestRow> Rows(IQueryable<LabRequest> query)=>query.Select(r=>new LabRequestRow {
        Id=r.Id,MedicalRecordId=r.MedicalRecordId,PatientId=r.MedicalRecord.PatientId,
        PatientLabel=r.MedicalRecord.Patient.PatientNumber+" — "+r.MedicalRecord.Patient.FirstName+" "+r.MedicalRecord.Patient.LastName,
        DoctorName=r.MedicalRecord.Doctor.FirstName+" "+r.MedicalRecord.Doctor.LastName,RequestedByUserId=r.RequestedByUserId,
        TestCode=r.TestCode,TestName=r.TestName,Category=r.Category,SampleType=r.SampleType,ResultKind=r.ResultKind,Unit=r.Unit,ReferenceRange=r.ReferenceRange,
        Status=r.Status,SampleIdentifier=r.SampleIdentifier,RequestedAtUtc=r.RequestedAtUtc,CollectedAtUtc=r.CollectedAtUtc,CollectedByUserId=r.CollectedByUserId,
        ReceivedAtUtc=r.ReceivedAtUtc,ReceivedByUserId=r.ReceivedByUserId,ProcessingAtUtc=r.ProcessingAtUtc,ProcessingByUserId=r.ProcessingByUserId,
        CompletedAtUtc=r.CompletedAtUtc,CancelledAtUtc=r.CancelledAtUtc,CancelledByUserId=r.CancelledByUserId,CancellationReason=r.CancellationReason,Version=r.Version });

    [HttpGet]
    public async Task<IActionResult> Index(string? search,LabRequestStatus? status,DateOnly? date,Guid? patientId,Guid? medicalRecordId,int page=1,bool worklist=false)
    {
        search=(search??"").Trim();
        if(worklist && !Staff)return Forbid();
        if(!ModelState.IsValid || search.Length>100 || (status.HasValue && !Enum.IsDefined(status.Value)) || (date.HasValue && date.Value.Year is <1900 or >9998))return BadRequest("Invalid laboratory filter.");
        var query=Authorized();
        if(patientId.HasValue)query=query.Where(r=>r.MedicalRecord.PatientId==patientId);
        if(medicalRecordId.HasValue)query=query.Where(r=>r.MedicalRecordId==medicalRecordId);
        if(status.HasValue)query=query.Where(r=>r.Status==status);
        else if(worklist)query=query.Where(r=>r.Status!=LabRequestStatus.Completed && r.Status!=LabRequestStatus.Cancelled);
        if(date.HasValue){var from=HospitalTime.ToUtc(date.Value,TimeOnly.MinValue);var until=from.AddDays(1);query=query.Where(r=>r.RequestedAtUtc>=from && r.RequestedAtUtc<until);}
        if(search.Length>0){var term=search.ToUpperInvariant();query=query.Where(r=>r.TestCode.Contains(term)||r.TestName.ToUpper().Contains(term)||r.MedicalRecord.Patient.PatientNumber.ToUpper().Contains(term)||
            (r.MedicalRecord.Patient.FirstName+" "+r.MedicalRecord.Patient.LastName).ToUpper().Contains(term)||(r.SampleIdentifier!=null && r.SampleIdentifier.Contains(term)));}
        var count=await query.CountAsync();var pages=Math.Max(1,(int)Math.Ceiling(count/20d));page=Math.Clamp(page,1,pages);
        var model=new LabRequestList {Search=search,Status=status,Date=date,PatientId=patientId,MedicalRecordId=medicalRecordId,Worklist=worklist,Page=page,TotalPages=pages,Total=count,
            Requests=await Rows(query.OrderByDescending(r=>r.RequestedAtUtc).ThenBy(r=>r.Id)).Skip((page-1)*20).Take(20).ToListAsync()};
        Audit(patientId??Guid.Empty,"LabWorklistViewed");await Db.SaveChangesAsync();return View("Index",model);
    }
    [HttpGet,Authorize(Roles="Laboratory Staff")]
    public Task<IActionResult> Worklist()=>Index(null,null,null,null,null,1,true);

    [HttpGet]
    public async Task<IActionResult> Details(Guid id)
    {
        var model=await DetailsModel(id);if(model==null)return NotFound();
        Audit(id,"LabRequestViewed");await Db.SaveChangesAsync();return View(model);
    }
    private async Task<LabRequestDetails?> DetailsModel(Guid id)
    {
        var request=await Rows(Authorized().Where(r=>r.Id==id)).SingleOrDefaultAsync();if(request==null)return null;
        var results=await Db.LabResultRevisions.AsNoTracking().Where(r=>r.LabRequestId==id && (r.FinalizedAtUtc!=null || (Staff && r.AuthorUserId==Account))).OrderBy(r=>r.Number).ToListAsync();
        return new() {Request=request,Results=results,LaboratoryStaff=Staff,Account=Account,
            LatestRevisionId=await Db.LabResultRevisions.Where(r=>r.LabRequestId==id).OrderByDescending(r=>r.Number).Select(r=>(Guid?)r.Id).FirstOrDefaultAsync(),
            CanCancel=LaboratoryRules.MayCancel(new LabRequest {Status=request.Status}) && (Staff || (request.RequestedByUserId==Account && request.Status==LabRequestStatus.Requested))};
    }
    private Task<MedicalRecord?> OrderEncounter(Guid id)=>Db.MedicalRecords.Include(r=>r.Patient).Include(r=>r.Doctor).Include(r=>r.Appointment)
        .SingleOrDefaultAsync(r=>r.Id==id && r.AuthorUserId==Account && !r.Patient.IsArchived && r.Doctor.IsActive && r.Doctor.IdentityUserId==Account &&
            MedicalRecordAccess.CareAppointments(Db,Account).Any(a=>a.Id==r.AppointmentId && a.DoctorId==r.DoctorId && a.PatientId==r.PatientId));
    private async Task<LabRequestForm> OrderChoices(LabRequestForm form,MedicalRecord encounter)
    {
        form.PatientLabel=encounter.Patient.PatientNumber+" — "+encounter.Patient.FirstName+" "+encounter.Patient.LastName;
        form.Tests=await Db.LabTests.AsNoTracking().Where(t=>t.IsActive).OrderBy(t=>t.Category).ThenBy(t=>t.Name)
            .Select(t=>new SelectListItem(t.Code+" — "+t.Name+" ("+t.SampleType+", "+t.ResultKind+")",t.Id.ToString())).ToListAsync();
        return form;
    }
    [HttpGet,Authorize(Roles="Doctor")]
    public async Task<IActionResult> Create(Guid medicalRecordId)
    {
        var encounter=await OrderEncounter(medicalRecordId);return encounter==null?NotFound():View("RequestForm",await OrderChoices(new(){MedicalRecordId=medicalRecordId,CreationToken=Guid.NewGuid()},encounter));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Doctor")]
    public async Task<IActionResult> Create(LabRequestForm form)
    {
        var encounter=await OrderEncounter(form.MedicalRecordId);if(encounter==null)return NotFound();
        var existing=await Db.LabRequests.AsNoTracking().SingleOrDefaultAsync(r=>r.CreationToken==form.CreationToken);
        if(existing!=null)
        {
            if(existing.RequestedByUserId==Account && existing.MedicalRecordId==form.MedicalRecordId && existing.LabTestId==form.LabTestId)return RedirectToAction(nameof(Details),new{id=existing.Id});
            ModelState.AddModelError("","This form has already been used. Open a new request form.");
        }
        if(form.CreationToken==Guid.Empty)ModelState.AddModelError("","Reload the request form.");
        var test=await Db.LabTests.SingleOrDefaultAsync(t=>t.Id==form.LabTestId && t.IsActive);
        if(test==null)ModelState.AddModelError(nameof(form.LabTestId),"Select an active laboratory test.");
        if(!ModelState.IsValid || test==null)return View("RequestForm",await OrderChoices(form,encounter));
        var request=new LabRequest {MedicalRecordId=encounter.Id,LabTestId=test.Id,CreationToken=form.CreationToken,RequestedByUserId=Account,
            TestCode=test.Code,TestName=test.Name,Category=test.Category,SampleType=test.SampleType,ResultKind=test.ResultKind,Unit=test.Unit,ReferenceRange=test.ReferenceRange,RequestedAtUtc=clock.GetUtcNow().UtcDateTime};
        // Coordinate against catalogue edits/deactivation and care-relationship changes.
        test.Version=Guid.NewGuid();encounter.Version=Guid.NewGuid();encounter.Patient.Version=Guid.NewGuid();encounter.Doctor.Version=Guid.NewGuid();
        var appointment=await Db.Appointments.SingleAsync(a=>a.Id==encounter.AppointmentId);appointment.Version=Guid.NewGuid();
        Db.LabRequests.Add(request);Audit(request.Id,"LabRequestCreated");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id=request.Id}):View("RequestForm",await OrderChoices(form,encounter));
    }

    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> Progress(Guid id,Guid version,string command)
    {
        var request=await Db.LabRequests.SingleOrDefaultAsync(r=>r.Id==id);if(request==null)return NotFound();
        if(!CheckVersion(request.Version,version))return Conflict("This request changed. Reload before continuing.");
        var now=clock.GetUtcNow().UtcDateTime;string action;
        switch(command)
        {
            case "Collect" when LaboratoryRules.MayCollect(request):
                request.SampleIdentifier="LAB-"+Guid.NewGuid().ToString("N").ToUpperInvariant();request.CollectedAtUtc=now;request.CollectedByUserId=Account;request.Status=LabRequestStatus.SampleCollected;action="LabSampleCollected";break;
            case "Receive" when LaboratoryRules.MayReceive(request):request.ReceivedAtUtc=now;request.ReceivedByUserId=Account;action="LabSampleReceived";break;
            case "Process" when LaboratoryRules.MayProcess(request):request.ProcessingAtUtc=now;request.ProcessingByUserId=Account;request.Status=LabRequestStatus.InProgress;action="LabProcessingStarted";break;
            default:return BadRequest("Invalid laboratory transition. Reload the request and follow collection, receipt and processing in order.");
        }
        request.Version=Guid.NewGuid();Audit(id,action);return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("The request changed while saving. Reload before continuing.");
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(Guid id,Guid version,string? reason)
    {
        if(!await Authorized().AnyAsync(r=>r.Id==id))return NotFound();
        var request=await Db.LabRequests.SingleAsync(r=>r.Id==id);
        if(!Staff && (request.RequestedByUserId!=Account || request.Status!=LabRequestStatus.Requested))return Forbid();
        if(!LaboratoryRules.MayCancel(request))return BadRequest("Completed and cancelled requests are terminal.");
        if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length>1000)return BadRequest("A cancellation reason of 1–1000 characters is required.");
        if(!CheckVersion(request.Version,version))return Conflict("This request changed. Reload before cancelling.");
        request.Status=LabRequestStatus.Cancelled;request.CancelledAtUtc=clock.GetUtcNow().UtcDateTime;request.CancelledByUserId=Account;request.CancellationReason=reason.Trim();request.Version=Guid.NewGuid();
        Audit(id,"LabRequestCancelled");return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("The request changed while saving. Reload before continuing.");
    }

    [HttpGet,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> EnterResult(Guid id)
    {
        var request=await Rows(Authorized().Where(r=>r.Id==id && r.Status==LabRequestStatus.InProgress)).SingleOrDefaultAsync();if(request==null)return NotFound();
        var existing=await Db.LabResultRevisions.Where(r=>r.LabRequestId==id).SingleOrDefaultAsync();
        if(existing!=null)return existing.AuthorUserId==Account?RedirectToAction(nameof(EditResult),new{id=existing.Id}):Conflict("Another laboratory staff member owns this result draft.");
        return View("ResultForm",new LabResultForm {LabRequestId=id,CreationToken=Guid.NewGuid(),Request=request});
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> EnterResult(LabResultForm form)
    {
        var request=await Db.LabRequests.SingleOrDefaultAsync(r=>r.Id==form.LabRequestId && r.Status==LabRequestStatus.InProgress);if(request==null)return NotFound();
        var existing=await Db.LabResultRevisions.SingleOrDefaultAsync(r=>r.CreationToken==form.CreationToken);
        if(existing!=null && existing.AuthorUserId==Account && existing.LabRequestId==request.Id)return RedirectToAction(nameof(Details),new{id=request.Id});
        if(form.CreationToken==Guid.Empty || existing!=null)ModelState.AddModelError("","Reload the result entry form.");
        if(await Db.LabResultRevisions.AnyAsync(r=>r.LabRequestId==request.Id))return Conflict("This request already has a result. Open the existing revision.");
        ValidateResult(form,request,false);
        if(!ModelState.IsValid)return View("ResultForm",await PrepareResult(form,request));
        var revision=new LabResultRevision {LabRequest=request,Number=1,CreationToken=form.CreationToken,AuthorUserId=Account,CreatedAtUtc=clock.GetUtcNow().UtcDateTime};
        CopyResult(form,revision);Db.LabResultRevisions.Add(revision);request.Version=Guid.NewGuid();Audit(revision.Id,"LabResultDraftCreated");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id=request.Id}):View("ResultForm",await PrepareResult(form,request));
    }
    private Task<LabResultRevision?> OwnDraft(Guid id)=>Db.LabResultRevisions.Include(r=>r.LabRequest).SingleOrDefaultAsync(r=>r.Id==id && r.AuthorUserId==Account && r.FinalizedAtUtc==null &&
        (r.LabRequest.Status==LabRequestStatus.InProgress || (r.Number>1 && r.LabRequest.Status==LabRequestStatus.Completed)));
    [HttpGet,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> EditResult(Guid id)
    {
        var revision=await OwnDraft(id);return revision==null?NotFound():View("ResultForm",await PrepareResult(ToForm(revision),revision.LabRequest));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> EditResult(Guid id,LabResultForm form)
    {
        var revision=await OwnDraft(id);if(revision==null)return NotFound();form.Id=id;form.LabRequestId=revision.LabRequestId;form.IsCorrection=revision.Number>1;
        CheckVersion(revision.Version,form.Version);ValidateResult(form,revision.LabRequest,false);
        if(!ModelState.IsValid)return View("ResultForm",await PrepareResult(form,revision.LabRequest));
        CopyResult(form,revision);revision.Version=Guid.NewGuid();revision.LabRequest.Version=Guid.NewGuid();Audit(id,"LabResultDraftUpdated");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id=revision.LabRequestId}):View("ResultForm",await PrepareResult(form,revision.LabRequest));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> FinalizeResult(Guid id,Guid version)
    {
        var revision=await OwnDraft(id);if(revision==null)return NotFound();var form=ToForm(revision);
        CheckVersion(revision.Version,version);ValidateResult(form,revision.LabRequest,true);
        if(!ModelState.IsValid)return View("ResultForm",await PrepareResult(form,revision.LabRequest));
        var now=clock.GetUtcNow().UtcDateTime;revision.FinalizedAtUtc=now;revision.UpdatedAtUtc=now;revision.Version=Guid.NewGuid();
        var request=revision.LabRequest;request.Version=Guid.NewGuid();
        if(revision.Number==1){request.Status=LabRequestStatus.Completed;request.CompletedAtUtc=now;}
        Audit(id,revision.Number==1?"LabResultFinalized":"LabResultCorrectionFinalized");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id=request.Id}):View("ResultForm",await PrepareResult(form,request));
    }
    [HttpPost,ValidateAntiForgeryToken,Authorize(Roles="Laboratory Staff")]
    public async Task<IActionResult> Correct(Guid id,Guid version,string? reason)
    {
        var previous=await Db.LabResultRevisions.Include(r=>r.LabRequest).SingleOrDefaultAsync(r=>r.Id==id && r.AuthorUserId==Account && r.FinalizedAtUtc!=null && r.LabRequest.Status==LabRequestStatus.Completed);
        if(previous==null)return NotFound();
        if(!CheckVersion(previous.Version,version) || await Db.LabResultRevisions.AnyAsync(r=>r.LabRequestId==previous.LabRequestId && r.Number>previous.Number))return Conflict("The result changed. Reload before starting a correction.");
        if(string.IsNullOrWhiteSpace(reason)||reason.Trim().Length>1000)return BadRequest("A correction reason of 1–1000 characters is required.");
        var next=new LabResultRevision {LabRequest=previous.LabRequest,Number=previous.Number+1,AuthorUserId=Account,CreationToken=Guid.NewGuid(),NumericValue=previous.NumericValue,TextValue=previous.TextValue,
            Comments=previous.Comments,CorrectionReason=reason.Trim(),CreatedAtUtc=clock.GetUtcNow().UtcDateTime,UpdatedAtUtc=clock.GetUtcNow().UtcDateTime};
        Db.LabResultRevisions.Add(next);previous.LabRequest.Version=Guid.NewGuid();Audit(next.Id,"LabResultCorrectionStarted");
        return await SaveAsync()?RedirectToAction(nameof(EditResult),new{id=next.Id}):Conflict("The result changed while starting a correction. Reload before continuing.");
    }

    [HttpGet]
    public async Task<IActionResult> Report(Guid id,bool pdf=false)
    {
        var result=await Db.LabResultRevisions.AsNoTracking().SingleOrDefaultAsync(r=>r.Id==id && r.FinalizedAtUtc!=null && Authorized().Any(q=>q.Id==r.LabRequestId));
        if(result==null)return NotFound();var request=await Rows(Authorized().Where(r=>r.Id==result.LabRequestId)).SingleAsync();
        var report=new LabReport {Request=request,Result=result};
        if(pdf && !LaboratoryPdf.Supports(report))return UnprocessableEntity("Use the printable report and browser Save as PDF for scripts outside Latin-script PDF support.");
        var bytes=pdf?LaboratoryPdf.Render(report):null;
        Audit(id,pdf?"LabReportExported":"LabReportViewed");await Db.SaveChangesAsync();
        if(bytes!=null){Response.Headers.XContentTypeOptions="nosniff";return File(bytes,"application/pdf",$"laboratory-report-{id:N}.pdf");}
        return View(report);
    }
    private void ValidateResult(LabResultForm form,LabRequest request,bool finalizing)
    {
        var error=LaboratoryRules.ValidateResult(request.ResultKind,form.NumericValue,form.TextValue,finalizing);if(error!=null)ModelState.AddModelError("",error);
        if(form.IsCorrection && string.IsNullOrWhiteSpace(form.CorrectionReason))ModelState.AddModelError(nameof(form.CorrectionReason),"A correction reason is required.");
    }
    private async Task<LabResultForm> PrepareResult(LabResultForm form,LabRequest request)
    {form.Request=await Rows(Authorized().Where(r=>r.Id==request.Id)).SingleAsync();return form;}
    private void CopyResult(LabResultForm form,LabResultRevision revision)
    {revision.NumericValue=form.NumericValue;revision.TextValue=Optional(form.TextValue);revision.Comments=Optional(form.Comments);revision.CorrectionReason=revision.Number>1?Optional(form.CorrectionReason):null;revision.UpdatedAtUtc=clock.GetUtcNow().UtcDateTime;}
    private static LabResultForm ToForm(LabResultRevision r)=>new() {Id=r.Id,LabRequestId=r.LabRequestId,Version=r.Version,NumericValue=r.NumericValue,TextValue=r.TextValue,Comments=r.Comments,CorrectionReason=r.CorrectionReason,IsCorrection=r.Number>1};
}
