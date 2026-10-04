using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public async Task<PaginatedData<PayrollPeriodItem>?> GetPayrollPeriodsAsync(int? year = null, string? status = null, string? businessSegment = null, CancellationToken ct = default)
    {
        var url = "api/v1/payroll/periods?pageSize=50";
        if (year.HasValue) url += $"&year={year.Value}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(businessSegment)) url += $"&businessSegment={Uri.EscapeDataString(businessSegment)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PayrollPeriodItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> CreatePayrollPeriodAsync(CreatePayrollPeriodModel model, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsJsonAsync("api/v1/payroll/periods", model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> CancelPayrollPeriodAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/cancel", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<DownloadedFile?> DownloadPayrollPeriodXlsxAsync(Guid periodId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.GetAsync($"api/v1/payroll-exports/periods/{periodId}/xlsx", ct), ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var content = await response.Content.ReadAsByteArrayAsync(ct);
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "bang-luong.xlsx" : fileName), content);
    }

    public async Task<PayrollPeriodDetailItem?> GetPayrollPeriodDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/payroll/periods/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollCalculationResultItem?> CalculatePayrollAsync(Guid periodId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsync($"api/v1/payroll/periods/{periodId}/calculate", null, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollCalculationResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<PayslipItem>?> GetPayslipsAsync(Guid periodId, Guid? departmentId = null, string? search = null, CancellationToken ct = default)
    {
        var url = $"api/v1/payroll/periods/{periodId}/payslips?pageSize=100";
        if (departmentId.HasValue) url += $"&departmentId={departmentId.Value}";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PayslipItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<PayrollAdjustmentItem>?> GetPayrollAdjustmentsAsync(Guid periodId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/payroll/periods/{periodId}/adjustments", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<PayrollAdjustmentItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollAdjustmentItem?> AddPayrollAdjustmentAsync(Guid periodId, CreatePayrollAdjustmentModel model, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/adjustments", model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollAdjustmentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> DeletePayrollAdjustmentAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/payroll/adjustments/{adjustmentId}", ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PayrollPeriodItem?> SubmitPayrollForReviewAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/submit-review", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> ApprovePayrollAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/approve", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> MarkPayrollPaidAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/mark-paid", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollPeriodItem?> PublishPayrollAsync(Guid periodId, string? comments = null, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/publish", new { Comments = comments }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollPeriodItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<PayslipItem>?> GetMyPayslipsAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/payroll/my-payslips?pageSize=50", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PayslipItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

}
