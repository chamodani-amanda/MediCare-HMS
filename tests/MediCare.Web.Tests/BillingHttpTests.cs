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

public class BillingHttpTests
{
    [Theory]
    [InlineData(null)][InlineData("Administrator")][InlineData("Receptionist")][InlineData("Doctor")][InlineData("Nurse")][InlineData("Pharmacist")][InlineData("Laboratory Staff")][InlineData("Accountant")]
    public async Task BillingRoutesEnforceLeastPrivilege(string? role)
    {
        await using var site=await Site.Create();site.Role(role);var refused=role==null?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden;
        foreach(var path in new[]{"/Billing","/Billing/Invoices","/Billing/Charges","/Billing/Draft","/Billing/Charge","/Billing/Details/"+site.InvoiceId,"/Billing/Receipt/"+site.PaymentId,"/Billing/Pdf/"+site.InvoiceId})Assert.Equal(role=="Accountant"?HttpStatusCode.OK:refused,(await site.Client.GetAsync(path)).StatusCode);
        Assert.Equal(role=="Administrator"?HttpStatusCode.OK:refused,(await site.Client.GetAsync("/BillingCatalogue")).StatusCode);
    }
    [Theory]
    [InlineData("Billing/Charge","Accountant")][InlineData("Billing/Draft","Accountant")][InlineData("Billing/Issue","Accountant")][InlineData("Billing/Void","Accountant")][InlineData("Billing/Pay","Accountant")][InlineData("Billing/Reverse","Accountant")][InlineData("BillingCatalogue/Edit","Administrator")][InlineData("BillingCatalogue/Price","Administrator")]
    public async Task MutationsRequireAntiforgery(string path,string role)
    {
        await using var site=await Site.Create();site.Role(role);Assert.Equal(HttpStatusCode.BadRequest,(await site.Client.PostAsync("/"+path,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }
    [Fact] public async Task AccountantPagesAndPdfDoNotExposeClinicalOrContactInformation()
    {
        await using var site=await Site.Create();site.Role("Accountant");foreach(var path in new[]{"/Billing","/Billing/Invoices","/Billing/Charges","/Billing/Details/"+site.InvoiceId,"/Billing/Receipt/"+site.PaymentId,"/Billing/Charge?patientId="+site.PatientId,"/Billing/Draft?patientId="+site.PatientId}){
            var response=await site.Client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("no-store",response.Headers.CacheControl!.ToString());var html=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("PRIVATE-CONTACT",html);Assert.DoesNotContain("PRIVATE-DIAGNOSIS",html);Assert.DoesNotContain("Medical Records</a>",html);
        }
        foreach(var path in new[]{"/MedicalRecords","/Laboratory","/Pharmacy","/Admissions","/Outpatients"})Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.GetAsync(path)).StatusCode);
        var pdf=await site.Client.GetAsync("/Billing/Receipt/"+site.PaymentId+"?pdf=true");Assert.Equal(HttpStatusCode.OK,pdf.StatusCode);Assert.Equal("application/pdf",pdf.Content.Headers.ContentType!.MediaType);Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync()));
    }
    [Fact] public async Task AdminCatalogueValidatesAndAuditsWithoutFinancialAccess()
    {
        await using var site=await Site.Create();site.Role("Administrator");var page=await site.Client.GetAsync("/BillingCatalogue/Edit");var html=await page.Content.ReadAsStringAsync();var token=Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;Assert.NotEmpty(token);
        site.Client.DefaultRequestHeaders.Add("Cookie",page.Headers.GetValues("Set-Cookie").First().Split(';')[0]);
        var response=await site.Client.PostAsync("/BillingCatalogue/Edit",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",WebUtility.HtmlDecode(token)},{"Code","synthetic"},{"Name","Explicit service"},{"Category","Consultation"},{"IsActive","true"}}));Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        using var scope=site.Host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();Assert.True(await db.BillingServices.AnyAsync(s=>s.Code=="SYNTHETIC"));Assert.True(await db.SecurityAuditLogs.AnyAsync(a=>a.Action=="BillingServiceSaved"));
    }
    private sealed class Site:IAsyncDisposable
    {
        public IHost Host=null!;public HttpClient Client=null!;public Guid InvoiceId,PaymentId,PatientId;private string root="";
        public static async Task<Site> Create()
        {
            var root=Path.Combine(Path.GetTempPath(),"medicare-billing-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var name=Guid.NewGuid().ToString();
            var host=await new HostBuilder().ConfigureWebHost(web=>web.UseTestServer().UseContentRoot(root).UseSetting(WebHostDefaults.ApplicationKey,typeof(BillingController).Assembly.GetName().Name)
                .ConfigureServices(s=>{s.AddControllersWithViews().AddApplicationPart(typeof(BillingController).Assembly);s.AddDbContext<MediCareDbContext>(o=>o.UseInMemoryDatabase(name));s.AddSingleton(TimeProvider.System);s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Auth>("Test",_=>{});s.AddAuthorization();})
                .Configure(app=>{app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseEndpoints(e=>e.MapControllerRoute("default","{controller=Billing}/{action=Index}/{id?}"));})).StartAsync();
            using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();var patient=new Patient{PatientNumber="SYN-BILL",FirstName="Synthetic",LastName="Patient",PhoneNumber="PRIVATE-CONTACT"};var service=new BillingService{Code="SYN",Name="Explicit service",Category="Other"};var price=new BillingPrice{Service=service,UnitPrice=100,EffectiveAtUtc=DateTime.UtcNow.AddDays(-1)};var charge=new PatientCharge{PatientId=patient.Id,Price=price,Description="Configured service",Quantity=1,UnitPrice=100,Total=100,Token=Guid.NewGuid()};var invoice=new BillingInvoice{Number=1,PatientId=patient.Id,Token=Guid.NewGuid(),State=InvoiceState.Issued,Total=100,IssuedAtUtc=DateTime.UtcNow,CreatedAtUtc=DateTime.UtcNow};invoice.Lines.Add(new BillingInvoiceLine{Charge=charge});var payment=new BillingPayment{Number=1,Invoice=invoice,Token=Guid.NewGuid(),Amount=10,RecordedAtUtc=DateTime.UtcNow,ActorUserId="accountant"};db.AddRange(patient,payment);await db.SaveChangesAsync();return new(){Host=host,Client=host.GetTestClient(),InvoiceId=invoice.Id,PaymentId=payment.Id,PatientId=patient.Id,root=root};
        }
        public void Role(string? role){Client.DefaultRequestHeaders.Remove("X-Role");if(role!=null)Client.DefaultRequestHeaders.Add("X-Role",role);}
        public async ValueTask DisposeAsync(){Client.Dispose();await Host.StopAsync();Host.Dispose();Directory.Delete(root,true);}
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logs,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(options,logs,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(!Request.Headers.TryGetValue("X-Role",out var role)?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"billing-test-user"),new Claim(ClaimTypes.Role,role.ToString())},"Test")),"Test")));
    }
}
