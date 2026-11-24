
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediCare.Web.Tests;

public class ReportsSecurityAndValidationTests
{
    // The entire ReportsController must be Admin-only.
    [Fact]
    public void ReportsController_RequiresAdministrator()
    {
        var authorization =
            typeof(ReportsController)
                .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Administrator", authorization.Roles);
    }

    // Every report and CSV endpoint must use HTTP GET,
    // without overriding authorization anonymously.
    [Theory]
    [InlineData("Index")]
    [InlineData("PatientReport")]
    [InlineData("ExportPatientsCsv")]
    [InlineData("AppointmentReport")]
    [InlineData("ExportAppointmentsCsv")]
    [InlineData("LaboratoryReport")]
    [InlineData("ExportLaboratoryCsv")]
    [InlineData("PharmacyReport")]
    [InlineData("ExportPharmacyCsv")]
    [InlineData("BillingReport")]
    [InlineData("ExportBillingCsv")]
    [InlineData("StaffReport")]
    [InlineData("ExportStaffCsv")]
    public void ReportEndpoints_UseGet_AndAreNotAnonymous(
        string action)
    {
        var method = GetAction(action);

        Assert.NotNull(
            method.GetCustomAttribute<HttpGetAttribute>());

        Assert.Null(
            method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // Reject unsupported status/stock filters.
    [Theory]
    [InlineData("PatientReport")]
    [InlineData("ExportPatientsCsv")]
    [InlineData("AppointmentReport")]
    [InlineData("ExportAppointmentsCsv")]
    [InlineData("LaboratoryReport")]
    [InlineData("ExportLaboratoryCsv")]
    [InlineData("PharmacyReport")]
    [InlineData("ExportPharmacyCsv")]
    [InlineData("BillingReport")]
    [InlineData("ExportBillingCsv")]
    [InlineData("StaffReport")]
    [InlineData("ExportStaffCsv")]
    public async Task ReportEndpoints_RejectInvalidStatus(
        string action)
    {
        var result = await InvokeInvalidRequest(
            action,
            search: null,
            status: "INVALID_STATUS");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // Reject search strings longer than 100 characters.
    [Theory]
    [InlineData("PatientReport")]
    [InlineData("ExportPatientsCsv")]
    [InlineData("AppointmentReport")]
    [InlineData("ExportAppointmentsCsv")]
    [InlineData("LaboratoryReport")]
    [InlineData("ExportLaboratoryCsv")]
    [InlineData("PharmacyReport")]
    [InlineData("ExportPharmacyCsv")]
    [InlineData("BillingReport")]
    [InlineData("ExportBillingCsv")]
    [InlineData("StaffReport")]
    [InlineData("ExportStaffCsv")]
    public async Task ReportEndpoints_RejectLongSearch(
        string action)
    {
        var result = await InvokeInvalidRequest(
            action,
            search: new string('X', 101),
            status: "All");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // Date range must not end before it begins.
    [Theory]
    [InlineData("AppointmentReport")]
    [InlineData("ExportAppointmentsCsv")]
    [InlineData("LaboratoryReport")]
    [InlineData("ExportLaboratoryCsv")]
    [InlineData("BillingReport")]
    [InlineData("ExportBillingCsv")]
    [InlineData("StaffReport")]
    [InlineData("ExportStaffCsv")]
    public async Task DateReports_RejectReversedDateRange(
        string action)
    {
        var result = await InvokeInvalidRequest(
            action,
            search: null,
            status: "All",
            fromDate: new DateOnly(2026, 11, 22),
            toDate: new DateOnly(2026, 11, 21));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // The shared CSV helper must escape values safely.
    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    [InlineData("a\nb", "\"a b\"")]
    [InlineData("=2+2", "\"'=2+2\"")]
    [InlineData("+123", "\"'+123\"")]
    [InlineData("-123", "\"'-123\"")]
    [InlineData("@formula", "\"'@formula\"")]
    public void CsvCell_EscapesSpecialValues(
        string input,
        string expected)
    {
        var method = typeof(ReportsController)
            .GetMethod(
                "CsvCell",
                BindingFlags.NonPublic |
                BindingFlags.Static);

        Assert.NotNull(method);

        var actual = method.Invoke(
            null, new object?[] { input });

        Assert.Equal(expected, actual);
    }

    private static MethodInfo GetAction(string action)
    {
        var method = typeof(ReportsController)
            .GetMethod(
                action,
                BindingFlags.Public |
                BindingFlags.Instance);

        Assert.NotNull(method);

        return method;
    }

    private static async Task<IActionResult>
        InvokeInvalidRequest(
            string action,
            string? search,
            string status,
            DateOnly? fromDate = null,
            DateOnly? toDate = null)
    {
        // Invalid filters must be rejected before
        // the controller accesses PostgreSQL.
        var controller = new ReportsController(null!);

        var method = GetAction(action);

        object?[] arguments = method
            .GetParameters()
            .Select(parameter => parameter.Name switch
            {
                "search" => (object?)search,
                "status" => status,
                "stockFilter" => status,
                "fromDate" => fromDate,
                "toDate" => toDate,
                "page" => 1,
                _ => null
            })
            .ToArray();

        var task = Assert.IsAssignableFrom<
            Task<IActionResult>>(
                method.Invoke(controller, arguments));

        return await task;
    }
}
