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

    // Existing method for staff account audit events.
    // Keep its behavior unchanged.
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

    // NEW: Queue an audit event for a non-account record.
    // The calling controller saves the record and audit
    // entry together using one SaveChangesAsync call.
    public void QueueRecordSuccess(
        string actorUserId,
        string recordType,
        Guid recordId,
        string action)
    {
        if (string.IsNullOrWhiteSpace(actorUserId) ||
            string.IsNullOrWhiteSpace(recordType) ||
            recordId == Guid.Empty ||
            string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException(
                "Audit event information is incomplete.");
        }

        var targetRecordId = $"{recordType}:{recordId:D}";

        if (actorUserId.Length > 450 ||
            targetRecordId.Length > 450 ||
            action.Length > 64)
        {
            throw new ArgumentException(
                "Audit event information exceeds allowed lengths.");
        }

        _db.SecurityAuditLogs.Add(new SecurityAuditLog
        {
            ActorUserId = actorUserId,
            TargetUserId = targetRecordId,
            Action = action,
            Succeeded = true,
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}