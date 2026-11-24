using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace MediCare.Web.Models;

public class Doctor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(50)] public string RegistrationNumber { get; set; } = "";
    [MaxLength(80)] public string FirstName { get; set; } = "";
    [MaxLength(80)] public string LastName { get; set; } = "";
    [MaxLength(1000)] public string Qualifications { get; set; } = "";
    [MaxLength(120)] public string Specialization { get; set; } = "";
    [MaxLength(150)] public string? Email { get; set; }
    [MaxLength(150)] public string? NormalizedEmail { get; set; }
    [MaxLength(20)] public string? PhoneNumber { get; set; }
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    [MaxLength(450)] public string? IdentityUserId { get; set; }
    public IdentityUser? IdentityUser { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
