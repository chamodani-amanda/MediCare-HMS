using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class LaboratoryPersistenceTests
{
    [Theory]
    [InlineData(LabResultKind.Numeric,null,null,true,false)]
    [InlineData(LabResultKind.Numeric,"1.12345678",null,true,true)]
    [InlineData(LabResultKind.Numeric,"1.123456789",null,false,false)]
    [InlineData(LabResultKind.Numeric,"-5.25",null,true,true)]
    [InlineData(LabResultKind.Numeric,null,"text",false,false)]
    [InlineData(LabResultKind.Text,"1",null,false,false)]
    [InlineData(LabResultKind.Text,null," ",true,false)]
    [InlineData(LabResultKind.Text,null,"Synthetic text",true,true)]
    [InlineData(LabResultKind.Text,null,null,false,true)]
    [InlineData((LabResultKind)99,null,null,false,false)]
    public void FormatsAndFinalizationAreValidated(LabResultKind kind,string? number,string? text,bool finalizing,bool valid)
    {
        var numeric=number==null?(decimal?)null:decimal.Parse(number,System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(valid,LaboratoryRules.ValidateResult(kind,numeric,text,finalizing)==null);
    }
    [Theory]
    [InlineData(LabRequestStatus.Requested,true,false,false,true)]
    [InlineData(LabRequestStatus.SampleCollected,false,true,false,true)]
    [InlineData(LabRequestStatus.InProgress,false,false,false,true)]
    [InlineData(LabRequestStatus.Completed,false,false,false,false)]
    [InlineData(LabRequestStatus.Cancelled,false,false,false,false)]
    public void WorkflowTransitions(LabRequestStatus status,bool collect,bool receive,bool process,bool cancel)
    {
        var request=new LabRequest {Status=status};Assert.Equal(collect,LaboratoryRules.MayCollect(request));Assert.Equal(receive,LaboratoryRules.MayReceive(request));Assert.Equal(process,LaboratoryRules.MayProcess(request));Assert.Equal(cancel,LaboratoryRules.MayCancel(request));
        if(status==LabRequestStatus.SampleCollected){request.ReceivedAtUtc=DateTime.UtcNow;Assert.True(LaboratoryRules.MayProcess(request));Assert.False(LaboratoryRules.MayReceive(request));}
    }
    [Fact]
    public void MigrationIsAdditiveWithImmutableResultsAndDeferredCompletionChecks()
    {
        using var db=Postgres();var assembly=db.GetService<IMigrationsAssembly>();var key=assembly.Migrations.Keys.Single(k=>k.EndsWith("_AddLaboratoryManagement"));var migration=assembly.CreateMigration(assembly.Migrations[key],db.Database.ProviderName!);
        Assert.All(migration.UpOperations,o=>Assert.True(o is CreateTableOperation or CreateIndexOperation or SqlOperation));
        var tables=migration.UpOperations.OfType<CreateTableOperation>().ToList();Assert.Equal(3,tables.Count);Assert.All(tables,t=>Assert.StartsWith("lab_",t.Name));
        var keys=tables.SelectMany(t=>t.ForeignKeys).ToList();Assert.Equal(9,keys.Count);Assert.All(keys,k=>Assert.Equal(ReferentialAction.Restrict,k.OnDelete));
        Assert.Equal(14,tables.Sum(t=>t.CheckConstraints.Count));
        var sql=migration.UpOperations.OfType<SqlOperation>().Single().Sql;Assert.Contains("Finalized laboratory results are immutable",sql);Assert.Contains("Recorded sample custody is immutable",sql);
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED",sql);Assert.Contains("FOR UPDATE",sql);Assert.DoesNotContain("DROP ",sql,StringComparison.OrdinalIgnoreCase);Assert.DoesNotContain("TRUNCATE",sql,StringComparison.OrdinalIgnoreCase);
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(),i=>Assert.StartsWith("lab_",i.Table));
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(),i=>i.Table=="lab_requests" && i.Columns.SequenceEqual(new[]{"SampleIdentifier"}) && i.IsUnique);
    }
    [Fact]
    public void DoctorCareScopeAndFinalizedResultSharingTranslateToPostgres()
    {
        using var db=Postgres();var sql=LaboratoryAccess.Visible(db,"doctor-account",false).Where(r=>r.TestCode.Contains("SYN")).ToQueryString();
        Assert.Contains("IdentityUserId",sql);Assert.Contains("IsActive",sql);Assert.Contains("Scheduled",sql);Assert.Contains("Completed",sql);Assert.Contains("EXISTS",sql);Assert.Contains("FinalizedAtUtc",sql);Assert.Contains("RequestedByUserId",sql);
        var numeric=db.Model.FindEntityType(typeof(LabResultRevision))!.FindProperty(nameof(LabResultRevision.NumericValue))!;Assert.Equal(28,numeric.GetPrecision());Assert.Equal(8,numeric.GetScale());
    }
    [Theory]
    [InlineData("catalogue")]
    [InlineData("request")]
    [InlineData("result")]
    public async Task EfConcurrencyRejectsStaleWrites(string entity)
    {
        var options=new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using(var db=new MediCareDbContext(options)){db.AddRange(new LabTest {Code="SYN",Name="Synthetic",Category="Synthetic",SampleType="Synthetic"},new LabRequest {CreationToken=Guid.NewGuid()},new LabResultRevision {AuthorUserId="synthetic",CreationToken=Guid.NewGuid(),Number=1});await db.SaveChangesAsync();}
        await using var a=new MediCareDbContext(options);await using var b=new MediCareDbContext(options);
        if(entity=="catalogue"){var first=await a.LabTests.SingleAsync();var stale=await b.LabTests.SingleAsync();first.Version=Guid.NewGuid();await a.SaveChangesAsync();stale.Version=Guid.NewGuid();}
        else if(entity=="request"){var first=await a.LabRequests.SingleAsync();var stale=await b.LabRequests.SingleAsync();first.Version=Guid.NewGuid();await a.SaveChangesAsync();stale.Version=Guid.NewGuid();}
        else{var first=await a.LabResultRevisions.SingleAsync();var stale=await b.LabResultRevisions.SingleAsync();first.Version=Guid.NewGuid();await a.SaveChangesAsync();stale.Version=Guid.NewGuid();}
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>b.SaveChangesAsync());
    }
    private static MediCareDbContext Postgres()=>new(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
}
