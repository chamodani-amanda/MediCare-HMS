using MediCare.Web.Data;
using System.Security.Claims;
using MediCare.Web.Services;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

// Only Administrator and Receptionist can access this controller.
[Authorize(Roles = "Administrator,Receptionist")]
public class PatientsController : Controller
{
    private readonly MediCareDbContext _db;

    // Database dependency injection
    public PatientsController(MediCareDbContext db)
    {
        _db = db;
    }

    // ==========================================
    // GET: Display Patient Registration Form
    // ==========================================
    [HttpGet]
    public IActionResult Create()
    {
        return View(new PatientCreateViewModel());
    }

    // ==========================================
    // POST: Save New Patient to PostgreSQL
    // ==========================================
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        PatientCreateViewModel model)
    {
        // Step 1: Validate the submitted form.
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Step 2: Ensure date of birth is provided.
        if (model.DateOfBirth is not DateOnly dateOfBirth)
        {
            ModelState.AddModelError(
                nameof(model.DateOfBirth),
                "Date of birth is required.");

            return View(model);
        }

        // Step 3: Create a new Patient object.
        var patient = new Patient
        {
            Id = Guid.NewGuid(),

            // Generate a unique patient number.
            PatientNumber = "PAT-" +
                Guid.NewGuid()
                    .ToString("N")[..16]
                    .ToUpperInvariant(),

            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),

            DateOfBirth = dateOfBirth,

            Gender = model.Gender,
            PhoneNumber = model.PhoneNumber,
            Email = model.Email,
            Address = model.Address,

            EmergencyContactName = model.EmergencyContactName,
            EmergencyContactPhone = model.EmergencyContactPhone,

            IsArchived = false,

            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        // Step 4: Add the new patient to Entity Framework.
        _db.Patients.Add(patient);

        // Step 5: Save patient information in PostgreSQL.
        Audit(patient.Id, "PatientRegistered");
        await _db.SaveChangesAsync();

        // Step 6: Show a successful registration message.
        TempData["SuccessMessage"] =
            $"Patient registered successfully. " +
            $"Patient Number: {patient.PatientNumber}";

        // Step 7: Redirect to prevent duplicate form submissions.
        return RedirectToAction(nameof(Create));
    }

    // ==========================================
// GET: View and Search Registered Patients
// ==========================================
[HttpGet]
public async Task<IActionResult> Index(
    string? search,
    int page = 1)
{
    const int pageSize = 20;

    // Step 1: Clean the submitted search text.
    string searchTerm = (search ?? string.Empty).Trim();

    if (searchTerm.Length > 100)
    {
        return BadRequest(
            "Search text must be 100 characters or fewer.");
    }

    // Step 2: Start with active patient records.
    var query = _db.Patients
        .AsNoTracking()
        .Where(patient => !patient.IsArchived);

    // Step 3: Apply search when text is provided.
    if (!string.IsNullOrWhiteSpace(searchTerm))
    {
        string pattern = $"%{searchTerm}%";

        query = query.Where(patient =>
            EF.Functions.ILike(patient.PatientNumber, pattern) ||
            EF.Functions.ILike(patient.FirstName, pattern) ||
            EF.Functions.ILike(patient.LastName, pattern) ||
            EF.Functions.ILike(
                patient.FirstName + " " + patient.LastName,
                pattern));
    }

    // Step 4: Count matching patients.
    int totalPatients = await query.CountAsync();

    // Step 5: Calculate pagination.
    int totalPages = Math.Max(
        1,
        (int)Math.Ceiling(
            totalPatients / (double)pageSize));

    page = Math.Clamp(page, 1, totalPages);

    // Step 6: Retrieve matching patients.
    var patients = await query
        .OrderByDescending(patient => patient.CreatedAtUtc)
        .ThenByDescending(patient => patient.Id)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(patient => new PatientListItemViewModel
        {
            Id = patient.Id,

            PatientNumber = patient.PatientNumber,

            FullName = patient.FirstName + " " +
                       patient.LastName,

            DateOfBirth = patient.DateOfBirth,

            CreatedAtUtc = patient.CreatedAtUtc
        })
        .ToListAsync();

    // Step 7: Prepare the ViewModel.
    var model = new PatientListViewModel
    {
        Patients = patients,

        SearchTerm = searchTerm,

        Page = page,

        TotalPages = totalPages,

        TotalPatients = totalPatients
    };

    return View(model);
}

// ==========================================
// GET: Display Edit Patient Form
// ==========================================
[HttpGet]
public async Task<IActionResult> Edit(Guid id)
{
    if (id == Guid.Empty)
    {
        return BadRequest();
    }

    // Find the patient in PostgreSQL.
    var patient = await _db.Patients
        .AsNoTracking()
        .FirstOrDefaultAsync(p =>
            p.Id == id && !p.IsArchived);

    if (patient == null)
    {
        return NotFound();
    }

    // Fill the Edit form with existing information.
    var model = new PatientEditViewModel
    {
        Id = patient.Id,
        Version = patient.Version,
        PatientNumber = patient.PatientNumber,

        FirstName = patient.FirstName,
        LastName = patient.LastName,
        DateOfBirth = patient.DateOfBirth,
        Gender = patient.Gender,
        PhoneNumber = patient.PhoneNumber,
        Email = patient.Email,
        Address = patient.Address,

        EmergencyContactName =
            patient.EmergencyContactName,

        EmergencyContactPhone =
            patient.EmergencyContactPhone
    };

    return View(model);
}

// ==========================================
// POST: Update Patient in PostgreSQL
// ==========================================
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Edit(
    PatientEditViewModel model)
{
    if (model.Id == Guid.Empty)
    {
        return BadRequest();
    }

    // Retrieve the existing patient.
    var patient = await _db.Patients
        .FirstOrDefaultAsync(p =>
            p.Id == model.Id && !p.IsArchived);

    if (patient == null)
    {
        return NotFound();
    }

    // Restore the original patient number.
    model.PatientNumber = patient.PatientNumber;

    // Validate all submitted fields.
    if (!ModelState.IsValid)
    {
        return View(model);
    }

    // Make sure date of birth is present.
    if (model.DateOfBirth is not DateOnly dateOfBirth)
    {
        ModelState.AddModelError(
            nameof(model.DateOfBirth),
            "Date of birth is required.");

        return View(model);
    }

    // Update only the permitted patient fields.
    if (model.Version == Guid.Empty || model.Version != patient.Version)
    {
        ModelState.AddModelError("", "Patient changed. Reload the form before editing again.");
        return View(model);
    }
    _db.Entry(patient).Property(p => p.Version).OriginalValue = model.Version;
    patient.Version = Guid.NewGuid();
    patient.FirstName = model.FirstName.Trim();
    patient.LastName = model.LastName.Trim();
    patient.DateOfBirth = dateOfBirth;
    patient.Gender = model.Gender;
    patient.PhoneNumber = model.PhoneNumber;
    patient.Email = model.Email;
    patient.Address = model.Address;

    patient.EmergencyContactName =
        model.EmergencyContactName;

    patient.EmergencyContactPhone =
        model.EmergencyContactPhone;

    // Record the update time.
    patient.UpdatedAtUtc = DateTime.UtcNow;

    // Save changes in PostgreSQL.
    Audit(patient.Id, "PatientUpdated");
    try { await _db.SaveChangesAsync(); }
    catch (DbUpdateConcurrencyException)
    {
        ModelState.AddModelError("", "Patient changed while saving. Reload the form before editing again.");
        return View(model);
    }

    TempData["SuccessMessage"] =
        "Patient information updated successfully.";

    return RedirectToAction(nameof(Index));
}
// ==========================================
// GET: Display Patient Profile
// ==========================================
[HttpGet]
public async Task<IActionResult> Details(Guid id)
{
    // Reject an empty patient ID.
    if (id == Guid.Empty)
    {
        return BadRequest();
    }

    // Retrieve the active patient from PostgreSQL.
    var patient = await _db.Patients
        .AsNoTracking()
        .Where(p => p.Id == id && (!p.IsArchived || User.IsInRole("Administrator")))
        .Select(p => new PatientDetailsViewModel
        {
            Id = p.Id,
            Version = p.Version,
            IsArchived = p.IsArchived,

            PatientNumber = p.PatientNumber,

            FullName = p.FirstName + " " + p.LastName,

            DateOfBirth = p.DateOfBirth,

            Gender = p.Gender,

            PhoneNumber = p.PhoneNumber,

            Email = p.Email,

            Address = p.Address,

            EmergencyContactName =
                p.EmergencyContactName,

            EmergencyContactPhone =
                p.EmergencyContactPhone,

            CreatedAtUtc = p.CreatedAtUtc,

            UpdatedAtUtc = p.UpdatedAtUtc
        })
        .FirstOrDefaultAsync();

    // Return 404 if the patient does not exist
    // or the patient has been archived.
    if (patient == null)
    {
        return NotFound();
    }

    patient.Documents = await _db.PatientDocuments.AsNoTracking().Where(d => d.PatientId == id)
        .OrderByDescending(d => d.UploadedAtUtc).ToListAsync();
    return View(patient);
}

    private void Audit(Guid target, string action)
    {
        var actor = User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated user identifier required.");
        _db.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorUserId = actor, TargetUserId = target.ToString(), Action = action, Succeeded = true
        });
    }

    [HttpGet, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Archived(int page = 1)
    {
        page = Math.Clamp(page, 1, 1000000);
        ViewBag.Page = page;
        return View(await _db.Patients.AsNoTracking().Where(p => p.IsArchived)
            .OrderBy(p => p.PatientNumber).Skip((page - 1) * 20).Take(21).ToListAsync());
    }

    [HttpGet, Authorize(Roles = "Administrator")]
    public async Task<IActionResult> Archive(Guid id)
    {
        var patient = await _db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && !p.IsArchived);
        return patient == null ? NotFound() : View(patient);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator")]
    public Task<IActionResult> Archive(Guid id, Guid version) => SetArchived(id, version, true);

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Administrator")]
    public Task<IActionResult> Reactivate(Guid id, Guid version) => SetArchived(id, version, false);

    private async Task<IActionResult> SetArchived(Guid id, Guid version, bool archived)
    {
        var patient = await _db.Patients.SingleOrDefaultAsync(p => p.Id == id);
        if (patient == null) return NotFound();
        if (version == Guid.Empty || patient.Version != version || patient.IsArchived == archived)
            return Conflict("Patient changed. Reload and try again.");
        patient.IsArchived = archived;
        patient.Version = Guid.NewGuid();
        patient.UpdatedAtUtc = DateTime.UtcNow;
        Audit(id, archived ? "PatientArchived" : "PatientReactivated");
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Conflict("Patient changed. Reload and try again."); }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocument(Guid id, IFormFile? document,
        [FromServices] PatientDocumentStorage storage)
    {
        var patient = await _db.Patients.SingleOrDefaultAsync(p => p.Id == id && !p.IsArchived);
        if (patient == null) return NotFound();
        if (document == null) { TempData["DocumentError"] = "Select a document."; return RedirectToAction(nameof(Details), new { id }); }
        (string Name, string Type, long Size) saved;
        try { saved = await storage.SaveAsync(document); }
        catch (InvalidDataException ex)
        {
            TempData["DocumentError"] = ex.Message;
            return RedirectToAction(nameof(Details), new { id });
        }
        try
        {
            var name = Path.GetFileName(document.FileName.Replace('\\', '/'));
            name = new string(name.Where(c => !char.IsControl(c)).Take(255).ToArray());
            var metadata = new PatientDocument
            {
                PatientId = id, OriginalFileName = string.IsNullOrWhiteSpace(name) ? "document" : name,
                StoredFileName = saved.Name, ContentType = saved.Type, Size = saved.Size,
                UploadedByUserId = User.FindFirstValue(ClaimTypes.NameIdentifier)!
            };
            _db.PatientDocuments.Add(metadata);
            patient.Version = Guid.NewGuid();
            Audit(metadata.Id, "PatientDocumentUploaded");
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            storage.Delete(saved.Name);
            return Conflict("Patient changed during upload. Reload and try again.");
        }
        catch
        {
            storage.Delete(saved.Name);
            throw;
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> DownloadDocument(Guid id, Guid documentId,
        [FromServices] PatientDocumentStorage storage)
    {
        var document = await _db.PatientDocuments.AsNoTracking().SingleOrDefaultAsync(d =>
            d.Id == documentId && d.PatientId == id && (!d.Patient.IsArchived || User.IsInRole("Administrator")));
        if (document == null) return NotFound();
        Stream stream;
        try { stream = storage.Open(document.StoredFileName); }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }
        try
        {
            Audit(document.Id, "PatientDocumentDownloaded");
            await _db.SaveChangesAsync();
        }
        catch { await stream.DisposeAsync(); throw; }
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, document.ContentType, document.OriginalFileName);
    }
}
