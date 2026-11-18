using MediCare.Web.Services;
using MediCare.Web.Data;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class StaffAccountsController : Controller
{ 
    // Reserved long-duration lockout for administrator suspension.
private static readonly DateTimeOffset DeactivatedUntil =
    new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

private static readonly DateTimeOffset DeactivationThreshold =
    new(2090, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly MediCareDbContext _db;
    private readonly SecurityAuditService _audit;

   public StaffAccountsController(
    UserManager<IdentityUser> userManager,
    RoleManager<IdentityRole> roleManager,
    MediCareDbContext db,
    SecurityAuditService audit)
{
    _userManager = userManager;
    _roleManager = roleManager;
    _db = db;
    _audit = audit;
}
    [HttpGet]
public async Task<IActionResult> Index()
{
    var users = await _userManager.Users
        .AsNoTracking()
        .OrderBy(u => u.UserName)
        .ToListAsync();

    var accounts = new List<StaffAccountListItemViewModel>();

    var currentUserId = _userManager.GetUserId(User);

    foreach (var user in users)
    {
        var roles = await _userManager.GetRolesAsync(user);

        bool isDeactivated =
            user.LockoutEnd is { } end &&
            end >= DeactivationThreshold;

        bool isTemporarilyLocked =
            !isDeactivated &&
            user.LockoutEnd is { } lockoutEnd &&
            lockoutEnd > DateTimeOffset.UtcNow;

        bool canManage =
            user.Id != currentUserId &&
            !roles.Contains("Administrator");

        accounts.Add(new StaffAccountListItemViewModel
        {
            Id = user.Id,
            Email = user.Email ?? user.UserName ?? "",
            Roles = roles.Count > 0
                ? string.Join(", ", roles)
                : "No role assigned",
            IsDeactivated = isDeactivated,
            IsTemporarilyLocked = isTemporarilyLocked,
            CanManage = canManage
        });
    }

    return View(accounts);
}

    [HttpGet]
    public IActionResult Create()
    {
        return View(new StaffAccountCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        StaffAccountCreateViewModel model)
    {
        var email = model.Email?.Trim() ?? "";
        var role = model.Role?.Trim() ?? "";

        // Accept only approved non-admin roles.
        if (!StaffAccountCreateViewModel.AssignableRoles
                .Contains(role, StringComparer.Ordinal))
        {
            ModelState.AddModelError(
                nameof(model.Role),
                "Select a permitted hospital role.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            ModelState.AddModelError(
                nameof(model.Role),
                "The selected role does not exist.");

            return View(model);
        }

        // Account creation and role assignment
        // must succeed together.
        await using var transaction =
            await _db.Database.BeginTransactionAsync();

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = false,
            LockoutEnabled = true
        };

        var createResult =
            await _userManager.CreateAsync(
                user, model.Password);

        if (!createResult.Succeeded)
        {
            await transaction.RollbackAsync();

            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(
                    string.Empty, error.Description);
            }

            return View(model);
        }

        var roleResult =
            await _userManager.AddToRoleAsync(user, role);

        if (!roleResult.Succeeded)
        {
            await transaction.RollbackAsync();

            foreach (var error in roleResult.Errors)
            {
                ModelState.AddModelError(
                    string.Empty, error.Description);
            }

            return View(model);
        }

        // Identify the Administrator performing this action.
var actorId = _userManager.GetUserId(User);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Challenge();
}

// Record successful staff account creation.
await _audit.RecordSuccessAsync(
    actorId,
    user.Id,
    "StaffAccountCreated");

        await transaction.CommitAsync();

        TempData["SuccessMessage"] =
            "Staff account created successfully.";

        return RedirectToAction(nameof(Index));
    }

    // POST: Deactivate a non-administrator staff account.
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Deactivate(string id)
{
    if (string.IsNullOrWhiteSpace(id))
        return BadRequest();

    var user = await _userManager.FindByIdAsync(id);

    if (user == null)
        return NotFound();

    var actorId = _userManager.GetUserId(User);

    if (string.IsNullOrWhiteSpace(actorId))
        return Challenge();

    // Never deactivate Administrator accounts.
    if (user.Id == actorId ||
        await _userManager.IsInRoleAsync(
            user, "Administrator"))
    {
        TempData["ErrorMessage"] =
            "Administrator accounts cannot be deactivated.";

        return RedirectToAction(nameof(Index));
    }

    // Prevent duplicate deactivation.
    if (user.LockoutEnd is { } end &&
        end >= DeactivationThreshold)
    {
        TempData["ErrorMessage"] =
            "This account is already deactivated.";

        return RedirectToAction(nameof(Index));
    }

    // Perform account update and audit write together.
    await using var transaction =
        await _db.Database.BeginTransactionAsync();

    var result = await _userManager
        .SetLockoutEndDateAsync(user, DeactivatedUntil);

    if (!result.Succeeded)
    {
        TempData["ErrorMessage"] =
            "Could not deactivate the account.";

        return RedirectToAction(nameof(Index));
    }

    var stampResult =
        await _userManager.UpdateSecurityStampAsync(user);

    if (!stampResult.Succeeded)
    {
        TempData["ErrorMessage"] =
            "Could not invalidate the account sessions.";

        return RedirectToAction(nameof(Index));
    }

    await _audit.RecordSuccessAsync(
        actorId,
        user.Id,
        "StaffAccountDeactivated");

    await transaction.CommitAsync();

    TempData["SuccessMessage"] =
        "Staff account deactivated successfully.";

    return RedirectToAction(nameof(Index));
}

// POST: Reactivate a deactivated staff account
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Reactivate(string id)
{
    if (string.IsNullOrWhiteSpace(id))
        return BadRequest();

    var user = await _userManager.FindByIdAsync(id);

    if (user == null)
        return NotFound();

    var actorId = _userManager.GetUserId(User);

    if (string.IsNullOrWhiteSpace(actorId))
        return Challenge();

    // Protect Administrator accounts.
    if (user.Id == actorId ||
        await _userManager.IsInRoleAsync(
            user, "Administrator"))
    {
        TempData["ErrorMessage"] =
            "Administrator accounts cannot be changed here.";

        return RedirectToAction(nameof(Index));
    }

    // Only reactivate administratively deactivated accounts.
    bool isDeactivated =
        user.LockoutEnd is { } end &&
        end >= DeactivationThreshold;

    if (!isDeactivated)
    {
        TempData["ErrorMessage"] =
            "This account is not deactivated.";

        return RedirectToAction(nameof(Index));
    }

    // Keep the account change and audit record together.
    await using var transaction =
        await _db.Database.BeginTransactionAsync();

    var result =
        await _userManager.SetLockoutEndDateAsync(user, null);

    if (!result.Succeeded)
    {
        TempData["ErrorMessage"] =
            "Could not reactivate the account.";

        return RedirectToAction(nameof(Index));
    }

    var resetResult =
        await _userManager.ResetAccessFailedCountAsync(user);

    if (!resetResult.Succeeded)
    {
        TempData["ErrorMessage"] =
            "Could not reset failed login attempts.";

        return RedirectToAction(nameof(Index));
    }

    var stampResult =
        await _userManager.UpdateSecurityStampAsync(user);

    if (!stampResult.Succeeded)
    {
        TempData["ErrorMessage"] =
            "Could not invalidate previous sessions.";

        return RedirectToAction(nameof(Index));
    }

    // Record successful reactivation.
    await _audit.RecordSuccessAsync(
        actorId,
        user.Id,
        "StaffAccountReactivated");

    await transaction.CommitAsync();

    TempData["SuccessMessage"] =
        "Staff account reactivated successfully.";

    return RedirectToAction(nameof(Index));
}
// GET: Display the Administrator password reset form
[HttpGet]
public async Task<IActionResult> ResetPassword(string id)
{
    if (string.IsNullOrWhiteSpace(id))
        return BadRequest();

    var user = await _userManager.FindByIdAsync(id);

    if (user == null)
        return NotFound();

    // Protect the current Administrator and all admin accounts.
    if (user.Id == _userManager.GetUserId(User) ||
        await _userManager.IsInRoleAsync(
            user, "Administrator"))
    {
        TempData["ErrorMessage"] =
            "Administrator passwords cannot be reset here.";

        return RedirectToAction(nameof(Index));
    }

    // Do not reset administratively deactivated accounts.
    if (user.LockoutEnd is { } end &&
        end >= DeactivationThreshold)
    {
        TempData["ErrorMessage"] =
            "Reactivate this account before resetting its password.";

        return RedirectToAction(nameof(Index));
    }

    ViewBag.StaffEmail = user.Email;

    return View(new StaffPasswordResetViewModel
    {
        UserId = user.Id
    });
}

// POST: Reset a staff account password
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ResetPassword(
    StaffPasswordResetViewModel model)
{
    if (string.IsNullOrWhiteSpace(model.UserId))
        return BadRequest();

    var user = await _userManager.FindByIdAsync(model.UserId);

    if (user == null)
        return NotFound();

    // Repeat all authorization checks on the server.
    if (user.Id == _userManager.GetUserId(User) ||
        await _userManager.IsInRoleAsync(
            user, "Administrator"))
    {
        TempData["ErrorMessage"] =
            "Administrator passwords cannot be reset here.";

        return RedirectToAction(nameof(Index));
    }

    if (user.LockoutEnd is { } end &&
        end >= DeactivationThreshold)
    {
        TempData["ErrorMessage"] =
            "Reactivate this account before resetting its password.";

        return RedirectToAction(nameof(Index));
    }

    if (!ModelState.IsValid)
    {
        ViewBag.StaffEmail = user.Email;
        return View(model);
    }
    
    // Identify the Administrator performing the reset.
var actorId = _userManager.GetUserId(User);

if (string.IsNullOrWhiteSpace(actorId))
{
    return Challenge();
}

// Keep the password reset and audit entry together.
await using var transaction =
    await _db.Database.BeginTransactionAsync();

    // Generate a reset token through ASP.NET Core Identity.
    var token =
        await _userManager.GeneratePasswordResetTokenAsync(user);

    // Validate the token and securely replace the password.
    var result = await _userManager.ResetPasswordAsync(
        user,
        token,
        model.TemporaryPassword);

    if (!result.Succeeded)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(
                string.Empty,
                error.Description);
        }

        ViewBag.StaffEmail = user.Email;
        return View(model);
    }

    // Rotate the security stamp to invalidate existing sessions.
    var stampResult =
        await _userManager.UpdateSecurityStampAsync(user);

    if (!stampResult.Succeeded)
    {
        TempData["ErrorMessage"] =
            "Password was reset, but session invalidation " +
            "could not be confirmed. Investigate before use.";

        return RedirectToAction(nameof(Index));
    }
    // Record the successful administrator-assisted reset.
// Never include the password or reset token in audit logs.
await _audit.RecordSuccessAsync(
    actorId,
    user.Id,
    "StaffPasswordReset");

await transaction.CommitAsync();

    TempData["SuccessMessage"] =
        "Password reset successfully. " +
        "Ask the staff member to change it after signing in.";

    return RedirectToAction(nameof(Index));
}
}