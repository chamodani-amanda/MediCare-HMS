using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public enum AppointmentStatus { Scheduled, Completed, Cancelled, NoShow }

public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingToken { get; set; }
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public int DurationMinutes { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;
    [MaxLength(500)] public string? CancellationReason { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
