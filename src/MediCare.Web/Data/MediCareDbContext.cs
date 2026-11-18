using MediCare.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Data;

public class MediCareDbContext
    : IdentityDbContext<IdentityUser>
{
    public MediCareDbContext(
        DbContextOptions<MediCareDbContext> options)
        : base(options)
    {
    }

    public DbSet<Patient> Patients => Set<Patient>();
    
    public DbSet<SecurityAuditLog> SecurityAuditLogs
    => Set<SecurityAuditLog>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.ToTable("patients");

            entity.HasKey(p => p.Id);

            entity.HasIndex(p => p.PatientNumber)
                  .IsUnique();

            entity.Property(p => p.PatientNumber)
                  .IsRequired()
                  .HasMaxLength(20);
        });

        modelBuilder.Entity<SecurityAuditLog>(entity =>
{
    entity.ToTable("security_audit_logs");

    entity.HasKey(a => a.Id);

    entity.Property(a => a.Action)
          .HasMaxLength(64)
          .IsRequired();

    entity.Property(a => a.ActorUserId)
          .HasMaxLength(450)
          .IsRequired();

    entity.Property(a => a.TargetUserId)
          .HasMaxLength(450)
          .IsRequired();

    entity.HasIndex(a => a.OccurredAtUtc);

    entity.HasIndex(a => a.TargetUserId);
});

    }
}