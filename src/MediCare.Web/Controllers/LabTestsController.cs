using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Administrator")]
public class LabTestsController(MediCareDbContext db, TimeProvider clock) : ClinicalAdministrationControllerBase(db)
{
    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool includeInactive=false, int page=1)
    {
        search=(search??"").Trim(); if (!ModelState.IsValid || search.Length>100) return BadRequest("Invalid catalogue filter.");
        var query=Db.LabTests.AsNoTracking().Where(t=>includeInactive || t.IsActive);
        if(search.Length>0) {var term=search.ToUpperInvariant();query=query.Where(t=>t.Code.Contains(term)||t.Name.ToUpper().Contains(term)||t.Category.ToUpper().Contains(term));}
        var count=await query.CountAsync();var pages=Math.Max(1,(int)Math.Ceiling(count/20d));page=Math.Clamp(page,1,pages);
        return View(new LabCatalogueList {Search=search,IncludeInactive=includeInactive,Page=page,TotalPages=pages,Total=count,Tests=await query.OrderBy(t=>t.Category).ThenBy(t=>t.Name).ThenBy(t=>t.Id).Skip((page-1)*20).Take(20).ToListAsync()});
    }
    [HttpGet] public IActionResult Create()=>View("Form",new LabTestForm());
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(LabTestForm form)
    {
        await Validate(form,Guid.Empty);if(!ModelState.IsValid)return View("Form",form);
        var test=new LabTest {CreatedAtUtc=clock.GetUtcNow().UtcDateTime};Copy(form,test);Db.LabTests.Add(test);Audit(test.Id,"LabTestCreated");
        return await SaveAsync()?RedirectToAction(nameof(Index)):View("Form",form);
    }
    [HttpGet]
    public async Task<IActionResult> Edit(Guid id)
    {
        var t=await Db.LabTests.AsNoTracking().SingleOrDefaultAsync(t=>t.Id==id);if(t==null)return NotFound();
        return View("Form",new LabTestForm {Id=t.Id,Version=t.Version,Code=t.Code,Name=t.Name,Category=t.Category,Description=t.Description,SampleType=t.SampleType,ResultKind=t.ResultKind,Unit=t.Unit,ReferenceRange=t.ReferenceRange,IsActive=t.IsActive});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id,LabTestForm form)
    {
        var test=await Db.LabTests.SingleOrDefaultAsync(t=>t.Id==id);if(test==null)return NotFound();form.Id=id;
        CheckVersion(test.Version,form.Version);await Validate(form,id);if(!ModelState.IsValid)return View("Form",form);
        Copy(form,test);test.Version=Guid.NewGuid();Audit(id,"LabTestUpdated");
        return await SaveAsync()?RedirectToAction(nameof(Index)):View("Form",form);
    }
    private async Task Validate(LabTestForm form,Guid id)
    {
        form.Code=(form.Code??"").Trim().ToUpperInvariant();
        if(await Db.LabTests.AnyAsync(t=>t.Id!=id && t.Code==form.Code))ModelState.AddModelError(nameof(form.Code),"This test code already exists.");
        if(!Enum.IsDefined(form.ResultKind))ModelState.AddModelError(nameof(form.ResultKind),"Choose Numeric or Text.");
    }
    private void Copy(LabTestForm form,LabTest test)
    {
        test.Code=form.Code;test.Name=form.Name.Trim();test.Category=form.Category.Trim();test.Description=Optional(form.Description);
        test.SampleType=form.SampleType.Trim();test.ResultKind=form.ResultKind;test.Unit=Optional(form.Unit);test.ReferenceRange=Optional(form.ReferenceRange);
        test.IsActive=form.IsActive;test.UpdatedAtUtc=clock.GetUtcNow().UtcDateTime;
    }
}
