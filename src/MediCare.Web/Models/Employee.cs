using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class Employee
{
    // Unique employee record identifier
    public Guid Id { get; set; } = Guid.NewGuid();

    // Hospital employee number
    [Required]
    [MaxLength(30)]
    public string EmployeeNumber { get; set; } = "";

    // Employee personal details
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = "";

    [MaxLength(100)]
    public string? JobTitle { get; set; }

    [Phone]
    [MaxLength(30)]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    [MaxLength(256)]
    public string? Email { get; set; }

    // Employment information
    public DateOnly HireDate { get; set; }

    public bool IsActive { get; set; } = true;

    // Existing department relationship
    public Guid? DepartmentId { get; set; }

    public Department? Department { get; set; }

    // Optional link to an existing Identity account
    [MaxLength(450)]
    public string? IdentityUserId { get; set; }

    // Concurrency protection (configured later in EF Core)
    public Guid Version { get; set; } = Guid.NewGuid();

    // Record timestamps
    public DateTime CreatedAtUtc { get; set; }
        = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; }
        = DateTime.UtcNow;
}