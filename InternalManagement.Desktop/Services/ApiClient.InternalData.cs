using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── Dữ liệu nội bộ tách theo mảng & bảng tổng tài chính ──

    public async Task<CompanyFinancialOverviewItem?> GetCompanyFinancialOverviewAsync(
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken ct = default)
    {
        var query = new List<string>();
        if (from.HasValue) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to.HasValue) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        var url = "api/v1/finance/overview" + (query.Count == 0 ? string.Empty : "?" + string.Join("&", query));

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CompanyFinancialOverviewItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<InternalCustomerItem>?> GetInternalCustomersAsync(
        string segment,
        string? search = null,
        string? status = null,
        CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InternalCustomerItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalCustomerItem?> CreateInternalCustomerAsync(string segment, InternalCustomerModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang";
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalCustomerItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalCustomerItem?> UpdateInternalCustomerAsync(string segment, Guid id, InternalCustomerModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang/{id}";
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalCustomerItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteInternalCustomerAsync(string segment, Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.DeleteAsync($"api/v1/noi-bo/{NormalizeSegment(segment)}/khach-hang/{id}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InternalResourceItem>?> GetInternalResourcesAsync(
        string segment,
        string? search = null,
        string? resourceType = null,
        CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(resourceType)) url += $"&resourceType={Uri.EscapeDataString(resourceType)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InternalResourceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalResourceItem?> CreateInternalResourceAsync(string segment, InternalResourceModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu";
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalResourceItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<InternalResourceItem?> UpdateInternalResourceAsync(string segment, Guid id, InternalResourceModel model, CancellationToken ct = default)
    {
        var url = $"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu/{id}";
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync(url, model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<InternalResourceItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteInternalResourceAsync(string segment, Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.DeleteAsync($"api/v1/noi-bo/{NormalizeSegment(segment)}/tai-lieu/{id}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    private static string NormalizeSegment(string segment) => segment.Trim().ToLowerInvariant() switch
    {
        "cong-nghe-giao-duc" => "cong-nghe-giao-duc",
        "thoi-trang" => "thoi-trang",
        _ => throw new ArgumentOutOfRangeException(nameof(segment), "Mảng dữ liệu không hợp lệ.")
    };

}
