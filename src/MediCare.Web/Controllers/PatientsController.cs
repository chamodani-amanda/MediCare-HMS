using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;
[Authorize(Roles = "Administrator,Receptionist")]

public class PatientsController : Controller
{

    private readonly MediCareDbContext _db;

public PatientsController(MediCareDbContext db)
{
    _db = db;
}

    [HttpGet]
    public IActionResult Create()
    {
        return View(new PatientCreateViewModel());
    }

    [HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Create(
    PatientCreateViewModel model)
{
    // Step 1: Validate submitted information.
    if (!ModelState.IsValid)
    {
        return View(model);
    }

    if (model.DateOfBirth is not DateOnly dateOfBirth)
{
    ModelState.AddModelError(
        nameof(model.DateOfBirth),
        "Date of birth is required.");

    return View(model);
}

    // Step 2: Create a new patient record.
    var patient = new Patient
    {
        Id = Guid.NewGuid(),

        PatientNumber = "PAT-" +
            Guid.NewGuid().ToString("N")[..16].ToUpperInvariant(),

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

    // Step 3: Tell Entity Framework to add this patient.
    _db.Patients.Add(patient);

    // Step 4: Save the patient to PostgreSQL.
    await _db.SaveChangesAsync();

    // Step 5: Display the successful registration message.
    TempData["SuccessMessage"] =
        $"Patient registered successfully. " +
        $"Patient Number: {patient.PatientNumber}";

    // Step 6: Redirect to avoid duplicate submissions.
    return RedirectToAction(nameof(Create));
}
}