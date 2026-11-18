using MediCare.Web.Data;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class SecurityAuditLogsController : Controller
{
    private readonly MediCareDbContext _db;

    public SecurityAuditLogsController(MediCareDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int page = 1)
    {
        const int pageSize = 20;

        var totalRecords = await _db.SecurityAuditLogs
            .AsNoTracking()
            .CountAsync();

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalRecords / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var records = await (
            from log in _db.SecurityAuditLogs.AsNoTracking()

            join actor in _db.Users.AsNoTracking()
                on log.ActorUserId equals actor.Id
                into actorMatches

            from actor in actorMatches.DefaultIfEmpty()

            join target in _db.Users.AsNoTracking()
                on log.TargetUserId equals target.Id
                into targetMatches

            from target in targetMatches.DefaultIfEmpty()

            orderby log.OccurredAtUtc descending, log.Id descending

            select new SecurityAuditLogRowViewModel
            {
                Action = log.Action,

                ActorEmail = actor != null
                    ? (actor.Email ?? log.ActorUserId)
                    : log.ActorUserId,

                TargetEmail = target != null
                    ? (target.Email ?? log.TargetUserId)
                    : log.TargetUserId,

                Succeeded = log.Succeeded,

                OccurredAtUtc = log.OccurredAtUtc
            }
        )
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync();

        var model = new SecurityAuditLogListViewModel
        {
            Records = records,
            Page = page,
            TotalPages = totalPages,
            TotalRecords = totalRecords
        };

        return View(model);
    }
}