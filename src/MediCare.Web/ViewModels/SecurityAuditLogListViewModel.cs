namespace MediCare.Web.ViewModels;

public class SecurityAuditLogRowViewModel
{
    public string Action { get; set; } = string.Empty;

    public string ActorEmail { get; set; } = string.Empty;

    public string TargetEmail { get; set; } = string.Empty;

    public bool Succeeded { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}

public class SecurityAuditLogListViewModel
{
    public List<SecurityAuditLogRowViewModel> Records { get; set; }
        = new();

    public int Page { get; set; }

    public int TotalPages { get; set; }

    public int TotalRecords { get; set; }
}