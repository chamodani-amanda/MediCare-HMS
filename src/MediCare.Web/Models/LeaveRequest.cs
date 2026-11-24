using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class LeaveRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Employee requesting leave.
    public Guid EmployeeId { get; set; }

    public Employee Employee { get; set; } = null!;

    // Examples: Annual, Sick, Casual, Unpaid.
    [Required]
    [MaxLength(40)]
    public string LeaveType { get; set; } = "";

    // Inclusive leave date range.
    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    // Pending, Approved or Rejected.
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    // Existing Identity account that reviewed the request.
    [MaxLength(450)]
    public string? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(500)]
    public string? ReviewComment { get; set; }

    // Concurrency protection.
    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; }
        = DateTime.UtcNow;
}