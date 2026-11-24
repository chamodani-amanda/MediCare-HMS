namespace MediCare.Web.ViewModels;

// Represents one patient in the Patients List.
public class PatientListItemViewModel
{
    public Guid Id { get; set; }

    public string PatientNumber { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public DateOnly DateOfBirth { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

// Represents the complete Patient List page.
public class PatientListViewModel
{
    public List<PatientListItemViewModel> Patients { get; set; }
        = new();

    // Stores the text entered in the search box.
    public string SearchTerm { get; set; } = string.Empty;

    public int Page { get; set; }

    public int TotalPages { get; set; }

    public int TotalPatients { get; set; }
}