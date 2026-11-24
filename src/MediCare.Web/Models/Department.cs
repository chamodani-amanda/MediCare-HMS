using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(100)] public string NormalizedName { get; set; } = "";
    [MaxLength(1000)] public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
}
