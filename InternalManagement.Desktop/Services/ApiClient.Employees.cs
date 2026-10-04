using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public async Task<PaginatedData<EmployeeItem>?> GetEmployeesAsync(
        string? search = null,
        Guid? departmentId = null,
        string? status = null,
        string? businessSegment = null,
        CancellationToken ct = default,
        int pageIndex = 1,
        int pageSize = 200)
    {
        var url = $"api/v1/employees?pageIndex={pageIndex}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId.Value}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(businessSegment)) url += $"&businessSegment={Uri.EscapeDataString(businessSegment)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<EmployeeItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeeItem?> GetEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/employees/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<EmployeeItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeePaymentDetails?> GetEmployeePaymentDetailsAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() =>
            _httpClient.GetAsync($"api/v1/employees/{id}/payment-details", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<EmployeePaymentDetails>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<List<DepartmentItem>?> GetDepartmentsAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/departments", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<DepartmentItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeeItem?> CreateEmployeeAsync(CreateEmployeeModel model, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/employees", model, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;
        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<EmployeeItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<EmployeeItem?> UpdateEmployeeAsync(Guid id, UpdateEmployeeModel model, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/employees/{id}", model, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;
        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<EmployeeItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteEmployeeAsync(Guid id, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/employees/{id}", ct), ct);
        return res.IsSuccessStatusCode;
    }

}
