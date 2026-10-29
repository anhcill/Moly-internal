using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class CscaStudentGroupItem
{
    public required string StudentKey { get; init; }
    public required string StudentName { get; init; }
    public string? StudentCode { get; init; }
    public string? PhoneNumber { get; init; }
    public string? Email { get; init; }
    public int? Age { get; init; }
    public string? Hometown { get; init; }
    public required string ClassesSummary { get; init; }
    public required string CoursesSummary { get; init; }
    public int ClassCount { get; init; }
    public decimal TotalTuitionFee { get; init; }
    public decimal TotalDiscountAmount { get; init; }
    public decimal TotalPayableAmount { get; init; }
    public decimal TotalPaidAmount { get; init; }
    public decimal TotalDebtAmount { get; init; }
    public DateTime? LatestDebtDueDate { get; init; }
    public DateTime JoinedAt { get; init; }
    public string? Notes { get; init; }
    public bool IsTrial { get; init; }
    public required IReadOnlyList<ApiClient.CscaStudentDirectoryItem> Enrollments { get; init; }

    public ApiClient.CscaStudentDirectoryItem PrimaryEnrollment =>
        Enrollments.OrderByDescending(e => e.JoinedAt).First();

    public string DebtStatusDisplay =>
        TotalDebtAmount <= 0 ? "Đã hoàn thành" : $"Nợ {TotalDebtAmount:N0} đ";
}

public sealed class CustomersViewModel : ObservableViewModel
{
    private readonly ApiClient _apiClient;
    private IReadOnlyList<ApiClient.CustomerItem> _customers = Array.Empty<ApiClient.CustomerItem>();
    private IReadOnlyList<ApiClient.CscaStudentDirectoryItem> _students = Array.Empty<ApiClient.CscaStudentDirectoryItem>();
    private IReadOnlyList<CscaStudentGroupItem> _paidStudents = Array.Empty<CscaStudentGroupItem>();
    private IReadOnlyList<CscaStudentGroupItem> _trialStudents = Array.Empty<CscaStudentGroupItem>();
    private int _totalCustomerCount;

    public CustomersViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IReadOnlyList<ApiClient.CustomerItem> Customers
    {
        get => _customers;
        private set => Set(ref _customers, value);
    }

    public IReadOnlyList<ApiClient.CscaStudentDirectoryItem> Students
    {
        get => _students;
        private set => Set(ref _students, value);
    }

    public IReadOnlyList<CscaStudentGroupItem> PaidStudents
    {
        get => _paidStudents;
        private set => Set(ref _paidStudents, value);
    }

    public IReadOnlyList<CscaStudentGroupItem> TrialStudents
    {
        get => _trialStudents;
        private set => Set(ref _trialStudents, value);
    }

    public int TotalCustomerCount
    {
        get => _totalCustomerCount;
        private set => Set(ref _totalCustomerCount, value);
    }

    public async Task<bool> LoadAsync(string? search = null, CancellationToken ct = default)
    {
        var customersTask = _apiClient.GetCustomersAsync(search, ct);
        var studentsTask = _apiClient.GetCscaStudentDirectoryAsync(search, ct);
        await Task.WhenAll(customersTask, studentsTask);

        var page = customersTask.Result;
        var rawStudents = studentsTask.Result ?? new List<ApiClient.CscaStudentDirectoryItem>();

        Customers = page?.Items ?? Array.Empty<ApiClient.CustomerItem>();
        Students = (IReadOnlyList<ApiClient.CscaStudentDirectoryItem>)rawStudents;
        TotalCustomerCount = page?.TotalCount ?? 0;

        // Tách biệt học viên chính thức (có học phí) và học viên học thử (miễn phí)
        // Deduplicate: Mỗi học viên chỉ xuất hiện 1 lần trong mỗi danh sách, gộp các lớp học và cộng dồn học phí/nợ
        var paidRaw = rawStudents.Where(s => !s.IsTrial && s.TuitionFee > 0);
        var trialRaw = rawStudents.Where(s => s.IsTrial || s.TuitionFee == 0);

        PaidStudents = GroupStudentList(paidRaw, isTrial: false);
        TrialStudents = GroupStudentList(trialRaw, isTrial: true);

        return page is not null && studentsTask.Result is not null;
    }

    private static List<CscaStudentGroupItem> GroupStudentList(IEnumerable<ApiClient.CscaStudentDirectoryItem> items, bool isTrial)
    {
        return items
            .GroupBy(s =>
            {
                if (!string.IsNullOrWhiteSpace(s.PhoneNumber))
                    return "P:" + s.PhoneNumber.Trim();
                if (!string.IsNullOrWhiteSpace(s.Email))
                    return "E:" + s.Email.Trim().ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(s.StudentCode))
                    return "C:" + s.StudentCode.Trim().ToUpperInvariant();
                return "N:" + s.StudentName.Trim().ToLowerInvariant();
            })
            .Select(g =>
            {
                var enrollments = g.OrderByDescending(e => e.JoinedAt).ToList();
                var primary = enrollments[0];
                var classCodes = enrollments.Select(e => e.ClassCode).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct();
                var courseTitles = enrollments.Select(e => e.CourseTitle).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct();

                return new CscaStudentGroupItem
                {
                    StudentKey = g.Key,
                    StudentName = primary.StudentName,
                    StudentCode = primary.StudentCode,
                    PhoneNumber = primary.PhoneNumber,
                    Email = primary.Email,
                    Age = primary.Age,
                    Hometown = primary.Hometown,
                    ClassesSummary = string.Join(", ", classCodes),
                    CoursesSummary = string.Join(", ", courseTitles),
                    ClassCount = enrollments.Count,
                    TotalTuitionFee = enrollments.Sum(e => e.TuitionFee),
                    TotalDiscountAmount = enrollments.Sum(e => e.DiscountAmount),
                    TotalPayableAmount = enrollments.Sum(e => e.PayableAmount),
                    TotalPaidAmount = enrollments.Sum(e => e.PaidAmount),
                    TotalDebtAmount = enrollments.Sum(e => e.DebtAmount),
                    LatestDebtDueDate = enrollments.Where(e => e.DebtDueDate.HasValue).Select(e => e.DebtDueDate).OrderByDescending(d => d).FirstOrDefault(),
                    JoinedAt = primary.JoinedAt,
                    Notes = primary.Notes,
                    IsTrial = isTrial,
                    Enrollments = enrollments
                };
            })
            .OrderBy(s => s.StudentName)
            .ToList();
    }
}
