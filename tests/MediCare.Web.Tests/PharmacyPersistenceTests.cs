using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class PharmacyPersistenceTests
{
    [Theory]
    [InlineData("0",false)][InlineData("-1",false)][InlineData("0.001",true)][InlineData("1.123",true)][InlineData("1.1234",false)][InlineData("1000000000",true)][InlineData("1000000001",false)]
    public void DispensingQuantityMustBePositiveBoundedAndExactlyRepresentable(string value,bool expected) => Assert.Equal(expected,PharmacyRules.ValidQuantity(decimal.Parse(value,System.Globalization.CultureInfo.InvariantCulture)));
    [Theory]
    [InlineData(-1,false)][InlineData(0,false)][InlineData(1,true)]
    public void ExpiryDateItselfIsUnavailable(int days,bool usable)
    {
        var today=new DateOnly(2026,11,19);var batch=new MedicineBatch{Medicine=new Medicine(),AvailableQuantity=10,ExpiresOn=today.AddDays(days)};
        Assert.Equal(usable,PharmacyRules.Usable(batch,today));batch.Medicine.IsActive=false;Assert.False(PharmacyRules.Usable(batch,today));
    }
    [Fact]
    public void MigrationAddsOnlyExpectedSchemaAndAppendOnlyStockGuards()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var assembly=db.GetService<IMigrationsAssembly>();var key=assembly.Migrations.Keys.Single(k=>k.EndsWith("_AddPharmacyManagement"));var migration=assembly.CreateMigration(assembly.Migrations[key],db.Database.ProviderName!);
        Assert.All(migration.UpOperations,o=>Assert.True(o is CreateTableOperation or CreateIndexOperation or AddColumnOperation or AddCheckConstraintOperation or AddForeignKeyOperation or SqlOperation));
        Assert.Equal(new[]{"inventory_movements","medicine_batches","medicines","pharmacy_dispenses"},migration.UpOperations.OfType<CreateTableOperation>().Select(t=>t.Name).OrderBy(n=>n).ToArray());
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(),c=>{Assert.Equal("medical_prescriptions",c.Table);Assert.True(c.IsNullable);});
        Assert.All(migration.UpOperations.OfType<CreateTableOperation>().SelectMany(t=>t.ForeignKeys),fk=>Assert.Equal(ReferentialAction.Restrict,fk.OnDelete));
        var guards=migration.UpOperations.OfType<SqlOperation>().Single().Sql;
        foreach(var expected in new[]{"FOR UPDATE","DEFERRABLE INITIALLY DEFERRED","Stock requires a ledger entry","remaining authorization","QuarantineReturn","FEFO exception","Pharmacist role required","Prescription lineage cannot change patient or medicine"})Assert.Contains(expected,guards);
        Assert.DoesNotContain("DROP ",guards,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("TRUNCATE",guards,StringComparison.OrdinalIgnoreCase);
        Assert.Throws<NotSupportedException>(()=>migration.DownOperations);
    }
    [Fact]
    public void EligibilityAndRemainingBalanceQueriesTranslateToPostgreSql()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var sql=PharmacyRules.Eligible(db).Select(p=>new{p.Id,Remaining=p.AuthorizedQuantity-(db.PharmacyDispenses.Where(d=>d.LineageId==p.LineageId).Sum(d=>(decimal?)d.Quantity)??0)}).ToQueryString();
        foreach(var expected in new[]{"FinalizedAtUtc","IsArchived","IsActive","IdentityUserId","AuthorUserId","Scheduled","Completed","LineageId","NOT EXISTS"})Assert.Contains(expected,sql);
        Assert.DoesNotContain("Symptoms",sql);Assert.DoesNotContain("Diagnosis",sql);
    }
    [Theory]
    [InlineData("medicine")][InlineData("batch")]
    public async Task StaleInventoryAndCatalogueEditsAreRejectedByEf(string entity)
    {
        var options=new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using(var seed=new MediCareDbContext(options)){seed.Add(new MedicineBatch{Medicine=new Medicine{Code="SYN"},BatchNumber="SYN"});await seed.SaveChangesAsync();}
        await using var a=new MediCareDbContext(options);await using var b=new MediCareDbContext(options);
        if(entity=="medicine"){var first=await a.Medicines.SingleAsync();var stale=await b.Medicines.SingleAsync();first.Version=Guid.NewGuid();await a.SaveChangesAsync();stale.Version=Guid.NewGuid();}
        else{var first=await a.MedicineBatches.SingleAsync();var stale=await b.MedicineBatches.SingleAsync();first.Version=Guid.NewGuid();await a.SaveChangesAsync();stale.Version=Guid.NewGuid();}
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>b.SaveChangesAsync());
    }
}
