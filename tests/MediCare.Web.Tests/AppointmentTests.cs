using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Web.Tests;

public class AppointmentTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Nurse", HttpStatusCode.Forbidden)]
    [InlineData("Doctor", HttpStatusCode.OK)]
    [InlineData("Receptionist", HttpStatusCode.OK)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task PermissionsAndPatientPrivacy(string? role, HttpStatusCode expected)
    {
        await using var site = await Site.Create(); site.Role(role, "doctor-a");
        Assert.Equal(expected, (await site.Client.GetAsync("/Appointments")).StatusCode);
        Assert.Equal(expected, (await site.Client.GetAsync("/Appointments/Calendar")).StatusCode);
        var mutation = role is "Administrator" or "Receptionist" ? HttpStatusCode.OK : role == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
        foreach (var path in new[] { "/Appointments/Create", "/Appointments/Availability" }) Assert.Equal(mutation, (await site.Client.GetAsync(path)).StatusCode);
        if (role is not "Administrator" and not "Receptionist")
            foreach (var action in new[] { "Create", "Reschedule", "ChangeStatus" })
                Assert.Equal(role == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
                    (await site.Client.PostAsync("/Appointments/" + action, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Reschedule")]
    [InlineData("ChangeStatus")]
    public async Task MutationsRejectMissingTokens(string action)
    {
        await using var site = await Site.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.PostAsync("/Appointments/" + action,
            new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(0, await site.Read(db => db.Appointments.CountAsync()));
    }

    [Fact]
    public async Task BookingIsUtcAuditedIdempotentAndDifferentTokenCannotOverlap()
    {
        await using var site = await Site.Create(); var fields = site.Booking();
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Create", "/Appointments/Create", fields)).StatusCode);
        var appointment = await site.Read(db => db.Appointments.SingleAsync());
        Assert.Equal(HospitalTime.ToUtc(site.Date, new TimeOnly(9,0)), appointment.StartAtUtc);
        Assert.Equal(appointment.StartAtUtc.AddMinutes(30), appointment.EndAtUtc);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Create", "/Appointments/Create", fields)).StatusCode);
        Assert.Equal(1, await site.Read(db => db.Appointments.CountAsync()));
        var overlap = site.Booking("09:15", 60);
        Assert.Contains("overlaps", await (await site.Post("Create", "/Appointments/Create", overlap)).Content.ReadAsStringAsync());
        Assert.Equal(1, await site.Read(db => db.SecurityAuditLogs.CountAsync()));
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Create", "/Appointments/Create", site.Booking("09:30", 15))).StatusCode);
        var html = await site.Client.GetStringAsync("/Appointments"); Assert.Contains("PAT-APPT-A", html);
        Assert.Contains("09:00", html); Assert.DoesNotContain("03:30", html);
    }

    [Theory]
    [InlineData("08:45", 30)]
    [InlineData("16:45", 30)]
    [InlineData("09:05", 30)]
    [InlineData("09:00:01", 30)]
    [InlineData("09:00", 20)]
    [InlineData("09:00", 0)]
    [InlineData("09:00", 90)]
    public async Task InvalidHoursIntervalsAndDurationsAreRejected(string time, int duration)
    {
        await using var site = await Site.Create();
        Assert.Equal(HttpStatusCode.OK, (await site.Post("Create", "/Appointments/Create", site.Booking(time, duration))).StatusCode);
        Assert.Equal(0, await site.Read(db => db.Appointments.CountAsync()));
    }

    [Fact]
    public async Task ArchivedPatientsInactiveDoctorsAndPastDatesAreRejected()
    {
        await using var site = await Site.Create();
        using (var scope = site.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            (await db.Patients.SingleAsync(p => p.Id == site.PatientId)).IsArchived = true;
            (await db.Doctors.SingleAsync(d => d.Id == site.DoctorId)).IsActive = false; await db.SaveChangesAsync();
        }
        var response = await site.Post("Create", "/Appointments/Create", site.Booking());
        var html = await response.Content.ReadAsStringAsync(); Assert.Contains("active patient", html); Assert.Contains("active doctor", html);
        var fields = site.Booking(); fields["Date"] = "2020-01-01";
        Assert.Contains("Only future", await (await site.Post("Create", "/Appointments/Create", fields)).Content.ReadAsStringAsync());
        Assert.Equal(0, await site.Read(db => db.Appointments.CountAsync()));
    }

    [Fact]
    public async Task WholeDurationCanSpanAdjacentWorkingShifts()
    {
        await using var site = await Site.Create();
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Create", "/Appointments/Create", site.Booking("11:45", 45))).StatusCode);
    }

    [Fact]
    public async Task ReschedulingCancellationAndTerminalHistory()
    {
        await using var site = await Site.Create();
        await site.Post("Create", "/Appointments/Create", site.Booking()); var appointment = await site.Read(db => db.Appointments.SingleAsync());
        var fields = site.Booking("10:00", 45); fields["Id"] = appointment.Id.ToString(); fields["Version"] = appointment.Version.ToString();
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Reschedule", "/Appointments/Reschedule/" + appointment.Id, fields)).StatusCode);
        Assert.Contains("record changed", await (await site.Post("Reschedule", "/Appointments/Reschedule/" + appointment.Id, fields)).Content.ReadAsStringAsync());
        appointment = await site.Read(db => db.Appointments.SingleAsync());
        var status = new Dictionary<string,string> { ["Id"] = appointment.Id.ToString(), ["Version"] = appointment.Version.ToString(), ["Status"] = "Completed" };
        Assert.Contains("require the appointment to have ended", await (await site.Post("ChangeStatus", "/Appointments/ChangeStatus/" + appointment.Id, status)).Content.ReadAsStringAsync());
        status["Status"] = "Cancelled"; status["CancellationReason"] = "Changed plans";
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("ChangeStatus", "/Appointments/ChangeStatus/" + appointment.Id, status)).StatusCode);
        appointment = await site.Read(db => db.Appointments.SingleAsync());
        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status); Assert.NotNull(appointment.CancelledAtUtc);
        Assert.Equal(HospitalTime.ToUtc(site.Date, new TimeOnly(10,0)), appointment.StartAtUtc);
        status["Version"] = appointment.Version.ToString(); status["Status"] = "Scheduled";
        Assert.Contains("terminal", await (await site.Post("ChangeStatus", "/Appointments/ChangeStatus/" + appointment.Id, status)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Create", "/Appointments/Create", site.Booking("10:00", 45))).StatusCode);
        Assert.Equal(2, await site.Read(db => db.Appointments.CountAsync()));
        Assert.Contains(await site.Read(db => db.SecurityAuditLogs.Select(a => a.Action).ToListAsync()), a => a == "AppointmentCancelled");
    }

    [Theory]
    [InlineData("Completed")]
    [InlineData("NoShow")]
    public async Task FinishedAppointmentsCanBecomeTerminalAfterEnd(string statusName)
    {
        await using var site = await Site.Create(); await site.Post("Create", "/Appointments/Create", site.Booking());
        var appointment = await site.Read(db => db.Appointments.SingleAsync()); site.Clock.Now = new DateTimeOffset(appointment.EndAtUtc.AddSeconds(1));
        var fields = new Dictionary<string,string> { ["Id"] = appointment.Id.ToString(), ["Version"] = appointment.Version.ToString(), ["Status"] = statusName };
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("ChangeStatus", "/Appointments/ChangeStatus/" + appointment.Id, fields)).StatusCode);
        Assert.Equal(statusName, (await site.Read(db => db.Appointments.SingleAsync())).Status.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/Appointments/Reschedule/" + appointment.Id)).StatusCode);
    }

    [Fact]
    public async Task DoctorsCanOnlySeeTheirLinkedAppointmentsAcrossAllFiltersAndPages()
    {
        await using var site = await Site.Create(); await site.Post("Create", "/Appointments/Create", site.Booking());
        var first = await site.Read(db => db.Appointments.SingleAsync());
        var fields = site.Booking(); fields["DoctorId"] = site.OtherDoctorId.ToString(); fields["PatientId"] = site.OtherPatientId.ToString();
        await site.Post("Create", "/Appointments/Create", fields);
        var other = await site.Read(db => db.Appointments.SingleAsync(a => a.DoctorId == site.OtherDoctorId));
        site.Role("Doctor", "doctor-a");
        var html = await site.Client.GetStringAsync("/Appointments"); Assert.Contains("PAT-APPT-A", html); Assert.DoesNotContain("PAT-PRIVATE-B", html);
        Assert.Equal(HttpStatusCode.OK, (await site.Client.GetAsync("/Appointments/Details/" + first.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/Appointments/Details/" + other.Id)).StatusCode);
        Assert.DoesNotContain("PAT-PRIVATE-B", await site.Client.GetStringAsync("/Appointments?doctorId=" + site.OtherDoctorId + "&search=PRIVATE"));
        Assert.DoesNotContain("PAT-PRIVATE-B", await site.Client.GetStringAsync("/Appointments/Calendar?date=" + site.Date.ToString("yyyy-MM-dd")));
        site.Role("Doctor", "unlinked-account"); Assert.DoesNotContain("PAT-APPT-A", await site.Client.GetStringAsync("/Appointments"));
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/Appointments/Details/" + first.Id)).StatusCode);
    }

    [Fact]
    public async Task AvailabilityAndScheduleEditsRespectExistingBookings()
    {
        await using var site = await Site.Create(); await site.Post("Create", "/Appointments/Create", site.Booking());
        var html = await site.Client.GetStringAsync($"/Appointments/Availability?doctorId={site.DoctorId}&date={site.Date:yyyy-MM-dd}&durationMinutes=30");
        Assert.DoesNotContain(">09:00</a>", html); Assert.Contains(">09:30</a>", html);
        var schedule = await site.Read(db => db.DoctorSchedules.SingleAsync(s => s.DoctorId == site.DoctorId && s.StartTime == new TimeOnly(9,0)));
        var version = await site.Read(db => db.Doctors.Where(d => d.Id == site.DoctorId).Select(d => d.Version).SingleAsync());
        var fields = new Dictionary<string,string> { ["Id"] = schedule.Id.ToString(), ["DoctorId"] = site.DoctorId.ToString(), ["Version"] = schedule.Version.ToString(),
            ["DoctorVersion"] = version.ToString(), ["DayOfWeek"] = ((int)site.Date.DayOfWeek).ToString(), ["StartTime"] = "10:00", ["EndTime"] = "12:00", ["IsActive"] = "true" };
        var response = await site.PostPath("/DoctorSchedules/Edit", "/DoctorSchedules/Edit/" + schedule.Id, fields);
        Assert.Contains("outside working hours", await response.Content.ReadAsStringAsync());
        Assert.Equal(new TimeOnly(9,0), (await site.Read(db => db.DoctorSchedules.SingleAsync(s => s.Id == schedule.Id))).StartTime);
    }

    [Fact]
    public async Task ListingFiltersAndPaginationAreBounded()
    {
        await using var site = await Site.Create();
        using (var scope = site.Host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            for (var i = 0; i < 24; i++) db.Appointments.Add(new Appointment { BookingToken = Guid.NewGuid(), PatientId = site.PatientId, DoctorId = site.DoctorId,
                StartAtUtc = HospitalTime.ToUtc(site.Date.AddDays(i), new TimeOnly(9,0)), EndAtUtc = HospitalTime.ToUtc(site.Date.AddDays(i), new TimeOnly(9,30)), DurationMinutes = 30 });
            await db.SaveChangesAsync();
        }
        Assert.Contains("Page 2 of 2", await site.Client.GetStringAsync("/Appointments?page=2&search=PAT-APPT-A"));
        Assert.Contains("1 appointment(s)", await site.Client.GetStringAsync($"/Appointments?date={site.Date:yyyy-MM-dd}&doctorId={site.DoctorId}&status=Scheduled"));
        Assert.Contains("0 appointment(s)", await site.Client.GetStringAsync("/Appointments?status=Cancelled"));
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.GetAsync("/Appointments?date=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.GetAsync("/Appointments?status=99")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.GetAsync("/Appointments?search=" + new string('x',101))).StatusCode);
    }

    private sealed class TestClock : TimeProvider { public DateTimeOffset Now = new(2030,1,1,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class Site : IAsyncDisposable
    {
        public IHost Host { get; private init; } = null!; public HttpClient Client { get; private init; } = null!;
        public Guid PatientId { get; private init; } public Guid OtherPatientId { get; private init; }
        public Guid DoctorId { get; private init; } public Guid OtherDoctorId { get; private init; }
        public DateOnly Date { get; private init; } public TestClock Clock { get; private init; } = null!;
        private string Root { get; init; } = ""; private readonly Dictionary<string,string> _cookies = new();
        public static async Task<Site> Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "medicare-appointments-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var name = Guid.NewGuid().ToString(); var clock = new TestClock();
            var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().UseContentRoot(root)
                .UseSetting(WebHostDefaults.ApplicationKey, typeof(AppointmentsController).Assembly.GetName().Name)
                .ConfigureServices(services =>
                {
                    services.AddControllersWithViews().AddApplicationPart(typeof(AppointmentsController).Assembly);
                    services.AddDbContext<MediCareDbContext>(options => options.UseInMemoryDatabase(name));
                    services.AddSingleton<TimeProvider>(clock); services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { }); services.AddAuthorization();
                }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllerRoute("default", "{controller=Appointments}/{action=Index}/{id?}")); })).StartAsync();
            using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var department = new Department { Name = "Test", NormalizedName = "TEST" }; db.Add(department);
            var a = new Doctor { FirstName = "Linked", LastName = "Doctor A", IdentityUserId = "doctor-a", RegistrationNumber = "A", DepartmentId = department.Id };
            var b = new Doctor { FirstName = "Other", LastName = "Doctor B", IdentityUserId = "doctor-b", RegistrationNumber = "B", DepartmentId = department.Id }; db.AddRange(a,b);
            var patient = new Patient { FirstName = "Patient", LastName = "A", PatientNumber = "PAT-APPT-A" };
            var other = new Patient { FirstName = "Private", LastName = "B", PatientNumber = "PAT-PRIVATE-B" }; db.AddRange(patient,other);
            var date = new DateOnly(2030,1,2); while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
            db.DoctorSchedules.AddRange(new DoctorSchedule { DoctorId = a.Id, DayOfWeek = date.DayOfWeek, StartTime = new TimeOnly(9,0), EndTime = new TimeOnly(12,0) },
                new DoctorSchedule { DoctorId = a.Id, DayOfWeek = date.DayOfWeek, StartTime = new TimeOnly(12,0), EndTime = new TimeOnly(17,0) },
                new DoctorSchedule { DoctorId = b.Id, DayOfWeek = date.DayOfWeek, StartTime = new TimeOnly(9,0), EndTime = new TimeOnly(17,0) }); await db.SaveChangesAsync();
            var site = new Site { Host = host, Client = host.GetTestClient(), Root = root, PatientId = patient.Id, OtherPatientId = other.Id,
                DoctorId = a.Id, OtherDoctorId = b.Id, Date = date, Clock = clock }; site.Role("Administrator"); return site;
        }
        public void Role(string? role, string user = "booking-staff") { Client.DefaultRequestHeaders.Remove("X-Test-Role"); Client.DefaultRequestHeaders.Remove("X-Test-User"); if (role != null) Client.DefaultRequestHeaders.Add("X-Test-Role",role); Client.DefaultRequestHeaders.Add("X-Test-User",user); }
        public async Task<T> Read<T>(Func<MediCareDbContext,Task<T>> query) { using var scope = Host.Services.CreateScope(); return await query(scope.ServiceProvider.GetRequiredService<MediCareDbContext>()); }
        public Dictionary<string,string> Booking(string start = "09:00", int duration = 30) => new() { ["BookingToken"] = Guid.NewGuid().ToString(), ["PatientId"] = PatientId.ToString(), ["DoctorId"] = DoctorId.ToString(), ["Date"] = Date.ToString("yyyy-MM-dd"), ["StartTime"] = start, ["DurationMinutes"] = duration.ToString() };
        public Task<HttpResponseMessage> Post(string action, string form, Dictionary<string,string> fields) => PostPath("/Appointments/" + action, form, fields);
        public async Task<HttpResponseMessage> PostPath(string path, string form, Dictionary<string,string> fields)
        {
            var response = await Client.GetAsync(form); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            if (response.Headers.TryGetValues("Set-Cookie",out var cookies)) { foreach (var cookie in cookies) { var pair=cookie.Split(';')[0]; var index=pair.IndexOf('='); _cookies[pair[..index]]=pair[(index+1)..]; }
                Client.DefaultRequestHeaders.Remove("Cookie"); Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",_cookies.Select(c=>c.Key+"="+c.Value))); }
            var html = await response.Content.ReadAsStringAsync(); var token = Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\""); Assert.True(token.Success);
            return await Client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>(fields) { ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value) }));
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await Host.StopAsync(); Host.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role",out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] {
                new Claim(ClaimTypes.NameIdentifier,Request.Headers["X-Test-User"].ToString()), new Claim(ClaimTypes.Role,role.ToString()) },"Test")),"Test")));
        }
    }
}
