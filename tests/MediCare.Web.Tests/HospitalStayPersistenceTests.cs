using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class HospitalStayPersistenceTests
{
    [Fact]
    public void StayMigrationIsAdditiveAndArbitratesBedsInPostgreSql()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var a=db.GetService<IMigrationsAssembly>();var key=a.Migrations.Keys.Single(k=>k.EndsWith("_AddInpatientAndOutpatientManagement"));var m=a.CreateMigration(a.Migrations[key],db.Database.ProviderName!);
        Assert.All(m.UpOperations,o=>Assert.True(o is CreateTableOperation or CreateIndexOperation or SqlOperation));Assert.Equal(8,m.UpOperations.OfType<CreateTableOperation>().Count());
        Assert.All(m.UpOperations.OfType<CreateTableOperation>().SelectMany(t=>t.ForeignKeys),f=>Assert.Equal(ReferentialAction.Restrict,f.OnDelete));
        var sql=m.UpOperations.OfType<SqlOperation>().Single().Sql;
        foreach(var text in new[]{"EXCLUDE USING gist","tstzrange","FOR UPDATE","DEFERRABLE INITIALLY DEFERRED","Admission and current bed allocation must balance","Initial bed assignment history required","Discharged admission is immutable","Existing authorized finalized EMR care reference required","Assigned treating doctor clinical author required"})Assert.Contains(text,sql);
        Assert.DoesNotContain("DROP ",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("TRUNCATE",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("DELETE FROM",sql,StringComparison.OrdinalIgnoreCase);
        var indexes=m.UpOperations.OfType<CreateIndexOperation>().ToList();Assert.Contains(indexes,i=>i.Table=="admissions"&&i.IsUnique&&i.Filter=="\"Status\" <> 'Discharged'");
        Assert.Contains(indexes,i=>i.Table=="bed_occupancies"&&i.IsUnique&&i.Columns.SequenceEqual(new[]{"BedId"})&&i.Filter!=null);Assert.Contains(indexes,i=>i.Table=="outpatient_visits"&&i.IsUnique&&i.Columns.SequenceEqual(new[]{"AppointmentId"}));
        Assert.Throws<NotSupportedException>(()=>m.DownOperations);
    }
    [Theory]
    [InlineData("ward")][InlineData("bed")][InlineData("admission")][InlineData("visit")][InlineData("admissionNote")][InlineData("visitNote")]
    public async Task StaleVersionsCannotOverwriteFacilityOrPatientStayChanges(string kind)
    {
        var options=new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using(var seed=new MediCareDbContext(options)){seed.AddRange(new Ward(),new Bed(),new Admission(),new OutpatientVisit(),new AdmissionClinical{AdmissionId=Guid.NewGuid()},new OutpatientClinical{OutpatientVisitId=Guid.NewGuid()});await seed.SaveChangesAsync();}
        await using var first=new MediCareDbContext(options);await using var stale=new MediCareDbContext(options);
        async Task<object> Load(MediCareDbContext db)=>kind switch{"ward"=>await db.Wards.SingleAsync(),"bed"=>await db.Beds.SingleAsync(),"admission"=>await db.Admissions.SingleAsync(),"visit"=>await db.OutpatientVisits.SingleAsync(),"admissionNote"=>await db.AdmissionClinicalNotes.SingleAsync(),_=>await db.OutpatientClinicalNotes.SingleAsync()};
        var current=await Load(first);var old=await Load(stale);first.Entry(current).Property("Version").CurrentValue=Guid.NewGuid();await first.SaveChangesAsync();stale.Entry(old).Property("Version").CurrentValue=Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
    }
    [Fact]
    public void AvailabilityQueryTranslatesWithoutExposingClinicalNotes()
    {
        using var db=new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var sql=db.Beds.Where(b=>b.IsOperational&&b.Ward.IsActive&&b.Ward.Department.IsActive&&!db.BedOccupancies.Any(o=>o.BedId==b.Id&&o.EndedAtUtc==null)).Select(b=>new{b.Id,b.Number}).ToQueryString();
        Assert.Contains("NOT EXISTS",sql);Assert.Contains("EndedAtUtc",sql);Assert.DoesNotContain("admission_clinical_notes",sql);Assert.DoesNotContain("medical_records",sql);
    }
}
