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

public class LaboratoryTests
{
    [Theory]
    [InlineData(null,HttpStatusCode.Unauthorized)]
    [InlineData("Administrator",HttpStatusCode.Forbidden)]
    [InlineData("Receptionist",HttpStatusCode.Forbidden)]
    [InlineData("Accountant",HttpStatusCode.Forbidden)]
    [InlineData("Nurse",HttpStatusCode.Forbidden)]
    [InlineData("Doctor",HttpStatusCode.OK)]
    [InlineData("Laboratory Staff",HttpStatusCode.OK)]
    public async Task ClinicalRoleBoundary(string? role,HttpStatusCode expected)
    {
        await using var site=await Site.Create();site.Role(role);
        Assert.Equal(expected,(await site.Client.GetAsync("/Laboratory")).StatusCode);
        Assert.Equal(role=="Laboratory Staff"?HttpStatusCode.OK:role==null?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,(await site.Client.GetAsync("/Laboratory/Worklist")).StatusCode);
        Assert.Equal(role=="Administrator"?HttpStatusCode.OK:role==null?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,(await site.Client.GetAsync("/LabTests")).StatusCode);
        if(role is not "Doctor" and not "Laboratory Staff")
            foreach(var route in new[]{"Details/","Report/","Create?medicalRecordId="})Assert.Equal(expected,(await site.Client.GetAsync("/Laboratory/"+route+site.EncounterId)).StatusCode);
    }
    [Theory]
    [InlineData("Create","Doctor")]
    [InlineData("Cancel","Doctor")]
    [InlineData("Progress","Laboratory Staff")]
    [InlineData("EnterResult","Laboratory Staff")]
    [InlineData("EditResult","Laboratory Staff")]
    [InlineData("FinalizeResult","Laboratory Staff")]
    [InlineData("Correct","Laboratory Staff")]
    public async Task MutationsRequireAntiforgery(string action,string role)
    {
        await using var site=await Site.Create();site.Role(role);
        Assert.Equal(HttpStatusCode.BadRequest,(await site.Client.PostAsync("/Laboratory/"+action,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(0,await site.Read(db=>db.LabRequests.CountAsync()));
    }
    [Fact]
    public async Task CatalogueManagementValidationVersioningAndPermissions()
    {
        await using var site=await Site.Create();site.Role("Administrator","admin");
        var fields=new Dictionary<string,string>{["Code"]="new-test",["Name"]="Synthetic catalogue",["Category"]="Synthetic",["SampleType"]="Synthetic sample",["ResultKind"]="Text",["IsActive"]="true",["Description"]="Synthetic description"};
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/LabTests/Create","/LabTests/Create",fields)).StatusCode);
        Assert.Contains("already exists",await(await site.Post("/LabTests/Create","/LabTests/Create",fields)).Content.ReadAsStringAsync());
        var test=await site.Read(db=>db.LabTests.SingleAsync(t=>t.Code=="NEW-TEST"));fields["Version"]=test.Version.ToString();fields["Name"]="Changed synthetic catalogue";
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/LabTests/Edit/"+test.Id,"/LabTests/Edit/"+test.Id,fields)).StatusCode);
        Assert.Contains("record changed",await(await site.Post("/LabTests/Edit/"+test.Id,"/LabTests/Edit/"+test.Id,fields)).Content.ReadAsStringAsync());
        fields["Code"]="bad code";Assert.Equal(HttpStatusCode.OK,(await site.Post("/LabTests/Create","/LabTests/Create",fields)).StatusCode);
        Assert.Contains("Changed synthetic catalogue",await site.Client.GetStringAsync("/LabTests?search=NEW-TEST"));
        site.Role("Doctor");Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.PostAsync("/LabTests/Create",new FormUrlEncodedContent(fields))).StatusCode);
        site.Role("Laboratory Staff","lab-a");Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.GetAsync("/LabTests/Create")).StatusCode);
    }
    [Fact]
    public async Task RequestIsIdempotentScopedAndKeepsCatalogueSnapshot()
    {
        await using var site=await Site.Create();var form=site.RequestFields();
        Assert.Equal(HttpStatusCode.Redirect,(await site.CreateRequest(form)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.CreateRequest(form)).StatusCode);
        var request=await site.Request();Assert.Equal(LabRequestStatus.Requested,request.Status);Assert.Equal(1,await site.Read(db=>db.LabRequests.CountAsync()));
        await site.Update(db=>{var test=db.LabTests.Single(t=>t.Id==site.TestId);test.Name="Changed future test";test.Unit="Changed unit";});
        var details=await site.Client.GetStringAsync("/Laboratory/Details/"+request.Id);Assert.Contains("Synthetic numeric test",details);Assert.DoesNotContain("Changed future test",details);
        site.Role("Doctor","doctor-b");Assert.Contains("0 request(s)",await site.Client.GetStringAsync("/Laboratory?search=PAT-LAB-A"));
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync(site.CreatePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Laboratory/Details/"+request.Id)).StatusCode);
        site.Role("Doctor","unlinked");Assert.DoesNotContain("PAT-LAB-A",await site.Client.GetStringAsync("/Laboratory"));
    }
    [Theory]
    [InlineData("InactiveTest")]
    [InlineData("InactiveDoctor")]
    [InlineData("ArchivedPatient")]
    [InlineData("CancelledAppointment")]
    public async Task IneligibleRequestsAreRejected(string condition)
    {
        await using var site=await Site.Create();await site.Update(db=>
        {
            if(condition=="InactiveTest")db.LabTests.Single(t=>t.Id==site.TestId).IsActive=false;
            if(condition=="InactiveDoctor")db.Doctors.Single(d=>d.Id==site.DoctorId).IsActive=false;
            if(condition=="ArchivedPatient")db.Patients.Single(p=>p.Id==site.PatientId).IsArchived=true;
            if(condition=="CancelledAppointment")db.Appointments.Single().Status=AppointmentStatus.Cancelled;
        });
        if(condition=="InactiveTest")Assert.Equal(HttpStatusCode.OK,(await site.CreateRequest(site.RequestFields())).StatusCode);
        else Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync(site.CreatePath)).StatusCode);
        Assert.Equal(0,await site.Read(db=>db.LabRequests.CountAsync()));
    }
    [Fact]
    public async Task SampleWorkflowDraftFinalizationCorrectionReportsAndHistory()
    {
        await using var site=await Site.Create();await site.CreateRequest(site.RequestFields());var request=await site.Request();site.Role("Laboratory Staff","lab-a");
        Assert.Equal(HttpStatusCode.BadRequest,(await site.Progress(request,"Process")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Progress(request,"Collect")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await site.Progress(request,"Collect")).StatusCode);
        request=await site.Request();Assert.Matches("^LAB-[A-F0-9]{32}$",request.SampleIdentifier!);Assert.Equal("lab-a",request.CollectedByUserId);
        Assert.Equal(HttpStatusCode.BadRequest,(await site.Progress(request,"Process")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Progress(request,"Receive")).StatusCode);request=await site.Request();Assert.Equal("lab-a",request.ReceivedByUserId);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Progress(request,"Process")).StatusCode);request=await site.Request();Assert.Equal(LabRequestStatus.InProgress,request.Status);
        var worklist=await site.Client.GetStringAsync("/Laboratory/Worklist");Assert.Contains("PAT-LAB-A",worklist);Assert.DoesNotContain("DO-NOT-EXPOSE",worklist);Assert.DoesNotContain("PRIVATE-CONTACT",worklist);
        Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.GetAsync("/MedicalRecords")).StatusCode);
        var form=new Dictionary<string,string>{["LabRequestId"]=request.Id.ToString(),["CreationToken"]=Guid.NewGuid().ToString(),["NumericValue"]="123.4567",["Comments"]="Synthetic result comment"};
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EnterResult","/Laboratory/EnterResult/"+request.Id,form)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EnterResult","/Laboratory/EnterResult/"+request.Id,form)).StatusCode);
        var result=await site.Result();Assert.Null(result.FinalizedAtUtc);
        site.Role("Doctor");Assert.DoesNotContain("123.4567",await site.Client.GetStringAsync("/Laboratory/Details/"+request.Id));
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Laboratory/Report/"+result.Id)).StatusCode);
        site.Role("Laboratory Staff","lab-b");Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Laboratory/EditResult/"+result.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await site.Post("/Laboratory/FinalizeResult/"+result.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=result.Version.ToString()})).StatusCode);
        site.Role("Laboratory Staff","lab-a");
        form["Version"]=result.Version.ToString();form["NumericValue"]="234.5678";
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EditResult/"+result.Id,"/Laboratory/EditResult/"+result.Id,form)).StatusCode);
        Assert.Contains("record changed",await(await site.Post("/Laboratory/EditResult/"+result.Id,"/Laboratory/EditResult/"+result.Id,form)).Content.ReadAsStringAsync());
        result=await site.Result();Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/FinalizeResult/"+result.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=result.Version.ToString()})).StatusCode);
        result=await site.Result();Assert.NotNull(result.FinalizedAtUtc);Assert.Equal(LabRequestStatus.Completed,(await site.Request()).Status);
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Laboratory/EditResult/"+result.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/Correct/"+result.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=result.Version.ToString(),["reason"]="Synthetic correction"})).StatusCode);
        var revisions=await site.Read(db=>db.LabResultRevisions.OrderBy(r=>r.Number).ToListAsync());Assert.Equal(2,revisions.Count);Assert.Equal(234.5678m,revisions[0].NumericValue);Assert.Null(revisions[1].FinalizedAtUtc);
        var correction=revisions[1];form["Version"]=correction.Version.ToString();form["CorrectionReason"]="Synthetic correction";form["NumericValue"]="345.6789";
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EditResult/"+correction.Id,"/Laboratory/EditResult/"+correction.Id,form)).StatusCode);
        site.Role("Doctor");var detail=await site.Client.GetAsync("/Laboratory/Details/"+request.Id);Assert.Contains("no-store",detail.Headers.CacheControl!.ToString());Assert.DoesNotContain("345.6789",await detail.Content.ReadAsStringAsync());
        var report=await site.Client.GetAsync("/Laboratory/Report/"+result.Id+"?pdf=true");Assert.Equal("application/pdf",report.Content.Headers.ContentType!.MediaType);Assert.Equal("attachment",report.Content.Headers.ContentDisposition!.DispositionType);
        Assert.StartsWith("%PDF-",System.Text.Encoding.ASCII.GetString(await report.Content.ReadAsByteArrayAsync()));Assert.Contains("no-store",report.Headers.CacheControl!.ToString());
        Assert.Contains("234.5678",await site.Client.GetStringAsync("/Laboratory/Report/"+result.Id));
        site.Role("Doctor","doctor-b");Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Laboratory/Report/"+result.Id+"?pdf=true")).StatusCode);
        // A second treating doctor receives finalized history, not the correction draft.
        await site.Update(db=>db.Appointments.Add(new Appointment {PatientId=site.PatientId,DoctorId=site.OtherDoctorId,BookingToken=Guid.NewGuid(),StartAtUtc=DateTime.UtcNow,EndAtUtc=DateTime.UtcNow.AddMinutes(30),DurationMinutes=30}));
        var shared=await site.Client.GetStringAsync("/Laboratory/Details/"+request.Id);Assert.Contains("234.5678",shared);Assert.DoesNotContain("345.6789",shared);
        Assert.Null(await site.Read(db=>db.MedicalRecordRevisions.Select(r=>r.FinalizedAtUtc).SingleAsync()));Assert.Equal(AppointmentStatus.Scheduled,await site.Read(db=>db.Appointments.Where(a=>a.DoctorId==site.DoctorId).Select(a=>a.Status).SingleAsync()));
    }
    [Fact]
    public async Task CancellationIsRestrictedAndRetainsSampleHistory()
    {
        await using var site=await Site.Create();await site.CreateRequest(site.RequestFields());var request=await site.Request();
        site.Role("Laboratory Staff","lab-a");await site.Progress(request,"Collect");request=await site.Request();
        site.Role("Doctor");Assert.Equal(HttpStatusCode.Forbidden,(await site.Post("/Laboratory/Cancel/"+request.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=request.Version.ToString(),["reason"]="Synthetic reason"})).StatusCode);
        site.Role("Laboratory Staff","lab-a");Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/Cancel/"+request.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=request.Version.ToString(),["reason"]="Synthetic cancellation"})).StatusCode);
        var cancelled=await site.Request();Assert.Equal(LabRequestStatus.Cancelled,cancelled.Status);Assert.Equal(request.SampleIdentifier,cancelled.SampleIdentifier);
        Assert.Equal(HttpStatusCode.BadRequest,(await site.Progress(cancelled,"Receive")).StatusCode);
        Assert.Contains("Cancelled",await site.Client.GetStringAsync("/Laboratory?status=Cancelled"));Assert.DoesNotContain("PAT-LAB-A",await site.Client.GetStringAsync("/Laboratory/Worklist"));
    }
    [Fact]
    public async Task NumericFormatAndIncompleteFinalizationAreValidated()
    {
        await using var site=await Site.Create();await site.CreateRequest(site.RequestFields());site.Role("Laboratory Staff","lab-a");var request=await site.Request();
        foreach(var step in new[]{"Collect","Receive","Process"}){await site.Progress(request,step);request=await site.Request();}
        var form=new Dictionary<string,string>{["LabRequestId"]=request.Id.ToString(),["CreationToken"]=Guid.NewGuid().ToString(),["TextValue"]="Wrong format"};
        Assert.Contains("numeric result, not text",await(await site.Post("/Laboratory/EnterResult","/Laboratory/EnterResult/"+request.Id,form)).Content.ReadAsStringAsync());
        form.Remove("TextValue");form["NumericValue"]="0.123456789";Assert.Contains("8 decimal places",await(await site.Post("/Laboratory/EnterResult","/Laboratory/EnterResult/"+request.Id,form)).Content.ReadAsStringAsync());
        form.Remove("NumericValue");Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EnterResult","/Laboratory/EnterResult/"+request.Id,form)).StatusCode);var result=await site.Result();
        Assert.Contains("required before finalization",await(await site.Post("/Laboratory/FinalizeResult/"+result.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=result.Version.ToString()})).Content.ReadAsStringAsync());
        Assert.Equal(LabRequestStatus.InProgress,(await site.Request()).Status);
    }
    [Fact]
    public async Task ListingPaginationAndFilterValidation()
    {
        await using var site=await Site.Create();await site.CreateRequest(site.RequestFields());
        await site.Update(db=>{for(var i=0;i<22;i++)db.LabRequests.Add(new LabRequest {MedicalRecordId=site.EncounterId,LabTestId=site.TestId,RequestedByUserId="doctor-a",CreationToken=Guid.NewGuid(),TestCode="SYN",TestName="Synthetic",RequestedAtUtc=DateTime.UtcNow});});
        Assert.Contains("Page 2 of 2",await site.Client.GetStringAsync("/Laboratory?page=2&search=PAT-LAB-A"));
        foreach(var query in new[]{"date=invalid","status=99","search="+new string('x',101)})Assert.Equal(HttpStatusCode.BadRequest,(await site.Client.GetAsync("/Laboratory?"+query)).StatusCode);
    }

    [Theory]
    [InlineData("Create","Doctor")]
    [InlineData("Edit","Doctor")]
    [InlineData("Create","Laboratory Staff")]
    [InlineData("Edit","Laboratory Staff")]
    public async Task NonAdministratorsCannotMutateCatalogue(string action,string role)
    {
        await using var site=await Site.Create();site.Role(role);Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.PostAsync("/LabTests/"+action,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }
    [Theory]
    [InlineData("Create")]
    [InlineData("Edit")]
    public async Task CatalogueMutationsRequireAntiforgery(string action)
    {
        await using var site=await Site.Create();site.Role("Administrator");Assert.Equal(HttpStatusCode.BadRequest,(await site.Client.PostAsync("/LabTests/"+action,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
    }
    [Fact]
    public async Task TextResultsAndFinalizedCorrectionsRetainPriorValueAndProtectDownloads()
    {
        await using var site=await Site.Create();await site.Update(db=>db.LabTests.Single(t=>t.Id==site.TestId).ResultKind=LabResultKind.Text);
        await site.CreateRequest(site.RequestFields());site.Role("Laboratory Staff","lab-a");var request=await site.Request();foreach(var command in new[]{"Collect","Receive","Process"}){await site.Progress(request,command);request=await site.Request();}
        var fields=new Dictionary<string,string>{["LabRequestId"]=request.Id.ToString(),["CreationToken"]=Guid.NewGuid().ToString(),["TextValue"]="Synthetic original text"};
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EnterResult","/Laboratory/EnterResult/"+request.Id,fields)).StatusCode);var first=await site.Result();
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/FinalizeResult/"+first.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=first.Version.ToString()})).StatusCode);first=await site.Result();
        Assert.Equal(HttpStatusCode.BadRequest,(await site.Post("/Laboratory/Correct/"+first.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=first.Version.ToString(),["reason"]=" "})).StatusCode);
        site.Role("Laboratory Staff","lab-b");Assert.Equal(HttpStatusCode.NotFound,(await site.Post("/Laboratory/Correct/"+first.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=first.Version.ToString(),["reason"]="Synthetic unauthorized correction"})).StatusCode);
        site.Role("Laboratory Staff","lab-a");Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/Correct/"+first.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=first.Version.ToString(),["reason"]="Synthetic reason"})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await site.Post("/Laboratory/Correct/"+first.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=first.Version.ToString(),["reason"]="Synthetic duplicate"})).StatusCode);
        var correction=await site.Read(db=>db.LabResultRevisions.SingleAsync(r=>r.Number==2));fields["Version"]=correction.Version.ToString();fields["CorrectionReason"]="Synthetic reason";fields["TextValue"]="Synthetic corrected \u0dc3\u0dd2\u0d82\u0dc4\u0dbd";
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/EditResult/"+correction.Id,"/Laboratory/EditResult/"+correction.Id,fields)).StatusCode);correction=await site.Read(db=>db.LabResultRevisions.SingleAsync(r=>r.Number==2));
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/FinalizeResult/"+correction.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=correction.Version.ToString()})).StatusCode);
        Assert.Equal("Synthetic original text",await site.Read(db=>db.LabResultRevisions.Where(r=>r.Number==1).Select(r=>r.TextValue).SingleAsync()));
        site.Role("Doctor");Assert.Contains(fields["TextValue"],WebUtility.HtmlDecode(await site.Client.GetStringAsync("/Laboratory/Report/"+correction.Id)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await site.Client.GetAsync("/Laboratory/Report/"+correction.Id+"?pdf=true")).StatusCode);
        foreach(var role in new[]{"Administrator","Receptionist","Accountant","Nurse"}){site.Role(role);Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.GetAsync("/Laboratory/Report/"+first.Id+"?pdf=true")).StatusCode);}
    }
    [Fact]
    public async Task DoctorsCancelOnlyOwnUncollectedRequestsAndLoseAccessAfterCareRevocation()
    {
        await using var site=await Site.Create();await site.CreateRequest(site.RequestFields());var request=await site.Request();
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Laboratory/Cancel/"+request.Id,"/Laboratory/Details/"+request.Id,new(){["version"]=request.Version.ToString(),["reason"]="Synthetic doctor cancellation"})).StatusCode);
        Assert.Equal(LabRequestStatus.Cancelled,(await site.Request()).Status);Assert.Null((await site.Request()).SampleIdentifier);
        await site.Update(db=>db.Appointments.Single().Status=AppointmentStatus.NoShow);
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Laboratory/Details/"+request.Id)).StatusCode);
    }

    private sealed class Site:IAsyncDisposable
    {
        public IHost Host {get;private init;}=null!;public HttpClient Client {get;private init;}=null!;
        public Guid EncounterId {get;private init;} public Guid TestId {get;private init;}public Guid DoctorId {get;private init;}public Guid OtherDoctorId {get;private init;}public Guid PatientId {get;private init;}
        private string Root {get;init;}="";private readonly Dictionary<string,string> cookies=new();private string? token;
        public string CreatePath=>"/Laboratory/Create?medicalRecordId="+EncounterId;
        public static async Task<Site> Create()
        {
            var root=Path.Combine(Path.GetTempPath(),"medicare-lab-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var name=Guid.NewGuid().ToString();
            var host=await new HostBuilder().ConfigureWebHost(web=>web.UseTestServer().UseContentRoot(root).UseSetting(WebHostDefaults.ApplicationKey,typeof(LaboratoryController).Assembly.GetName().Name)
                .ConfigureServices(s=>{s.AddControllersWithViews().AddApplicationPart(typeof(LaboratoryController).Assembly);s.AddDbContext<MediCareDbContext>(o=>o.UseInMemoryDatabase(name));s.AddSingleton(TimeProvider.System);s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,TestAuth>("Test",_=>{});s.AddAuthorization();})
                .Configure(app=>{app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseEndpoints(e=>e.MapControllerRoute("default","{controller=Laboratory}/{action=Index}/{id?}"));})).StartAsync();
            using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var department=new Department {Name="Synthetic",NormalizedName="SYNTHETIC"};var doctor=new Doctor {Department=department,RegistrationNumber="SYN-A",IdentityUserId="doctor-a",FirstName="Synthetic",LastName="Doctor"};
            var other=new Doctor {Department=department,RegistrationNumber="SYN-B",IdentityUserId="doctor-b",FirstName="Other",LastName="Doctor"};var patient=new Patient {PatientNumber="PAT-LAB-A",FirstName="Synthetic",LastName="Patient",PhoneNumber="PRIVATE-CONTACT"};
            var appointment=new Appointment {Patient=patient,Doctor=doctor,BookingToken=Guid.NewGuid(),StartAtUtc=DateTime.UtcNow.AddHours(-1),EndAtUtc=DateTime.UtcNow,DurationMinutes=60};
            var encounter=new MedicalRecord {Patient=patient,Doctor=doctor,Appointment=appointment,AuthorUserId="doctor-a",CreationToken=Guid.NewGuid(),CreatedAtUtc=DateTime.UtcNow};
            var revision=new MedicalRecordRevision {MedicalRecord=encounter,Number=1,AuthorUserId="doctor-a",Symptoms="DO-NOT-EXPOSE-CLINICAL-NOTES",Diagnosis="DO-NOT-EXPOSE-DIAGNOSIS",CreatedAtUtc=DateTime.UtcNow};
            var test=new LabTest {Code="SYN-NUM",Name="Synthetic numeric test",Category="Synthetic",SampleType="Synthetic sample",ResultKind=LabResultKind.Numeric,Unit="Synthetic unit",ReferenceRange="Configured synthetic range",CreatedAtUtc=DateTime.UtcNow,UpdatedAtUtc=DateTime.UtcNow};
            db.AddRange(revision,other,test);await db.SaveChangesAsync();var site=new Site {Host=host,Client=host.GetTestClient(),Root=root,EncounterId=encounter.Id,TestId=test.Id,DoctorId=doctor.Id,OtherDoctorId=other.Id,PatientId=patient.Id};site.Role("Doctor");return site;
        }
        public void Role(string? role,string user="doctor-a"){Client.DefaultRequestHeaders.Remove("X-Test-Role");Client.DefaultRequestHeaders.Remove("X-Test-User");if(role!=null)Client.DefaultRequestHeaders.Add("X-Test-Role",role);Client.DefaultRequestHeaders.Add("X-Test-User",user);}
        public async Task<T> Read<T>(Func<MediCareDbContext,Task<T>> action){using var scope=Host.Services.CreateScope();return await action(scope.ServiceProvider.GetRequiredService<MediCareDbContext>());}
        public async Task Update(Action<MediCareDbContext> action){using var scope=Host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();action(db);await db.SaveChangesAsync();}
        public Task<LabRequest> Request()=>Read(db=>db.LabRequests.AsNoTracking().SingleAsync());public Task<LabResultRevision> Result()=>Read(db=>db.LabResultRevisions.AsNoTracking().SingleAsync());
        public Dictionary<string,string> RequestFields()=>new(){["MedicalRecordId"]=EncounterId.ToString(),["LabTestId"]=TestId.ToString(),["CreationToken"]=Guid.NewGuid().ToString()};
        public Task<HttpResponseMessage> CreateRequest(Dictionary<string,string> fields)=>Post("/Laboratory/Create",CreatePath,fields);
        public Task<HttpResponseMessage> Progress(LabRequest r,string command)=>Post("/Laboratory/Progress/"+r.Id,"/Laboratory/Details/"+r.Id,new(){["version"]=r.Version.ToString(),["command"]=command});
        public async Task<HttpResponseMessage> Post(string path,string form,Dictionary<string,string> fields)
        {
            var response=await Client.GetAsync(form);if(response.StatusCode==HttpStatusCode.Redirect)response=await Client.GetAsync(response.Headers.Location);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            if(response.Headers.TryGetValues("Set-Cookie",out var headers)){foreach(var header in headers){var pair=header.Split(';')[0];var at=pair.IndexOf('=');cookies[pair[..at]]=pair[(at+1)..];}Client.DefaultRequestHeaders.Remove("Cookie");Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(c=>c.Key+"="+c.Value)));}
            var match=Regex.Match(await response.Content.ReadAsStringAsync(),"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");if(match.Success)token=WebUtility.HtmlDecode(match.Groups[1].Value);Assert.NotNull(token);
            return await Client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>(fields){["__RequestVerificationToken"]=token}));
        }
        public async ValueTask DisposeAsync(){Client.Dispose();await Host.StopAsync();Host.Dispose();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(!Request.Headers.TryGetValue("X-Test-Role",out var role)?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,Request.Headers["X-Test-User"].ToString()),new Claim(ClaimTypes.Role,role.ToString())},"Test")),"Test")));
    }
}
