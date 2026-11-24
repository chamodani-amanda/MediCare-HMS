
using MediCare.Web.Models;

namespace MediCare.Web.ViewModels;

public class AppointmentReportRowViewModel
{
    public Guid Id { get; set; }

    public string PatientNumber { get; set; } = "";
    public string PatientName { get; set; } = "";
    public string DoctorName { get; set; } = "";

    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }

    public AppointmentStatus Status { get; set; }
}

public class AppointmentReportViewModel
{
    public List<AppointmentReportRowViewModel> Appointments
        { get; set; } = new();

    public string Search { get; set; } = "";
    public string Status { get; set; } = "All";

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public int TotalAppointments { get; set; }
    public int ScheduledAppointments { get; set; }
    public int CompletedAppointments { get; set; }
    public int CancelledAppointments { get; set; }
    public int NoShowAppointments { get; set; }

    public int MatchingAppointments { get; set; }

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}
