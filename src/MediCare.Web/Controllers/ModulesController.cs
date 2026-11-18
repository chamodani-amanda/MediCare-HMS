using Microsoft.AspNetCore.Mvc;

namespace MediCare.Web.Controllers;

public class ModulesController : Controller
{
    public IActionResult Index()
    {
        var modules = new List<string>
        {
            "Patient Management",
            "Doctor Management",
            "Appointment Management",
            "Electronic Medical Records",
            "Laboratory Management",
            "Pharmacy Management",
            "Billing and Payments",
            "Inpatient and Outpatient",
            "Staff Management",
            "Reports and Analytics"
        };

        return View(modules);
    }
}