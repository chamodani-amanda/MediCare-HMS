using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class DoctorPersistenceTests
{
    [Fact]
    public void MigrationOnlyAddsExpectedTablesAndIndexes()
    {
        // Builds PostgreSQL metadata and SQL only; never opens a connection.
        using var db = new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>()
            .UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var assembly = db.GetService<IMigrationsAssembly>();
        var id = assembly.Migrations.Keys.Single(k => k.EndsWith("_AddDoctorManagement"));
        var migration = assembly.CreateMigration(assembly.Migrations[id], db.Database.ProviderName!);
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToList();
        Assert.Equal(new[] { "departments", "doctor_schedules", "doctors" }, tables.Select(t => t.Name).Order().ToArray());
        Assert.All(tables.SelectMany(t => t.ForeignKeys), fk => Assert.Equal(ReferentialAction.Restrict, fk.OnDelete));
        var schedule = tables.Single(t => t.Name == "doctor_schedules"); Assert.Equal(2, schedule.CheckConstraints.Count);
        var indexes = migration.UpOperations.OfType<CreateIndexOperation>().ToList();
        Assert.Contains(indexes, i => i.Table == "doctors" && i.Columns.SequenceEqual(new[] { "RegistrationNumber" }) && i.IsUnique);
        Assert.Contains(indexes, i => i.Table == "doctors" && i.Columns.SequenceEqual(new[] { "IdentityUserId" }) && i.IsUnique);
        Assert.Contains(indexes, i => i.Table == "departments" && i.IsUnique);
        Assert.Contains(indexes, i => i.Table == "doctor_schedules" && i.IsUnique && i.Filter == "\"IsActive\" = TRUE");
        foreach (var type in new[] { typeof(Doctor), typeof(Department), typeof(DoctorSchedule) })
            Assert.True(db.Model.FindEntityType(type)!.FindProperty("Version")!.IsConcurrencyToken);
        var from = new TimeOnly(10,0); var until = new TimeOnly(11,0);
        var sql = db.DoctorSchedules.Where(s => s.IsActive && s.DayOfWeek == DayOfWeek.Monday && s.StartTime < until && s.EndTime > from).ToQueryString();
        Assert.Contains("doctor_schedules", sql);
        Assert.Contains("\"StartTime\" <", sql);
        Assert.Contains("\"EndTime\" >", sql);
    }

    [Theory]
    [InlineData("doctor")]
    [InlineData("department")]
    [InlineData("schedule")]
    public async Task ConcurrentWritersCannotOverwriteEachOther(string entity)
    {
        var name = Guid.NewGuid().ToString();
        MediCareDbContext Context() => new(new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(name).Options);
        using var first = Context(); using var second = Context();
        first.Add(new Department()); first.Add(new Doctor()); first.Add(new DoctorSchedule()); await first.SaveChangesAsync();
        if (entity == "doctor")
        {
            var a = await first.Doctors.SingleAsync(); var b = await second.Doctors.SingleAsync();
            a.Version = Guid.NewGuid(); a.FirstName = "First"; b.Version = Guid.NewGuid(); b.FirstName = "Second";
        }
        else if (entity == "department")
        {
            var a = await first.Departments.SingleAsync(); var b = await second.Departments.SingleAsync();
            a.Version = Guid.NewGuid(); a.Name = "First"; b.Version = Guid.NewGuid(); b.Name = "Second";
        }
        else
        {
            var a = await first.DoctorSchedules.SingleAsync(); var b = await second.DoctorSchedules.SingleAsync();
            a.Version = Guid.NewGuid(); a.EndTime = new TimeOnly(13,0); b.Version = Guid.NewGuid(); b.EndTime = new TimeOnly(14,0);
        }
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }
}
