using System.ComponentModel.DataAnnotations;
using MediCare.Web.Models;

namespace MediCare.Web.ViewModels;

public class StockForm
{
    public Guid MedicineId { get; set; }
    public Guid BatchId { get; set; }
    public Guid Token { get; set; }
    public Guid Version { get; set; }
    [StringLength(80)] public string? BatchNumber { get; set; }
    [StringLength(200)] public string? Supplier { get; set; }
    public DateOnly? ManufacturedOn { get; set; }
    public DateOnly ExpiresOn { get; set; }
    [Range(typeof(decimal), "-1000000000", "1000000000")] public decimal Quantity { get; set; }
    [Required, StringLength(1000)] public string Reason { get; set; } = "";
}
public class DispenseForm
{
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] public PharmacyPrescriptionRow? Prescription { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever] public List<MedicineBatch> Batches { get; set; } = new();
    public Guid PrescriptionId { get; set; }
    public Guid Token { get; set; }
    [Range(typeof(decimal), "0.001", "1000000000")] public decimal Quantity { get; set; }
    public Guid? ExceptionBatchId { get; set; }
    [StringLength(1000)] public string? ExceptionReason { get; set; }
}
public class PharmacyPrescriptionRow
{
    public Guid Id { get; set; }
    public string PatientLabel { get; set; } = "";
    public string MedicineLabel { get; set; } = "";
    public string Instructions { get; set; } = "";
    public decimal Authorized { get; set; }
    public decimal Dispensed { get; set; }
    public decimal Remaining => Math.Max(0, Authorized - Dispensed);
}
public class PharmacyPage<T>
{
    public List<T> Rows { get; set; } = new();
    public string? Search { get; set; }
    public string? Filter { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; }
    public int Pages { get; set; }
    public int Total { get; set; }
}
public class StockAlertRow
{
    public Medicine Medicine { get; set; } = null!;
    public decimal Available { get; set; }
}
