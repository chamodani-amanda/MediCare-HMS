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
    public DbSet<BillingService> BillingServices => Set<BillingService>();
    public DbSet<BillingPrice> BillingPrices => Set<BillingPrice>();
    public DbSet<PatientCharge> PatientCharges => Set<PatientCharge>();
    public DbSet<BillingInvoice> BillingInvoices => Set<BillingInvoice>();
    public DbSet<BillingInvoiceLine> BillingInvoiceLines => Set<BillingInvoiceLine>();
    public DbSet<BillingPayment> BillingPayments => Set<BillingPayment>();
    public DbSet<Ward> Wards => Set<Ward>();
    public DbSet<Bed> Beds => Set<Bed>();
    public DbSet<OutpatientVisit> OutpatientVisits => Set<OutpatientVisit>();
    public DbSet<Admission> Admissions => Set<Admission>();
    public DbSet<BedOccupancy> BedOccupancies => Set<BedOccupancy>();
    public DbSet<BedTransfer> BedTransfers => Set<BedTransfer>();
    public DbSet<AdmissionClinical> AdmissionClinicalNotes => Set<AdmissionClinical>();
    public DbSet<OutpatientClinical> OutpatientClinicalNotes => Set<OutpatientClinical>();
    public DbSet<Medicine> Medicines => Set<Medicine>();
    public DbSet<MedicineBatch> MedicineBatches => Set<MedicineBatch>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<PharmacyDispense> PharmacyDispenses => Set<PharmacyDispense>();
    public DbSet<PatientDocument> PatientDocuments => Set<PatientDocument>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Department> Departments => Set<Department>();
    // Staff Management
public DbSet<Employee> Employees => Set<Employee>();

public DbSet<Attendance> Attendances => Set<Attendance>();

public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<MedicalRecord> MedicalRecords => Set<MedicalRecord>();
    public DbSet<MedicalRecordRevision> MedicalRecordRevisions => Set<MedicalRecordRevision>();
    public DbSet<MedicalPrescription> MedicalPrescriptions => Set<MedicalPrescription>();
    public DbSet<LabTest> LabTests => Set<LabTest>();
    public DbSet<LabRequest> LabRequests => Set<LabRequest>();
    public DbSet<LabResultRevision> LabResultRevisions => Set<LabResultRevision>();
    
    public DbSet<SecurityAuditLog> SecurityAuditLogs
    => Set<SecurityAuditLog>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ConfigureBilling();
        modelBuilder.ConfigureMedicalRecords();
        modelBuilder.ConfigureLaboratory();
        modelBuilder.ConfigurePharmacy();
        modelBuilder.ConfigureHospitalStays();
        modelBuilder.ConfigureStaffManagement();
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("appointments", table =>
            {
                table.HasCheckConstraint("CK_appointments_status", "\"Status\" IN ('Scheduled','Completed','Cancelled','NoShow')");
                table.HasCheckConstraint("CK_appointments_duration", "isfinite(\"StartAtUtc\") AND isfinite(\"EndAtUtc\") AND \"EndAtUtc\" > \"StartAtUtc\" AND \"DurationMinutes\" IN (15,30,45,60) AND \"EndAtUtc\" = \"StartAtUtc\" + \"DurationMinutes\" * INTERVAL '1 minute'");
                table.HasCheckConstraint("CK_appointments_booking_token", "\"BookingToken\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                table.HasCheckConstraint("CK_appointments_start_interval", "EXTRACT(MINUTE FROM (\"StartAtUtc\" AT TIME ZONE 'UTC'))::int % 15 = 0 AND EXTRACT(SECOND FROM (\"StartAtUtc\" AT TIME ZONE 'UTC')) = 0");
                table.HasCheckConstraint("CK_appointments_cancellation", "(\"Status\" = 'Cancelled' AND \"CancelledAtUtc\" IS NOT NULL) OR (\"Status\" <> 'Cancelled' AND \"CancelledAtUtc\" IS NULL AND \"CancellationReason\" IS NULL)");
            });
            entity.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(a => a.Version).IsConcurrencyToken();
            entity.HasIndex(a => a.BookingToken).IsUnique();
            entity.HasIndex(a => new { a.DoctorId, a.StartAtUtc });
            entity.HasIndex(a => new { a.PatientId, a.StartAtUtc });
            entity.HasIndex(a => new { a.StartAtUtc, a.Status });
            entity.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(a => a.Doctor).WithMany().HasForeignKey(a => a.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("departments");
            entity.HasIndex(d => d.NormalizedName).IsUnique();
            entity.Property(d => d.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<Doctor>(entity =>
        {
            entity.ToTable("doctors");
            entity.HasIndex(d => d.RegistrationNumber).IsUnique();
            entity.HasIndex(d => d.NormalizedEmail).IsUnique();
            entity.HasIndex(d => d.IdentityUserId).IsUnique();
            entity.HasIndex(d => new { d.IsActive, d.LastName, d.FirstName });
            entity.Property(d => d.Version).IsConcurrencyToken();
            entity.HasOne(d => d.Department).WithMany().HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(d => d.IdentityUser).WithMany().HasForeignKey(d => d.IdentityUserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<DoctorSchedule>(entity =>
        {
            entity.ToTable("doctor_schedules", table =>
            {
                table.HasCheckConstraint("CK_doctor_schedules_day", "\"DayOfWeek\" BETWEEN 0 AND 6");
                table.HasCheckConstraint("CK_doctor_schedules_times", "\"StartTime\" < \"EndTime\"");
            });
            entity.Property(s => s.Version).IsConcurrencyToken();
            entity.HasIndex(s => new { s.DoctorId, s.DayOfWeek, s.StartTime }).IsUnique().HasFilter("\"IsActive\" = TRUE");
            entity.HasOne(s => s.Doctor).WithMany().HasForeignKey(s => s.DoctorId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Patient>().Property(p => p.Version).IsConcurrencyToken();
        modelBuilder.Entity<PatientDocument>(entity =>
        {
            entity.ToTable("patient_documents");
            entity.HasKey(d => d.Id);
            entity.HasIndex(d => d.StoredFileName).IsUnique();
            entity.HasOne(d => d.Patient).WithMany().HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

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
