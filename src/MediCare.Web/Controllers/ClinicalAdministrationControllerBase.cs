using System.Security.Claims;
using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCare.Web.Controllers;

public abstract class ClinicalAdministrationControllerBase(MediCareDbContext db) : Controller
{
    protected readonly MediCareDbContext Db = db;
    protected void Audit(Guid id, string action) => Db.SecurityAuditLogs.Add(new SecurityAuditLog
    {
        ActorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Authenticated user ID required."),
        TargetUserId = id.ToString(), Action = action, Succeeded = true
    });

    protected bool CheckVersion(Guid current, Guid submitted)
    {
        if (submitted != Guid.Empty && current == submitted) return true;
        ModelState.AddModelError("", "This record changed. Reload the form before saving again.");
        return false;
    }

    protected async Task<bool> SaveAsync()
    {
        try { await Db.SaveChangesAsync(); return true; }
        catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "This record changed while saving. Reload the form."); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" or "23503" or "23514" or "23P01" or "40P01" or "40001" })
        {
            ModelState.AddModelError("", "The record conflicts with existing information or a related record changed. Reload and review your entries.");
        }
        catch (PostgresException ex) when (ex.SqlState is "23505" or "23503" or "23514" or "23P01" or "40P01" or "40001")
        {
            ModelState.AddModelError("", "The operation conflicts with current information. Reload and review your entries.");
        }
        return false;
    }
    protected static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
