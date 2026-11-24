
namespace MediCare.Web.ViewModels;

public class PharmacyReportRowViewModel
{
    public Guid Id { get; set; }

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string GenericName { get; set; } = "";
    public string Strength { get; set; } = "";
    public string Formulation { get; set; } = "";
    public string DispensingUnit { get; set; } = "";

    public bool IsActive { get; set; }

    public decimal LowStockThreshold { get; set; }
    public decimal UsableQuantity { get; set; }
    public decimal ExpiredQuantity { get; set; }

    public int ExpiringSoonBatches { get; set; }
}

public class PharmacyReportViewModel
{
    public List<PharmacyReportRowViewModel> Medicines
        { get; set; } = new();

    public string Search { get; set; } = "";
    public string StockFilter { get; set; } = "All";

    public DateOnly AsOfDate { get; set; }

    public int TotalMedicines { get; set; }
    public int ActiveMedicines { get; set; }
    public int LowStockMedicines { get; set; }
    public int ExpiringSoonMedicines { get; set; }
    public int ExpiredStockMedicines { get; set; }

    public int DispenseRecords { get; set; }
    public int ReversalRecords { get; set; }

    public int MatchingMedicines { get; set; }

    public int Page { get; set; } = 1;
    public int TotalPages { get; set; } = 1;
}
