using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.Models;

public class PatientDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    [MaxLength(255)] public string OriginalFileName { get; set; } = "";
    [MaxLength(40)] public string StoredFileName { get; set; } = "";
    [MaxLength(50)] public string ContentType { get; set; } = "";
    public long Size { get; set; }
    [MaxLength(450)] public string UploadedByUserId { get; set; } = "";
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}
