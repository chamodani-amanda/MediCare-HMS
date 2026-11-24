namespace MediCare.Web.ViewModels;

// Reuse the validation rules from PatientCreateViewModel.
public class PatientEditViewModel : PatientCreateViewModel
{
    public Guid Id { get; set; }
    public Guid Version { get; set; }

    public string PatientNumber { get; set; } = string.Empty;
}
