using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public async Task<PaginatedData<InterviewCustomerItem>?> GetInterviewCustomersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/interview/customers?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InterviewCustomerItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InterviewFinancialSummaryItem?> GetInterviewFinancialSummaryAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/interview/financial-summary", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InterviewFinancialSummaryItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateInterviewCustomerAsync(string fullName, string email, string phone, string packageName, int sessions, decimal paidAmount, CancellationToken ct = default)
    {
        var req = new { FullName = fullName, Email = email, Phone = phone, PackageName = packageName, SessionCount = sessions, PaidAmount = paidAmount, Status = 1 };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/interview/customers", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<List<BusinessUnitProfitItem>?> GetBusinessUnitProfitSummaryAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/finance/profit-summary", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<BusinessUnitProfitItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

}
