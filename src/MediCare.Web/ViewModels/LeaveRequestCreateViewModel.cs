using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class LeaveRequestCreateViewModel
{
    [Required(ErrorMessage = "Please select an employee.")]
    [Display(Name = "Employee")]
    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "Please select a leave type.")]
    [Display(Name = "Leave Type")]
    public string LeaveType { get; set; } = "";

    [Required(ErrorMessage = "Start date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "Start Date")]
    public DateOnly? StartDate { get; set; }

    [Required(ErrorMessage = "End date is required.")]
    [DataType(DataType.Date)]
    [Display(Name = "End Date")]
    public DateOnly? EndDate { get; set; }

    [StringLength(500)]
    [Display(Name = "Reason")]
    public string? Reason { get; set; }
}