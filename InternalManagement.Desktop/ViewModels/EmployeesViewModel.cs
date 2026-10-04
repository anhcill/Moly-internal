using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class EmployeesViewModel : ObservableViewModel
{
    private readonly ApiClient _apiClient;
    private string _searchText = string.Empty;
    private Guid? _selectedDepartmentId;
    private IReadOnlyList<DepartmentOption> _departmentOptions = [new(null, "-- Tất Cả Phòng Ban --")];
    private IReadOnlyList<ApiClient.DepartmentItem> _departments = [];
    private IReadOnlyList<ApiClient.EmployeeItem> _workingEmployees = [];
    private IReadOnlyList<ApiClient.EmployeeItem> _profiles = [];
    private IReadOnlyList<ApiClient.EmployeeItem> _missingCv = [];
    private IReadOnlyList<ApiClient.EmployeeItem> _resigned = [];
    private IReadOnlyList<ApiClient.EmployeeItem> _blacklisted = [];
    private int _totalCount;
    private bool _hasLoaded;

    public EmployeesViewModel(ApiClient apiClient) => _apiClient = apiClient;

    public sealed record DepartmentOption(Guid? Id, string Name);

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    public Guid? SelectedDepartmentId
    {
        get => _selectedDepartmentId;
        set => Set(ref _selectedDepartmentId, value);
    }

    public IReadOnlyList<DepartmentOption> DepartmentOptions
    {
        get => _departmentOptions;
        private set => Set(ref _departmentOptions, value);
    }

    public IReadOnlyList<ApiClient.DepartmentItem> Departments
    {
        get => _departments;
        private set => Set(ref _departments, value);
    }

    public IReadOnlyList<ApiClient.EmployeeItem> WorkingEmployees
    {
        get => _workingEmployees;
        private set { Set(ref _workingEmployees, value); RaiseEmptyStates(); }
    }

    public IReadOnlyList<ApiClient.EmployeeItem> Profiles
    {
        get => _profiles;
        private set { Set(ref _profiles, value); RaiseEmptyStates(); }
    }

    public IReadOnlyList<ApiClient.EmployeeItem> MissingCv
    {
        get => _missingCv;
        private set { Set(ref _missingCv, value); RaiseEmptyStates(); }
    }

    public IReadOnlyList<ApiClient.EmployeeItem> Resigned
    {
        get => _resigned;
        private set { Set(ref _resigned, value); RaiseEmptyStates(); }
    }

    public IReadOnlyList<ApiClient.EmployeeItem> Blacklisted
    {
        get => _blacklisted;
        private set { Set(ref _blacklisted, value); RaiseEmptyStates(); }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => Set(ref _totalCount, value);
    }

    public int ActiveCount => WorkingEmployees.Count;
    public int ResignedCount => Resigned.Count;
    public int BlacklistedCount => Blacklisted.Count;
    public bool IsWorkingEmpty => _hasLoaded && WorkingEmployees.Count == 0;
    public bool IsProfilesEmpty => _hasLoaded && Profiles.Count == 0;
    public bool IsMissingCvEmpty => _hasLoaded && MissingCv.Count == 0;
    public bool IsResignedEmpty => _hasLoaded && Resigned.Count == 0;
    public bool IsBlacklistedEmpty => _hasLoaded && Blacklisted.Count == 0;

    public async Task<bool> LoadDepartmentsAsync(CancellationToken ct = default)
    {
        var departments = await _apiClient.GetDepartmentsAsync(ct);
        if (departments is null) return false;

        Departments = departments;
        DepartmentOptions = [new(null, "-- Tất Cả Phòng Ban --"),
            .. departments.Select(d => new DepartmentOption(d.Id, $"{d.Code} - {d.Name}"))];
        if (SelectedDepartmentId.HasValue && departments.All(d => d.Id != SelectedDepartmentId.Value))
            SelectedDepartmentId = null;
        return true;
    }

    public async Task<bool> LoadAsync(string businessSegment, CancellationToken ct = default)
    {
        var result = await _apiClient.GetEmployeesAsync(
            SearchText.Trim(), SelectedDepartmentId, businessSegment: businessSegment, ct: ct);
        if (result is null)
        {
            Clear();
            return false;
        }

        var items = result.Items;
        WorkingEmployees = items.Where(e => e.Status is "Active" or "Probation" or "OnLeave" ||
            string.IsNullOrWhiteSpace(e.Status)).ToArray();
        Profiles = items.ToArray();
        MissingCv = WorkingEmployees.Where(e => string.IsNullOrWhiteSpace(e.CvUrlOrPath)).ToArray();
        Resigned = items.Where(e => e.Status is "Resigned" or "Inactive").ToArray();
        Blacklisted = items.Where(e => e.Status == "Blacklisted").ToArray();
        TotalCount = result.TotalCount;
        _hasLoaded = true;
        RaiseEmptyStates();
        return true;
    }

    private void Clear()
    {
        _hasLoaded = false;
        WorkingEmployees = [];
        Profiles = [];
        MissingCv = [];
        Resigned = [];
        Blacklisted = [];
        TotalCount = 0;
        RaiseEmptyStates();
    }

    private void RaiseEmptyStates()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(ResignedCount));
        OnPropertyChanged(nameof(BlacklistedCount));
        OnPropertyChanged(nameof(IsWorkingEmpty));
        OnPropertyChanged(nameof(IsProfilesEmpty));
        OnPropertyChanged(nameof(IsMissingCvEmpty));
        OnPropertyChanged(nameof(IsResignedEmpty));
        OnPropertyChanged(nameof(IsBlacklistedEmpty));
    }
}
