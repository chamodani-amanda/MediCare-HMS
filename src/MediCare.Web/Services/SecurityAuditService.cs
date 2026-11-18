using MediCare.Web.Data;
using MediCare.Web.Models;

namespace MediCare.Web.Services;

public class SecurityAuditService
{
    private readonly MediCareDbContext _db;

    public SecurityAuditService(MediCareDbContext db)
    {
        _db = db;
    }

    public async Task RecordSuccessAsync(
        string actorUserId,
        string targetUserId,
        string action)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) ||
            string.IsNullOrWhiteSpace(targetUserId) ||
            string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException(
                "Audit event information is incomplete.");
        }

        var auditLog = new SecurityAuditLog
        {
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Action = action,
            Succeeded = true,
            OccurredAtUtc = DateTime.UtcNow
        };

        _db.SecurityAuditLogs.Add(auditLog);

        await _db.SaveChangesAsync();
    }
}