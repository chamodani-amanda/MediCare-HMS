using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MediCare.Web.Tests;

public class AppointmentPersistenceTests
{
    [Theory]
    [InlineData(AppointmentStatus.Scheduled,AppointmentStatus.Completed,-1,false)]
    [InlineData(AppointmentStatus.Scheduled,AppointmentStatus.Completed,0,true)]
    [InlineData(AppointmentStatus.Scheduled,AppointmentStatus.NoShow,-1,false)]
    [InlineData(AppointmentStatus.Scheduled,AppointmentStatus.NoShow,0,true)]
    [InlineData(AppointmentStatus.Scheduled,AppointmentStatus.Cancelled,-60,true)]
    [InlineData(AppointmentStatus.Scheduled,AppointmentStatus.Scheduled,60,false)]
    [InlineData(AppointmentStatus.Completed,AppointmentStatus.Cancelled,60,false)]
    [InlineData(AppointmentStatus.Cancelled,AppointmentStatus.Scheduled,60,false)]
    [InlineData(AppointmentStatus.NoShow,AppointmentStatus.Completed,60,false)]
    public void StatusBoundariesFollowSuppliedRules(AppointmentStatus current, AppointmentStatus next, int seconds, bool allowed)
    {
        var end = new DateTime(2030,1,1,12,0,0,DateTimeKind.Utc);
        Assert.Equal(allowed,AppointmentRules.MayChangeStatus(current,next,end,end.AddSeconds(seconds)));
    }

    [Fact]
    public void TimezoneAndWorkingHourGaps()
    {
        var date = new DateOnly(2030,1,7); var local = new TimeOnly(9,15);
        var utc = HospitalTime.ToUtc(date,local); Assert.Equal(new TimeOnly(3,45),TimeOnly.FromDateTime(utc));
        Assert.Equal(DateTimeKind.Utc,utc.Kind); Assert.Equal(local,TimeOnly.FromDateTime(HospitalTime.Local(utc)));
        var shifts = new[] { new DoctorSchedule { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(9,0), EndTime = new TimeOnly(12,0) },
            new DoctorSchedule { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(13,0), EndTime = new TimeOnly(17,0) } };
        Assert.False(AppointmentRules.Covers(shifts,DayOfWeek.Monday,new TimeOnly(11,45),new TimeOnly(13,15)));
        Assert.True(AppointmentRules.Covers(shifts,DayOfWeek.Monday,new TimeOnly(13,0),new TimeOnly(14,0)));
        Assert.False(AppointmentRules.Covers(shifts,DayOfWeek.Tuesday,new TimeOnly(13,0),new TimeOnly(14,0)));
    }

    [Fact]
    public async Task ConflictingAppointmentUpdatesAreRejected()
    {
        var name = Guid.NewGuid().ToString();
        MediCareDbContext Context() => new(new DbContextOptionsBuilder<MediCareDbContext>().UseInMemoryDatabase(name).Options);
        using var a = Context(); using var b = Context();
        a.Add(new Appointment { BookingToken = Guid.NewGuid() }); await a.SaveChangesAsync();
        var first = await a.Appointments.SingleAsync(); var stale = await b.Appointments.SingleAsync();
        first.Version = Guid.NewGuid(); first.Status = AppointmentStatus.Cancelled; await a.SaveChangesAsync();
        stale.Version = Guid.NewGuid(); stale.Status = AppointmentStatus.Completed;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    [Fact]
    public void PostgreSqlMappingAndMigrationIncludeRaceProtection()
    {
        using var db = new MediCareDbContext(new DbContextOptionsBuilder<MediCareDbContext>().UseNpgsql("Host=localhost;Database=medicare_dev;Username=unused").Options);
        var entity = db.Model.FindEntityType(typeof(Appointment))!;
        Assert.True(entity.FindProperty(nameof(Appointment.Version))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(),i=>i.IsUnique && i.Properties.Count == 1 && i.Properties[0].Name == nameof(Appointment.BookingToken));
        Assert.All(entity.GetForeignKeys(),fk=>Assert.Equal(DeleteBehavior.Restrict,fk.DeleteBehavior));
        var assembly = db.GetService<IMigrationsAssembly>();
        var id = assembly.Migrations.Keys.Single(k=>k.EndsWith("_AddAppointmentManagement"));
        var migration = assembly.CreateMigration(assembly.Migrations[id],db.Database.ProviderName!);
        Assert.All(migration.UpOperations,op=>Assert.True(op is AlterDatabaseOperation or CreateTableOperation or CreateIndexOperation or SqlOperation));
        Assert.Equal("appointments",Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        var sql = Assert.Single(migration.UpOperations.OfType<SqlOperation>()).Sql;
        Assert.Contains("EXCLUDE USING gist",sql); Assert.Contains("tstzrange",sql); Assert.Contains("'[)'",sql); Assert.Contains("Cancelled",sql);
        var from = DateTime.SpecifyKind(new DateTime(2030,1,1),DateTimeKind.Utc); var until = from.AddMinutes(30);
        var query = db.Appointments.Where(a=>a.Doctor.IdentityUserId=="linked-account" && a.Status!=AppointmentStatus.Cancelled && a.StartAtUtc<until && a.EndAtUtc>from).ToQueryString();
        Assert.Contains("IdentityUserId",query); Assert.Contains("StartAtUtc",query);
    }
}
