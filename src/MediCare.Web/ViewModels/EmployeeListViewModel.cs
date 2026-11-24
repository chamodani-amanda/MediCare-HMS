namespace MediCare.Web.ViewModels;

public class EmployeeListItemViewModel
{
    public Guid Id { get; set; }

    public string EmployeeNumber { get; set; } = "";

    public string FullName { get; set; } = "";

    public string? JobTitle { get; set; }

    public string? DepartmentName { get; set; }

    public bool IsActive { get; set; }
}

public class EmployeeListViewModel
{
    public List<EmployeeListItemViewModel> Employees { get; set; }
        = new();

    public string SearchTerm { get; set; } = "";

    public int Page { get; set; }

    public int TotalPages { get; set; }

    public int TotalEmployees { get; set; }
}