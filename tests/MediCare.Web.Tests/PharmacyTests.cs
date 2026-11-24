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

public class PharmacyTests
{
    [Theory]
    [InlineData(null,HttpStatusCode.Unauthorized)]
    [InlineData("Administrator",HttpStatusCode.Forbidden)]
    [InlineData("Doctor",HttpStatusCode.Forbidden)]
    [InlineData("Nurse",HttpStatusCode.Forbidden)]
    [InlineData("Receptionist",HttpStatusCode.Forbidden)]
    [InlineData("Laboratory Staff",HttpStatusCode.Forbidden)]
    [InlineData("Accountant",HttpStatusCode.Forbidden)]
    [InlineData("Pharmacist",HttpStatusCode.OK)]
    public async Task EveryPharmacyPageChecksRole(string? role,HttpStatusCode expected)
    {
        await using var site=await Site.Create();site.Role(role);
        foreach(var route in new[]{"Index","Batches","LowStock","History","Dispensings","Dispense/"+site.RxId,"Receive?medicineId="+site.MedicineId,"Adjust/"+site.BatchId})
            Assert.Equal(expected,(await site.Client.GetAsync("/Pharmacy/"+route)).StatusCode);
        Assert.Equal(role is "Administrator" or "Pharmacist"?HttpStatusCode.OK:role==null?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden,(await site.Client.GetAsync("/Medicines")).StatusCode);
    }
    [Theory]
    [InlineData("Receive")][InlineData("Adjust")][InlineData("Dispense")][InlineData("Reverse")]
    public async Task PharmacyMutationsRequireAntiforgery(string action)
    {
        await using var site=await Site.Create();Assert.Equal(HttpStatusCode.BadRequest,(await site.Client.PostAsync("/Pharmacy/"+action,new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(0,await site.Read(db=>db.PharmacyDispenses.CountAsync()));Assert.Equal(0,await site.Read(db=>db.InventoryMovements.CountAsync()));
    }
    [Theory]
    [InlineData("draft")][InlineData("superseded")][InlineData("archived")][InlineData("inactiveDoctor")][InlineData("unlinked")][InlineData("cancelled")][InlineData("inactiveMedicine")][InlineData("legacy")]
    public async Task IneligibleOrdersNeverAppearOrDispense(string condition)
    {
        await using var site=await Site.Create();await site.Update(db=> {
            var p=db.MedicalPrescriptions.Include(p=>p.MedicalRecordRevision).ThenInclude(r=>r.MedicalRecord).ThenInclude(r=>r.Patient).Include(p=>p.MedicalRecordRevision.MedicalRecord.Doctor).Include(p=>p.MedicalRecordRevision.MedicalRecord.Appointment).Include(p=>p.Medicine).Single();
            if(condition=="draft")p.MedicalRecordRevision.FinalizedAtUtc=null;
            if(condition=="superseded")db.MedicalRecordRevisions.Add(new MedicalRecordRevision{MedicalRecordId=p.MedicalRecordRevision.MedicalRecordId,Number=2,AuthorUserId="doctor",AmendmentReason="Synthetic amendment"});
            if(condition=="archived")p.MedicalRecordRevision.MedicalRecord.Patient.IsArchived=true;
            if(condition=="inactiveDoctor")p.MedicalRecordRevision.MedicalRecord.Doctor.IsActive=false;
            if(condition=="unlinked")p.MedicalRecordRevision.MedicalRecord.Doctor.IdentityUserId="different";
            if(condition=="cancelled")p.MedicalRecordRevision.MedicalRecord.Appointment.Status=AppointmentStatus.Cancelled;
            if(condition=="inactiveMedicine")p.Medicine!.IsActive=false;
            if(condition=="legacy"){p.MedicineId=null;p.AuthorizedQuantity=null;p.DispensingUnit=null;p.LineageId=null;}
        });
        Assert.DoesNotContain("PAT-PHARM",await site.Client.GetStringAsync("/Pharmacy"));
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Pharmacy/Dispense/"+site.RxId)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await site.Post("/Pharmacy/Dispense","/Medicines/Create",site.DispenseFields(1))).StatusCode);
    }
    [Fact]
    public async Task MinimalWorklistAndHistoryDoNotExposeClinicalNotesOrContacts()
    {
        await using var site=await Site.Create();
        foreach(var route in new[]{"/Pharmacy","/Pharmacy/Dispensings","/Pharmacy/Dispense/"+site.RxId}) {
            var html=await site.Client.GetStringAsync(route);Assert.DoesNotContain("PRIVATE-CLINICAL",html);Assert.DoesNotContain("PRIVATE-CONTACT",html);
        }
        Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.GetAsync("/MedicalRecords")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await site.Client.GetAsync("/Patients")).StatusCode);
    }
    [Fact]
    public async Task PartialThenFullDispensingPreservesLedgerAndIsIdempotent()
    {
        await using var site=await Site.Create();var fields=site.DispenseFields(4);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,fields)).StatusCode);
        Assert.Equal(1,await site.Read(db=>db.PharmacyDispenses.CountAsync()));Assert.Equal(-4,await site.Read(db=>db.InventoryMovements.SumAsync(m=>m.Quantity)));
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,site.DispenseFields(6))).StatusCode);
        Assert.Contains("exceeds remaining",await(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,site.DispenseFields(1))).Content.ReadAsStringAsync());
        Assert.Equal(10,await site.Read(db=>db.PharmacyDispenses.SumAsync(d=>d.Quantity)));Assert.Equal(2,await site.Read(db=>db.SecurityAuditLogs.CountAsync(a=>a.Action=="PrescriptionDispensed")));
    }
    [Theory]
    [InlineData("expired")][InlineData("empty")][InlineData("tooMuch")][InlineData("unreasonedException")]
    public async Task InvalidStockAndQuantityAreRejectedWithoutWriting(string condition)
    {
        await using var site=await Site.Create();await site.Update(db=>{var b=db.MedicineBatches.Single();if(condition=="expired")b.ExpiresOn=DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));if(condition=="empty")b.AvailableQuantity=0;});
        var fields=site.DispenseFields(condition=="tooMuch"?11:2);if(condition=="unreasonedException")fields["ExceptionBatchId"]=site.BatchId.ToString();
        Assert.Equal(HttpStatusCode.OK,(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,fields)).StatusCode);
        Assert.Equal(0,await site.Read(db=>db.PharmacyDispenses.CountAsync()));Assert.Equal(0,await site.Read(db=>db.InventoryMovements.CountAsync()));
    }
    [Fact]
    public async Task ReversalRequiresReasonAndQuarantineAndRetainsHistory()
    {
        await using var site=await Site.Create();await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,site.DispenseFields(4));var d=await site.Read(db=>db.PharmacyDispenses.SingleAsync());
        var fields=new Dictionary<string,string>{{"id",d.Id.ToString()},{"token",Guid.NewGuid().ToString()},{"reason","Synthetic returned goods"}};
        Assert.Equal(HttpStatusCode.BadRequest,(await site.Post("/Pharmacy/Reverse","/Pharmacy/Dispensings",fields)).StatusCode);
        fields["returnedToQuarantine"]="true";Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Reverse","/Pharmacy/Dispensings",fields)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Reverse","/Pharmacy/Dispensings",fields)).StatusCode);
        Assert.Equal(0,await site.Read(db=>db.PharmacyDispenses.SumAsync(x=>x.Quantity)));Assert.Equal(1,await site.Read(db=>db.InventoryMovements.CountAsync(m=>m.Kind=="QuarantineReturn")));
        Assert.Equal(2,await site.Read(db=>db.PharmacyDispenses.CountAsync()));
    }
    [Fact]
    public async Task AmendmentCopiesKeepPriorDispensingBalance()
    {
        await using var site=await Site.Create();await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,site.DispenseFields(7));
        await site.Update(db=> {
            var p=db.MedicalPrescriptions.Include(p=>p.MedicalRecordRevision).Single();
            db.MedicalRecordRevisions.Add(new MedicalRecordRevision { MedicalRecordId=p.MedicalRecordRevision.MedicalRecordId,AuthorUserId="doctor",Number=2,FinalizedAtUtc=DateTime.UtcNow,AmendmentReason="Synthetic",Prescriptions=new(){new MedicalPrescription {LineageId=p.LineageId,MedicineId=p.MedicineId,AuthorizedQuantity=10,DispensingUnit=p.DispensingUnit,Medication="Synthetic",Dosage="Synthetic",Route="Synthetic",Frequency="Synthetic",Duration="Synthetic"}} });
        });
        var rows=await site.Read(db=>PharmacyRules.Eligible(db).Select(p=>p.Id).ToListAsync());Assert.Single(rows);Assert.NotEqual(site.RxId,rows[0]);
        var fields=site.DispenseFields(4);fields["PrescriptionId"]=rows[0].ToString();Assert.Contains("exceeds remaining",await(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+rows[0],fields)).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await site.Client.GetAsync("/Pharmacy/Dispense/"+site.RxId)).StatusCode);
    }
    [Fact]
    public async Task ReceivingIsIdempotentAndRecordsAuditAndPositiveMovement()
    {
        await using var site=await Site.Create();var fields=new Dictionary<string,string>{{"MedicineId",site.MedicineId.ToString()},{"Token",Guid.NewGuid().ToString()},{"BatchNumber","new-batch"},{"Supplier","Synthetic supplier"},{"ExpiresOn",DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd")},{"Quantity","12.5"},{"Reason","Synthetic delivery"}};
        for(var i=0;i<2;i++)Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Receive","/Pharmacy/Receive?medicineId="+site.MedicineId,fields)).StatusCode);
        Assert.Equal(1,await site.Read(db=>db.InventoryMovements.CountAsync(m=>m.Kind=="Receive")));Assert.Equal(1,await site.Read(db=>db.SecurityAuditLogs.CountAsync(a=>a.Action=="PharmacyStockReceived")));
        Assert.Equal("NEW-BATCH",await site.Read(db=>db.MedicineBatches.Where(b=>b.Id!=site.BatchId).Select(b=>b.BatchNumber).SingleAsync()));
    }
    [Fact]
    public async Task LowStockAndExpiryAlertsUseUnexpiredStock()
    {
        await using var site=await Site.Create();await site.Update(db=>{db.MedicineBatches.Single().ExpiresOn=HospitalTime.Today(TimeProvider.System);db.Medicines.Single().LowStockThreshold=1;});
        Assert.Contains("SYN-MED",await site.Client.GetStringAsync("/Pharmacy/LowStock"));Assert.Contains("SYN-BATCH",await site.Client.GetStringAsync("/Pharmacy/Batches?filter=expired"));Assert.DoesNotContain("SYN-BATCH",await site.Client.GetStringAsync("/Pharmacy/Batches?filter=near"));
    }
    [Fact]
    public async Task MedicineIdentityCannotBeEditedAndCatalogueIsValidated()
    {
        await using var site=await Site.Create();site.Role("Administrator");var m=await site.Read(db=>db.Medicines.SingleAsync());
        var fields=new Dictionary<string,string>{{"Code",m.Code},{"Name","Changed"},{"GenericName",m.GenericName},{"Strength",m.Strength},{"Formulation",m.Formulation},{"DispensingUnit",m.DispensingUnit},{"Version",m.Version.ToString()},{"IsActive","true"},{"LowStockThreshold","2"}};
        Assert.Contains("identity cannot be changed",await(await site.Post("/Medicines/Edit/"+m.Id,"/Medicines/Edit/"+m.Id,fields)).Content.ReadAsStringAsync());
        fields["Name"]=m.Name;Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Medicines/Edit/"+m.Id,"/Medicines/Edit/"+m.Id,fields)).StatusCode);
        Assert.Contains("record changed",await(await site.Post("/Medicines/Edit/"+m.Id,"/Medicines/Edit/"+m.Id,fields)).Content.ReadAsStringAsync());
        fields["Code"]="NEW-MED";fields["Strength"]=" ";Assert.Equal(HttpStatusCode.OK,(await site.Post("/Medicines/Create","/Medicines/Create",fields)).StatusCode);
        Assert.Equal(1,await site.Read(db=>db.Medicines.CountAsync()));
    }

    [Fact]
    public async Task DoctorMustExplicitlyAuthorizeIncreaseAndAmendmentKeepsLineage()
    {
        await using var site=await Site.Create();await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,site.DispenseFields(4));
        var original=await site.Read(db=>db.MedicalPrescriptions.Include(p=>p.MedicalRecordRevision).SingleAsync());site.Role("Doctor");
        var revision=original.MedicalRecordRevision;
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/MedicalRecords/Amend/"+revision.Id,"/MedicalRecords/Details/"+revision.Id,new(){["version"]=revision.Version.ToString(),["reason"]="Synthetic authorized amendment"})).StatusCode);
        var draft=await site.Read(db=>db.MedicalRecordRevisions.Include(r=>r.Prescriptions).SingleAsync(r=>r.Number==2));var copied=Assert.Single(draft.Prescriptions);Assert.Equal(original.LineageId,copied.LineageId);
        var fields=new Dictionary<string,string>{{"Version",draft.Version.ToString()},{"AmendmentReason","Synthetic authorized amendment"},{"Symptoms","Synthetic"},{"Diagnosis","Synthetic"},{"Treatment","Synthetic"},
            {"Prescriptions[0].SourceId",copied.Id.ToString()},{"Prescriptions[0].MedicineId",site.MedicineId.ToString()},{"Prescriptions[0].AuthorizedQuantity","12"},{"Prescriptions[0].DispensingUnit","unit"},{"Prescriptions[0].Medication","Synthetic medicine"},{"Prescriptions[0].Dosage","Synthetic dosage"},{"Prescriptions[0].Route","Synthetic route"},{"Prescriptions[0].Frequency","Synthetic frequency"},{"Prescriptions[0].Duration","Synthetic duration"}};
        Assert.Contains("Explicitly confirm",await(await site.Post("/MedicalRecords/Edit/"+draft.Id,"/MedicalRecords/Edit/"+draft.Id,fields)).Content.ReadAsStringAsync());
        fields["Prescriptions[0].ConfirmAuthorizationChange"]="true";
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/MedicalRecords/Edit/"+draft.Id,"/MedicalRecords/Edit/"+draft.Id,fields)).StatusCode);
        var updated=await site.Read(db=>db.MedicalRecordRevisions.Include(r=>r.Prescriptions).SingleAsync(r=>r.Id==draft.Id));Assert.Equal(original.LineageId,Assert.Single(updated.Prescriptions).LineageId);
        Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/MedicalRecords/Finalize/"+draft.Id,"/MedicalRecords/Details/"+draft.Id,new(){["version"]=updated.Version.ToString()})).StatusCode);
        site.Role("Pharmacist");var finalId=updated.Prescriptions.Single().Id;
        var confirmation=await site.Client.GetStringAsync("/Pharmacy/Dispense/"+finalId);Assert.Contains("Remaining: 8",confirmation);Assert.Contains("PAT-PHARM",confirmation);Assert.DoesNotContain("PRIVATE-CLINICAL",confirmation);
    }
    [Fact]
    public async Task DuplicateCatalogueAndBatchInputsDoNotWriteAnotherRecord()
    {
        await using var site=await Site.Create();var m=await site.Read(db=>db.Medicines.SingleAsync());
        var fields=new Dictionary<string,string>{{"Code",m.Code},{"Name",m.Name},{"GenericName",m.GenericName},{"Strength",m.Strength},{"Formulation",m.Formulation},{"DispensingUnit",m.DispensingUnit},{"LowStockThreshold","0"}};
        Assert.Contains("already exists",await(await site.Post("/Medicines/Create","/Medicines/Create",fields)).Content.ReadAsStringAsync());
        var receipt=new Dictionary<string,string>{{"MedicineId",m.Id.ToString()},{"Token",Guid.NewGuid().ToString()},{"BatchNumber","syn-batch"},{"Supplier","Synthetic supplier"},{"ExpiresOn",DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd")},{"Quantity","1"},{"Reason","Synthetic receipt"}};
        Assert.Contains("already exists",await(await site.Post("/Pharmacy/Receive","/Pharmacy/Receive?medicineId="+m.Id,receipt)).Content.ReadAsStringAsync());Assert.Equal(1,await site.Read(db=>db.Medicines.CountAsync()));Assert.Equal(1,await site.Read(db=>db.MedicineBatches.CountAsync()));Assert.Equal(0,await site.Read(db=>db.InventoryMovements.CountAsync()));
    }
    [Fact]
    public async Task AdjustmentsRequireReasonCurrentVersionAndNonnegativeResult()
    {
        await using var site=await Site.Create();var batch=await site.Read(db=>db.MedicineBatches.SingleAsync());
        var fields=new Dictionary<string,string>{{"BatchId",batch.Id.ToString()},{"Token",Guid.NewGuid().ToString()},{"Version",Guid.NewGuid().ToString()},{"Quantity","-21"},{"Reason","Synthetic reconciliation"}};
        Assert.Equal(HttpStatusCode.OK,(await site.Post("/Pharmacy/Adjust","/Pharmacy/Adjust/"+batch.Id,fields)).StatusCode);Assert.Equal(0,await site.Read(db=>db.InventoryMovements.CountAsync()));
        fields["Version"]=batch.Version.ToString();fields["Quantity"]="-1";fields["Reason"]="";Assert.Equal(HttpStatusCode.OK,(await site.Post("/Pharmacy/Adjust","/Pharmacy/Adjust/"+batch.Id,fields)).StatusCode);
        fields["Reason"]="Synthetic reconciliation";Assert.Equal(HttpStatusCode.Redirect,(await site.Post("/Pharmacy/Adjust","/Pharmacy/Adjust/"+batch.Id,fields)).StatusCode);
        var movement=await site.Read(db=>db.InventoryMovements.SingleAsync());Assert.Equal(-1,movement.Quantity);Assert.Equal(batch.Version,movement.ExpectedBatchVersion);Assert.Equal("Adjust",movement.Kind);
    }
    [Fact]
    public async Task MissingSubmissionTokenCannotIssueStock()
    {
        await using var site=await Site.Create();var fields=site.DispenseFields(1);fields.Remove("Token");
        Assert.Equal(HttpStatusCode.OK,(await site.Post("/Pharmacy/Dispense","/Pharmacy/Dispense/"+site.RxId,fields)).StatusCode);
        Assert.Equal(0,await site.Read(db=>db.PharmacyDispenses.CountAsync()));Assert.Equal(0,await site.Read(db=>db.InventoryMovements.CountAsync()));
    }
    private sealed class Site:IAsyncDisposable
    {
        public IHost Host=null!; public HttpClient Client=null!;public string Root=""; public Guid RxId,MedicineId,BatchId;
        private readonly Dictionary<string,string> cookies=new();
        public static async Task<Site> Create()
        {
            var root=Path.Combine(Path.GetTempPath(),"medicare-pharmacy-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var name=Guid.NewGuid().ToString();
            var host=await new HostBuilder().ConfigureWebHost(web=>web.UseTestServer().UseContentRoot(root).UseSetting(WebHostDefaults.ApplicationKey,typeof(PharmacyController).Assembly.GetName().Name)
                .ConfigureServices(s=>{s.AddControllersWithViews().AddApplicationPart(typeof(PharmacyController).Assembly);s.AddDbContext<MediCareDbContext>(o=>o.UseInMemoryDatabase(name));s.AddSingleton(TimeProvider.System);s.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());s.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions,TestAuth>("Test",_=>{});s.AddAuthorization();})
                .Configure(app=>{app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.UseEndpoints(e=>e.MapControllerRoute("default","{controller=Pharmacy}/{action=Index}/{id?}"));})).StartAsync();
            using var scope=host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();
            var patient=new Patient{PatientNumber="PAT-PHARM",FirstName="Synthetic",LastName="Patient",PhoneNumber="PRIVATE-CONTACT"};var doctor=new Doctor{IdentityUserId="doctor",FirstName="Synthetic",LastName="Doctor",RegistrationNumber="SYN-DOC",Department=new Department{Name="Synthetic",NormalizedName="SYNTHETIC"}};
            var appointment=new Appointment{Patient=patient,Doctor=doctor,BookingToken=Guid.NewGuid(),StartAtUtc=DateTime.UtcNow,EndAtUtc=DateTime.UtcNow.AddMinutes(30),DurationMinutes=30};
            var record=new MedicalRecord{Patient=patient,Doctor=doctor,Appointment=appointment,AuthorUserId="doctor",CreationToken=Guid.NewGuid()};
            var revision=new MedicalRecordRevision{MedicalRecord=record,Number=1,AuthorUserId="doctor",FinalizedAtUtc=DateTime.UtcNow,Symptoms="PRIVATE-CLINICAL",Diagnosis="PRIVATE-CLINICAL",Treatment="PRIVATE-CLINICAL"};
            var medicine=new Medicine{Code="SYN-MED",Name="Synthetic medicine",GenericName="Synthetic generic",Strength="Synthetic strength",Formulation="Synthetic form",DispensingUnit="unit"};
            var rx=new MedicalPrescription{MedicalRecordRevision=revision,Medicine=medicine,LineageId=Guid.NewGuid(),AuthorizedQuantity=10,DispensingUnit="unit",Medication="Synthetic medicine",Dosage="Synthetic dosage",Route="Synthetic route",Frequency="Synthetic frequency",Duration="Synthetic duration"};
            var batch=new MedicineBatch{Medicine=medicine,BatchNumber="SYN-BATCH",Supplier="Synthetic supplier",ExpiresOn=DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),AvailableQuantity=20,ReceivedAtUtc=DateTime.UtcNow};
            db.AddRange(rx,batch);await db.SaveChangesAsync();var site=new Site{Host=host,Client=host.GetTestClient(),Root=root,RxId=rx.Id,MedicineId=medicine.Id,BatchId=batch.Id};site.Role("Pharmacist");return site;
        }
        public void Role(string? role){Client.DefaultRequestHeaders.Remove("X-Test-Role");if(role!=null)Client.DefaultRequestHeaders.Add("X-Test-Role",role);}
        public async Task<T> Read<T>(Func<MediCareDbContext,Task<T>> action){using var scope=Host.Services.CreateScope();return await action(scope.ServiceProvider.GetRequiredService<MediCareDbContext>());}
        public async Task Update(Action<MediCareDbContext> action){using var scope=Host.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<MediCareDbContext>();action(db);await db.SaveChangesAsync();}
        public Dictionary<string,string> DispenseFields(decimal quantity)=>new(){{"PrescriptionId",RxId.ToString()},{"Token",Guid.NewGuid().ToString()},{"Quantity",quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)}};
        public async Task<HttpResponseMessage> Post(string path,string form,Dictionary<string,string> fields)
        {
            var response=await Client.GetAsync(form);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            if(response.Headers.TryGetValues("Set-Cookie",out var headers)){foreach(var header in headers){var pair=header.Split(';')[0];var at=pair.IndexOf('=');cookies[pair[..at]]=pair[(at+1)..];}Client.DefaultRequestHeaders.Remove("Cookie");Client.DefaultRequestHeaders.Add("Cookie",string.Join("; ",cookies.Select(c=>c.Key+"="+c.Value)));}
            var match=Regex.Match(await response.Content.ReadAsStringAsync(),"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");Assert.True(match.Success);
            return await Client.PostAsync(path,new FormUrlEncodedContent(new Dictionary<string,string>(fields){["__RequestVerificationToken"]=WebUtility.HtmlDecode(match.Groups[1].Value)}));
        }
        public async ValueTask DisposeAsync(){Client.Dispose();await Host.StopAsync();Host.Dispose();Directory.Delete(Root,true);}
    }
    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()=>Task.FromResult(!Request.Headers.TryGetValue("X-Test-Role",out var role)?AuthenticateResult.NoResult():AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,role=="Doctor"?"doctor":"pharmacist"),new Claim(ClaimTypes.Role,role.ToString())},"Test")),"Test")));
    }
}
