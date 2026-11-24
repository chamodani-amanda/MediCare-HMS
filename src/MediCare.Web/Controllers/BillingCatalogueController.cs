using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles="Administrator")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public class BillingCatalogueController(MediCareDbContext db,TimeProvider clock):ClinicalAdministrationControllerBase(db)
{
    public async Task<IActionResult> Index(string? search,int page=1)
    {
        var q=Db.BillingServices.AsNoTracking();if(!string.IsNullOrWhiteSpace(search)){search=search.Trim();q=q.Where(x=>x.Code.Contains(search)||x.Name.Contains(search)||x.Category.Contains(search));}
        var count=await q.CountAsync();page=Math.Clamp(page,1,Math.Max(1,(count+19)/20));return View(new BillingPage<BillingService>{Items=await q.OrderBy(x=>x.Code).Skip((page-1)*20).Take(20).ToListAsync(),Page=page,TotalPages=(count+19)/20,Search=search});
    }
    public async Task<IActionResult> Edit(Guid? id)
    {
        if(id==null)return View(new BillingServiceForm());var s=await Db.BillingServices.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==id);return s==null?NotFound():View(new BillingServiceForm{Id=s.Id,Version=s.Version,Code=s.Code,Name=s.Name,Category=s.Category,Description=s.Description,IsActive=s.IsActive});
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(BillingServiceForm form)
    {
        form.Code=(form.Code??"").Trim().ToUpperInvariant();form.Name=(form.Name??"").Trim();form.Category=(form.Category??"").Trim();
        if(form.Code.Length==0||form.Name.Length==0||form.Category.Length==0)ModelState.AddModelError("","Code, name and category are required.");
        if(await Db.BillingServices.AnyAsync(s=>s.Code==form.Code&&s.Id!=form.Id))ModelState.AddModelError("","Service code already exists.");
        BillingService? s=null;if(form.Id!=Guid.Empty){s=await Db.BillingServices.SingleOrDefaultAsync(s=>s.Id==form.Id);if(s==null)return NotFound();CheckVersion(s.Version,form.Version);if(s.Code!=form.Code)ModelState.AddModelError("","Service codes are permanent.");}
        if(!ModelState.IsValid)return View(form);if(s==null){s=new BillingService{Code=form.Code};Db.Add(s);}s.Name=form.Name;s.Category=form.Category;s.Description=Optional(form.Description);s.IsActive=form.IsActive;s.Version=Guid.NewGuid();Audit(s.Id,"BillingServiceSaved");return await SaveAsync()?RedirectToAction(nameof(Index)):View(form);
    }
    public async Task<IActionResult> Prices(Guid id)
    {
        var service=await Db.BillingServices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id);if(service==null)return NotFound();ViewBag.Service=service;return View(await Db.BillingPrices.AsNoTracking().Where(x=>x.ServiceId==id).OrderByDescending(x=>x.EffectiveAtUtc).Take(200).ToListAsync());
    }
    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> Price(BillingPriceForm form)
    {
        if(!BillingRules.Money(form.UnitPrice))ModelState.AddModelError("","Enter a valid nonnegative LKR amount with at most two decimal places.");
        if(!await Db.BillingServices.AnyAsync(s=>s.Id==form.ServiceId&&s.IsActive))return NotFound();
        var now=clock.GetUtcNow().UtcDateTime;var effective=now;
        if(form.EffectiveDate.HasValue!=form.EffectiveTime.HasValue)ModelState.AddModelError("","Specify both effective date and time, or leave both blank for now.");
        if(form.EffectiveDate.HasValue&&form.EffectiveTime.HasValue){effective=HospitalTime.ToUtc(form.EffectiveDate.Value,form.EffectiveTime.Value);if(effective<now)ModelState.AddModelError("","Prices cannot be backdated.");}
        if(!ModelState.IsValid)return BadRequest("Invalid price or effective time. Return to the price form and review the values.");
        var price=new BillingPrice{ServiceId=form.ServiceId,UnitPrice=form.UnitPrice,EffectiveAtUtc=effective,CreatedAtUtc=now,ActorUserId=User.FindFirstValue(ClaimTypes.NameIdentifier)!};Db.Add(price);Audit(price.Id,"BillingPriceAdded");return await SaveAsync()?RedirectToAction(nameof(Prices),new{id=form.ServiceId}):Conflict("Price conflicts with existing information. Reload before retrying.");
    }
}
