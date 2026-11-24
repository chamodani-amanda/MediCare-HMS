using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class Attendance
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Employee who attended work.
    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    // Calendar date in the hospital's local timezone.
    public DateOnly WorkDate { get; set; }

    // Actual attendance timestamps in UTC.
    public DateTime CheckInAtUtc { get; set; }

    public DateTime? CheckOutAtUtc { get; set; }

    // Optional remarks.
    [MaxLength(500)]
    public string? Notes { get; set; }

    // Identity user who recorded the attendance.
    [MaxLength(450)]
    public string? RecordedByUserId { get; set; }

    // Protect against conflicting updates.
    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; }
        = DateTime.UtcNow;
}