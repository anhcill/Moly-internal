using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class CustomersViewModel : ObservableViewModel
{
    private readonly ApiClient _apiClient;
    private IReadOnlyList<ApiClient.CustomerItem> _customers = Array.Empty<ApiClient.CustomerItem>();
    private IReadOnlyList<ApiClient.CscaStudentDirectoryItem> _students = Array.Empty<ApiClient.CscaStudentDirectoryItem>();
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
        var students = studentsTask.Result;
        Customers = page?.Items ?? Array.Empty<ApiClient.CustomerItem>();
        Students = (IReadOnlyList<ApiClient.CscaStudentDirectoryItem>?)students
            ?? Array.Empty<ApiClient.CscaStudentDirectoryItem>();
        TotalCustomerCount = page?.TotalCount ?? 0;
        return page is not null && students is not null;
    }

}
