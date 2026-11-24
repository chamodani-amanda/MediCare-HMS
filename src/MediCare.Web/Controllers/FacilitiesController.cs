using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Administrator")]
public class FacilitiesController(MediCareDbContext db,TimeProvider clock):HospitalCareControllerBase(db,clock)
{
    public async Task<IActionResult> Index(int page=1)
    {
        var q=Db.Wards.AsNoTracking().Include(w=>w.Department).OrderBy(w=>w.Code).Select(w=>new WardSummary{Ward=w,
            OperationalBeds=Db.Beds.Count(b=>b.WardId==w.Id&&b.IsOperational),OccupiedBeds=Db.BedOccupancies.Count(o=>o.Bed.WardId==w.Id&&o.EndedAtUtc==null),
            AvailableBeds=Db.Beds.Count(b=>b.WardId==w.Id&&b.IsOperational&&w.IsActive&&w.Department.IsActive&&!Db.BedOccupancies.Any(o=>o.BedId==b.Id&&o.EndedAtUtc==null))});
        return View(await Paginate(q,page));
    }
    public async Task<IActionResult> Beds(Guid wardId,int page=1)
    {
        if(!await Db.Wards.AnyAsync(w=>w.Id==wardId))return NotFound();ViewBag.WardId=wardId;
        return View(await Paginate(Db.Beds.AsNoTracking().Include(b=>b.Ward).Where(b=>b.WardId==wardId).OrderBy(b=>b.Number).Select(b=>new FacilityForm{Id=b.Id,WardId=b.WardId,Code=b.Number,Active=b.IsOperational,Version=b.Version}),page));
    }
    public async Task<IActionResult> Ward(Guid? id)
    {
        var form=new FacilityForm();if(id.HasValue){var w=await Db.Wards.AsNoTracking().SingleOrDefaultAsync(w=>w.Id==id);if(w==null)return NotFound();form=new(){Id=w.Id,Code=w.Code,Name=w.Name,DepartmentId=w.DepartmentId,Active=w.IsActive,Version=w.Version};}return await Form(form,false);
    }
    public async Task<IActionResult> Bed(Guid? id,Guid wardId)
    {
        var form=new FacilityForm{WardId=wardId};if(id.HasValue){var b=await Db.Beds.AsNoTracking().SingleOrDefaultAsync(b=>b.Id==id);if(b==null)return NotFound();form=new(){Id=b.Id,WardId=b.WardId,Code=b.Number,Active=b.IsOperational,Version=b.Version};}return await Form(form,true);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Ward(FacilityForm model)
    {
        Normalize(model);if(string.IsNullOrWhiteSpace(model.Name))ModelState.AddModelError(nameof(model.Name),"Ward name is required.");
        if(!await Db.Departments.AnyAsync(d=>d.Id==model.DepartmentId&&d.IsActive))ModelState.AddModelError("","Choose an active department.");
        if(await Db.Wards.AnyAsync(w=>w.Id!=model.Id&&w.Code==model.Code))ModelState.AddModelError("","Ward code already exists.");
        Ward? ward=null;if(model.Id!=Guid.Empty){ward=await Db.Wards.SingleOrDefaultAsync(w=>w.Id==model.Id);if(ward==null)return NotFound();CheckVersion(ward.Version,model.Version);
            if(ward.Code!=model.Code||ward.DepartmentId!=model.DepartmentId)ModelState.AddModelError("","Ward code and department are permanent; register a new ward for a different identity.");
            if(!model.Active&&await Db.BedOccupancies.AnyAsync(o=>o.Bed.WardId==ward.Id&&o.EndedAtUtc==null))ModelState.AddModelError("","Transfer or discharge occupants before deactivating a ward.");}
        if(!ModelState.IsValid)return await Form(model,false);
        if(ward==null){ward=new Ward{Code=model.Code,DepartmentId=model.DepartmentId};Db.Wards.Add(ward);}ward.Name=model.Name!.Trim();ward.IsActive=model.Active;ward.Version=Guid.NewGuid();Audit(ward.Id,"WardSaved");
        return await SaveAsync()?RedirectToAction(nameof(Index)):await Form(model,false);
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Bed(FacilityForm model)
    {
        Normalize(model);if(!await Db.Wards.AnyAsync(w=>w.Id==model.WardId&&w.IsActive&&w.Department.IsActive))ModelState.AddModelError("","Choose an active ward in an active department.");
        if(await Db.Beds.AnyAsync(b=>b.Id!=model.Id&&b.WardId==model.WardId&&b.Number==model.Code))ModelState.AddModelError("","This bed number already exists in the ward.");
        Bed? bed=null;if(model.Id!=Guid.Empty){bed=await Db.Beds.SingleOrDefaultAsync(b=>b.Id==model.Id);if(bed==null)return NotFound();CheckVersion(bed.Version,model.Version);
            if(bed.WardId!=model.WardId||bed.Number!=model.Code)ModelState.AddModelError("","Bed number and ward are permanent; register a different bed rather than changing history.");
            if(!model.Active&&await Db.BedOccupancies.AnyAsync(o=>o.BedId==bed.Id&&o.EndedAtUtc==null))ModelState.AddModelError("","An occupied bed cannot be taken out of service.");}
        if(!ModelState.IsValid)return await Form(model,true);
        if(bed==null){bed=new Bed{WardId=model.WardId,Number=model.Code};Db.Beds.Add(bed);}bed.IsOperational=model.Active;bed.Version=Guid.NewGuid();Audit(bed.Id,"BedSaved");
        return await SaveAsync()?RedirectToAction(nameof(Beds),new{wardId=bed.WardId}):await Form(model,true);
    }
    private void Normalize(FacilityForm model){model.Code=(model.Code??"").Trim().ToUpperInvariant();if(model.Code.Length==0)ModelState.AddModelError("","A code or bed number is required.");}
    private async Task<IActionResult> Form(FacilityForm model,bool bed)
    {
        model.IsBed=bed;model.Departments=await Db.Departments.AsNoTracking().Where(d=>d.IsActive).OrderBy(d=>d.Name).ToListAsync();model.Wards=await Db.Wards.AsNoTracking().OrderBy(w=>w.Code).ToListAsync();return View("Form",model);
    }
}
