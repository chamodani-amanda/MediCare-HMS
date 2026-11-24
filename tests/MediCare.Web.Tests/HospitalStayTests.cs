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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MediCare.Web.Tests;

public class HospitalStayTests
{
    [Theory]
    [InlineData(null)][InlineData("Administrator")][InlineData("Nurse")][InlineData("Pharmacist")][InlineData("Laboratory Staff")][InlineData("Accountant")]
    public async Task UnapprovedRolesCannotAccessPatientStayData(string? role)
    {
        await using var s=await Site.Create();s.Role(role);var expected=role==null?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden;
        foreach(var path in new[]{"/Admissions","/Outpatients","/Admissions/Details/"+Guid.NewGuid(),"/Outpatients/Clinical/"+Guid.NewGuid()})Assert.Equal(expected,(await s.Client.GetAsync(path)).StatusCode);
        Assert.Equal(role=="Administrator"?HttpStatusCode.OK:expected,(await s.Client.GetAsync("/Facilities")).StatusCode);
    }
    [Theory]
    [InlineData("Admissions/Create","Receptionist")][InlineData("Admissions/Transfer","Receptionist")][InlineData("Admissions/Discharge","Receptionist")][InlineData("Admissions/Clearance","Doctor")][InlineData("Admissions/Clinical","Doctor")]
    [InlineData("Outpatients/Create","Receptionist")][InlineData("Outpatients/Cancel","Receptionist")][InlineData("Outpatients/Progress","Doctor")][InlineData("Outpatients/Clinical","Doctor")][InlineData("Facilities/Ward","Administrator")][InlineData("Facilities/Bed","Administrator")]
    public async Task EveryMutationRequiresAntiforgery(string path,string role)
    {
        await using var s=await Site.Create();s.Role(role);Assert.Equal(HttpStatusCode.BadRequest,(await s.Client.PostAsync("/"+path,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(0,await s.Read(db=>db.Admissions.CountAsync()));Assert.Equal(0,await s.Read(db=>db.OutpatientVisits.CountAsync()));
    }
    [Fact]
    public async Task AdmissionTransferClinicalClearanceAndCheckoutPreserveHistoryAndPrivacy()
    {
        await using var s=await Site.Create();var registration=s.Fields(true);
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Admissions/Create","/Admissions/Create",registration)).StatusCode);
        var a=await s.Read(db=>db.Admissions.SingleAsync());Assert.Equal(1,await s.Read(db=>db.BedOccupancies.CountAsync(o=>o.EndedAtUtc==null)));
        registration["BedId"]=s.OtherBed.ToString();Assert.Equal(HttpStatusCode.BadRequest,(await s.Post("/Admissions/Create","/Admissions/Create",registration)).StatusCode);
        var transfer=new Dictionary<string,string>{{"version",a.Version.ToString()},{"bedId",s.OtherBed.ToString()},{"token",Guid.NewGuid().ToString()},{"reason","Synthetic operational transfer"}};
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Admissions/Transfer/"+a.Id,"/Admissions/Details/"+a.Id,transfer)).StatusCode);
        Assert.Equal(2,await s.Read(db=>db.BedTransfers.CountAsync()));Assert.Equal(s.OtherBed,await s.Read(db=>db.BedOccupancies.Where(o=>o.EndedAtUtc==null).Select(o=>o.BedId).SingleAsync()));
        s.Role("Doctor");a=await s.Read(db=>db.Admissions.SingleAsync());Assert.Equal(HttpStatusCode.BadRequest,(await s.Post("/Admissions/Clearance/"+a.Id,"/Admissions/Details/"+a.Id,new(){{"version",a.Version.ToString()}})).StatusCode);
        var note=new Dictionary<string,string>{{"ParentVersion",a.Version.ToString()},{"AdmissionReason","PRIVATE-ADMISSION-REASON"},{"DischargeSummary","PRIVATE-DISCHARGE-SUMMARY"}};
        Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Admissions/Clinical/"+a.Id,"/Admissions/Clinical/"+a.Id,note)).StatusCode);
        var doctorHtml=await s.Client.GetStringAsync("/Admissions/Details/"+a.Id);Assert.Contains("PRIVATE-ADMISSION-REASON",doctorHtml);
        s.Role("Receptionist");var receptionHtml=await s.Client.GetStringAsync("/Admissions/Details/"+a.Id);Assert.DoesNotContain("PRIVATE-ADMISSION-REASON",receptionHtml);Assert.DoesNotContain("PRIVATE-DISCHARGE-SUMMARY",receptionHtml);Assert.DoesNotContain("PRIVATE-CONTACT",receptionHtml);
        Assert.Equal(HttpStatusCode.Forbidden,(await s.Client.GetAsync("/Admissions/Clinical/"+a.Id)).StatusCode);
        a=await s.Read(db=>db.Admissions.SingleAsync());Assert.Equal(HttpStatusCode.Conflict,(await s.Post("/Admissions/Discharge/"+a.Id,"/Admissions/Details/"+a.Id,new(){{"version",a.Version.ToString()},{"token",Guid.NewGuid().ToString()}})).StatusCode);
        s.Role("Doctor");Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Admissions/Clearance/"+a.Id,"/Admissions/Details/"+a.Id,new(){{"version",a.Version.ToString()}})).StatusCode);
        Assert.Equal(1,await s.Read(db=>db.BedOccupancies.CountAsync(o=>o.EndedAtUtc==null)));Assert.Equal(HttpStatusCode.NotFound,(await s.Client.GetAsync("/Admissions/Clinical/"+a.Id)).StatusCode);
        s.Role("Receptionist");a=await s.Read(db=>db.Admissions.SingleAsync());var checkout=new Dictionary<string,string>{{"version",a.Version.ToString()},{"token",Guid.NewGuid().ToString()}};
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Admissions/Discharge/"+a.Id,"/Admissions/Details/"+a.Id,checkout)).StatusCode);
        checkout["token"]=Guid.NewGuid().ToString();Assert.Equal(HttpStatusCode.Conflict,(await s.Post("/Admissions/Discharge/"+a.Id,"/Admissions/Details/"+a.Id,checkout)).StatusCode);
        Assert.Equal(AdmissionStatus.Discharged,await s.Read(db=>db.Admissions.Select(a=>a.Status).SingleAsync()));Assert.Equal(0,await s.Read(db=>db.BedOccupancies.CountAsync(o=>o.EndedAtUtc==null)));Assert.Equal(3,await s.Read(db=>db.BedTransfers.CountAsync()));Assert.Equal(1,await s.Read(db=>db.AdmissionClinicalNotes.CountAsync()));
    }
    [Theory]
    [InlineData("archived")][InlineData("inactiveDoctor")][InlineData("unlinked")][InlineData("inactiveWard")][InlineData("unavailableBed")][InlineData("department")][InlineData("token")]
    public async Task InvalidAdmissionsAreRejected(string condition)
    {
        await using var s=await Site.Create();await s.Update(db=>{if(condition=="archived")db.Patients.Single().IsArchived=true;if(condition=="inactiveDoctor")db.Doctors.Single(d=>d.Id==s.DoctorId).IsActive=false;if(condition=="unlinked")db.Doctors.Single(d=>d.Id==s.DoctorId).IdentityUserId=null;if(condition=="inactiveWard")db.Wards.Single().IsActive=false;if(condition=="unavailableBed")db.Beds.Single(b=>b.Id==s.BedId).IsOperational=false;if(condition=="department")db.Departments.Single().IsActive=false;});
        var fields=s.Fields(true);if(condition=="token")fields.Remove("Token");Assert.Equal(HttpStatusCode.OK,(await s.Post("/Admissions/Create","/Admissions/Create",fields)).StatusCode);Assert.Equal(0,await s.Read(db=>db.Admissions.CountAsync()));Assert.Equal(0,await s.Read(db=>db.BedTransfers.CountAsync()));
    }
    [Fact]
    public async Task DuplicateActiveAdmissionAndOccupiedBedAreRejected()
    {
        await using var s=await Site.Create();await s.Post("/Admissions/Create","/Admissions/Create",s.Fields(true));
        var fields=s.Fields(true);fields["BedId"]=s.OtherBed.ToString();Assert.Contains("already has an active admission",await(await s.Post("/Admissions/Create","/Admissions/Create",fields)).Content.ReadAsStringAsync());
        await s.Update(db=>db.Patients.Add(new Patient{PatientNumber="SYN-SECOND",FirstName="Second",LastName="Synthetic"}));var second=await s.Read(db=>db.Patients.Where(p=>p.Id!=s.PatientId).Select(p=>p.Id).SingleAsync());fields["PatientId"]=second.ToString();fields["BedId"]=s.BedId.ToString();Assert.Contains("available operational bed",await(await s.Post("/Admissions/Create","/Admissions/Create",fields)).Content.ReadAsStringAsync());Assert.Equal(1,await s.Read(db=>db.Admissions.CountAsync()));
    }
    [Fact]
    public async Task DoctorScopeDoesNotBroadenEmrOrAllowOtherDoctorAccess()
    {
        await using var s=await Site.Create();await s.Post("/Admissions/Create","/Admissions/Create",s.Fields(true));var id=await s.Read(db=>db.Admissions.Select(a=>a.Id).SingleAsync());
        s.Role("Doctor","other");Assert.DoesNotContain("PAT-STAY",await s.Client.GetStringAsync("/Admissions"));Assert.Equal(HttpStatusCode.NotFound,(await s.Client.GetAsync("/Admissions/Details/"+id)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await s.Client.GetAsync("/Admissions/Clinical/"+id)).StatusCode);
        s.Role("Doctor");Assert.Contains("PAT-STAY",await s.Client.GetStringAsync("/Admissions"));Assert.DoesNotContain("PAT-STAY",await s.Client.GetStringAsync("/MedicalRecords"));
        var a=await s.Read(db=>db.Admissions.SingleAsync());var form=new Dictionary<string,string>{{"ParentVersion",a.Version.ToString()},{"AdmissionReason","Synthetic"},{"MedicalRecordId",Guid.NewGuid().ToString()}};
        Assert.Contains("authorized",await(await s.Post("/Admissions/Clinical/"+id,"/Admissions/Clinical/"+id,form)).Content.ReadAsStringAsync());Assert.Equal(0,await s.Read(db=>db.AdmissionClinicalNotes.CountAsync()));
    }
    [Fact]
    public async Task OutpatientWalkInTransitionsFollowUpPrivacyAndNoImplicitAppointment()
    {
        await using var s=await Site.Create();var fields=s.Fields(false);for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Outpatients/Create","/Outpatients/Create",fields)).StatusCode);
        var v=await s.Read(db=>db.OutpatientVisits.SingleAsync());Assert.Equal(0,await s.Read(db=>db.Appointments.CountAsync()));
        s.Role("Doctor");Assert.Equal(HttpStatusCode.BadRequest,(await s.Post("/Outpatients/Progress/"+v.Id,"/Outpatients/Details/"+v.Id,new(){{"version",v.Version.ToString()},{"command","Complete"}})).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Outpatients/Progress/"+v.Id,"/Outpatients/Details/"+v.Id,new(){{"version",v.Version.ToString()},{"command","Start"}})).StatusCode);v=await s.Read(db=>db.OutpatientVisits.SingleAsync());
        Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Outpatients/Clinical/"+v.Id,"/Outpatients/Clinical/"+v.Id,new(){{"ParentVersion",v.Version.ToString()},{"FollowUpInformation","PRIVATE-FOLLOWUP"},{"FollowUpOn",DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd")}})).StatusCode);
        s.Role("Receptionist");Assert.DoesNotContain("PRIVATE-FOLLOWUP",await s.Client.GetStringAsync("/Outpatients/Details/"+v.Id));Assert.Equal(HttpStatusCode.BadRequest,(await s.Post("/Outpatients/Cancel/"+v.Id,"/Outpatients/Details/"+v.Id,new(){{"version",v.Version.ToString()},{"reason","Synthetic"}})).StatusCode);
        s.Role("Doctor");v=await s.Read(db=>db.OutpatientVisits.SingleAsync());Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Outpatients/Progress/"+v.Id,"/Outpatients/Details/"+v.Id,new(){{"version",v.Version.ToString()},{"command","Complete"}})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await s.Client.GetAsync("/Outpatients/Clinical/"+v.Id)).StatusCode);Assert.Equal(0,await s.Read(db=>db.MedicalRecords.CountAsync()));Assert.Equal(0,await s.Read(db=>db.Appointments.CountAsync()));Assert.Equal(1,await s.Read(db=>db.OutpatientClinicalNotes.CountAsync()));
    }
    [Fact]
    public async Task ReceptionistCancellationIsTerminalAndRetainsVisit()
    {
        await using var s=await Site.Create();await s.Post("/Outpatients/Create","/Outpatients/Create",s.Fields(false));var v=await s.Read(db=>db.OutpatientVisits.SingleAsync());
        Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Outpatients/Cancel/"+v.Id,"/Outpatients/Details/"+v.Id,new(){{"version",v.Version.ToString()},{"reason","Synthetic administrative reason"}})).StatusCode);
        v=await s.Read(db=>db.OutpatientVisits.SingleAsync());Assert.Equal(OutpatientVisitStatus.Cancelled,v.Status);Assert.NotNull(v.CancelledAtUtc);Assert.Equal(1,await s.Read(db=>db.OutpatientVisits.CountAsync()));
        s.Role("Doctor");Assert.Equal(HttpStatusCode.BadRequest,(await s.Post("/Outpatients/Progress/"+v.Id,"/Outpatients/Details/"+v.Id,new(){{"version",v.Version.ToString()},{"command","Start"}})).StatusCode);
    }
    [Fact]
    public async Task MatchingAppointmentIsNotCompletedAndCanOnlyBeRegisteredOnce()
    {
        await using var s=await Site.Create();var appointment=new Appointment{PatientId=s.PatientId,DoctorId=s.DoctorId,BookingToken=Guid.NewGuid(),StartAtUtc=DateTime.UtcNow.AddHours(1),EndAtUtc=DateTime.UtcNow.AddHours(1).AddMinutes(30),DurationMinutes=30};await s.Update(db=>db.Appointments.Add(appointment));
        var fields=s.Fields(false);fields["AppointmentId"]=appointment.Id.ToString();Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Outpatients/Create","/Outpatients/Create",fields)).StatusCode);fields["Token"]=Guid.NewGuid().ToString();Assert.Equal(HttpStatusCode.OK,(await s.Post("/Outpatients/Create","/Outpatients/Create",fields)).StatusCode);
        Assert.Equal(1,await s.Read(db=>db.OutpatientVisits.CountAsync()));Assert.Equal(AppointmentStatus.Scheduled,await s.Read(db=>db.Appointments.Select(a=>a.Status).SingleAsync()));
    }
    [Fact]
    public async Task WardAndBedAdministrationValidateDuplicatesAndOccupiedDeactivation()
    {
        await using var s=await Site.Create();await s.Post("/Admissions/Create","/Admissions/Create",s.Fields(true));s.Role("Administrator");
        var ward=await s.Read(db=>db.Wards.SingleAsync());var fields=new Dictionary<string,string>{{"Code","syn-ward"},{"Name","Synthetic ward"},{"DepartmentId",ward.DepartmentId.ToString()},{"Active","true"}};
        Assert.Contains("already exists",await(await s.Post("/Facilities/Ward","/Facilities/Ward",fields)).Content.ReadAsStringAsync());
        fields["Id"]=ward.Id.ToString();fields["Version"]=ward.Version.ToString();fields["Active"]="false";Assert.Contains("occupants",await(await s.Post("/Facilities/Ward","/Facilities/Ward/"+ward.Id,fields)).Content.ReadAsStringAsync());
        var b=await s.Read(db=>db.Beds.SingleAsync(b=>b.Id==s.BedId));var bedFields=new Dictionary<string,string>{{"Code",b.Number},{"WardId",ward.Id.ToString()},{"Active","true"}};
        Assert.Contains("already exists",await(await s.Post("/Facilities/Bed","/Facilities/Bed?wardId="+ward.Id,bedFields)).Content.ReadAsStringAsync());
        bedFields["Id"]=b.Id.ToString();bedFields["Version"]=b.Version.ToString();bedFields["Active"]="false";Assert.Contains("occupied bed",await(await s.Post("/Facilities/Bed","/Facilities/Bed/"+b.Id,bedFields)).Content.ReadAsStringAsync());
        Assert.DoesNotContain("PAT-STAY",await s.Client.GetStringAsync("/Facilities"));Assert.Equal(HttpStatusCode.Forbidden,(await s.Client.GetAsync("/Admissions")).StatusCode);
    }
    [Theory]
    [InlineData("sameBed")][InlineData("stale")][InlineData("reason")][InlineData("inactive")][InlineData("department")]
    public async Task InvalidTransferLeavesCurrentAssignmentAndHistoryUnchanged(string condition)
    {
        await using var s=await Site.Create();await s.Post("/Admissions/Create","/Admissions/Create",s.Fields(true));var a=await s.Read(db=>db.Admissions.SingleAsync());var target=s.OtherBed;
        if(condition=="inactive")await s.Update(db=>db.Beds.Single(b=>b.Id==target).IsOperational=false);
        if(condition=="department"){var b=new Bed{Number="OTHER",Ward=new Ward{Code="OTHER",Name="Other",Department=new Department{Name="Other",NormalizedName="OTHER"}}};await s.Update(db=>db.Beds.Add(b));target=b.Id;}
        var fields=new Dictionary<string,string>{{"version",(condition=="stale"?Guid.NewGuid():a.Version).ToString()},{"bedId",(condition=="sameBed"?s.BedId:target).ToString()},{"token",Guid.NewGuid().ToString()},{"reason",condition=="reason"?"":"Synthetic reason"}};
        Assert.Equal(HttpStatusCode.Conflict,(await s.Post("/Admissions/Transfer/"+a.Id,"/Admissions/Details/"+a.Id,fields)).StatusCode);
        Assert.Equal(s.BedId,await s.Read(db=>db.BedOccupancies.Where(o=>o.EndedAtUtc==null).Select(o=>o.BedId).SingleAsync()));Assert.Equal(1,await s.Read(db=>db.BedTransfers.CountAsync()));
    }
    [Fact]
    public async Task TransferBetweenWardsRetainsBothWardIdentities()
    {
        await using var s=await Site.Create();var department=await s.Read(db=>db.Departments.Select(d=>d.Id).SingleAsync());var destination=new Bed{Number="CROSS",Ward=new Ward{Code="CROSS",Name="Cross ward",DepartmentId=department}};await s.Update(db=>db.Beds.Add(destination));
        await s.Post("/Admissions/Create","/Admissions/Create",s.Fields(true));var a=await s.Read(db=>db.Admissions.SingleAsync());
        Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Admissions/Transfer/"+a.Id,"/Admissions/Details/"+a.Id,new(){{"version",a.Version.ToString()},{"bedId",destination.Id.ToString()},{"token",Guid.NewGuid().ToString()},{"reason","Synthetic cross-ward transfer"}})).StatusCode);
        var html=await s.Client.GetStringAsync("/Admissions/Details/"+a.Id);Assert.Contains("CROSS",html);Assert.Contains("SYN-WARD",html);Assert.Equal(2,await s.Read(db=>db.BedOccupancies.CountAsync()));
    }
    [Fact]
    public async Task AdministratorCanRegisterWardAndBedWithoutPatientAccess()
    {
        await using var s=await Site.Create();s.Role("Administrator");var department=await s.Read(db=>db.Departments.Select(d=>d.Id).SingleAsync());
        Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Facilities/Ward","/Facilities/Ward",new(){{"Code","new-ward"},{"Name","New synthetic ward"},{"DepartmentId",department.ToString()},{"Active","true"}})).StatusCode);
        var ward=await s.Read(db=>db.Wards.SingleAsync(w=>w.Code=="NEW-WARD"));Assert.Equal(HttpStatusCode.Redirect,(await s.Post("/Facilities/Bed","/Facilities/Bed?wardId="+ward.Id,new(){{"Code","new-bed"},{"WardId",ward.Id.ToString()},{"Active","true"}})).StatusCode);
        Assert.Equal(1,await s.Read(db=>db.Beds.CountAsync(b=>b.WardId==ward.Id&&b.Number=="NEW-BED")));Assert.Equal(2,await s.Read(db=>db.SecurityAuditLogs.CountAsync(a=>a.Action=="WardSaved"||a.Action=="BedSaved")));
        Assert.Equal(HttpStatusCode.Forbidden,(await s.Client.GetAsync("/Outpatients")).StatusCode);
    }
    [Fact]
    public async Task UnrelatedDoctorCannotSeeOrChangeOutpatientClinicalData()
    {
        await using var s=await Site.Create();await s.Post("/Outpatients/Create","/Outpatients/Create",s.Fields(false));var v=await s.Read(db=>db.OutpatientVisits.SingleAsync());s.Role("Doctor","other");
        Assert.DoesNotContain("PAT-STAY",await s.Client.GetStringAsync("/Outpatients"));Assert.Equal(HttpStatusCode.NotFound,(await s.Client.GetAsync("/Outpatients/Details/"+v.Id)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await s.Client.GetAsync("/Outpatients/Clinical/"+v.Id)).StatusCode);
        s.Role("Doctor");Assert.Equal(HttpStatusCode.Forbidden,(await s.Client.GetAsync("/Outpatients/Create")).StatusCode);
    }
    private sealed class Site:IAsyncDisposable
    {
        public IHost Host=null!;public HttpClient Client=null!;private string root="";public Guid PatientId,DoctorId,BedId,OtherBed;private readonly Dictionary<string,string> cookies=new();
        public static async Task<Site> Create()
        {
            var root=Path.Combine(Path.GetTempPath(),"medicare-stay-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var database=Guid.NewGuid().ToString();
            var host=await new HostBuilder().ConfigureWebHost(web=>web.UseTestServer().UseContentRoot(root).UseSetting(WebHostDefaults.ApplicationKey,typeof(AdmissionsController).Assembly.GetName().Name)
                .ConfigureServices(s=>{s.AddControllersWithViews().AddApplicationPart(typeof(AdmissionsController).Assembly);s.AddDbContext<MediCareDbContext>(o=>o.UseInMemoryDatabase(database).ConfigureWarnings(w=>w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));s.AddSingleton(TimeProvider.System);s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,Auth>("Test",_=>{});s.AddAuthorization();})
                .Configure(app=>{app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseEndpoints(e=>e.MapControllerRoute("default","{controller=Admissions}/{action=Index}/{id?}"));})).StartAsync();
            using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();var department=new Department{Name="Synthetic",NormalizedName="SYNTHETIC"};
            var doctor=new Doctor{Department=department,IdentityUserId="doctor",RegistrationNumber="SYN-DOC",FirstName="Synthetic",LastName="Doctor"};var other=new Doctor{Department=department,IdentityUserId="other",RegistrationNumber="OTHER",FirstName="Other",LastName="Doctor"};var patient=new Patient{PatientNumber="PAT-STAY",FirstName="Synthetic",LastName="Patient",PhoneNumber="PRIVATE-CONTACT"};var ward=new Ward{Department=department,Code="SYN-WARD",Name="Synthetic ward"};var bed=new Bed{Ward=ward,Number="01"};var next=new Bed{Ward=ward,Number="02"};db.AddRange(patient,doctor,other,bed,next);await db.SaveChangesAsync();var site=new Site{Host=host,Client=host.GetTestClient(),root=root,PatientId=patient.Id,DoctorId=doctor.Id,BedId=bed.Id,OtherBed=next.Id};site.Role("Receptionist");return site;
        }
        public void Role(string? role,string? user=null){Client.DefaultRequestHeaders.Remove("X-Role");Client.DefaultRequestHeaders.Remove("X-User");if(role!=null)Client.DefaultRequestHeaders.Add("X-Role",role);Client.DefaultRequestHeaders.Add("X-User",user??(role=="Doctor"?"doctor":"staff"));}
        public async Task<T> Read<T>(Func<MediCareDbContext,Task<T>> action){using var scope=Host.Services.CreateScope();return await action(scope.ServiceProvider.GetRequiredService<MediCareDbContext>());}
        public async Task Update(Action<MediCareDbContext> action){using var scope=Host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();action(db);await db.SaveChangesAsync();}
        public Dictionary<string,string> Fields(bool inpatient)=>new(){{"PatientId",PatientId.ToString()},{"DoctorId",DoctorId.ToString()},{"BedId",(inpatient?BedId:Guid.Empty).ToString()},{"Token",Guid.NewGuid().ToString()}};
        public async Task<HttpResponseMessage> Post(string path,string form,Dictionary<string,string> fields)
        {
            var response=await Client.GetAsync(form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);if(response.Headers.TryGetValues("Set-Cookie",out var headers)){foreach(var header in headers){var pair=header.Split(';')[0];var at=pair.IndexOf('=');cookies[pair[..at]]=pair[(at+1)..];}Client.DefaultRequestHeaders.Remove("Cookie");Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(c=>c.Key+"="+c.Value)));}
            var match=Regex.Match(await response.Content.ReadAsStringAsync(),"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");Assert.True(match.Success);return await Client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>(fields){{"__RequestVerificationToken",WebUtility.HtmlDecode(match.Groups[1].Value)}}));
        }
        public async ValueTask DisposeAsync(){Client.Dispose();await Host.StopAsync();Host.Dispose();Directory.Delete(root,true);}
    }
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(!Request.Headers.TryGetValue("X-Role",out var role)?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,Request.Headers["X-User"].ToString()),new Claim(ClaimTypes.Role,role.ToString())},"Test")),"Test")));
    }
}
