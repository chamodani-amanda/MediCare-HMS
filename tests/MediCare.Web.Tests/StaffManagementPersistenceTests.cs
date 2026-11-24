using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace MediCare.Web.Tests;

public class StaffManagementPersistenceTests
{
    private static readonly InMemoryDatabaseRoot DatabaseRoot = new();

    private static MediCareDbContext CreateDatabase(string name)
    {
        var options = new DbContextOptionsBuilder<MediCareDbContext>()
            .UseInMemoryDatabase(name, DatabaseRoot)
            .Options;

        return new MediCareDbContext(options);
    }

    [Theory]
    [InlineData("Employee", "EmployeeCreated")]
    [InlineData("Employee", "EmployeeUpdated")]
    [InlineData("Attendance", "AttendanceCheckIn")]
    [InlineData("Attendance", "AttendanceCheckOut")]
    [InlineData("LeaveRequest", "LeaveRequestCreated")]
    [InlineData("LeaveRequest", "LeaveRequestApproved")]
    [InlineData("LeaveRequest", "LeaveRequestRejected")]
    public async Task AuditEventsStoreCorrectInformation(
        string recordType,
        string action)
    {
        using var db = CreateDatabase(Guid.NewGuid().ToString());

        var audit = new SecurityAuditService(db);
        var recordId = Guid.NewGuid();

        audit.QueueRecordSuccess(
            "test-administrator",
            recordType,
            recordId,
            action);

        // Queued audit records are not yet saved.
       Assert.Single(
    db.ChangeTracker.Entries<SecurityAuditLog>(),
    entry => entry.State == EntityState.Added);

        await db.SaveChangesAsync();

        var saved = await db.SecurityAuditLogs.SingleAsync();

        Assert.Equal(action, saved.Action);
        Assert.Equal("test-administrator", saved.ActorUserId);
        Assert.Equal(
            $"{recordType}:{recordId:D}",
            saved.TargetUserId);
        Assert.True(saved.Succeeded);
    }

    [Fact]
    public void InvalidAuditInformationIsRejected()
    {
        using var db = CreateDatabase(Guid.NewGuid().ToString());

        var audit = new SecurityAuditService(db);

        Assert.Throws<ArgumentException>(() =>
            audit.QueueRecordSuccess(
                "",
                "Employee",
                Guid.NewGuid(),
                "EmployeeCreated"));

        Assert.Throws<ArgumentException>(() =>
            audit.QueueRecordSuccess(
                "test-administrator",
                "Employee",
                Guid.Empty,
                "EmployeeCreated"));
    }

    [Fact]
    public async Task ConcurrentEmployeeUpdatesRejectStaleVersion()
    {
        string databaseName = Guid.NewGuid().ToString();
        Guid employeeId = Guid.NewGuid();

        // Create a fictional employee in the test database.
        using (var setup = CreateDatabase(databaseName))
        {
            setup.Employees.Add(new Employee
            {
                Id = employeeId,
                EmployeeNumber = "TEST-001",
                FirstName = "Test",
                LastName = "Employee",
                HireDate = new DateOnly(2026, 11, 19),
                Version = Guid.NewGuid()
            });

            await setup.SaveChangesAsync();
        }

        // Simulate two administrators loading the same record.
        using var adminA = CreateDatabase(databaseName);
        using var adminB = CreateDatabase(databaseName);

        var employeeA = await adminA.Employees
            .SingleAsync(e => e.Id == employeeId);

        var employeeB = await adminB.Employees
            .SingleAsync(e => e.Id == employeeId);

        // Administrator A saves first.
        employeeA.JobTitle = "Senior Receptionist";
        employeeA.Version = Guid.NewGuid();

        await adminA.SaveChangesAsync();

        // Administrator B attempts to save an older version.
        employeeB.JobTitle = "Manager";
        employeeB.Version = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => adminB.SaveChangesAsync());
    }
}