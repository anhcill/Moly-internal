using System.Net.Http.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public async Task<IReadOnlyList<PayrollWorkEntryItem>?> GetPayrollWorkEntriesAsync(
        Guid periodId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() =>
            _httpClient.GetAsync($"api/v1/payroll/periods/{periodId}/work-entries", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<PayrollWorkEntryItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PayrollWorkEntryItem?> AddPayrollWorkEntryAsync(
        Guid periodId, CreatePayrollWorkEntryModel model, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() =>
            _httpClient.PostAsJsonAsync($"api/v1/payroll/periods/{periodId}/work-entries", model, _jsonOptions, ct), ct);
        if (!await CheckManagementResponseAsync(response, "Ghi công việc tính lương", ct)) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PayrollWorkEntryItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> VoidPayrollWorkEntryAsync(Guid entryId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() =>
            _httpClient.PostAsync($"api/v1/payroll/work-entries/{entryId}/void", null, ct), ct);
        return await CheckManagementResponseAsync(response, "Hủy công việc tính lương", ct);
    }
}
