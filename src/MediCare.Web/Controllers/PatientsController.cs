using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;
[Authorize(Roles = "Administrator,Receptionist")]

public class PatientsController : Controller
{
    [HttpGet]
    public IActionResult Create()
    {
        return View(new PatientCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Create(PatientCreateViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Saving patients will be implemented later.

        ViewBag.ValidationPassed = true;

        return View(model);
    }
}