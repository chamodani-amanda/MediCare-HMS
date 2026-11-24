using System.Reflection;
using System.Security.Claims;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediCare.Web.Tests;

public class StaffManagementSecurityTests
{
    [Theory]
    [InlineData(typeof(EmployeesController))]
    [InlineData(typeof(AttendancesController))]
    [InlineData(typeof(LeaveRequestsController))]
    public void StaffControllersRequireAdministrator(Type controllerType)
    {
        var authorization = controllerType
            .GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorization);
        Assert.Equal("Administrator", authorization.Roles);

        // Individual actions must not bypass authorization.
        var methods = controllerType.GetMethods(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            Assert.Null(
                method.GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Theory]
    [InlineData(typeof(EmployeesController))]
    [InlineData(typeof(AttendancesController))]
    [InlineData(typeof(LeaveRequestsController))]
    public void StaffPostActionsRequireAntiforgery(Type controllerType)
    {
        var methods = controllerType.GetMethods(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly);

        var postActions = methods
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() != null)
            .ToList();

        Assert.NotEmpty(postActions);

        foreach (var method in postActions)
        {
            Assert.NotNull(
                method.GetCustomAttribute<
                    ValidateAntiForgeryTokenAttribute>());
        }
    }

    [Theory]
    [InlineData("Administrator", true)]
    [InlineData("Receptionist", false)]
    [InlineData("Doctor", false)]
    [InlineData("Nurse", false)]
    [InlineData("", false)]
    public async Task AuthorizationPolicyEnforcesAdministratorRole(
        string role,
        bool expectedAccess)
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddAuthorization()
            .BuildServiceProvider();

        var policyProvider = services
            .GetRequiredService<IAuthorizationPolicyProvider>();

        var authorizationService = services
            .GetRequiredService<IAuthorizationService>();

        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Role, role) },
                string.IsNullOrEmpty(role) ? null : "Test"));

        Type[] controllers =
        {
            typeof(EmployeesController),
            typeof(AttendancesController),
            typeof(LeaveRequestsController)
        };

        foreach (var controllerType in controllers)
        {
            var attributes = controllerType
                .GetCustomAttributes<AuthorizeAttribute>();

            var policy = await AuthorizationPolicy.CombineAsync(
                policyProvider, attributes);

            Assert.NotNull(policy);

            var result = await authorizationService
                .AuthorizeAsync(user, null, policy);

            Assert.Equal(expectedAccess, result.Succeeded);
        }
    }
}