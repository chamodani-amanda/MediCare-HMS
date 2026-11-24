using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Accountant")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class BillingController(MediCareDbContext db,TimeProvider clock):ClinicalAdministrationControllerBase(db)
{
    private DateTime Now=>clock.GetUtcNow().UtcDateTime;
    private string Actor=>User.FindFirstValue(ClaimTypes.NameIdentifier)??throw new InvalidOperationException("Authenticated actor required.");
    private IQueryable<BillingRow> Rows()=>Db.BillingInvoices.AsNoTracking().Join(Db.Patients,i=>i.PatientId,p=>p.Id,(i,p)=>new BillingRow{Invoice=i,PatientNumber=p.PatientNumber,PatientName=p.FirstName+" "+p.LastName,Paid=Db.BillingPayments.Where(x=>x.InvoiceId==i.Id).Sum(x=>(decimal?)(x.ReversesId==null?x.Amount:-x.Amount))??0});
    private async Task<decimal> Paid(Guid id)=>await Db.BillingPayments.Where(x=>x.InvoiceId==id).SumAsync(x=>(decimal?)(x.ReversesId==null?x.Amount:-x.Amount))??0;
    private async Task<BillingPage<T>> Page<T>(IQueryable<T> q,int page,string? search=null,string? status=null){var count=await q.CountAsync();page=Math.Clamp(page,1,Math.Max(1,(count+19)/20));return new(){Items=await q.Skip((page-1)*20).Take(20).ToListAsync(),Page=page,TotalPages=(count+19)/20,Search=search,Status=status};}
    private Task<List<BillingChoice>> Patients(string? search)=>Db.Patients.AsNoTracking().Where(p=>search!=null&&(p.PatientNumber.Contains(search)||p.FirstName.Contains(search)||p.LastName.Contains(search))).OrderBy(p=>p.PatientNumber).Select(p=>new BillingChoice{Id=p.Id,Label=p.PatientNumber+" — "+p.FirstName+" "+p.LastName}).Take(50).ToListAsync();
    public async Task<IActionResult> Index(DateOnly? from,DateOnly? to,int page=1)
    {
        if(from>to)return BadRequest("The end date must follow the start date.");var q=Db.BillingPayments.AsNoTracking();
        if(from.HasValue){var start=HospitalTime.ToUtc(from.Value,TimeOnly.MinValue);q=q.Where(p=>p.RecordedAtUtc>=start);}if(to.HasValue&&to.Value<DateOnly.MaxValue){var end=HospitalTime.ToUtc(to.Value.AddDays(1),TimeOnly.MinValue);q=q.Where(p=>p.RecordedAtUtc<end);}
        var outstanding=Rows().Where(r=>r.Invoice.State==InvoiceState.Issued&&r.Paid<r.Invoice.Total);var count=await q.CountAsync();page=Math.Clamp(page,1,Math.Max(1,(count+19)/20));
        return View(new BillingDashboard{OutstandingInvoices=await outstanding.CountAsync(),Outstanding=await outstanding.SumAsync(r=>(decimal?)(r.Invoice.Total-r.Paid))??0,RecordedPayments=await q.Where(p=>p.ReversesId==null).SumAsync(p=>(decimal?)p.Amount)??0,Reversals=await q.Where(p=>p.ReversesId!=null).SumAsync(p=>(decimal?)p.Amount)??0,Payments=await q.OrderByDescending(p=>p.RecordedAtUtc).ThenBy(p=>p.Id).Skip((page-1)*20).Take(20).ToListAsync(),Page=page,TotalPages=(count+19)/20,From=from,To=to});
    }
    public async Task<IActionResult> Invoices(string? search,string? status,int page=1)
    {
        var q=Rows();if(!string.IsNullOrWhiteSpace(search)){search=search.Trim();q=q.Where(r=>r.PatientNumber.Contains(search)||r.PatientName.Contains(search));}
        q=status switch{"Draft"=>q.Where(r=>r.Invoice.State==InvoiceState.Draft),"Voided"=>q.Where(r=>r.Invoice.State==InvoiceState.Voided),"Unpaid"=>q.Where(r=>r.Invoice.State==InvoiceState.Issued&&r.Paid==0&&r.Invoice.Total>0),"PartiallyPaid"=>q.Where(r=>r.Invoice.State==InvoiceState.Issued&&r.Paid>0&&r.Paid<r.Invoice.Total),"Paid"=>q.Where(r=>r.Invoice.State==InvoiceState.Issued&&r.Paid==r.Invoice.Total),"Outstanding"=>q.Where(r=>r.Invoice.State==InvoiceState.Issued&&r.Paid<r.Invoice.Total),_=>q};
        return View(await Page(q.OrderByDescending(r=>r.Invoice.CreatedAtUtc).ThenBy(r=>r.Invoice.Id),page,search,status));
    }
    public async Task<IActionResult> Charges(string? search,int page=1)
    {
        var q=Db.PatientCharges.AsNoTracking().Join(Db.Patients,c=>c.PatientId,p=>p.Id,(c,p)=>new ChargeRow{Charge=c,PatientName=p.PatientNumber+" — "+p.FirstName+" "+p.LastName});
        if(!string.IsNullOrWhiteSpace(search)){search=search.Trim();q=q.Where(x=>x.PatientName.Contains(search)||x.Charge.Description.Contains(search));}return View(await Page(q.OrderByDescending(x=>x.Charge.CreatedAtUtc).ThenBy(x=>x.Charge.Id),page,search));
    }
    public async Task<IActionResult> Charge(Guid? patientId,string? search)
    {
        var form=new ChargeForm{PatientId=patientId??Guid.Empty,Token=Guid.NewGuid()};return await ChargeView(form,search);
    }
    private async Task<IActionResult> ChargeView(ChargeForm form,string? search=null)
    {
        form.Patients=await Patients(search);if(form.PatientId!=Guid.Empty&&!form.Patients.Any(p=>p.Id==form.PatientId)){var p=await Db.Patients.Where(p=>p.Id==form.PatientId).Select(p=>new BillingChoice{Id=p.Id,Label=p.PatientNumber+" — "+p.FirstName+" "+p.LastName}).SingleOrDefaultAsync();if(p!=null)form.Patients.Add(p);}
        var now=Now;form.Services=await Db.BillingServices.AsNoTracking().Where(s=>s.IsActive&&Db.BillingPrices.Any(p=>p.ServiceId==s.Id&&p.EffectiveAtUtc<=now)).OrderBy(s=>s.Code).Select(s=>new BillingChoice{Id=s.Id,Label=s.Code+" — "+s.Name,UnitPrice=Db.BillingPrices.Where(p=>p.ServiceId==s.Id&&p.EffectiveAtUtc<=now).OrderByDescending(p=>p.EffectiveAtUtc).Select(p=>(decimal?)p.UnitPrice).FirstOrDefault()}).ToListAsync();
        // Source choices deliberately project identifiers and dates only, never clinical payloads.
        if(form.PatientId!=Guid.Empty){
            form.Sources.AddRange(await Db.Appointments.Where(a=>a.PatientId==form.PatientId).OrderByDescending(a=>a.StartAtUtc).Take(100).Select(a=>new BillingSourceChoice{Id=a.Id,Kind="Appointment",Label="Appointment "+a.Id,OccurredAtUtc=a.StartAtUtc}).ToListAsync());
            form.Sources.AddRange(await Db.LabRequests.Where(a=>a.MedicalRecord.PatientId==form.PatientId).OrderByDescending(a=>a.RequestedAtUtc).Take(100).Select(a=>new BillingSourceChoice{Id=a.Id,Kind="Laboratory",Label="Laboratory request "+a.Id,OccurredAtUtc=a.RequestedAtUtc}).ToListAsync());
            form.Sources.AddRange(await Db.PharmacyDispenses.Where(a=>a.Prescription.MedicalRecordRevision.MedicalRecord.PatientId==form.PatientId).OrderByDescending(a=>a.OccurredAtUtc).Take(100).Select(a=>new BillingSourceChoice{Id=a.Id,Kind="Pharmacy",Label="Dispensing entry "+a.Id,OccurredAtUtc=a.OccurredAtUtc}).ToListAsync());
            form.Sources.AddRange(await Db.Admissions.Where(a=>a.PatientId==form.PatientId).OrderByDescending(a=>a.AdmittedAtUtc).Take(100).Select(a=>new BillingSourceChoice{Id=a.Id,Kind="Admission",Label="Admission "+a.Id,OccurredAtUtc=a.AdmittedAtUtc}).ToListAsync());
            form.Sources.AddRange(await Db.OutpatientVisits.Where(a=>a.PatientId==form.PatientId).OrderByDescending(a=>a.RegisteredAtUtc).Take(100).Select(a=>new BillingSourceChoice{Id=a.Id,Kind="Outpatient",Label="Outpatient visit "+a.Id,OccurredAtUtc=a.RegisteredAtUtc}).ToListAsync());
        }return View("Charge",form);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Charge(ChargeForm form)
    {
        var old=await Db.PatientCharges.Include(c=>c.Price).SingleOrDefaultAsync(c=>c.Token==form.Token);
        if(old!=null)return old.PatientId==form.PatientId&&old.Price.ServiceId==form.ServiceId&&old.Quantity==form.Quantity&&Source(old)==(form.SourceKind??"",form.SourceId)?RedirectToAction(nameof(Charges)):Conflict("Submission token was already used for different charge information.");
        if(form.Token==Guid.Empty||!BillingRules.Quantity(form.Quantity))ModelState.AddModelError("","A nonempty submission token and positive quantity with up to three decimal places are required.");
        if(!await Db.Patients.AnyAsync(p=>p.Id==form.PatientId))ModelState.AddModelError("","Select an existing patient.");
        var now=Now;var price=await Db.BillingPrices.Include(p=>p.Service).Where(p=>p.ServiceId==form.ServiceId&&p.Service.IsActive&&p.EffectiveAtUtc<=now).OrderByDescending(p=>p.EffectiveAtUtc).FirstOrDefaultAsync();
        if(price==null)ModelState.AddModelError("","Select an active service with a configured effective price.");
        if(!await SourceMatches(form.PatientId,form.SourceKind,form.SourceId))ModelState.AddModelError("","Choose a matching patient source or leave both source fields empty.");
        if(form.SourceId.HasValue&&await SourceCharged(form))ModelState.AddModelError("","This source has already been charged for this service.");
        if(price!=null&&!BillingRules.Money(BillingRules.LineTotal(form.Quantity,price.UnitPrice)))ModelState.AddModelError("","Charge total exceeds the supported monetary range.");
        if(!ModelState.IsValid)return await ChargeView(form);
        var charge=new PatientCharge{Token=form.Token,PatientId=form.PatientId,PriceId=price!.Id,Description=price.Service.Code+" — "+price.Service.Name,Quantity=form.Quantity,UnitPrice=price.UnitPrice,Total=BillingRules.LineTotal(form.Quantity,price.UnitPrice),CreatedAtUtc=now,ActorUserId=Actor};
        switch(form.SourceKind){case "Appointment":charge.AppointmentId=form.SourceId;break;case "Laboratory":charge.LabRequestId=form.SourceId;break;case "Pharmacy":charge.PharmacyDispenseId=form.SourceId;break;case "Admission":charge.AdmissionId=form.SourceId;break;case "Outpatient":charge.OutpatientVisitId=form.SourceId;break;}
        price.Service.Version=Guid.NewGuid();Db.Add(charge);Audit(charge.Id,"PatientChargeCreated");return await SaveAsync()?RedirectToAction(nameof(Charges)):await ChargeView(form);
    }
    private static (string,Guid?) Source(PatientCharge c)=>c.AppointmentId.HasValue?("Appointment",c.AppointmentId):c.LabRequestId.HasValue?("Laboratory",c.LabRequestId):c.PharmacyDispenseId.HasValue?("Pharmacy",c.PharmacyDispenseId):c.AdmissionId.HasValue?("Admission",c.AdmissionId):c.OutpatientVisitId.HasValue?("Outpatient",c.OutpatientVisitId):("",null);
    private Task<bool> SourceMatches(Guid patient,string? kind,Guid? id)=>kind switch{
        null or ""=>Task.FromResult(id==null),"Appointment"=>Db.Appointments.AnyAsync(x=>x.Id==id&&x.PatientId==patient),"Laboratory"=>Db.LabRequests.AnyAsync(x=>x.Id==id&&x.MedicalRecord.PatientId==patient),"Pharmacy"=>Db.PharmacyDispenses.AnyAsync(x=>x.Id==id&&x.Prescription.MedicalRecordRevision.MedicalRecord.PatientId==patient),"Admission"=>Db.Admissions.AnyAsync(x=>x.Id==id&&x.PatientId==patient),"Outpatient"=>Db.OutpatientVisits.AnyAsync(x=>x.Id==id&&x.PatientId==patient),_=>Task.FromResult(false)};
    private Task<bool> SourceCharged(ChargeForm f)=>Db.PatientCharges.AnyAsync(c=>c.Price.ServiceId==f.ServiceId&&((f.SourceKind=="Appointment"&&c.AppointmentId==f.SourceId)||(f.SourceKind=="Laboratory"&&c.LabRequestId==f.SourceId)||(f.SourceKind=="Pharmacy"&&c.PharmacyDispenseId==f.SourceId)||(f.SourceKind=="Admission"&&c.AdmissionId==f.SourceId)||(f.SourceKind=="Outpatient"&&c.OutpatientVisitId==f.SourceId)));
    public async Task<IActionResult> Draft(Guid? id,Guid? patientId,string? search)
    {
        var form=new InvoiceForm{PatientId=patientId??Guid.Empty,Token=Guid.NewGuid()};if(id.HasValue){var invoice=await Db.BillingInvoices.Include(i=>i.Lines).SingleOrDefaultAsync(i=>i.Id==id);if(invoice==null)return NotFound();if(invoice.State!=InvoiceState.Draft)return Conflict("Only drafts can be edited.");form=new(){Id=invoice.Id,Token=invoice.Token,PatientId=invoice.PatientId,Version=invoice.Version,ChargeIds=invoice.Lines.Select(l=>l.ChargeId).ToList()};}return await DraftView(form,search);
    }
    private async Task<IActionResult> DraftView(InvoiceForm f,string? search=null)
    {
        f.Patients=await Patients(search);if(f.PatientId!=Guid.Empty&&!f.Patients.Any(p=>p.Id==f.PatientId)){var p=await Db.Patients.Where(p=>p.Id==f.PatientId).Select(p=>new BillingChoice{Id=p.Id,Label=p.PatientNumber+" — "+p.FirstName+" "+p.LastName}).SingleOrDefaultAsync();if(p!=null)f.Patients.Add(p);}
        f.Charges=await Db.PatientCharges.AsNoTracking().Where(c=>c.PatientId==f.PatientId&&!Db.BillingInvoiceLines.Any(l=>l.ChargeId==c.Id&&l.InvoiceId!=f.Id&&l.Invoice.State!=InvoiceState.Voided)).OrderBy(c=>c.CreatedAtUtc).Take(500).ToListAsync();return View("Draft",f);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Draft(InvoiceForm form)
    {
        var invoice=form.Id==Guid.Empty?await Db.BillingInvoices.Include(i=>i.Lines).SingleOrDefaultAsync(i=>i.Token==form.Token):await Db.BillingInvoices.Include(i=>i.Lines).SingleOrDefaultAsync(i=>i.Id==form.Id);
        if(form.Id==Guid.Empty&&invoice!=null)return invoice.PatientId==form.PatientId&&invoice.Lines.Select(l=>l.ChargeId).Order().SequenceEqual(form.ChargeIds.Order())?RedirectToAction(nameof(Details),new{id=invoice.Id}):Conflict("Submission token was already used.");
        if(form.Id!=Guid.Empty&&invoice==null)return NotFound();if(invoice!=null){if(invoice.State!=InvoiceState.Draft)return Conflict("Issued invoices cannot be edited.");CheckVersion(invoice.Version,form.Version);if(invoice.PatientId!=form.PatientId||invoice.Token!=form.Token)ModelState.AddModelError("","Invoice identity cannot change.");}
        if(form.Token==Guid.Empty||!await Db.Patients.AnyAsync(p=>p.Id==form.PatientId))ModelState.AddModelError("","Select a patient and use a valid submission token.");
        if(form.ChargeIds.Count>500||form.ChargeIds.Distinct().Count()!=form.ChargeIds.Count)ModelState.AddModelError("","Select at most 500 distinct charges.");
        var charges=await Db.PatientCharges.Where(c=>form.ChargeIds.Contains(c.Id)&&c.PatientId==form.PatientId).ToListAsync();
        if(charges.Count!=form.ChargeIds.Count||await Db.BillingInvoiceLines.AnyAsync(l=>form.ChargeIds.Contains(l.ChargeId)&&l.InvoiceId!=form.Id&&l.Invoice.State!=InvoiceState.Voided))ModelState.AddModelError("","Some charges are unavailable or belong to another patient.");
        var total=charges.Sum(c=>c.Total);if(!BillingRules.Money(total))ModelState.AddModelError("","Invoice total exceeds the supported range.");if(!ModelState.IsValid)return await DraftView(form);
        if(invoice==null){invoice=new BillingInvoice{Token=form.Token,PatientId=form.PatientId,CreatedAtUtc=Now,ActorUserId=Actor};Db.Add(invoice);}
        var ids=form.ChargeIds.ToHashSet();Db.BillingInvoiceLines.RemoveRange(invoice.Lines.Where(l=>!ids.Contains(l.ChargeId)).ToList());
        foreach(var charge in charges.Where(c=>!invoice.Lines.Any(l=>l.ChargeId==c.Id)))Db.Add(new BillingInvoiceLine{Invoice=invoice,ChargeId=charge.Id});
        // New invoices begin at zero; all lines and their total become visible atomically in the same save.
        invoice.Total=total;invoice.ActorUserId=Actor;invoice.Version=Guid.NewGuid();Audit(invoice.Id,"BillingDraftSaved");
        return await SaveAsync()?RedirectToAction(nameof(Details),new{id=invoice.Id}):await DraftView(form);
    }
    private async Task<InvoiceDetails?> Detail(Guid id)
    {
        var row=await Rows().SingleOrDefaultAsync(r=>r.Invoice.Id==id);if(row==null)return null;
        row.Invoice.Lines=await Db.BillingInvoiceLines.AsNoTracking().Include(l=>l.Charge).Where(l=>l.InvoiceId==id).OrderBy(l=>l.Charge.CreatedAtUtc).ThenBy(l=>l.Id).ToListAsync();return new(){Invoice=row.Invoice,PatientName=row.PatientName,PatientNumber=row.PatientNumber,Paid=row.Paid,Payments=await Db.BillingPayments.AsNoTracking().Where(p=>p.InvoiceId==id).OrderBy(p=>p.RecordedAtUtc).ToListAsync()};
    }
    public async Task<IActionResult> Details(Guid id){var detail=await Detail(id);return detail==null?NotFound():View(detail);}
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(Guid id,Guid version)
    {
        var invoice=await Db.BillingInvoices.Include(i=>i.Lines).SingleOrDefaultAsync(i=>i.Id==id);if(invoice==null)return NotFound();if(invoice.State!=InvoiceState.Draft||!CheckVersion(invoice.Version,version)||invoice.Lines.Count==0)return Conflict("Reload a draft with at least one charge before issuing.");
        invoice.State=InvoiceState.Issued;invoice.IssuedAtUtc=Now;invoice.ActorUserId=Actor;invoice.Version=Guid.NewGuid();Audit(id,"BillingInvoiceIssued");return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Invoice changed. Reload before retrying.");
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Void(Guid id,Guid version,string? reason)
    {
        var invoice=await Db.BillingInvoices.SingleOrDefaultAsync(i=>i.Id==id);if(invoice==null)return NotFound();reason=Optional(reason);
        if(invoice.State!=InvoiceState.Issued||!CheckVersion(invoice.Version,version)||reason==null||reason.Length>1000||await Db.BillingPayments.AnyAsync(p=>p.InvoiceId==id&&p.ReversesId==null&&!Db.BillingPayments.Any(r=>r.ReversesId==p.Id)))return Conflict("Void requires an issued invoice, a reason and no unreversed payments.");
        invoice.State=InvoiceState.Voided;invoice.VoidedAtUtc=Now;invoice.VoidReason=reason;invoice.ActorUserId=Actor;invoice.Version=Guid.NewGuid();Audit(id,"BillingInvoiceVoided");return await SaveAsync()?RedirectToAction(nameof(Details),new{id}):Conflict("Invoice changed. Reload before retrying.");
    }
    public async Task<IActionResult> Pay(Guid id)
    {
        var invoice=await Db.BillingInvoices.AsNoTracking().SingleOrDefaultAsync(i=>i.Id==id);if(invoice==null)return NotFound();if(invoice.State!=InvoiceState.Issued)return Conflict("Only issued invoices accept payments.");ViewBag.Balance=invoice.Total-await Paid(id);return View(new PaymentForm{InvoiceId=id,Version=invoice.Version,Token=Guid.NewGuid()});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(PaymentForm form)
    {
        form.BankReference=Optional(form.BankReference)?.ToUpperInvariant();var old=await Db.BillingPayments.SingleOrDefaultAsync(p=>p.Token==form.Token);
        if(old!=null)return old.InvoiceId==form.InvoiceId&&old.Amount==form.Amount&&old.Method==form.Method&&old.BankReference==form.BankReference&&old.ReversesId==null?RedirectToAction(nameof(Receipt),new{id=old.Id}):Conflict("Submission token was already used for different payment information.");
        var invoice=await Db.BillingInvoices.SingleOrDefaultAsync(i=>i.Id==form.InvoiceId);if(invoice==null)return NotFound();var balance=invoice.Total-await Paid(invoice.Id);
        if(invoice.State!=InvoiceState.Issued||!CheckVersion(invoice.Version,form.Version))ModelState.AddModelError("","Reload an issued invoice before payment.");
        if(form.Token==Guid.Empty||!BillingRules.Money(form.Amount,true)||form.Amount>balance)ModelState.AddModelError("","Enter a positive amount of up to two decimal places within the outstanding balance.");
        if(form.Method is not ("Cash" or "BankTransfer")||(form.Method=="Cash"&&form.BankReference!=null)||(form.Method=="BankTransfer"&&form.BankReference==null))ModelState.AddModelError("","Choose cash without a bank reference, or bank transfer with its reference.");
        if(form.BankReference!=null&&await Db.BillingPayments.AnyAsync(p=>p.BankReference==form.BankReference))ModelState.AddModelError("","Bank reference already recorded.");
        if(!ModelState.IsValid){ViewBag.Balance=balance;return View(form);}
        var payment=new BillingPayment{Token=form.Token,InvoiceId=invoice.Id,Amount=form.Amount,Method=form.Method,BankReference=form.BankReference,RecordedAtUtc=Now,ActorUserId=Actor};Db.Add(payment);invoice.Version=Guid.NewGuid();invoice.ActorUserId=Actor;Audit(payment.Id,"BillingPaymentRecorded");
        if(await SaveAsync())return RedirectToAction(nameof(Receipt),new{id=payment.Id});ViewBag.Balance=balance;return View(form);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Reverse(Guid id,Guid token,Guid version,string? reason)
    {
        reason=Optional(reason);var payment=await Db.BillingPayments.SingleOrDefaultAsync(p=>p.Id==id);if(payment==null)return NotFound();
        var old=await Db.BillingPayments.SingleOrDefaultAsync(p=>p.Token==token);if(old!=null)return old.ReversesId==id&&old.Reason==reason?RedirectToAction(nameof(Receipt),new{id=old.Id}):Conflict("Submission token was already used.");
        var invoice=await Db.BillingInvoices.SingleAsync(i=>i.Id==payment.InvoiceId);
        if(token==Guid.Empty||reason==null||reason.Length>1000||payment.ReversesId!=null||invoice.State!=InvoiceState.Issued||!CheckVersion(invoice.Version,version)||await Db.BillingPayments.AnyAsync(p=>p.ReversesId==id))return Conflict("A reason, current invoice and unreversed original payment are required.");
        var reversal=new BillingPayment{Token=token,InvoiceId=invoice.Id,Amount=payment.Amount,Method="Reversal",ReversesId=id,Reason=reason,RecordedAtUtc=Now,ActorUserId=Actor};Db.Add(reversal);invoice.Version=Guid.NewGuid();invoice.ActorUserId=Actor;Audit(reversal.Id,"BillingPaymentReversed");return await SaveAsync()?RedirectToAction(nameof(Receipt),new{id=reversal.Id}):Conflict("Payment changed. Reload before retrying.");
    }
    public async Task<IActionResult> Receipt(Guid id,bool pdf=false)
    {
        var payment=await Db.BillingPayments.AsNoTracking().SingleOrDefaultAsync(p=>p.Id==id);if(payment==null)return NotFound();var detail=await Detail(payment.InvoiceId);if(detail==null)return NotFound();ViewBag.Payment=payment;
        if(!pdf)return View(detail);return await PdfResult(detail,payment);
    }
    public async Task<IActionResult> Pdf(Guid id){var detail=await Detail(id);if(detail==null)return NotFound();if(detail.Invoice.State==InvoiceState.Draft)return Conflict("Issue the invoice before downloading its PDF.");return await PdfResult(detail,null);}
    private async Task<IActionResult> PdfResult(InvoiceDetails detail,BillingPayment? payment)
    {
        if(!BillingPdf.Supports(detail,payment))return UnprocessableEntity("Use browser printing for text outside the supported PDF font/script range.");
        var data=BillingPdf.Render(detail,payment);Audit(payment?.Id??detail.Invoice.Id,payment==null?"BillingInvoicePdf":"BillingReceiptPdf");if(!await SaveAsync())return Conflict("Unable to audit the financial export. Retry later.");return File(data,"application/pdf",payment==null?"invoice.pdf":"receipt.pdf");
    }
}
