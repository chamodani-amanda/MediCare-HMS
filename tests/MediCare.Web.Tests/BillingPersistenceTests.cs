using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class BillingPersistenceTests
{
    [Fact] public void MigrationAddsOnlyFinancialTablesSequencesAndProtectiveObjects()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);var a=db.GetService<IMigrationsAssembly>();var m=a.CreateMigration(a.Migrations.Single(x=>x.Key.EndsWith("_AddBillingAndPayments")).Value,db.Database.ProviderName!);
        Assert.All(m.UpOperations,o=>Assert.True(o is CreateTableOperation or CreateIndexOperation or CreateSequenceOperation or SqlOperation));Assert.Equal(6,m.UpOperations.OfType<CreateTableOperation>().Count());Assert.Equal(2,m.UpOperations.OfType<CreateSequenceOperation>().Count());Assert.All(m.UpOperations.OfType<CreateTableOperation>().SelectMany(t=>t.ForeignKeys),f=>Assert.Equal(ReferentialAction.Restrict,f.OnDelete));
        var sql=m.UpOperations.OfType<SqlOperation>().Single().Sql;foreach(var text in new[]{"FOR UPDATE","DEFERRABLE INITIALLY DEFERRED","Payment exceeds balance","Financial history is immutable","Source already charged for service","Invoice ledger must balance","Exact original payment reversal required","Issued invoice is immutable"})Assert.Contains(text,sql);
        Assert.DoesNotContain("DROP ",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("TRUNCATE",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DELETE FROM",sql,StringComparison.OrdinalIgnoreCase);Assert.Throws<NotSupportedException>(()=>m.DownOperations);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task StaleInvoiceOrCatalogueUpdatesFail(bool invoice)
    {
        var options=new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;await using(var seed=new MediCareDbContext(options)){seed.AddRange(new BillingInvoice(),new BillingService());await seed.SaveChangesAsync();}
        await using var first=new MediCareDbContext(options);await using var stale=new MediCareDbContext(options);object current=invoice?await first.BillingInvoices.SingleAsync():await first.BillingServices.SingleAsync();object old=invoice?await stale.BillingInvoices.SingleAsync():await stale.BillingServices.SingleAsync();first.Entry(current).Property("Version").CurrentValue=Guid.NewGuid();await first.SaveChangesAsync();stale.Entry(old).Property("Version").CurrentValue=Guid.NewGuid();await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
    }
    [Fact] public void OutstandingSummaryAndSourceChoicesTranslateWithoutClinicalPayloads()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var q=db.BillingInvoices.Join(db.Patients,i=>i.PatientId,p=>p.Id,(i,p)=>new{i.Id,i.Total,p.PatientNumber,Paid=db.BillingPayments.Where(x=>x.InvoiceId==i.Id).Sum(x=>(decimal?)(x.ReversesId==null?x.Amount:-x.Amount))??0}).Where(r=>r.Paid<r.Total);Assert.Contains("billing_payments",q.ToQueryString());Assert.DoesNotContain("PhoneNumber",q.ToQueryString());
        var source=db.PharmacyDispenses.Where(p=>p.Prescription.MedicalRecordRevision.MedicalRecord.PatientId==Guid.Empty).Select(p=>new{p.Id,p.OccurredAtUtc}).ToQueryString();Assert.DoesNotContain("Diagnosis",source);Assert.DoesNotContain("Medication",source);Assert.DoesNotContain("Instructions",source);
    }
}
