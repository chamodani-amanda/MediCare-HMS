using MediCare.Web.Data;
using MediCare.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Services;

public static class LaboratoryAccess
{
    public static IQueryable<LabRequest> Visible(MediCareDbContext db,string account,bool laboratoryStaff)
    {
        var query=db.LabRequests.AsNoTracking();
        return laboratoryStaff?query:query.Where(r=>MedicalRecordAccess.CareAppointments(db,account).Any(a=>a.PatientId==r.MedicalRecord.PatientId) &&
            (r.RequestedByUserId==account || r.Results.Any(v=>v.FinalizedAtUtc!=null)));
    }
}
