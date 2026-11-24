using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

public static class StaffManagementMapping
{
    public static void ConfigureStaffManagement(
        this ModelBuilder modelBuilder)
    {
        // =====================================
        // EMPLOYEE TABLE
        // =====================================
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employees");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.EmployeeNumber)
                .HasMaxLength(30)
                .IsRequired();

            entity.HasIndex(e => e.EmployeeNumber)
                .IsUnique();

            entity.HasIndex(e => e.IdentityUserId)
                .IsUnique()
                .HasFilter("\"IdentityUserId\" IS NOT NULL");

            entity.HasIndex(e => new
            {
                e.IsActive,
                e.LastName,
                e.FirstName
            });

            entity.Property(e => e.Version)
                .IsConcurrencyToken();

            // Existing Department relationship.
            entity.HasOne(e => e.Department)
                .WithMany()
                .HasForeignKey(e => e.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);

            // Optional existing Identity account.
            entity.HasOne<IdentityUser>()
                .WithMany()
                .HasForeignKey(e => e.IdentityUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // =====================================
        // ATTENDANCE TABLE
        // =====================================
        modelBuilder.Entity<Attendance>(entity =>
        {
            entity.ToTable("attendances", table =>
            {
                table.HasCheckConstraint(
                    "CK_attendances_checkout",
                    "\"CheckOutAtUtc\" IS NULL OR " +
                    "\"CheckOutAtUtc\" > \"CheckInAtUtc\"");
            });

            entity.HasKey(a => a.Id);

            // One attendance record per employee
            // for each local calendar date.
            entity.HasIndex(a => new
            {
                a.EmployeeId,
                a.WorkDate
            }).IsUnique();

            entity.Property(a => a.Version)
                .IsConcurrencyToken();

            entity.Property(a => a.Notes)
                .HasMaxLength(500);

            entity.HasOne(a => a.Employee)
                .WithMany()
                .HasForeignKey(a => a.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // =====================================
        // LEAVE REQUEST TABLE
        // =====================================
        modelBuilder.Entity<LeaveRequest>(entity =>
        {
            entity.ToTable("leave_requests", table =>
            {
                table.HasCheckConstraint(
                    "CK_leave_requests_dates",
                    "\"StartDate\" <= \"EndDate\"");

                table.HasCheckConstraint(
                    "CK_leave_requests_status",
                    "\"Status\" IN " +
                    "('Pending','Approved','Rejected')");
            });

            entity.HasKey(l => l.Id);

            entity.Property(l => l.LeaveType)
                .HasMaxLength(40)
                .IsRequired();

            entity.Property(l => l.Status)
                .HasMaxLength(20)
                .IsRequired();

            entity.Property(l => l.Version)
                .IsConcurrencyToken();

            entity.HasIndex(l => new
            {
                l.EmployeeId,
                l.StartDate,
                l.EndDate
            });

            entity.HasIndex(l => new
            {
                l.Status,
                l.StartDate
            });

            entity.HasOne(l => l.Employee)
                .WithMany()
                .HasForeignKey(l => l.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}