using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Services;

// Apply this scope before any clinical projection, search, detail or export.
public static class MedicalRecordAccess
{
    public static IQueryable<Doctor> LinkedDoctors(MediCareDbContext db, string account) =>
        db.Doctors.Where(d => d.IsActive && d.IdentityUserId == account);

    public static IQueryable<Appointment> CareAppointments(MediCareDbContext db, string account) =>
        db.Appointments.Where(a => a.Doctor.IsActive && a.Doctor.IdentityUserId == account &&
            (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Completed));

    public static IQueryable<MedicalRecordRevision> Visible(MediCareDbContext db, string account) =>
        db.MedicalRecordRevisions.Where(r => CareAppointments(db, account).Any(a => a.PatientId == r.MedicalRecord.PatientId) &&
            (r.FinalizedAtUtc != null || (r.AuthorUserId == account && r.MedicalRecord.Doctor.IdentityUserId == account && r.MedicalRecord.Doctor.IsActive)));
}
