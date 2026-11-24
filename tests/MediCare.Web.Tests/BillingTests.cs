using System.Security.Claims;
using MediCare.Web.Controllers;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class BillingTests
{
    [Theory]
    [InlineData("0",true)][InlineData("0.01",true)][InlineData("999999999.99",true)][InlineData("-1",false)][InlineData("0.001",false)][InlineData("1000000000",false)]
    public void MonetaryPrecisionAndRangeAreEnforced(string value,bool expected)=>Assert.Equal(expected,BillingRules.Money(decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture)));
    [Theory]
    [InlineData("0",false)][InlineData("-1",false)][InlineData("0.001",true)][InlineData("1.2345",false)][InlineData("999999",true)][InlineData("1000000",false)]
    public void QuantityPrecisionAndRangeAreEnforced(string value,bool expected)=>Assert.Equal(expected,BillingRules.Quantity(decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture)));
    [Fact] public void LineRoundingIsDecimalAndAwayFromZero(){Assert.Equal(.01m,BillingRules.LineTotal(.5m,.01m));Assert.Equal(3.34m,BillingRules.LineTotal(1.005m,3.32m));}
    [Fact] public void FinancialPermissionsAreSeparateFromClinicalRoles()
    {
        Assert.Equal("Accountant",typeof(BillingController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.Equal("Administrator",typeof(BillingCatalogueController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles);
        foreach(var type in new[]{typeof(BillingController),typeof(BillingCatalogueController)})foreach(var m in type.GetMethods().Where(m=>m.IsDefined(typeof(HttpPostAttribute),true)))Assert.True(m.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true),m.Name);
    }
    [Fact] public async Task DraftIssuePartialFullReverseVoidAndReplaceRetainHistory()
    {
        await using var f=new Fixture();await f.Seed();var charge=await f.Charge();var invoice=await f.Draft(charge.Id);Assert.Equal(100m,invoice.Total);
        Assert.IsType<RedirectToActionResult>(await f.Controller().Issue(invoice.Id,invoice.Version));invoice=await f.Invoice();
        var token=Guid.NewGuid();var payment=new PaymentForm{InvoiceId=invoice.Id,Version=invoice.Version,Token=token,Amount=40};Assert.IsType<RedirectToActionResult>(await f.Controller().Pay(payment));Assert.IsType<RedirectToActionResult>(await f.Controller().Pay(payment));Assert.Single(await f.Db.BillingPayments.ToListAsync());
        payment.Amount=41;Assert.IsType<ConflictObjectResult>(await f.Controller().Pay(payment));
        invoice=await f.Invoice();Assert.Equal("PartiallyPaid",BillingRules.PaymentStatus(invoice,BillingRules.NetPaid(await f.Db.BillingPayments.ToListAsync())));
        Assert.IsType<RedirectToActionResult>(await f.Controller().Pay(new PaymentForm{InvoiceId=invoice.Id,Version=invoice.Version,Token=Guid.NewGuid(),Amount=60,Method="BankTransfer",BankReference="syn-ref"}));
        invoice=await f.Invoice();Assert.Equal("Paid",BillingRules.PaymentStatus(invoice,BillingRules.NetPaid(await f.Db.BillingPayments.ToListAsync())));
        Assert.IsType<ConflictObjectResult>(await f.Controller().Void(invoice.Id,invoice.Version,"Correction"));
        foreach(var p in await f.Db.BillingPayments.Where(p=>p.ReversesId==null).ToListAsync()){invoice=await f.Invoice();Assert.IsType<RedirectToActionResult>(await f.Controller().Reverse(p.Id,Guid.NewGuid(),invoice.Version,"Synthetic correction"));}
        invoice=await f.Invoice();Assert.Equal(0m,BillingRules.NetPaid(await f.Db.BillingPayments.ToListAsync()));Assert.IsType<RedirectToActionResult>(await f.Controller().Void(invoice.Id,invoice.Version,"Replace incorrect invoice"));
        Assert.Equal(InvoiceState.Voided,(await f.Invoice()).State);Assert.Equal(4,await f.Db.BillingPayments.CountAsync());
        Assert.IsType<RedirectToActionResult>(await f.Controller().Draft(new InvoiceForm{Token=Guid.NewGuid(),PatientId=f.Patient.Id,ChargeIds=new(){charge.Id}}));Assert.Equal(2,await f.Db.BillingInvoices.CountAsync());Assert.Equal(2,await f.Db.BillingInvoiceLines.CountAsync());
    }
    [Theory]
    [InlineData("negative")][InlineData("precision")][InlineData("overpay")][InlineData("draft")][InlineData("method")][InlineData("bankReference")][InlineData("stale")][InlineData("token")]
    public async Task InvalidPaymentsNeverCreateLedgerOrAuditEntries(string condition)
    {
        await using var f=new Fixture();await f.Seed();var invoice=await f.Draft((await f.Charge()).Id);if(condition!="draft")await f.Controller().Issue(invoice.Id,invoice.Version);invoice=await f.Invoice();var before=await f.Db.SecurityAuditLogs.CountAsync();
        var form=new PaymentForm{InvoiceId=invoice.Id,Token=Guid.NewGuid(),Version=invoice.Version,Amount=50};switch(condition){case "negative":form.Amount=-1;break;case "precision":form.Amount=.001m;break;case "overpay":form.Amount=101;break;case "method":form.Method="Card";break;case "bankReference":form.Method="BankTransfer";break;case "stale":form.Version=Guid.NewGuid();break;case "token":form.Token=Guid.Empty;break;}
        Assert.IsType<ViewResult>(await f.Controller().Pay(form));Assert.Empty(await f.Db.BillingPayments.ToListAsync());Assert.Equal(before,await f.Db.SecurityAuditLogs.CountAsync());
    }
    [Theory]
    [InlineData("duplicate")][InlineData("wrongPatient")][InlineData("inactive")][InlineData("noPrice")][InlineData("quantity")][InlineData("sourceMismatch")]
    public async Task ChargesValidateConfiguredPricesPatientSourceAndDuplicates(string condition)
    {
        await using var f=new Fixture();await f.Seed();var appt=new Appointment{PatientId=f.Patient.Id,DoctorId=Guid.NewGuid()};f.Db.Add(appt);await f.Db.SaveChangesAsync();
        var form=new ChargeForm{Token=Guid.NewGuid(),PatientId=f.Patient.Id,ServiceId=f.Service.Id,Quantity=1,SourceKind="Appointment",SourceId=appt.Id};
        switch(condition){case "duplicate":Assert.IsType<RedirectToActionResult>(await f.Controller().Charge(form));form.Token=Guid.NewGuid();break;case "wrongPatient":form.PatientId=Guid.NewGuid();break;case "inactive":f.Service.IsActive=false;break;case "noPrice":f.Db.BillingPrices.Remove(f.Price);break;case "quantity":form.Quantity=0;break;case "sourceMismatch":form.SourceId=Guid.NewGuid();break;}
        await f.Db.SaveChangesAsync();Assert.IsType<ViewResult>(await f.Controller().Charge(form));Assert.Equal(condition=="duplicate"?1:0,await f.Db.PatientCharges.CountAsync());
    }
    [Fact] public async Task ChargeIdempotencyAndPriceSnapshotsSurviveCatalogueChanges()
    {
        await using var f=new Fixture();await f.Seed();var charge=await f.Charge();f.Service.Name="Updated catalogue";f.Price.UnitPrice=200;await f.Db.SaveChangesAsync();Assert.Equal(100,charge.UnitPrice);Assert.DoesNotContain("Updated",charge.Description);
        Assert.IsType<RedirectToActionResult>(await f.Controller().Charge(new ChargeForm{Token=charge.Token,PatientId=f.Patient.Id,ServiceId=f.Service.Id,Quantity=1}));Assert.Single(await f.Db.PatientCharges.ToListAsync());
    }
    [Fact] public async Task IssuedInvoiceCannotBeEditedOrIssuedTwice()
    {
        await using var f=new Fixture();await f.Seed();var i=await f.Draft((await f.Charge()).Id);await f.Controller().Issue(i.Id,i.Version);i=await f.Invoice();Assert.IsType<ConflictObjectResult>(await f.Controller().Issue(i.Id,i.Version));Assert.IsType<ConflictObjectResult>(await f.Controller().Draft(new InvoiceForm{Id=i.Id,Token=i.Token,PatientId=i.PatientId,Version=i.Version}));
    }
    [Fact] public async Task BankReferencesAndFullReversalsCannotBeDuplicated()
    {
        await using var f=new Fixture();await f.Seed();var invoice=await f.Draft((await f.Charge()).Id);await f.Controller().Issue(invoice.Id,invoice.Version);invoice=await f.Invoice();
        Assert.IsType<RedirectToActionResult>(await f.Controller().Pay(new PaymentForm{InvoiceId=invoice.Id,Version=invoice.Version,Token=Guid.NewGuid(),Amount=20,Method="BankTransfer",BankReference=" ref-1 "}));invoice=await f.Invoice();
        Assert.IsType<ViewResult>(await f.Controller().Pay(new PaymentForm{InvoiceId=invoice.Id,Version=invoice.Version,Token=Guid.NewGuid(),Amount=20,Method="BankTransfer",BankReference="REF-1"}));var p=await f.Db.BillingPayments.SingleAsync();
        Assert.IsType<ConflictObjectResult>(await f.Controller().Reverse(p.Id,Guid.NewGuid(),invoice.Version," "));var token=Guid.NewGuid();Assert.IsType<RedirectToActionResult>(await f.Controller().Reverse(p.Id,token,invoice.Version,"Correction"));Assert.IsType<RedirectToActionResult>(await f.Controller().Reverse(p.Id,token,invoice.Version,"Correction"));invoice=await f.Invoice();Assert.IsType<ConflictObjectResult>(await f.Controller().Reverse(p.Id,Guid.NewGuid(),invoice.Version,"Again"));
        var reversal=await f.Db.BillingPayments.SingleAsync(x=>x.ReversesId==p.Id);Assert.IsType<ConflictObjectResult>(await f.Controller().Reverse(reversal.Id,Guid.NewGuid(),invoice.Version,"Reverse reversal"));Assert.Equal(2,await f.Db.BillingPayments.CountAsync());
    }
    [Theory][InlineData("Appointment")][InlineData("Laboratory")][InlineData("Pharmacy")][InlineData("Admission")][InlineData("Outpatient")]
    public async Task EverySourceTypeMatchesPatientAndPreventsDuplicateServiceCharges(string kind)
    {
        await using var f=new Fixture();await f.Seed();var record=new MedicalRecord{PatientId=f.Patient.Id};var revision=new MedicalRecordRevision{MedicalRecord=record};var rx=new MedicalPrescription{MedicalRecordRevision=revision};var appt=new Appointment{PatientId=f.Patient.Id};var lab=new LabRequest{MedicalRecord=record};var dispense=new PharmacyDispense{Prescription=rx};var admission=new Admission{PatientId=f.Patient.Id};var visit=new OutpatientVisit{PatientId=f.Patient.Id};f.Db.AddRange(appt,lab,dispense,admission,visit);await f.Db.SaveChangesAsync();
        var source=kind switch{"Appointment"=>appt.Id,"Laboratory"=>lab.Id,"Pharmacy"=>dispense.Id,"Admission"=>admission.Id,_=>visit.Id};
        var form=new ChargeForm{Token=Guid.NewGuid(),PatientId=f.Patient.Id,ServiceId=f.Service.Id,Quantity=1,SourceKind=kind,SourceId=source};Assert.IsType<RedirectToActionResult>(await f.Controller().Charge(form));form.Token=Guid.NewGuid();Assert.IsType<ViewResult>(await f.Controller().Charge(form));Assert.Single(await f.Db.PatientCharges.ToListAsync());
    }
    [Fact] public void PdfPreservesLineTotalsAndRejectsUnsupportedScripts()
    {
        var detail=new InvoiceDetails{PatientName="Synthetic Patient",PatientNumber="SYN-1",Paid=40,Invoice=new BillingInvoice{Number=1,State=InvoiceState.Issued,Total=100,IssuedAtUtc=DateTime.UtcNow,Lines=new(){new BillingInvoiceLine{Charge=new PatientCharge{Description="Configured service",Quantity=2,UnitPrice=50,Total=100}}}}};
        Assert.Contains(BillingPdf.Text(detail,null),s=>s.Contains("2 x 50.00 LKR = 100.00 LKR"));Assert.Contains(BillingPdf.Text(detail,null),s=>s=="Outstanding: 60.00 LKR");Assert.StartsWith("%PDF",System.Text.Encoding.ASCII.GetString(BillingPdf.Render(detail,null)));detail.PatientName="සිංහල";Assert.False(BillingPdf.Supports(detail,null));Assert.Throws<ArgumentException>(()=>BillingPdf.Render(detail,null));
    }
    [Fact] public async Task EmptyDraftCannotIssueAndChargeCannotBeInTwoLiveInvoices()
    {
        await using var f=new Fixture();await f.Seed();Assert.IsType<RedirectToActionResult>(await f.Controller().Draft(new InvoiceForm{Token=Guid.NewGuid(),PatientId=f.Patient.Id}));var empty=await f.Invoice();Assert.IsType<ConflictObjectResult>(await f.Controller().Issue(empty.Id,empty.Version));
        var charge=await f.Charge();await f.Draft(charge.Id);Assert.IsType<ViewResult>(await f.Controller().Draft(new InvoiceForm{Token=Guid.NewGuid(),PatientId=f.Patient.Id,ChargeIds=new(){charge.Id}}));Assert.Equal(2,await f.Db.BillingInvoices.CountAsync());
    }
    [Fact] public void InvoiceAndReceiptPdfsContainOnlyFinancialProjection()
    {
        var d=new InvoiceDetails{Invoice=new BillingInvoice{Number=12,State=InvoiceState.Issued,Total=100,IssuedAtUtc=DateTime.UtcNow},PatientNumber="SYN",PatientName="Synthetic Patient",Paid=30};var p=new BillingPayment{Number=4,Amount=30,RecordedAtUtc=DateTime.UtcNow,ActorUserId="synthetic"};d.Payments.Add(p);
        foreach(var payment in new BillingPayment?[]{null,p}){var bytes=BillingPdf.Render(d,payment);Assert.True(bytes.Length>1000);Assert.Equal("%PDF",System.Text.Encoding.ASCII.GetString(bytes,0,4));}
        var text=string.Join("\n",BillingPdf.Text(d,null));Assert.Contains("100.00 LKR",text);Assert.Contains("70.00 LKR",text);Assert.DoesNotContain("Diagnosis",text);
        d.PatientName="සිංහල";Assert.False(BillingPdf.Supports(d,null));Assert.Throws<ArgumentException>(()=>BillingPdf.Render(d,null));
    }
    [Fact] public async Task ProviderConcurrencyRejectsStaleInvoiceAndCatalogueUpdates()
    {
        var options=new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;await using var a=new MediCareDbContext(options);var i=new BillingInvoice();var s=new BillingService();a.AddRange(i,s);await a.SaveChangesAsync();await using var stale=new MediCareDbContext(options);var old=await stale.BillingInvoices.SingleAsync();i.Version=Guid.NewGuid();await a.SaveChangesAsync();old.Version=Guid.NewGuid();await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
    }
    [Fact] public void BillingQueriesProjectOnlyNecessaryPatientInformation()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var sql=db.PatientCharges.Join(db.Patients,c=>c.PatientId,p=>p.Id,(c,p)=>new{c.Total,p.PatientNumber,p.FirstName,p.LastName}).ToQueryString();Assert.DoesNotContain("PhoneNumber",sql);Assert.DoesNotContain("medical_records",sql);Assert.DoesNotContain("Diagnosis",sql);
    }
    private sealed class Fixture:IAsyncDisposable
    {
        public MediCareDbContext Db=new(new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        public Patient Patient=new(){PatientNumber="SYN-BILL",FirstName="Synthetic",LastName="Patient",PhoneNumber="PRIVATE-CONTACT"};public BillingService Service=new(){Code="SYN",Name="Configured service",Category="Other"};public BillingPrice Price=null!;
        public async Task Seed(){Price=new(){Service=Service,UnitPrice=100,EffectiveAtUtc=DateTime.UtcNow.AddDays(-1),CreatedAtUtc=DateTime.UtcNow.AddDays(-1),ActorUserId="admin"};Db.AddRange(Patient,Price);await Db.SaveChangesAsync();}
        public BillingController Controller()=>new(Db,TimeProvider.System){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,"accountant"),new Claim(ClaimTypes.Role,"Accountant")},"Test"))}}};
        public async Task<PatientCharge> Charge(){Assert.IsType<RedirectToActionResult>(await Controller().Charge(new ChargeForm{Token=Guid.NewGuid(),PatientId=Patient.Id,ServiceId=Service.Id,Quantity=1}));return await Db.PatientCharges.OrderByDescending(c=>c.CreatedAtUtc).FirstAsync();}
        public async Task<BillingInvoice> Draft(Guid charge){Assert.IsType<RedirectToActionResult>(await Controller().Draft(new InvoiceForm{Token=Guid.NewGuid(),PatientId=Patient.Id,ChargeIds=new(){charge}}));return await Db.BillingInvoices.OrderByDescending(i=>i.CreatedAtUtc).FirstAsync();}
        public Task<BillingInvoice> Invoice()=>Db.BillingInvoices.OrderByDescending(i=>i.CreatedAtUtc).FirstAsync();
        public ValueTask DisposeAsync()=>Db.DisposeAsync();
    }
}
