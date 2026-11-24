using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class MedicalRecordPersistenceTests
{
    private static MediCareDbContext Postgres() => new(new DbContextOptionsBuilder<MediCareDbContext>()
        .UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);

    [Fact]
    public void MigrationIsAdditiveAndContainsFinalizedHistoryGuards()
    {
        using var db = Postgres(); var assembly = db.GetService<IMigrationsAssembly>();
        var key = assembly.Migrations.Keys.Single(k => k.EndsWith("_AddMedicalRecords"));
        var migration = assembly.CreateMigration(assembly.Migrations[key], db.Database.ProviderName!);
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation or SqlOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray(); Assert.Equal(3,tables.Length);
        Assert.All(tables, table => Assert.StartsWith("medical_",table.Name));
        Assert.All(tables.SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict,fk.OnDelete));
        var guards = migration.UpOperations.OfType<SqlOperation>().Single().Sql;
        Assert.Contains("Finalized medical revisions are immutable",guards);
        Assert.Contains("Prescriptions require an editable draft revision",guards);
        Assert.Contains("FOR UPDATE",guards); Assert.DoesNotContain("DROP ",guards,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE",guards,StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, migration.UpOperations.OfType<CreateIndexOperation>().Count(i => i.IsUnique && i.Table=="medical_record_revisions") +
            migration.UpOperations.OfType<CreateIndexOperation>().Count(i => i.IsUnique && i.Table=="medical_prescriptions"));
    }

    [Fact]
    public void PatientScopeAndDraftPrivacyTranslateToPostgreSql()
    {
        using var db = Postgres(); var sql = MedicalRecordAccess.Visible(db,"linked-doctor-account")
            .Where(r=>r.MedicalRecord.Patient.PatientNumber.Contains("TEST")).ToQueryString();
        Assert.Contains("EXISTS",sql); Assert.Contains("IdentityUserId",sql); Assert.Contains("IsActive",sql);
        Assert.Contains("Scheduled",sql); Assert.Contains("Completed",sql); Assert.Contains("FinalizedAtUtc",sql); Assert.Contains("AuthorUserId",sql);
    }

    [Theory]
    [InlineData("record")]
    [InlineData("revision")]
    public async Task RealEfConcurrencyTokensRejectStaleWrites(string entity)
    {
        var options = new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var record = new MedicalRecord { AuthorUserId="test", CreationToken=Guid.NewGuid(), CreatedAtUtc=DateTime.UtcNow };
        var revision = new MedicalRecordRevision { MedicalRecord=record, Number=1, AuthorUserId="test", CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow };
        await using(var seed=new MediCareDbContext(options)) { seed.Add(revision); await seed.SaveChangesAsync(); }
        await using var first=new MediCareDbContext(options); await using var stale=new MediCareDbContext(options);
        if(entity=="record")
        {
            var a=await first.MedicalRecords.SingleAsync(); var b=await stale.MedicalRecords.SingleAsync();
            a.Version=Guid.NewGuid(); await first.SaveChangesAsync(); b.Version=Guid.NewGuid();
        }
        else
        {
            var a=await first.MedicalRecordRevisions.SingleAsync(); var b=await stale.MedicalRecordRevisions.SingleAsync();
            a.Diagnosis="Synthetic update"; a.Version=Guid.NewGuid(); await first.SaveChangesAsync(); b.Diagnosis="Synthetic stale update"; b.Version=Guid.NewGuid();
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>stale.SaveChangesAsync());
    }

    [Fact]
    public void LongClinicalReportProducesMultiplePdfPages()
    {
        var revision = new MedicalRecordRevision { Number=1, AuthorUserId="synthetic-author", CreatedAtUtc=DateTime.UtcNow, UpdatedAtUtc=DateTime.UtcNow,
            Symptoms="Synthetic symptoms", Diagnosis="Synthetic diagnosis", Treatment=string.Concat(Enumerable.Repeat("Synthetic treatment history. ",250)),
            MedicalRecord=new MedicalRecord { Patient=new Patient { PatientNumber="TEST-REPORT", FirstName="Synthetic", LastName="Patient" },
                Doctor=new Doctor { FirstName="Synthetic", LastName="Doctor", RegistrationNumber="TEST" } } };
        var bytes=MedicalRecordPdf.Render(revision);
        using var stream=new MemoryStream(bytes); using var pdf=PdfSharp.Pdf.IO.PdfReader.Open(stream,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        Assert.True(pdf.PageCount>1);
    }
}
