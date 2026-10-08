using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public async Task<AttendanceItem?> RecordAttendanceAsync(RecordAttendanceModel model, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() =>
            _httpClient.PostAsJsonAsync("api/v1/attendance", model, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AttendanceItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<AttendanceItem>?> GetAttendanceRecordsAsync(DateOnly? fromDate = null, DateOnly? toDate = null, Guid? departmentId = null, string? businessSegment = null, CancellationToken ct = default)
    {
        var url = "api/v1/attendance?pageSize=100";
        if (fromDate.HasValue) url += $"&fromDate={fromDate.Value:yyyy-MM-dd}";
        if (toDate.HasValue) url += $"&toDate={toDate.Value:yyyy-MM-dd}";
        if (departmentId.HasValue) url += $"&departmentId={departmentId.Value}";
        if (!string.IsNullOrWhiteSpace(businessSegment)) url += $"&businessSegment={Uri.EscapeDataString(businessSegment)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<AttendanceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<AttendanceSummaryItem?> GetAttendanceSummaryAsync(DateOnly? fromDate = null, DateOnly? toDate = null, Guid? departmentId = null, string? businessSegment = null, CancellationToken ct = default)
    {
        var url = "api/v1/attendance/summary";
        var query = new List<string>();
        if (fromDate.HasValue) query.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
        if (toDate.HasValue) query.Add($"toDate={toDate.Value:yyyy-MM-dd}");
        if (departmentId.HasValue) query.Add($"departmentId={departmentId.Value}");
        if (!string.IsNullOrWhiteSpace(businessSegment)) query.Add($"businessSegment={Uri.EscapeDataString(businessSegment)}");
        if (query.Count > 0) url += "?" + string.Join("&", query);

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AttendanceSummaryItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<AttendanceImportResultItem?> ImportAttendanceFileAsync(string filePath, string? businessSegment = null, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);
        using var streamContent = new StreamContent(fileStream);
        form.Add(streamContent, "file", Path.GetFileName(filePath));
        if (!string.IsNullOrWhiteSpace(businessSegment))
        {
            form.Add(new StringContent(businessSegment), "businessSegment");
        }

        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsync("api/v1/attendance/import", form, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AttendanceImportResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<DownloadedFile?> DownloadAttendanceImportTemplateAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/attendance/template", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var fileName = contentDisposition?.FileNameStar ?? contentDisposition?.FileName?.Trim('"');
        return new DownloadedFile(Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "mau-nhap-cham-cong.xlsx" : fileName),
            await response.Content.ReadAsByteArrayAsync(ct));
    }

}
