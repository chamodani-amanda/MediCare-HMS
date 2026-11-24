
using MediCare.Web.Models;

namespace MediCare.Web.ViewModels;

public class LaboratoryReportRowViewModel
{
    public Guid Id { get; set; }

    public string PatientNumber { get; set; } = "";
    public string PatientName { get; set; } = "";

    public string TestCode { get; set; } = "";
    public string TestName { get; set; } = "";
    public string Category { get; set; } = "";

    public string? SampleIdentifier { get; set; }

    public DateTime RequestedAtUtc { get; set; }

    public LabRequestStatus Status { get; set; }
}

public class LaboratoryReportViewModel
{
    public List<LaboratoryReportRowViewModel> Requests
        { get; set; } = new();

    public string Search { get; set; } = "";
    public string Status { get; set; } = "All";

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int TotalRequests { get; set; }
    public int RequestedCount { get; set; }
    public int SampleCollectedCount { get; set; }
    public int InProgressCount { get; set; }
    public int CompletedCount { get; set; }
    public int CancelledCount { get; set; }

    public int MatchingRequests { get; set; }

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}
