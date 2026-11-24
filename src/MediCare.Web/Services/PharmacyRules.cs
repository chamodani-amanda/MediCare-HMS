using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Services;

public static class PharmacyRules
{
    public static bool ValidQuantity(decimal quantity) => quantity > 0 && quantity <= 1000000000m && decimal.Round(quantity, 3) == quantity;
    public static bool Usable(MedicineBatch batch, DateOnly today) => batch.Medicine.IsActive && batch.ExpiresOn > today && batch.AvailableQuantity > 0;
    // Expiry date itself is unavailable; a conservative date-only expiry convention.
    public static IQueryable<MedicalPrescription> Eligible(MediCareDbContext db) => db.MedicalPrescriptions.Where(p =>
        p.AuthorizedQuantity != null && p.MedicineId != null && p.LineageId != null && p.Medicine!.IsActive &&
        p.MedicalRecordRevision.FinalizedAtUtc != null && !p.MedicalRecordRevision.MedicalRecord.Patient.IsArchived &&
        p.MedicalRecordRevision.MedicalRecord.Doctor.IsActive && p.MedicalRecordRevision.MedicalRecord.Doctor.IdentityUserId == p.MedicalRecordRevision.MedicalRecord.AuthorUserId &&
        (p.MedicalRecordRevision.MedicalRecord.Appointment.Status == AppointmentStatus.Scheduled || p.MedicalRecordRevision.MedicalRecord.Appointment.Status == AppointmentStatus.Completed) &&
        !db.MedicalRecordRevisions.Any(r => r.MedicalRecordId == p.MedicalRecordRevision.MedicalRecordId && r.Number > p.MedicalRecordRevision.Number));

    public static async Task<Dictionary<Guid, decimal>> Balances(MediCareDbContext db, IEnumerable<Guid> lineages) =>
        await db.PharmacyDispenses.Where(d => lineages.Contains(d.LineageId)).GroupBy(d => d.LineageId).ToDictionaryAsync(g => g.Key, g => g.Sum(d => d.Quantity));
}
