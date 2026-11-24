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
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Web.Tests;

public class MedicalRecordTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Administrator", HttpStatusCode.Forbidden)]
    [InlineData("Receptionist", HttpStatusCode.Forbidden)]
    [InlineData("Nurse", HttpStatusCode.Forbidden)]
    [InlineData("Pharmacist", HttpStatusCode.Forbidden)]
    [InlineData("Doctor", HttpStatusCode.OK)]
    public async Task ClinicalRoleBoundary(string? role, HttpStatusCode expected)
    {
        await using var site = await Site.Create(); site.Role(role);
        Assert.Equal(expected, (await site.Client.GetAsync("/MedicalRecords")).StatusCode);
        if (role != "Doctor")
        {
            foreach (var path in new[] { "Details/", "Pdf/", "Edit/", "Create?appointmentId=" })
                Assert.Equal(expected, (await site.Client.GetAsync("/MedicalRecords/" + path + site.AppointmentId)).StatusCode);
            foreach (var action in new[] { "Create", "Edit", "Finalize", "Amend" })
                Assert.Equal(expected, (await site.Client.PostAsync("/MedicalRecords/" + action, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        }
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    [InlineData("Finalize")]
    [InlineData("Amend")]
    public async Task ChangesRequireAntiforgery(string action)
    {
        await using var site = await Site.Create();
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Client.PostAsync("/MedicalRecords/" + action, new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(0, await site.Read(db => db.MedicalRecords.CountAsync()));
    }

    [Fact]
    public async Task DraftCreationPrescriptionsIdempotencyAndPrivateReports()
    {
        await using var site = await Site.Create(); var fields = site.Form();
        Assert.Equal(HttpStatusCode.Redirect, (await site.CreateRecord(fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await site.CreateRecord(fields)).StatusCode);
        var revision = await site.Revision();
        Assert.Equal(1, await site.Read(db => db.MedicalRecords.CountAsync()));
        Assert.Equal("Synthetic diagnosis", revision.Diagnosis); Assert.Single(revision.Prescriptions);
        Assert.Equal("Test medication", revision.Prescriptions[0].Medication); Assert.Null(revision.FinalizedAtUtc);
        Assert.Equal("doctor-a", revision.AuthorUserId);
        var response = await site.Client.GetAsync("/MedicalRecords/Details/" + revision.Id);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Contains("Synthetic diagnosis", await response.Content.ReadAsStringAsync());
        Assert.Contains("Synthetic treatment", await site.Client.GetStringAsync("/MedicalRecords/Details/" + revision.Id + "?print=true"));
        var pdf = await site.Client.GetAsync("/MedicalRecords/Pdf/" + revision.Id);
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", pdf.Content.Headers.ContentDisposition!.DispositionType);
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync()));
        Assert.Contains("no-store", pdf.Headers.CacheControl!.ToString());
        Assert.Contains("MedicalReportExported", await site.Read(db => db.SecurityAuditLogs.Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task FinalizedHistorySharedOnlyWithCareDoctorsWhileDraftsStayPrivate()
    {
        await using var site = await Site.Create(); await site.CreateRecord(site.Form()); var revision = await site.Revision();
        site.Role("Doctor", "doctor-b");
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords/Details/" + revision.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords/Pdf/" + revision.Id)).StatusCode);
        Assert.DoesNotContain("PAT-EMR-A", await site.Client.GetStringAsync("/MedicalRecords"));
        await site.Update(db => { db.Appointments.Add(new Appointment { PatientId = site.PatientId, DoctorId = site.OtherDoctorId, BookingToken = Guid.NewGuid(), StartAtUtc = DateTime.UtcNow.AddHours(-1), EndAtUtc = DateTime.UtcNow, DurationMinutes = 60 }); });
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords/Details/" + revision.Id)).StatusCode);
        site.Role("Doctor");
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Finalize/" + revision.Id, "Details/" + revision.Id, new() { ["version"] = revision.Version.ToString() })).StatusCode);
        site.Role("Doctor", "doctor-b");
        Assert.Contains("Synthetic diagnosis", await site.Client.GetStringAsync("/MedicalRecords/Details/" + revision.Id));
        Assert.Equal(HttpStatusCode.OK, (await site.Client.GetAsync("/MedicalRecords/Pdf/" + revision.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords/Edit/" + revision.Id)).StatusCode);
        Assert.DoesNotContain("PAT-PRIVATE", await site.Client.GetStringAsync("/MedicalRecords?search=PRIVATE"));
        site.Role("Doctor", "unlinked");
        Assert.DoesNotContain("PAT-EMR-A", await site.Client.GetStringAsync("/MedicalRecords"));
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords?patientId=" + site.PatientId)).StatusCode);
    }

    [Fact]
    public async Task DraftEditsConcurrencyFinalizationAndAmendmentPreserveHistory()
    {
        await using var site = await Site.Create(); await site.CreateRecord(site.Form()); var original = await site.Revision();
        var edit = site.Form(); edit["Version"] = original.Version.ToString(); edit["Diagnosis"] = "Synthetic corrected diagnosis";
        var savedEdit = await site.Post("Edit/" + original.Id, "Edit/" + original.Id, edit);
        Assert.True(savedEdit.StatusCode == HttpStatusCode.Redirect, Regex.Match(await savedEdit.Content.ReadAsStringAsync(), "validation-summary-errors.*?</ul>", RegexOptions.Singleline).Value);
        Assert.Contains("record changed", await (await site.Post("Edit/" + original.Id, "Edit/" + original.Id, edit)).Content.ReadAsStringAsync());
        var updated = await site.Revision();
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Finalize/" + updated.Id, "Details/" + updated.Id, new() { ["version"] = updated.Version.ToString() })).StatusCode);
        var final = await site.Revision(); Assert.NotNull(final.FinalizedAtUtc);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords/Edit/" + final.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await site.Post("Amend/" + final.Id, "Details/" + final.Id, new() { ["version"] = final.Version.ToString(), ["reason"] = " " })).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Amend/" + final.Id, "Details/" + final.Id, new() { ["version"] = final.Version.ToString(), ["reason"] = "Synthetic correction reason" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await site.Post("Amend/" + final.Id, "Details/" + final.Id, new() { ["version"] = final.Version.ToString(), ["reason"] = "Duplicate attempt" })).StatusCode);
        var revisions = await site.Read(db => db.MedicalRecordRevisions.OrderBy(r => r.Number).ToListAsync());
        Assert.Equal(2, revisions.Count); Assert.Equal(final.Diagnosis, revisions[0].Diagnosis); Assert.Equal(final.FinalizedAtUtc, revisions[0].FinalizedAtUtc);
        Assert.Equal(2, revisions[1].Number); Assert.Null(revisions[1].FinalizedAtUtc); Assert.Equal("Synthetic correction reason", revisions[1].AmendmentReason);
        Assert.Single(await site.Read(db => db.MedicalPrescriptions.Where(p => p.MedicalRecordRevisionId == final.Id).ToListAsync()));
        var amendmentEdit = site.Form(); amendmentEdit["Version"] = revisions[1].Version.ToString(); amendmentEdit["AmendmentReason"] = "Synthetic correction reason"; amendmentEdit["Treatment"] = "Amended synthetic treatment";
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Edit/" + revisions[1].Id, "Edit/" + revisions[1].Id, amendmentEdit)).StatusCode);
        Assert.Contains("Synthetic treatment", await site.Client.GetStringAsync("/MedicalRecords/Details/" + final.Id));
        Assert.Contains("Amended synthetic treatment", await site.Client.GetStringAsync("/MedicalRecords/Details/" + revisions[1].Id));
    }

    [Theory]
    [InlineData("Cancelled")]
    [InlineData("NoShow")]
    [InlineData("Inactive")]
    [InlineData("Archived")]
    [InlineData("OtherDoctor")]
    public async Task IneligibleAppointmentsCannotCreate(string condition)
    {
        await using var site = await Site.Create();
        await site.Update(db =>
        {
            var appointment = db.Appointments.Single(a => a.Id == site.AppointmentId);
            if (Enum.TryParse<AppointmentStatus>(condition, out var status)) appointment.Status = status;
            if (condition == "Inactive") db.Doctors.Single(d => d.Id == site.DoctorId).IsActive = false;
            if (condition == "Archived") db.Patients.Single(p => p.Id == site.PatientId).IsArchived = true;
            if (condition == "OtherDoctor") appointment.DoctorId = site.OtherDoctorId;
        });
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync(site.CreatePath)).StatusCode);
        Assert.Equal(0, await site.Read(db => db.MedicalRecords.CountAsync()));
    }

    [Fact]
    public async Task IncompletePrescriptionsAndOversizedFieldsAreRejected()
    {
        await using var site = await Site.Create(); var form = site.Form(); form["Prescriptions[0].Dosage"] = "";
        Assert.Contains("Every prescription requires", await (await site.CreateRecord(form)).Content.ReadAsStringAsync());
        form = site.Form(); form["Diagnosis"] = new string('x',4001);
        Assert.Equal(HttpStatusCode.OK, (await site.CreateRecord(form)).StatusCode);
        Assert.Equal(0, await site.Read(db => db.MedicalRecords.CountAsync()));
    }

    [Fact]
    public async Task CareRelationshipRevocationHidesExistingDraftsAndFinalizedReports()
    {
        await using var site = await Site.Create(); await site.CreateRecord(site.Form()); var revision = await site.Revision();
        await site.Update(db => db.Appointments.Single(a => a.Id == site.AppointmentId).Status = AppointmentStatus.Cancelled);
        foreach (var path in new[] { "Details/", "Edit/", "Pdf/" }) Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/MedicalRecords/" + path + revision.Id)).StatusCode);
        Assert.DoesNotContain("PAT-EMR-A", await site.Client.GetStringAsync("/MedicalRecords"));
    }

    [Fact]
    public async Task BlankDraftCanBeSavedButNotFinalizedAndFiltersAreValidated()
    {
        await using var site = await Site.Create(); var fields = new Dictionary<string,string> { ["AppointmentId"] = site.AppointmentId.ToString(), ["CreationToken"] = Guid.NewGuid().ToString() };
        Assert.Equal(HttpStatusCode.Redirect, (await site.CreateRecord(fields)).StatusCode);
        var revision = await site.Revision();
        Assert.Contains("required before finalizing", await (await site.Post("Finalize/" + revision.Id, "Details/" + revision.Id, new() { ["version"] = revision.Version.ToString() })).Content.ReadAsStringAsync());
        Assert.Null((await site.Revision()).FinalizedAtUtc);
        Assert.Contains("1 revision(s)", await site.Client.GetStringAsync("/MedicalRecords?state=Draft&search=PAT-EMR-A"));
        Assert.Contains("0 revision(s)", await site.Client.GetStringAsync("/MedicalRecords?state=Finalized"));
        foreach (var query in new[] { "date=invalid", "state=Invalid", "search="+new string('x',101) })
            Assert.Equal(HttpStatusCode.BadRequest,(await site.Client.GetAsync("/MedicalRecords?"+query)).StatusCode);
    }

    [Fact]
    public async Task ComplexScriptPdfFailsSafelyWhileAuthorizedPrintPreservesText()
    {
        await using var site = await Site.Create(); var form = site.Form(); form["Observations"] = "Synthetic \u0dc3\u0dd2\u0d82\u0dc4\u0dbd";
        await site.CreateRecord(form); var revision = await site.Revision();
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await site.Client.GetAsync("/MedicalRecords/Pdf/"+revision.Id)).StatusCode);
        var print = await site.Client.GetStringAsync("/MedicalRecords/Details/"+revision.Id+"?print=true");
        Assert.Contains(form["Observations"],WebUtility.HtmlDecode(print));
        Assert.DoesNotContain("MedicalReportExported",await site.Read(db=>db.SecurityAuditLogs.Select(a=>a.Action).ToListAsync()));
    }

    private sealed class Site : IAsyncDisposable
    {
        public IHost Host { get; private init; } = null!; public HttpClient Client { get; private init; } = null!;
        public Guid PatientId { get; private init; } public Guid DoctorId { get; private init; }
        public Guid OtherDoctorId { get; private init; } public Guid AppointmentId { get; private init; }
        private string Root { get; init; } = ""; private readonly Dictionary<string,string> cookies = new();
        private string? antiforgeryToken;
        public string CreatePath => "/MedicalRecords/Create?appointmentId=" + AppointmentId;
        public static async Task<Site> Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "medicare-emr-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            var name = Guid.NewGuid().ToString();
            var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().UseContentRoot(root)
                .UseSetting(WebHostDefaults.ApplicationKey, typeof(MedicalRecordsController).Assembly.GetName().Name)
                .ConfigureServices(s =>
                {
                    s.AddControllersWithViews().AddApplicationPart(typeof(MedicalRecordsController).Assembly);
                    s.AddDbContext<MediCareDbContext>(o => o.UseInMemoryDatabase(name)); s.AddSingleton(TimeProvider.System);
                    s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", _ => { }); s.AddAuthorization();
                }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(e => e.MapControllerRoute("default", "{controller=MedicalRecords}/{action=Index}/{id?}")); })).StartAsync();
            using var scope = host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var department = new Department { Name = "Synthetic", NormalizedName = "SYNTHETIC" };
            var doctor = new Doctor { Department = department, IdentityUserId = "doctor-a", RegistrationNumber = "TEST-A", FirstName = "Synthetic", LastName = "Doctor A" };
            var other = new Doctor { Department = department, IdentityUserId = "doctor-b", RegistrationNumber = "TEST-B", FirstName = "Synthetic", LastName = "Doctor B" };
            var patient = new Patient { FirstName = "Synthetic", LastName = "Patient", PatientNumber = "PAT-EMR-A" };
            var privatePatient = new Patient { FirstName = "Private", LastName = "Patient", PatientNumber = "PAT-PRIVATE" };
            var appointment = new Appointment { Patient = patient, Doctor = doctor, BookingToken = Guid.NewGuid(), StartAtUtc = DateTime.UtcNow.AddHours(-1), EndAtUtc = DateTime.UtcNow, DurationMinutes = 60 };
            db.AddRange(appointment, other, privatePatient); await db.SaveChangesAsync();
            var site = new Site { Host = host, Client = host.GetTestClient(), Root = root, PatientId = patient.Id, DoctorId = doctor.Id, OtherDoctorId = other.Id, AppointmentId = appointment.Id }; site.Role("Doctor"); return site;
        }
        public void Role(string? role, string user = "doctor-a")
        { Client.DefaultRequestHeaders.Remove("X-Test-Role"); Client.DefaultRequestHeaders.Remove("X-Test-User"); if (role != null) Client.DefaultRequestHeaders.Add("X-Test-Role",role); Client.DefaultRequestHeaders.Add("X-Test-User",user); }
        public async Task<T> Read<T>(Func<MediCareDbContext,Task<T>> query) { using var scope = Host.Services.CreateScope(); return await query(scope.ServiceProvider.GetRequiredService<MediCareDbContext>()); }
        public async Task Update(Action<MediCareDbContext> action) { using var scope = Host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>(); action(db); await db.SaveChangesAsync(); }
        public Task<MedicalRecordRevision> Revision() => Read(db => db.MedicalRecordRevisions.AsNoTracking().Include(r => r.Prescriptions).SingleAsync());
        public Dictionary<string,string> Form() => new() { ["AppointmentId"] = AppointmentId.ToString(), ["CreationToken"] = Guid.NewGuid().ToString(), ["Symptoms"] = "Synthetic symptoms", ["Observations"] = "Synthetic observations", ["Diagnosis"] = "Synthetic diagnosis", ["Treatment"] = "Synthetic treatment", ["Prescriptions[0].Medication"] = "Test medication", ["Prescriptions[0].Dosage"] = "Test dose", ["Prescriptions[0].Route"] = "Test route", ["Prescriptions[0].Frequency"] = "Test frequency", ["Prescriptions[0].Duration"] = "Test duration", ["Prescriptions[0].Instructions"] = "Synthetic instructions" };
        public Task<HttpResponseMessage> CreateRecord(Dictionary<string,string> fields) => Post("Create", "Create?appointmentId=" + AppointmentId, fields);
        public async Task<HttpResponseMessage> Post(string path, string form, Dictionary<string,string> fields)
        {
            var response = await Client.GetAsync("/MedicalRecords/" + form);
            if (response.StatusCode == HttpStatusCode.Redirect) response = await Client.GetAsync(response.Headers.Location);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            if (response.Headers.TryGetValues("Set-Cookie",out var values))
            { foreach (var value in values) { var pair=value.Split(';')[0]; var at=pair.IndexOf('='); cookies[pair[..at]]=pair[(at+1)..]; } Client.DefaultRequestHeaders.Remove("Cookie"); Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(c=>c.Key+"="+c.Value))); }
            var html = await response.Content.ReadAsStringAsync(); var token = Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            if (token.Success) antiforgeryToken = WebUtility.HtmlDecode(token.Groups[1].Value);
            Assert.NotNull(antiforgeryToken);
            return await Client.PostAsync("/MedicalRecords/" + path,new FormUrlEncodedContent(new Dictionary<string,string>(fields) { ["__RequestVerificationToken"] = antiforgeryToken }));
        }
        public async ValueTask DisposeAsync() { Client.Dispose(); await Host.StopAsync(); Host.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role",out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier,Request.Headers["X-Test-User"].ToString()), new Claim(ClaimTypes.Role,role.ToString()) },"Test")),"Test")));
        }
    }
}
