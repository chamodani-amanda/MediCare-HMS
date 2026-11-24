using System.Net;
using System.Net.Http.Headers;
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

public class PatientHttpTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("Doctor", HttpStatusCode.Forbidden)]
    [InlineData("Receptionist", HttpStatusCode.OK)]
    [InlineData("Administrator", HttpStatusCode.OK)]
    public async Task PatientRoutesEnforceAuthenticationAndRoles(string? role, HttpStatusCode expected)
    {
        await using var site = await Site.Create();
        site.Role(role);
        foreach (var path in new[] { "/Patients", "/Patients/Create", $"/Patients/Details/{site.PatientId}", $"/Patients/Edit/{site.PatientId}" })
            Assert.Equal(expected, (await site.Client.GetAsync(path)).StatusCode);
        if (role is null or "Doctor")
            Assert.Equal(expected, (await site.Client.GetAsync($"/Patients/DownloadDocument?id={site.PatientId}&documentId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(role == "Administrator" ? HttpStatusCode.OK : role == null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden,
            (await site.Client.GetAsync("/Patients/Archived")).StatusCode);
    }

    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    [InlineData("UploadDocument")]
    [InlineData("Archive")]
    [InlineData("Reactivate")]
    public async Task MutationsRejectMissingAntiforgeryTokens(string action)
    {
        await using var site = await Site.Create(); site.Role("Administrator");
        var response = await site.Client.PostAsync("/Patients/" + action,
            new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = site.PatientId.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = site.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        Assert.False((await db.Patients.SingleAsync()).IsArchived);
        Assert.Empty(db.PatientDocuments);
        Assert.Empty(db.SecurityAuditLogs);
    }

    [Fact]
    public async Task DocumentArchiveAndConflictingEditFlowsWorkOverHttp()
    {
        await using var site = await Site.Create(); site.Role("Receptionist");
        var token = await site.Token($"/Patients/Details/{site.PatientId}");
        using (var upload = new MultipartFormDataContent())
        {
            upload.Add(new StringContent(site.PatientId.ToString()), "id");
            upload.Add(new StringContent(token), "__RequestVerificationToken");
            var file = new ByteArrayContent("%PDF-1.7 test document"u8.ToArray());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            upload.Add(file, "document", "test.pdf");
            Assert.Equal(HttpStatusCode.Redirect, (await site.Client.PostAsync("/Patients/UploadDocument", upload)).StatusCode);
        }
        PatientDocument document;
        using (var scope = site.Host.Services.CreateScope())
            document = await scope.ServiceProvider.GetRequiredService<MediCareDbContext>().PatientDocuments.AsNoTracking().SingleAsync();
        var download = await site.Client.GetAsync($"/Patients/DownloadDocument?id={site.PatientId}&documentId={document.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.True(download.Headers.CacheControl!.NoStore);
        Assert.Equal("%PDF-1.7 test document", await download.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync($"/Patients/DownloadDocument?id={Guid.NewGuid()}&documentId={document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync("/App_Data/PatientDocuments/" + document.StoredFileName)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await site.Client.GetAsync($"/Patients/Archive/{site.PatientId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await site.Post("Archive", token, await site.Version())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await site.Post("Reactivate", token, await site.Version())).StatusCode);

        site.Role("Administrator");
        token = await site.Token($"/Patients/Archive/{site.PatientId}");
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Archive", token, await site.Version())).StatusCode);
        Assert.Contains("PAT-HTTP-TEST", await site.Client.GetStringAsync("/Patients/Archived"));
        site.Role("Receptionist");
        Assert.Equal(HttpStatusCode.NotFound, (await site.Client.GetAsync($"/Patients/Details/{site.PatientId}")).StatusCode);
        Assert.DoesNotContain("PAT-HTTP-TEST", await site.Client.GetStringAsync("/Patients"));
        site.Role("Administrator");
        token = await site.Token($"/Patients/Details/{site.PatientId}");
        Assert.Equal(HttpStatusCode.OK, (await site.Client.GetAsync($"/Patients/DownloadDocument?id={site.PatientId}&documentId={document.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await site.Post("Reactivate", token, await site.Version())).StatusCode);

        token = await site.Token($"/Patients/Edit/{site.PatientId}");
        var version = await site.Version();
        Dictionary<string,string> EditFields() => new()
        {
            ["id"] = site.PatientId.ToString(), ["version"] = version.ToString(), ["__RequestVerificationToken"] = token,
            ["FirstName"] = "Updated", ["LastName"] = "Patient", ["DateOfBirth"] = "2000-01-01"
        };
        Assert.Equal(HttpStatusCode.Redirect, (await site.Client.PostAsync("/Patients/Edit", new FormUrlEncodedContent(EditFields()))).StatusCode);
        var stale = await site.Client.PostAsync("/Patients/Edit", new FormUrlEncodedContent(EditFields()));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Patient changed", await stale.Content.ReadAsStringAsync());
        using var finalScope = site.Host.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<MediCareDbContext>();
        var patient = await db.Patients.SingleAsync();
        Assert.Equal(site.PatientId, patient.Id);
        Assert.Equal("PAT-HTTP-TEST", patient.PatientNumber);
        Assert.False(patient.IsArchived);
        Assert.Single(db.PatientDocuments);
        Assert.Contains(await db.SecurityAuditLogs.Select(a => a.Action).ToListAsync(), a => a == "PatientArchived");
    }

    private sealed class Site : IAsyncDisposable
    {
        public IHost Host { get; private init; } = null!;
        public HttpClient Client { get; private init; } = null!;
        public Guid PatientId { get; private init; }
        private string Root { get; init; } = "";
        private readonly Dictionary<string,string> _cookies = new();
        public static async Task<Site> Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "medicare-http-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var name = Guid.NewGuid().ToString();
            var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .UseContentRoot(root).UseSetting(WebHostDefaults.ApplicationKey, typeof(PatientsController).Assembly.GetName().Name)
                .ConfigureServices(services =>
                {
                    services.AddControllersWithViews().AddApplicationPart(typeof(PatientsController).Assembly);
                    services.AddDbContext<MediCareDbContext>(options => options.UseInMemoryDatabase(name));
                    services.AddSingleton<PatientDocumentStorage>();
                    services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthentication>("Test", _ => { });
                    services.AddAuthorization();
                }).Configure(app =>
                {
                    app.UseRouting(); app.UseAuthentication(); app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllerRoute("default", "{controller=Patients}/{action=Index}/{id?}"));
                })).StartAsync();
            using var scope = host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var patient = new Patient { FirstName = "HTTP", LastName = "Patient", PatientNumber = "PAT-HTTP-TEST", DateOfBirth = new DateOnly(2000,1,1) };
            db.Add(patient); await db.SaveChangesAsync();
            return new Site { Host = host, Client = host.GetTestClient(), PatientId = patient.Id, Root = root };
        }
        public void Role(string? role)
        {
            Client.DefaultRequestHeaders.Remove("X-Test-Role");
            if (role != null) Client.DefaultRequestHeaders.Add("X-Test-Role", role);
        }
        public async Task<string> Token(string path)
        {
            var response = await Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    var pair = cookie.Split(';')[0];
                    var separator = pair.IndexOf('=');
                    _cookies[pair[..separator]] = pair[(separator + 1)..];
                }
                Client.DefaultRequestHeaders.Remove("Cookie");
                Client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", _cookies.Select(c => c.Key + "=" + c.Value)));
            }
            var html = await response.Content.ReadAsStringAsync();
            var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
            Assert.True(match.Success, "Rendered form must include an antiforgery token.");
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }
        public async Task<Guid> Version()
        {
            using var scope = Host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<MediCareDbContext>().Patients.Select(p => p.Version).SingleAsync();
        }
        public Task<HttpResponseMessage> Post(string action, string token, Guid version) => Client.PostAsync("/Patients/" + action,
            new FormUrlEncodedContent(new Dictionary<string,string> { ["id"] = PatientId.ToString(), ["version"] = version.ToString(), ["__RequestVerificationToken"] = token }));
        public async ValueTask DisposeAsync()
        {
            Client.Dispose(); await Host.StopAsync(); Host.Dispose();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    // Test-host-only authentication; never registered in the production application.
    private sealed class HeaderAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Role", out var role)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "http-test-user"), new Claim(ClaimTypes.Role, role.ToString()) }, "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }
}
