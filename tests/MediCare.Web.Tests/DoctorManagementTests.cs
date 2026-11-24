using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Web.Tests;

public class DoctorManagementTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Nurse", HttpStatusCode.Forbidden)]
    [InlineData("Accountant", HttpStatusCode.Forbidden)]
    [InlineData("Receptionist", HttpStatusCode.OK)]
    [InlineData("Doctor", HttpStatusCode.OK)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task ReadAndAdministrativeRoutesEnforceRoles(string? role, HttpStatusCode expected)
    {
        await using var site = await Site.Create(); site.Role(role);
        Assert.Equal(expected, (await site.Client.GetAsync("/Doctors")).StatusCode);
        Assert.Equal(expected, (await site.Client.GetAsync($"/Doctors/Details/{site.DoctorId}")).StatusCode);
        var admin = role == "Administrator" ? HttpStatusCode.OK : role == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        foreach (var path in new[] { "/Doctors/Create", $"/Doctors/Edit/{site.DoctorId}", "/Departments", "/Departments/Create",
            $"/Departments/Edit/{site.DepartmentId}", $"/DoctorSchedules?doctorId={site.DoctorId}", $"/DoctorSchedules/Create?doctorId={site.DoctorId}" })
            Assert.Equal(admin, (await site.Client.GetAsync(path)).StatusCode);
        if (role != "Administrator")
        {
            foreach (var path in new[] { "/Doctors/Create", "/Doctors/Edit", "/Departments/Create", "/Departments/Edit", "/DoctorSchedules/Create", "/DoctorSchedules/Edit" })
                Assert.Equal(role == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                    (await site.Client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        }
    }

    [Theory]
    [InlineData("/Doctors/Create")]
    [InlineData("/Doctors/Edit")]
    [InlineData("/Departments/Create")]
    [InlineData("/Departments/Edit")]
    [InlineData("/DoctorSchedules/Create")]
    [InlineData("/DoctorSchedules/Edit")]
    public async Task AllAdministrativePostsRequireAntiforgery(string path)
    {
        await using var site = await Site.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Empty(await site.Read(db => db.SecurityAuditLogs.ToListAsync()));
    }

    [Theory]
    [InlineData("08:00", "09:00", 1, true)]
    [InlineData("12:00", "13:00", 1, true)]
    [InlineData("10:00", "11:00", 1, false)]
    [InlineData("08:00", "13:00", 1, false)]
    [InlineData("08:00", "10:00", 1, false)]
    [InlineData("11:00", "13:00", 1, false)]
    [InlineData("09:00", "12:00", 1, false)]
    [InlineData("10:00", "11:00", 2, true)]
    [InlineData("12:00", "09:00", 1, false)]
    [InlineData("10:00", "10:00", 1, false)]
    [InlineData("10:00", "11:00", 99, false)]
    public async Task ScheduleValidationRejectsOverlapsAndInvalidRanges(string start, string end, int day, bool accepted)
    {
        await using var site = await Site.Create();
        var response = await site.Post("/DoctorSchedules/Create", $"/DoctorSchedules/Create?doctorId={site.DoctorId}", new()
        {
            ["DoctorId"] = site.DoctorId.ToString(), ["DoctorVersion"] = (await site.Doctor()).Version.ToString(),
            ["DayOfWeek"] = day.ToString(), ["StartTime"] = start, ["EndTime"] = end, ["IsActive"] = "true"
        });
        Assert.Equal(accepted ? HttpStatusCode.Redirect : HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(accepted ? 2 : 1, await site.Read(db => db.DoctorSchedules.CountAsync()));
        Assert.Equal(accepted ? 1 : 0, await site.Read(db => db.SecurityAuditLogs.CountAsync()));
    }

    [Fact]
    public async Task DoctorRegistrationValidationDuplicatesAndAccountLinking()
    {
        await using var site = await Site.Create();
        var fields = site.DoctorFields(); fields["RegistrationNumber"] = " new-001 "; fields["Email"] = "new@example.test";
        fields["IdentityUserId"] = "doctor-account";
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).StatusCode);
        var created = await site.Read(db => db.Doctors.SingleAsync(d => d.RegistrationNumber == "NEW-001"));
        Assert.Equal("doctor-account", created.IdentityUserId);
        Assert.Equal("NEW@EXAMPLE.TEST", created.NormalizedEmail);
        Assert.Equal(2, await site.Read(db => db.Users.CountAsync()));
        Assert.Contains("Qualifications", await site.Client.GetStringAsync("/Doctors/Details/" + created.Id));
        Assert.Equal(HttpStatusCode.OK, (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).StatusCode);
        fields["RegistrationNumber"] = "NEW-002"; fields["Email"] = "another@example.test";
        Assert.Contains("already linked", await (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).Content.ReadAsStringAsync());
        fields["IdentityUserId"] = "non-doctor-account";
        Assert.Contains("Doctor-role", await (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).Content.ReadAsStringAsync());
        fields["IdentityUserId"] = ""; fields["Email"] = "not-an-email";
        Assert.Equal(HttpStatusCode.OK, (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).StatusCode);
        fields["Email"] = "new@example.test";
        Assert.Contains("already uses", await (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).Content.ReadAsStringAsync());
        fields["Email"] = ""; fields["FirstName"] = " ";
        Assert.Equal(HttpStatusCode.OK, (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).StatusCode);
        Assert.Equal(2, await site.Read(db => db.Doctors.CountAsync()));
    }

    [Fact]
    public async Task DoctorEditingRejectsStaleChangesAndInactiveProfilesAreHidden()
    {
        await using var site = await Site.Create();
        var doctor = await site.Doctor(); var fields = site.DoctorFields();
        fields["Id"] = doctor.Id.ToString(); fields["Version"] = doctor.Version.ToString(); fields["FirstName"] = "Changed";
        fields["IsActive"] = "false";
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("/Doctors/Edit", "/Doctors/Edit/" + doctor.Id, fields)).StatusCode);
        var stale = await site.Post("/Doctors/Edit", "/Doctors/Edit/" + doctor.Id, fields);
        Assert.Contains("record changed", await stale.Content.ReadAsStringAsync());
        Assert.Equal(doctor.Id, (await site.Doctor()).Id);
        Assert.Equal(doctor.RegistrationNumber, (await site.Doctor()).RegistrationNumber);
        Assert.Single(await site.Read(db => db.DoctorSchedules.ToListAsync()));
        site.Role("Receptionist");
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/Doctors/Details/" + doctor.Id)).StatusCode);
        Assert.DoesNotContain("REG-HTTP-001", await site.Client.GetStringAsync("/Doctors?includeInactive=true"));
        site.Role("Administrator");
        Assert.Contains("REG-HTTP-001", await site.Client.GetStringAsync("/Doctors?includeInactive=true"));
        fields["Version"] = (await site.Doctor()).Version.ToString(); fields["IsActive"] = "true";
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("/Doctors/Edit", "/Doctors/Edit/" + doctor.Id, fields)).StatusCode);
        Assert.True((await site.Doctor()).IsActive);
        Assert.Contains(await site.Read(db => db.SecurityAuditLogs.Select(a => a.Action).ToListAsync()), a => a == "DoctorActivated");
    }

    [Fact]
    public async Task DepartmentValidationAndActiveDoctorProtection()
    {
        await using var site = await Site.Create();
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("/Departments/Create", "/Departments/Create", new() { ["Name"] = " Neurology ", ["IsActive"] = "true" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await site.Post("/Departments/Create", "/Departments/Create", new() { ["Name"] = "neurology", ["IsActive"] = "true" })).StatusCode);
        Assert.Equal(2, await site.Read(db => db.Departments.CountAsync()));
        var department = await site.Read(db => db.Departments.SingleAsync(d => d.Id == site.DepartmentId));
        var blocked = await site.Post("/Departments/Edit", "/Departments/Edit/" + department.Id, new() {
            ["Id"] = department.Id.ToString(), ["Version"] = department.Version.ToString(), ["Name"] = department.Name, ["IsActive"] = "false"
        });
        Assert.Contains("Move or deactivate", await blocked.Content.ReadAsStringAsync());
        Assert.True((await site.Read(db => db.Departments.SingleAsync(d => d.Id == site.DepartmentId))).IsActive);
    }

    [Fact]
    public async Task SearchPaginationAndLengthLimits()
    {
        await using var site = await Site.Create();
        using (var scope = site.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            for (var i = 0; i < 24; i++) db.Doctors.Add(new Doctor { FirstName = "Paged", LastName = "Doctor" + i.ToString("D2"), RegistrationNumber = "PAGED-" + i, DepartmentId = site.DepartmentId });
            await db.SaveChangesAsync();
        }
        var html = await site.Client.GetStringAsync("/Doctors?search=Paged&page=2");
        Assert.Contains("Page 2 of 2", html); Assert.Contains("24 doctor(s)", html);
        Assert.Contains("REG-HTTP-001", await site.Client.GetStringAsync("/Doctors?search=Cardiology"));
        Assert.DoesNotContain("REG-HTTP-001", await site.Client.GetStringAsync("/Doctors?search=%25"));
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.GetAsync("/Doctors?search=" + new string('x', 101))).StatusCode);
    }

    [Fact]
    public async Task ScheduleEditsRejectStaleDoctorVersionsAndCanDeactivateWithoutDeleting()
    {
        await using var site = await Site.Create();
        var schedule = await site.Read(db => db.DoctorSchedules.SingleAsync()); var doctor = await site.Doctor();
        var fields = new Dictionary<string,string> { ["Id"] = schedule.Id.ToString(), ["DoctorId"] = doctor.Id.ToString(),
            ["Version"] = schedule.Version.ToString(), ["DoctorVersion"] = doctor.Version.ToString(),
            ["DayOfWeek"] = "1", ["StartTime"] = "09:00", ["EndTime"] = "12:00", ["IsActive"] = "false" };
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("/DoctorSchedules/Edit", "/DoctorSchedules/Edit/" + schedule.Id, fields)).StatusCode);
        Assert.False((await site.Read(db => db.DoctorSchedules.SingleAsync())).IsActive);
        Assert.Contains("record changed", await (await site.Post("/DoctorSchedules/Edit", "/DoctorSchedules/Edit/" + schedule.Id, fields)).Content.ReadAsStringAsync());
        Assert.Single(await site.Read(db => db.DoctorSchedules.ToListAsync()));
        var staleCreation = new Dictionary<string,string>(fields); staleCreation.Remove("Id"); staleCreation["IsActive"] = "true";
        Assert.Contains("record changed", await (await site.Post("/DoctorSchedules/Create", "/DoctorSchedules/Create?doctorId=" + doctor.Id, staleCreation)).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LockedLinkedAccountsStayVisibleAndCannotBeSilentlyRelinked()
    {
        await using var site = await Site.Create();
        using (var scope = site.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var doctor = await db.Doctors.SingleAsync(); doctor.IdentityUserId = "doctor-account";
            var account = await db.Users.SingleAsync(u => u.Id == "doctor-account"); account.LockoutEnd = DateTimeOffset.UtcNow.AddYears(1);
            await db.SaveChangesAsync();
        }
        var html = await site.Client.GetStringAsync("/Doctors/Edit/" + site.DoctorId);
        Assert.Contains("sign-in locked", html);
        Assert.Matches("<option[^>]*selected[^>]*value=\"doctor-account\"", html);
        var fields = site.DoctorFields(); fields["IdentityUserId"] = "doctor-account";
        fields["Id"] = site.DoctorId.ToString(); fields["Version"] = (await site.Doctor()).Version.ToString();
        Assert.Contains("Doctor-role", await (await site.Post("/Doctors/Edit", "/Doctors/Edit/" + site.DoctorId, fields)).Content.ReadAsStringAsync());
        Assert.Equal("doctor-account", (await site.Doctor()).IdentityUserId);
    }

    [Fact]
    public async Task ActiveDoctorsCannotBeRegisteredInInactiveOrMissingDepartments()
    {
        await using var site = await Site.Create();
        using (var scope = site.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var department = await db.Departments.SingleAsync(); department.IsActive = false; await db.SaveChangesAsync();
        }
        var fields = site.DoctorFields(); fields["RegistrationNumber"] = "NEW-DEPT";
        Assert.Contains("active department", await (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).Content.ReadAsStringAsync());
        fields["DepartmentId"] = Guid.NewGuid().ToString();
        Assert.Contains("active department", await (await site.Post("/Doctors/Create", "/Doctors/Create", fields)).Content.ReadAsStringAsync());
        Assert.Equal(1, await site.Read(db => db.Doctors.CountAsync()));
    }

    private sealed class Site : IAsyncDisposable
    {
        public IHost Host { get; private init; } = null!;
        public HttpClient Client { get; private init; } = null!;
        public Guid DepartmentId { get; private init; }
        public Guid DoctorId { get; private init; }
        private string Root { get; init; } = "";
        private readonly Dictionary<string,string> _cookies = new();
        public static async Task<Site> Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "medicare-doctors-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var name = Guid.NewGuid().ToString();
            var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().UseContentRoot(root)
                .UseSetting(WebHostDefaults.ApplicationKey, typeof(DoctorsController).Assembly.GetName().Name)
                .ConfigureServices(services =>
                {
                    services.AddControllersWithViews().AddApplicationPart(typeof(DoctorsController).Assembly);
                    services.AddDbContext<MediCareDbContext>(options => options.UseInMemoryDatabase(name));
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { }); services.AddAuthorization();
                }).Configure(app =>
                {
                    app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllerRoute("default", "{controller=Doctors}/{action=Index}/{id?}"));
                })).StartAsync();
            using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var department = new Department { Name = "Cardiology", NormalizedName = "CARDIOLOGY" }; db.Add(department);
            var doctor = new Doctor { RegistrationNumber = "REG-HTTP-001", FirstName = "Test", LastName = "Doctor", DepartmentId = department.Id, Qualifications = "MBBS", Specialization = "Cardiology" }; db.Add(doctor);
            db.Add(new DoctorSchedule { DoctorId = doctor.Id, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9,0), EndTime = new TimeOnly(12,0) });
            db.Users.AddRange(new IdentityUser { Id = "doctor-account", Email = "doctor@example.test" }, new IdentityUser { Id = "non-doctor-account", Email = "staff@example.test" });
            db.Roles.Add(new IdentityRole { Id = "doctor-role", Name = "Doctor" }); db.UserRoles.Add(new IdentityUserRole<string> { UserId = "doctor-account", RoleId = "doctor-role" });
            await db.SaveChangesAsync();
            var site = new Site { Host = host, Client = host.GetTestClient(), DepartmentId = department.Id, DoctorId = doctor.Id, Root = root }; site.Role("Administrator"); return site;
        }
        public void Role(string? role) { Client.DefaultRequestHeaders.Remove("X-Test-Role"); if (role != null) Client.DefaultRequestHeaders.Add("X-Test-Role", role); }
        public async Task<T> Read<T>(Func<MediCareDbContext,Task<T>> query) { using var scope = Host.Services.CreateScope(); return await query(scope.ServiceProvider.GetRequiredService<MediCareDbContext>()); }
        public Task<Doctor> Doctor() => Read(db => db.Doctors.AsNoTracking().SingleAsync(d => d.Id == DoctorId));
        public Dictionary<string,string> DoctorFields() => new() { ["RegistrationNumber"] = "REG-HTTP-001", ["FirstName"] = "Test", ["LastName"] = "Doctor", ["Qualifications"] = "MBBS", ["Specialization"] = "Cardiology", ["DepartmentId"] = DepartmentId.ToString(), ["IsActive"] = "true" };
        public async Task<HttpResponseMessage> Post(string path, string formPath, Dictionary<string,string> fields)
        {
            var response = await Client.GetAsync(formPath); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies) { var pair = cookie.Split(';')[0]; var index = pair.IndexOf('='); _cookies[pair[..index]] = pair[(index+1)..]; }
                Client.DefaultRequestHeaders.Remove("Cookie"); Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
            }
            var html = await response.Content.ReadAsStringAsync();
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(match.Success);
            var payload = new Dictionary<string,string>(fields) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(match.Groups[1].Value) };
            return await Client.PostAsync(path, new FormUrlEncodedContent(payload));
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await Host.StopAsync(); Host.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier, "management-test"), new Claim(ClaimTypes.Role, role.ToString()) }, "Test")), "Test")));
        }
    }
}
