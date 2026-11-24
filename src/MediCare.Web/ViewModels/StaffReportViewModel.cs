
namespace MediCare.Web.ViewModels;

public class StaffReportRowViewModel
{
    public Guid Id { get; set; }

    public string EmployeeNumber { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? JobTitle { get; set; }
    public string DepartmentName { get; set; } = "";

    public DateOnly HireDate { get; set; }
    public bool IsActive { get; set; }

    public int AttendanceRecords { get; set; }
    public int OpenCheckIns { get; set; }

    public int PendingLeaves { get; set; }
    public int ApprovedLeaves { get; set; }
    public int RejectedLeaves { get; set; }
}

public class StaffReportViewModel
{
    public List<StaffReportRowViewModel> Employees
        { get; set; } = new();

    public string Search { get; set; } = "";
    public string Status { get; set; } = "All";

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int MatchingEmployees { get; set; }
    public int ActiveEmployees { get; set; }
    public int InactiveEmployees { get; set; }

    public int TotalAttendanceRecords { get; set; }
    public int OpenCheckIns { get; set; }

    public int PendingLeaves { get; set; }
    public int ApprovedLeaves { get; set; }
    public int RejectedLeaves { get; set; }

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}
