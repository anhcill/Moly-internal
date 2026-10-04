using System.Globalization;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class InterviewCustomersViewModel : ObservableViewModel
{
    private readonly ApiClient _apiClient;
    private IReadOnlyList<ApiClient.InterviewCustomerItem> _items = Array.Empty<ApiClient.InterviewCustomerItem>();
    private string _customerCount = "0";
    private string _sessionCount = "0";
    private string _revenueText = "0 đ";

    public InterviewCustomersViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IReadOnlyList<ApiClient.InterviewCustomerItem> Items
    {
        get => _items;
        private set => Set(ref _items, value);
    }

    public string CustomerCount
    {
        get => _customerCount;
        private set => Set(ref _customerCount, value);
    }

    public string SessionCount
    {
        get => _sessionCount;
        private set => Set(ref _sessionCount, value);
    }

    public string RevenueText
    {
        get => _revenueText;
        private set => Set(ref _revenueText, value);
    }

    public async Task<bool> LoadAsync(string? search = null, CancellationToken ct = default)
    {
        var customersTask = _apiClient.GetInterviewCustomersAsync(search, ct);
        var summaryTask = _apiClient.GetInterviewFinancialSummaryAsync(ct);
        await Task.WhenAll(customersTask, summaryTask);

        var page = customersTask.Result;
        if (page is null)
        {
            Items = Array.Empty<ApiClient.InterviewCustomerItem>();
            CustomerCount = "0";
            SessionCount = "0";
            RevenueText = "0 đ";
            return false;
        }

        Items = page.Items;
        var summary = summaryTask.Result;
        CustomerCount = (summary?.TotalCustomers ?? page.TotalCount).ToString(CultureInfo.CurrentCulture);
        SessionCount = (summary?.TotalSessions ?? page.Items.Sum(item => item.SessionCount)).ToString(CultureInfo.CurrentCulture);
        var totalRevenue = summary?.TotalRevenue ?? page.Items.Sum(item => item.PaidAmount);
        RevenueText = $"{totalRevenue:N0} đ";
        return true;
    }

}
