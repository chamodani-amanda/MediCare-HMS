namespace MediCare.Web.ViewModels;

public class PatientReportRowViewModel
{
    public string PatientNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsArchived { get; set; }
    public DateTime RegisteredAtUtc { get; set; }
}

public class PatientReportViewModel
{
    public List<PatientReportRowViewModel> Patients { get; set; }
        = new();

    public string Search { get; set; } = "";
    public string Status { get; set; } = "All";

    public int TotalPatients { get; set; }
    public int ActivePatients { get; set; }
    public int ArchivedPatients { get; set; }
    public int MatchingPatients { get; set; }

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}