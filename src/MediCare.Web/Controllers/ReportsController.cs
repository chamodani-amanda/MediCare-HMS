using System.Text;
using MediCare.Web.Data;
using MediCare.Web.Models;
using MediCare.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Web.Controllers;

[Authorize(Roles = "Administrator")]
public class ReportsController : Controller
{
    private readonly MediCareDbContext _db;

    public ReportsController(MediCareDbContext db)
    {
        _db = db;
    }

    // GET: /Reports
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }

    // GET: /Reports/PatientReport
    [HttpGet]
    public async Task<IActionResult> PatientReport(
        string? search,
        string status = "All",
        int page = 1)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidFilters(searchTerm, status))
            return BadRequest("Invalid patient report filters.");

        const int pageSize = 20;

        var query = FilterPatients(searchTerm, status);

        int matchingPatients = await query.CountAsync();

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(
                matchingPatients / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var patients = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PatientReportRowViewModel
            {
                PatientNumber = p.PatientNumber,
                FullName = p.FirstName + " " + p.LastName,
                IsArchived = p.IsArchived,
                RegisteredAtUtc = p.CreatedAtUtc
            })
            .ToListAsync();

        int totalPatients = await _db.Patients
            .CountAsync();

        int archivedPatients = await _db.Patients
            .CountAsync(p => p.IsArchived);

        var model = new PatientReportViewModel
        {
            Patients = patients,
            Search = searchTerm,
            Status = status,
            TotalPatients = totalPatients,
            ActivePatients = totalPatients - archivedPatients,
            ArchivedPatients = archivedPatients,
            MatchingPatients = matchingPatients,
            Page = page,
            TotalPages = totalPages
        };

        return View(model);
    }

    // GET: /Reports/ExportPatientsCsv
    [HttpGet]
    public async Task<IActionResult> ExportPatientsCsv(
        string? search,
        string status = "All")
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidFilters(searchTerm, status))
            return BadRequest("Invalid export filters.");

        var query = FilterPatients(searchTerm, status);

        const int maximumExportRows = 10000;

        if (await query.CountAsync() > maximumExportRows)
        {
            return BadRequest(
                "Too many matching patients. " +
                "Please narrow your filters before exporting.");
        }

        var patients = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ThenBy(p => p.Id)
            .Select(p => new
            {
                p.PatientNumber,
                p.FirstName,
                p.LastName,
                p.IsArchived,
                p.CreatedAtUtc
            })
            .ToListAsync();

        var csv = new StringBuilder();

        csv.AppendLine(
            "Patient Number,Full Name,Status,Registered At (UTC)");

        foreach (var patient in patients)
        {
            csv.Append(CsvCell(patient.PatientNumber));
            csv.Append(",");

            csv.Append(CsvCell(
                patient.FirstName + " " + patient.LastName));
            csv.Append(",");

            csv.Append(patient.IsArchived ? "Archived" : "Active");
            csv.Append(",");

            csv.Append(
                patient.CreatedAtUtc.ToString(
                    "yyyy-MM-dd HH:mm:ss"));

            csv.AppendLine();
        }

        // UTF-8 BOM helps Excel recognize Unicode characters.
        byte[] bytes = Encoding.UTF8.GetBytes(
            "\uFEFF" + csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"patient-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private IQueryable<Patient> FilterPatients(
        string search,
        string status)
    {
        var query = _db.Patients.AsNoTracking();

        if (status == "Active")
        {
            query = query.Where(p => !p.IsArchived);
        }
        else if (status == "Archived")
        {
            query = query.Where(p => p.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escape SQL LIKE wildcard characters.
            string escaped = search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            string pattern = "%" + escaped + "%";

            query = query.Where(p =>
                EF.Functions.ILike(
                    p.PatientNumber, pattern, "\\") ||
                EF.Functions.ILike(
                    p.FirstName, pattern, "\\") ||
                EF.Functions.ILike(
                    p.LastName, pattern, "\\") ||
                EF.Functions.ILike(
                    p.FirstName + " " + p.LastName,
                    pattern, "\\"));
        }

        return query;
    }

    private static bool ValidFilters(
        string search,
        string status)
    {
        return search.Length <= 100 &&
               status is "All" or "Active" or "Archived";
    }

    private static string CsvCell(string? value)
    {
        string safe = (value ?? "")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Trim();

        // Prevent spreadsheet formula injection.
        if (safe.Length > 0 &&
            safe[0] is '=' or '+' or '-' or '@')
        {
            safe = "'" + safe;
        }

        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }


    // GET: /Reports/AppointmentReport
    [HttpGet]
    public async Task<IActionResult> AppointmentReport(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidAppointmentFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid appointment report filters.");
        }

        const int pageSize = 20;

        // Summary counts use the date and search filters,
        // regardless of the selected status.
        var baseQuery = FilterAppointments(
            searchTerm, fromDate, toDate, "All");

        var statusCounts = await baseQuery
            .GroupBy(a => a.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        int CountStatus(AppointmentStatus value) =>
            statusCounts
                .Where(s => s.Status == value)
                .Sum(s => s.Count);

        var query = FilterAppointments(
            searchTerm, fromDate, toDate, status);

        int matching = await query.CountAsync();

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(matching / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var rows = await query
            .OrderByDescending(a => a.StartAtUtc)
            .ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AppointmentReportRowViewModel
            {
                Id = a.Id,
                PatientNumber = a.Patient.PatientNumber,
                PatientName =
                    a.Patient.FirstName + " " + a.Patient.LastName,
                DoctorName =
                    a.Doctor.FirstName + " " + a.Doctor.LastName,
                StartAtUtc = a.StartAtUtc,
                EndAtUtc = a.EndAtUtc,
                Status = a.Status
            })
            .ToListAsync();

        var model = new AppointmentReportViewModel
        {
            Appointments = rows,
            Search = searchTerm,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,

            TotalAppointments = statusCounts.Sum(s => s.Count),

            ScheduledAppointments =
                CountStatus(AppointmentStatus.Scheduled),

            CompletedAppointments =
                CountStatus(AppointmentStatus.Completed),

            CancelledAppointments =
                CountStatus(AppointmentStatus.Cancelled),

            NoShowAppointments =
                CountStatus(AppointmentStatus.NoShow),

            MatchingAppointments = matching,
            Page = page,
            TotalPages = totalPages
        };

        return View(model);
    }

    // GET: /Reports/ExportAppointmentsCsv
    [HttpGet]
    public async Task<IActionResult> ExportAppointmentsCsv(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidAppointmentFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid export filters.");
        }

        var query = FilterAppointments(
            searchTerm, fromDate, toDate, status);

        const int maximumExportRows = 10000;

        if (await query.CountAsync() > maximumExportRows)
        {
            return BadRequest(
                "Too many appointments. Narrow your filters.");
        }

        var appointments = await query
            .OrderByDescending(a => a.StartAtUtc)
            .ThenBy(a => a.Id)
            .Select(a => new
            {
                PatientNumber = a.Patient.PatientNumber,
                PatientName =
                    a.Patient.FirstName + " " + a.Patient.LastName,
                DoctorName =
                    a.Doctor.FirstName + " " + a.Doctor.LastName,
                a.StartAtUtc,
                a.EndAtUtc,
                a.Status
            })
            .ToListAsync();

        var timeZone = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        var csv = new StringBuilder();

        csv.AppendLine(
            "Patient Number,Patient Name,Doctor," +
            "Start (Sri Lanka),End (Sri Lanka),Status");

        foreach (var appointment in appointments)
        {
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(
                appointment.StartAtUtc, timeZone);

            var localEnd = TimeZoneInfo.ConvertTimeFromUtc(
                appointment.EndAtUtc, timeZone);

            csv.Append(CsvCell(appointment.PatientNumber));
            csv.Append(',');

            csv.Append(CsvCell(appointment.PatientName));
            csv.Append(',');

            csv.Append(CsvCell(appointment.DoctorName));
            csv.Append(',');

            csv.Append(CsvCell(
                localStart.ToString("yyyy-MM-dd HH:mm")));
            csv.Append(',');

            csv.Append(CsvCell(
                localEnd.ToString("yyyy-MM-dd HH:mm")));
            csv.Append(',');

            csv.Append(CsvCell(appointment.Status.ToString()));
            csv.AppendLine();
        }

        byte[] bytes = Encoding.UTF8.GetBytes(
            "\uFEFF" + csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"appointment-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private IQueryable<Appointment> FilterAppointments(
        string search,
        DateOnly? fromDate,
        DateOnly? toDate,
        string status)
    {
        var query = _db.Appointments.AsNoTracking();

        if (status != "All")
        {
            var selectedStatus =
                Enum.Parse<AppointmentStatus>(status);

            query = query.Where(a =>
                a.Status == selectedStatus);
        }

        // User selects Sri Lanka calendar dates.
        // Appointment timestamps are stored as UTC.
        var timeZone = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        if (fromDate.HasValue)
        {
            var localStart = fromDate.Value.ToDateTime(
                TimeOnly.MinValue);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(
                localStart, timeZone);

            query = query.Where(a =>
                a.StartAtUtc >= utcStart);
        }

        if (toDate.HasValue)
        {
            var nextDay = toDate.Value
                .AddDays(1)
                .ToDateTime(TimeOnly.MinValue);

            var utcEndExclusive =
                TimeZoneInfo.ConvertTimeToUtc(
                    nextDay, timeZone);

            query = query.Where(a =>
                a.StartAtUtc < utcEndExclusive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string escaped = search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            string pattern = "%" + escaped + "%";

            query = query.Where(a =>
                EF.Functions.ILike(
                    a.Patient.PatientNumber, pattern, "\\") ||
                EF.Functions.ILike(
                    a.Patient.FirstName, pattern, "\\") ||
                EF.Functions.ILike(
                    a.Patient.LastName, pattern, "\\") ||
                EF.Functions.ILike(
                    a.Patient.FirstName + " " +
                    a.Patient.LastName, pattern, "\\") ||
                EF.Functions.ILike(
                    a.Doctor.FirstName, pattern, "\\") ||
                EF.Functions.ILike(
                    a.Doctor.LastName, pattern, "\\") ||
                EF.Functions.ILike(
                    a.Doctor.FirstName + " " +
                    a.Doctor.LastName, pattern, "\\"));
        }

        return query;
    }

    private static bool ValidAppointmentFilters(
        string search,
        string status,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (search.Length > 100)
            return false;

        if (status is not (
            "All" or "Scheduled" or "Completed" or
            "Cancelled" or "NoShow"))
        {
            return false;
        }

        if (fromDate.HasValue &&
            toDate.HasValue &&
            fromDate.Value > toDate.Value)
        {
            return false;
        }

        // Avoid overflow when calculating the exclusive end date.
        if (toDate == DateOnly.MaxValue)
            return false;

        return true;
    }

    // GET: /Reports/LaboratoryReport
    [HttpGet]
    public async Task<IActionResult> LaboratoryReport(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidLaboratoryFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid laboratory report filters.");
        }

        const int pageSize = 20;

        // Summary counts use search and date filters.
        // Status filtering applies to the table separately.
        var baseQuery = FilterLaboratoryRequests(
            searchTerm, fromDate, toDate, "All");

        var statusCounts = await baseQuery
            .GroupBy(r => r.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        int CountStatus(LabRequestStatus value) =>
            statusCounts
                .Where(s => s.Status == value)
                .Sum(s => s.Count);

        var query = FilterLaboratoryRequests(
            searchTerm, fromDate, toDate, status);

        int matching = await query.CountAsync();

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(matching / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var rows = await query
            .OrderByDescending(r => r.RequestedAtUtc)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new LaboratoryReportViewModel
        {
            Requests = rows,
            Search = searchTerm,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,

            TotalRequests = statusCounts.Sum(s => s.Count),

            RequestedCount =
                CountStatus(LabRequestStatus.Requested),

            SampleCollectedCount =
                CountStatus(LabRequestStatus.SampleCollected),

            InProgressCount =
                CountStatus(LabRequestStatus.InProgress),

            CompletedCount =
                CountStatus(LabRequestStatus.Completed),

            CancelledCount =
                CountStatus(LabRequestStatus.Cancelled),

            MatchingRequests = matching,
            Page = page,
            TotalPages = totalPages
        };

        return View(model);
    }

    // GET: /Reports/ExportLaboratoryCsv
    [HttpGet]
    public async Task<IActionResult> ExportLaboratoryCsv(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidLaboratoryFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid export filters.");
        }

        var query = FilterLaboratoryRequests(
            searchTerm, fromDate, toDate, status);

        const int maximumExportRows = 10000;

        if (await query.CountAsync() > maximumExportRows)
        {
            return BadRequest(
                "Too many laboratory records. " +
                "Please narrow your filters.");
        }

        var requests = await query
            .OrderByDescending(r => r.RequestedAtUtc)
            .ThenBy(r => r.Id)
            .ToListAsync();

        var timeZone = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        var csv = new StringBuilder();

        csv.AppendLine(
            "Patient Number,Patient Name,Test Code," +
            "Test Name,Category,Sample Identifier," +
            "Requested At (Sri Lanka),Status");

        foreach (var request in requests)
        {
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(
                request.RequestedAtUtc, timeZone);

            csv.Append(CsvCell(request.PatientNumber));
            csv.Append(',');

            csv.Append(CsvCell(request.PatientName));
            csv.Append(',');

            csv.Append(CsvCell(request.TestCode));
            csv.Append(',');

            csv.Append(CsvCell(request.TestName));
            csv.Append(',');

            csv.Append(CsvCell(request.Category));
            csv.Append(',');

            csv.Append(CsvCell(request.SampleIdentifier));
            csv.Append(',');

            csv.Append(CsvCell(
                localDate.ToString("yyyy-MM-dd HH:mm")));
            csv.Append(',');

            csv.Append(CsvCell(request.Status.ToString()));
            csv.AppendLine();
        }

        byte[] bytes = Encoding.UTF8.GetBytes(
            "\uFEFF" + csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"laboratory-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    // Shared query for the Laboratory Report and CSV export.
    private IQueryable<LaboratoryReportRowViewModel>
        FilterLaboratoryRequests(
            string search,
            DateOnly? fromDate,
            DateOnly? toDate,
            string status)
    {
        // LabRequest links to MedicalRecord.
        // MedicalRecord links to a patient through PatientId.
        var query =
            from request in _db.LabRequests.AsNoTracking()
            join patient in _db.Patients.AsNoTracking()
                on request.MedicalRecord.PatientId equals patient.Id
            select new LaboratoryReportRowViewModel
            {
                Id = request.Id,

                PatientNumber = patient.PatientNumber,

                PatientName =
                    patient.FirstName + " " + patient.LastName,

                // Use snapshots from the original lab order.
                TestCode = request.TestCode,
                TestName = request.TestName,
                Category = request.Category,
                SampleIdentifier = request.SampleIdentifier,

                RequestedAtUtc = request.RequestedAtUtc,
                Status = request.Status
            };

        if (status != "All")
        {
            var selectedStatus =
                Enum.Parse<LabRequestStatus>(status);

            query = query.Where(r =>
                r.Status == selectedStatus);
        }

        var timeZone = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        if (fromDate.HasValue)
        {
            var localStart = fromDate.Value
                .ToDateTime(TimeOnly.MinValue);

            var utcStart = TimeZoneInfo.ConvertTimeToUtc(
                localStart, timeZone);

            query = query.Where(r =>
                r.RequestedAtUtc >= utcStart);
        }

        if (toDate.HasValue)
        {
            var localNextDay = toDate.Value
                .AddDays(1)
                .ToDateTime(TimeOnly.MinValue);

            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(
                localNextDay, timeZone);

            query = query.Where(r =>
                r.RequestedAtUtc < utcEnd);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            // Escape LIKE wildcard characters.
            string escaped = search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            string pattern = "%" + escaped + "%";

            query = query.Where(r =>
                EF.Functions.ILike(
                    r.PatientNumber, pattern, "\\") ||
                EF.Functions.ILike(
                    r.PatientName, pattern, "\\") ||
                EF.Functions.ILike(
                    r.TestCode, pattern, "\\") ||
                EF.Functions.ILike(
                    r.TestName, pattern, "\\") ||
                EF.Functions.ILike(
                    r.Category, pattern, "\\"));
        }

        return query;
    }

    private static bool ValidLaboratoryFilters(
        string search,
        string status,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (search.Length > 100)
            return false;

        if (status is not (
            "All" or
            "Requested" or
            "SampleCollected" or
            "InProgress" or
            "Completed" or
            "Cancelled"))
        {
            return false;
        }

        if (fromDate.HasValue &&
            toDate.HasValue &&
            fromDate.Value > toDate.Value)
        {
            return false;
        }

        if (toDate == DateOnly.MaxValue)
            return false;

        return true;
    }

    // GET: /Reports/PharmacyReport
    [HttpGet]
    public async Task<IActionResult> PharmacyReport(
        string? search,
        string stockFilter = "All",
        int page = 1)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidPharmacyFilters(searchTerm, stockFilter))
        {
            return BadRequest("Invalid pharmacy report filters.");
        }

        var sriLanka = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, sriLanka));

        DateOnly soon = today.AddDays(30);

        // Summary counts follow the search filter,
        // but not the selected stock-status filter.
        var baseQuery = FilterPharmacyMedicines(
            searchTerm, "All", today, soon);

        var query = FilterPharmacyMedicines(
            searchTerm, stockFilter, today, soon);

        const int pageSize = 20;

        int matching = await query.CountAsync();

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(matching / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var medicines = await query
            .OrderBy(m => m.Code)
            .ThenBy(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new PharmacyReportViewModel
        {
            Medicines = medicines,
            Search = searchTerm,
            StockFilter = stockFilter,
            AsOfDate = today,

            TotalMedicines = await baseQuery.CountAsync(),

            ActiveMedicines = await baseQuery
                .CountAsync(m => m.IsActive),

            LowStockMedicines = await baseQuery
                .CountAsync(m =>
                    m.IsActive &&
                    m.UsableQuantity <= m.LowStockThreshold),

            ExpiringSoonMedicines = await baseQuery
                .CountAsync(m =>
                    m.ExpiringSoonBatches > 0),

            ExpiredStockMedicines = await baseQuery
                .CountAsync(m =>
                    m.ExpiredQuantity > 0),

            // These are all-time dispensing record counts.
            // Reversal entries are included in DispenseRecords.
            DispenseRecords = await _db.PharmacyDispenses
                .AsNoTracking()
                .CountAsync(),

            ReversalRecords = await _db.PharmacyDispenses
                .AsNoTracking()
                .CountAsync(d => d.ReversesId != null),

            MatchingMedicines = matching,
            Page = page,
            TotalPages = totalPages
        };

        return View(model);
    }

    // GET: /Reports/ExportPharmacyCsv
    [HttpGet]
    public async Task<IActionResult> ExportPharmacyCsv(
        string? search,
        string stockFilter = "All")
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidPharmacyFilters(searchTerm, stockFilter))
        {
            return BadRequest("Invalid export filters.");
        }

        var sriLanka = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        DateOnly today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow, sriLanka));

        DateOnly soon = today.AddDays(30);

        var query = FilterPharmacyMedicines(
            searchTerm, stockFilter, today, soon);

        const int maximumExportRows = 10000;

        if (await query.CountAsync() > maximumExportRows)
        {
            return BadRequest(
                "Too many matching medicines. " +
                "Please narrow your search.");
        }

        var medicines = await query
            .OrderBy(m => m.Code)
            .ThenBy(m => m.Id)
            .ToListAsync();

        var csv = new StringBuilder();

        csv.AppendLine(
            "Medicine Code,Medicine Name,Generic Name," +
            "Strength,Formulation,Dispensing Unit," +
            "Usable Quantity,Expired Quantity," +
            "Low Stock Threshold,Expiring Soon Batches,Status");

        foreach (var medicine in medicines)
        {
            string status = !medicine.IsActive
                ? "Inactive"
                : medicine.UsableQuantity <= medicine.LowStockThreshold
                    ? "Low Stock"
                    : "Active";

            csv.Append(CsvCell(medicine.Code));
            csv.Append(',');

            csv.Append(CsvCell(medicine.Name));
            csv.Append(',');

            csv.Append(CsvCell(medicine.GenericName));
            csv.Append(',');

            csv.Append(CsvCell(medicine.Strength));
            csv.Append(',');

            csv.Append(CsvCell(medicine.Formulation));
            csv.Append(',');

            csv.Append(CsvCell(medicine.DispensingUnit));
            csv.Append(',');

            csv.Append(medicine.UsableQuantity.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            csv.Append(',');

            csv.Append(medicine.ExpiredQuantity.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            csv.Append(',');

            csv.Append(medicine.LowStockThreshold.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            csv.Append(',');

            csv.Append(medicine.ExpiringSoonBatches);
            csv.Append(',');

            csv.Append(CsvCell(status));
            csv.AppendLine();
        }

        byte[] bytes = Encoding.UTF8.GetBytes(
            "\uFEFF" + csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"pharmacy-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    // Shared inventory query for report and CSV export.
    private IQueryable<PharmacyReportRowViewModel>
        FilterPharmacyMedicines(
            string search,
            string stockFilter,
            DateOnly today,
            DateOnly soon)
    {
        var medicines = _db.Medicines.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string escaped = search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            string pattern = "%" + escaped + "%";

            medicines = medicines.Where(m =>
                EF.Functions.ILike(m.Code, pattern, "\\") ||
                EF.Functions.ILike(m.Name, pattern, "\\") ||
                EF.Functions.ILike(m.GenericName, pattern, "\\") ||
                EF.Functions.ILike(m.Strength, pattern, "\\") ||
                EF.Functions.ILike(m.Formulation, pattern, "\\"));
        }

        var query = medicines.Select(m =>
            new PharmacyReportRowViewModel
            {
                Id = m.Id,
                Code = m.Code,
                Name = m.Name,
                GenericName = m.GenericName,
                Strength = m.Strength,
                Formulation = m.Formulation,
                DispensingUnit = m.DispensingUnit,
                IsActive = m.IsActive,
                LowStockThreshold = m.LowStockThreshold,

                // Expired batches are excluded
                // from the usable quantity.
                UsableQuantity = _db.MedicineBatches
                    .Where(b =>
                        b.MedicineId == m.Id &&
                        b.ExpiresOn >= today)
                    .Sum(b => (decimal?)b.AvailableQuantity)
                    ?? 0m,

                ExpiredQuantity = _db.MedicineBatches
                    .Where(b =>
                        b.MedicineId == m.Id &&
                        b.ExpiresOn < today)
                    .Sum(b => (decimal?)b.AvailableQuantity)
                    ?? 0m,

                ExpiringSoonBatches = _db.MedicineBatches
                    .Count(b =>
                        b.MedicineId == m.Id &&
                        b.AvailableQuantity > 0m &&
                        b.ExpiresOn >= today &&
                        b.ExpiresOn <= soon)
            });

        query = stockFilter switch
        {
            "Active" => query.Where(m => m.IsActive),

            "Inactive" => query.Where(m => !m.IsActive),

            "LowStock" => query.Where(m =>
                m.IsActive &&
                m.UsableQuantity <= m.LowStockThreshold),

            "ExpiringSoon" => query.Where(m =>
                m.ExpiringSoonBatches > 0),

            "ExpiredStock" => query.Where(m =>
                m.ExpiredQuantity > 0),

            _ => query
        };

        return query;
    }

    private static bool ValidPharmacyFilters(
        string search,
        string stockFilter)
    {
        return search.Length <= 100 &&
               stockFilter is (
                   "All" or
                   "Active" or
                   "Inactive" or
                   "LowStock" or
                   "ExpiringSoon" or
                   "ExpiredStock");
    }

    // GET: /Reports/BillingReport
    [HttpGet]
    public async Task<IActionResult> BillingReport(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidBillingFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid billing report filters.");
        }

        const int pageSize = 20;

        // Summary uses search/date filters, not status.
        var baseQuery = FilterBillingInvoices(
            searchTerm, "All", fromDate, toDate);

        var stateCounts = await baseQuery
            .GroupBy(i => i.State)
            .Select(g => new
            {
                State = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        int CountState(InvoiceState state) =>
            stateCounts
                .Where(s => s.State == state)
                .Sum(s => s.Count);

        // Financial totals include issued invoices only.
        // Draft and voided invoices are excluded.
        var issuedQuery = baseQuery
            .Where(i => i.State == InvoiceState.Issued);

        decimal issuedAmount =
            await issuedQuery.SumAsync(
                i => (decimal?)i.Total) ?? 0m;

        decimal netReceived =
            await issuedQuery.SumAsync(
                i => (decimal?)i.NetPaid) ?? 0m;

        var query = FilterBillingInvoices(
            searchTerm, status, fromDate, toDate);

        int matching = await query.CountAsync();

        int totalPages = Math.Max(
            1,
            (int)Math.Ceiling(matching / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var invoices = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .ThenByDescending(i => i.Number)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var model = new BillingReportViewModel
        {
            Invoices = invoices,
            Search = searchTerm,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,

            TotalInvoices = stateCounts.Sum(s => s.Count),
            DraftInvoices = CountState(InvoiceState.Draft),
            IssuedInvoices = CountState(InvoiceState.Issued),
            VoidedInvoices = CountState(InvoiceState.Voided),

            IssuedAmount = issuedAmount,
            NetReceivedAmount = netReceived,
            OutstandingAmount = issuedAmount - netReceived,

            MatchingInvoices = matching,
            Page = page,
            TotalPages = totalPages
        };

        return View(model);
    }

    // GET: /Reports/ExportBillingCsv
    [HttpGet]
    public async Task<IActionResult> ExportBillingCsv(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidBillingFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid export filters.");
        }

        var query = FilterBillingInvoices(
            searchTerm, status, fromDate, toDate);

        const int maximumExportRows = 10000;

        if (await query.CountAsync() > maximumExportRows)
        {
            return BadRequest(
                "Too many invoices. Please narrow your filters.");
        }

        var invoices = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .ThenByDescending(i => i.Number)
            .ToListAsync();

        var sriLanka = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        var csv = new StringBuilder();

        csv.AppendLine(
            "Invoice Number,Patient Number,Patient Name," +
            "Created At (Sri Lanka),Invoice State," +
            "Total (LKR),Net Paid (LKR)," +
            "Outstanding (LKR),Payment Status");

        foreach (var invoice in invoices)
        {
            var localDate = TimeZoneInfo.ConvertTimeFromUtc(
                invoice.CreatedAtUtc, sriLanka);

            csv.Append(CsvCell(invoice.DisplayNumber));
            csv.Append(',');

            csv.Append(CsvCell(invoice.PatientNumber));
            csv.Append(',');

            csv.Append(CsvCell(invoice.PatientName));
            csv.Append(',');

            csv.Append(CsvCell(
                localDate.ToString("yyyy-MM-dd HH:mm")));
            csv.Append(',');

            csv.Append(CsvCell(invoice.State.ToString()));
            csv.Append(',');

            csv.Append(invoice.Total.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            csv.Append(',');

            if (invoice.State == InvoiceState.Issued)
            {
                csv.Append(invoice.NetPaid.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            csv.Append(',');

            if (invoice.Balance.HasValue)
            {
                csv.Append(invoice.Balance.Value.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }

            csv.Append(',');
            csv.Append(CsvCell(invoice.PaymentStatus));
            csv.AppendLine();
        }

        byte[] bytes = Encoding.UTF8.GetBytes(
            "\uFEFF" + csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"billing-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    // Shared query for report and CSV export.
    private IQueryable<BillingReportRowViewModel>
        FilterBillingInvoices(
            string search,
            string status,
            DateOnly? fromDate,
            DateOnly? toDate)
    {
        var invoices = _db.BillingInvoices.AsNoTracking();

        // Search by patient name/number or exact invoice number.
        if (!string.IsNullOrWhiteSpace(search))
        {
            string escaped = search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            string pattern = "%" + escaped + "%";

            string numberText = search.StartsWith(
                "MC-I-", StringComparison.OrdinalIgnoreCase)
                ? search[5..]
                : search;

            bool hasInvoiceNumber =
                long.TryParse(numberText, out long invoiceNumber);

            invoices = invoices.Where(i =>
                EF.Functions.ILike(
                    _db.Patients
                        .Where(p => p.Id == i.PatientId)
                        .Select(p => p.PatientNumber)
                        .FirstOrDefault()!, pattern, "\\") ||

                _db.Patients
                    .Where(p => p.Id == i.PatientId)
                    .Any(p =>
                        EF.Functions.ILike(
                            p.FirstName, pattern, "\\") ||
                        EF.Functions.ILike(
                            p.LastName, pattern, "\\") ||
                        EF.Functions.ILike(
                            p.FirstName + " " + p.LastName,
                            pattern, "\\")) ||

                (hasInvoiceNumber && i.Number == invoiceNumber));
        }

        // Filter by invoice creation date in Sri Lanka.
        var sriLanka = TimeZoneInfo
            .FindSystemTimeZoneById("Asia/Colombo");

        if (fromDate.HasValue)
        {
            DateTime localStart = fromDate.Value
                .ToDateTime(TimeOnly.MinValue);

            DateTime utcStart = TimeZoneInfo.ConvertTimeToUtc(
                localStart, sriLanka);

            invoices = invoices.Where(i =>
                i.CreatedAtUtc >= utcStart);
        }

        if (toDate.HasValue)
        {
            DateTime localNextDay = toDate.Value
                .AddDays(1)
                .ToDateTime(TimeOnly.MinValue);

            DateTime utcEnd = TimeZoneInfo.ConvertTimeToUtc(
                localNextDay, sriLanka);

            invoices = invoices.Where(i =>
                i.CreatedAtUtc < utcEnd);
        }

        // Payment reversals subtract from received amounts.
        var query =
            from invoice in invoices
            join patient in _db.Patients.AsNoTracking()
                on invoice.PatientId equals patient.Id
            select new BillingReportRowViewModel
            {
                Id = invoice.Id,
                Number = invoice.Number,

                PatientNumber = patient.PatientNumber,
                PatientName =
                    patient.FirstName + " " + patient.LastName,

                State = invoice.State,
                CreatedAtUtc = invoice.CreatedAtUtc,
                Total = invoice.Total,

                NetPaid = _db.BillingPayments
                    .Where(p => p.InvoiceId == invoice.Id)
                    .Sum(p => (decimal?)(
                        p.ReversesId == null
                            ? p.Amount
                            : -p.Amount)) ?? 0m
            };

        query = status switch
        {
            "Draft" => query.Where(i =>
                i.State == InvoiceState.Draft),

            "Issued" => query.Where(i =>
                i.State == InvoiceState.Issued),

            "Voided" => query.Where(i =>
                i.State == InvoiceState.Voided),

            "Unpaid" => query.Where(i =>
                i.State == InvoiceState.Issued &&
                i.NetPaid <= 0m),

            "PartiallyPaid" => query.Where(i =>
                i.State == InvoiceState.Issued &&
                i.NetPaid > 0m &&
                i.NetPaid < i.Total),

            "Paid" => query.Where(i =>
                i.State == InvoiceState.Issued &&
                i.NetPaid >= i.Total),

            _ => query
        };

        return query;
    }

    private static bool ValidBillingFilters(
        string search,
        string status,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (search.Length > 100)
            return false;

        if (status is not (
            "All" or
            "Draft" or
            "Issued" or
            "Voided" or
            "Unpaid" or
            "PartiallyPaid" or
            "Paid"))
        {
            return false;
        }

        if (fromDate.HasValue &&
            toDate.HasValue &&
            fromDate.Value > toDate.Value)
        {
            return false;
        }

        if (toDate == DateOnly.MaxValue)
            return false;

        return true;
    }


    // GET: /Reports/StaffReport
    [HttpGet]
    public async Task<IActionResult> StaffReport(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        int page = 1)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidStaffReportFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid staff report filters.");
        }

        var employees = FilterStaffEmployees(searchTerm, status);

        var attendance = FilterStaffAttendance(fromDate, toDate);
        var leave = FilterStaffLeave(fromDate, toDate);

        const int pageSize = 20;

        int matching = await employees.CountAsync();

        int totalPages = Math.Max(
            1, (int)Math.Ceiling(matching / (double)pageSize));

        page = Math.Clamp(page, 1, totalPages);

        var pageQuery = employees
            .OrderBy(e => e.EmployeeNumber)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var rows = await LoadStaffReportRowsAsync(
            pageQuery, fromDate, toDate);

        // Summaries apply to all matching employees,
        // not only the current page.
        var matchingAttendance = attendance.Where(a =>
            employees.Any(e => e.Id == a.EmployeeId));

        var matchingLeave = leave.Where(l =>
            employees.Any(e => e.Id == l.EmployeeId));

        var leaveCounts = await matchingLeave
            .GroupBy(l => l.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        int CountLeave(string value) =>
            leaveCounts
                .Where(x => x.Status == value)
                .Sum(x => x.Count);

        var model = new StaffReportViewModel
        {
            Employees = rows,
            Search = searchTerm,
            Status = status,
            FromDate = fromDate,
            ToDate = toDate,

            MatchingEmployees = matching,

            ActiveEmployees =
                await employees.CountAsync(e => e.IsActive),

            InactiveEmployees =
                await employees.CountAsync(e => !e.IsActive),

            TotalAttendanceRecords =
                await matchingAttendance.CountAsync(),

            OpenCheckIns = await matchingAttendance
                .CountAsync(a => a.CheckOutAtUtc == null),

            PendingLeaves = CountLeave("Pending"),
            ApprovedLeaves = CountLeave("Approved"),
            RejectedLeaves = CountLeave("Rejected"),

            Page = page,
            TotalPages = totalPages
        };

        return View(model);
    }

    // GET: /Reports/ExportStaffCsv
    [HttpGet]
    public async Task<IActionResult> ExportStaffCsv(
        string? search,
        string status = "All",
        DateOnly? fromDate = null,
        DateOnly? toDate = null)
    {
        string searchTerm = (search ?? "").Trim();

        if (!ValidStaffReportFilters(
            searchTerm, status, fromDate, toDate))
        {
            return BadRequest("Invalid export filters.");
        }

        var employees = FilterStaffEmployees(searchTerm, status);

        const int maximumExportRows = 10000;

        if (await employees.CountAsync() > maximumExportRows)
        {
            return BadRequest(
                "Too many employees. Narrow your filters.");
        }

        var rows = await LoadStaffReportRowsAsync(
            employees
                .OrderBy(e => e.EmployeeNumber)
                .ThenBy(e => e.Id),
            fromDate,
            toDate);

        var csv = new StringBuilder();

        csv.AppendLine(
            "Employee Number,Full Name,Job Title,Department," +
            "Hire Date,Employment Status,Attendance Records," +
            "Open Check-ins,Pending Leave,Approved Leave," +
            "Rejected Leave");

        foreach (var employee in rows)
        {
            csv.Append(CsvCell(employee.EmployeeNumber));
            csv.Append(',');

            csv.Append(CsvCell(employee.FullName));
            csv.Append(',');

            csv.Append(CsvCell(employee.JobTitle));
            csv.Append(',');

            csv.Append(CsvCell(employee.DepartmentName));
            csv.Append(',');

            csv.Append(CsvCell(
                employee.HireDate.ToString("yyyy-MM-dd")));
            csv.Append(',');

            csv.Append(CsvCell(
                employee.IsActive ? "Active" : "Inactive"));
            csv.Append(',');

            csv.Append(employee.AttendanceRecords);
            csv.Append(',');

            csv.Append(employee.OpenCheckIns);
            csv.Append(',');

            csv.Append(employee.PendingLeaves);
            csv.Append(',');

            csv.Append(employee.ApprovedLeaves);
            csv.Append(',');

            csv.Append(employee.RejectedLeaves);
            csv.AppendLine();
        }

        byte[] bytes = Encoding.UTF8.GetBytes(
            "\uFEFF" + csv.ToString());

        return File(
            bytes,
            "text/csv; charset=utf-8",
            $"staff-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    // Search and employment-status filtering.
    private IQueryable<Employee> FilterStaffEmployees(
        string search,
        string status)
    {
        var query = _db.Employees.AsNoTracking();

        if (status == "Active")
        {
            query = query.Where(e => e.IsActive);
        }
        else if (status == "Inactive")
        {
            query = query.Where(e => !e.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string escaped = search
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");

            string pattern = "%" + escaped + "%";

            query = query.Where(e =>
                EF.Functions.ILike(
                    e.EmployeeNumber, pattern, "\\") ||
                EF.Functions.ILike(
                    e.FirstName, pattern, "\\") ||
                EF.Functions.ILike(
                    e.LastName, pattern, "\\") ||
                EF.Functions.ILike(
                    e.FirstName + " " + e.LastName,
                    pattern, "\\"));
        }

        return query;
    }

    // Attendance date range uses WorkDate
    // (already stored as a local calendar date).
    private IQueryable<Attendance> FilterStaffAttendance(
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        var query = _db.Attendances.AsNoTracking();

        if (fromDate.HasValue)
        {
            DateOnly start = fromDate.Value;
            query = query.Where(a => a.WorkDate >= start);
        }

        if (toDate.HasValue)
        {
            DateOnly end = toDate.Value;
            query = query.Where(a => a.WorkDate <= end);
        }

        return query;
    }

    // Leave requests overlapping the selected date range.
    private IQueryable<LeaveRequest> FilterStaffLeave(
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        var query = _db.LeaveRequests.AsNoTracking();

        if (fromDate.HasValue)
        {
            DateOnly start = fromDate.Value;
            query = query.Where(l => l.EndDate >= start);
        }

        if (toDate.HasValue)
        {
            DateOnly end = toDate.Value;
            query = query.Where(l => l.StartDate <= end);
        }

        return query;
    }

    // Load employee rows plus attendance and leave counts.
    private async Task<List<StaffReportRowViewModel>>
        LoadStaffReportRowsAsync(
            IQueryable<Employee> employeeQuery,
            DateOnly? fromDate,
            DateOnly? toDate)
    {
        var rows = await employeeQuery
            .Select(e => new StaffReportRowViewModel
            {
                Id = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                FullName = e.FirstName + " " + e.LastName,
                JobTitle = e.JobTitle,

                DepartmentName = e.Department != null
                    ? e.Department.Name
                    : "Not assigned",

                HireDate = e.HireDate,
                IsActive = e.IsActive
            })
            .ToListAsync();

        if (rows.Count == 0)
            return rows;

        var employeeIds = rows.Select(r => r.Id).ToArray();

        var attendanceCounts = await FilterStaffAttendance(
                fromDate, toDate)
            .Where(a => employeeIds.Contains(a.EmployeeId))
            .GroupBy(a => a.EmployeeId)
            .Select(g => new
            {
                EmployeeId = g.Key,
                Total = g.Count(),
                Open = g.Count(a => a.CheckOutAtUtc == null)
            })
            .ToListAsync();

        var leaveCounts = await FilterStaffLeave(
                fromDate, toDate)
            .Where(l => employeeIds.Contains(l.EmployeeId))
            .GroupBy(l => new { l.EmployeeId, l.Status })
            .Select(g => new
            {
                g.Key.EmployeeId,
                g.Key.Status,
                Total = g.Count()
            })
            .ToListAsync();

        var rowsById = rows.ToDictionary(r => r.Id);

        foreach (var count in attendanceCounts)
        {
            var row = rowsById[count.EmployeeId];

            row.AttendanceRecords = count.Total;
            row.OpenCheckIns = count.Open;
        }

        foreach (var count in leaveCounts)
        {
            var row = rowsById[count.EmployeeId];

            switch (count.Status)
            {
                case "Pending":
                    row.PendingLeaves = count.Total;
                    break;

                case "Approved":
                    row.ApprovedLeaves = count.Total;
                    break;

                case "Rejected":
                    row.RejectedLeaves = count.Total;
                    break;
            }
        }

        return rows;
    }

    private static bool ValidStaffReportFilters(
        string search,
        string status,
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        if (search.Length > 100)
            return false;

        if (status is not ("All" or "Active" or "Inactive"))
            return false;

        if (fromDate.HasValue &&
            toDate.HasValue &&
            fromDate.Value > toDate.Value)
        {
            return false;
        }

        return true;
    }

}