using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── CSCA Course LMS operations ──

    public async Task<LmsIntegrationOverviewItem?> GetLmsIntegrationOverviewAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/overview", ct), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadLmsFailureAsync(response, "Tải tổng quan đồng bộ", ct));

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsIntegrationOverviewItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<LmsCourseMappingItem>?> GetLmsCourseMappingsAsync(CancellationToken ct = default)
    {
        var items = new List<LmsCourseMappingItem>();
        var page = 1;
        var totalCount = 0;
        do
        {
            using var response = await SendWithRefreshAsync(
                () => _httpClient.GetAsync($"api/v1/lms-integration/course-mappings?pageIndex={page}&pageSize=200", ct), ct);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadLmsFailureAsync(response, "Tải danh sách khóa học đồng bộ", ct));
            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<LmsCourseMappingItem>>>(_jsonOptions, ct);
            if (envelope?.Data is null) return null;
            totalCount = envelope.Data.TotalCount;
            if (envelope.Data.Items.Count == 0) break;
            items.AddRange(envelope.Data.Items);
            page++;
        } while (items.Count < totalCount);
        return new PaginatedData<LmsCourseMappingItem>(items, totalCount, 1, items.Count);
    }

    public async Task<LmsCourseMappingItem?> UpsertLmsCourseMappingAsync(
        Guid courseId,
        LmsCourseMappingRequest request,
        CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PutAsJsonAsync($"api/v1/lms-integration/course-mappings/{courseId}", request, _jsonOptions, ct),
            ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadLmsFailureAsync(response, "Lưu liên kết khóa học", ct));

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsCourseMappingItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<LmsOutboxItem>?> GetLmsOutboxAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/outbox?pageSize=100", ct), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadLmsFailureAsync(response, "Tải tiến trình đồng bộ", ct));

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<LmsOutboxItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RetryLmsOutboxAsync(Guid outboxId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsync($"api/v1/lms-integration/outbox/{outboxId}/retry", null, ct), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadLmsFailureAsync(response, "Thử lại lệnh đồng bộ", ct));
        return true;
    }

    public async Task<LmsOutboxDispatchResultItem?> DispatchLmsOutboxAsync(int batchSize = 20, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsync($"api/v1/lms-integration/outbox/dispatch?batchSize={Math.Clamp(batchSize, 1, 100)}", null, ct), ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadLmsFailureAsync(response, "Gửi dữ liệu sang Web Course", ct));

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsOutboxDispatchResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    private static async Task<string> ReadLmsFailureAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        string? detail = null;
        string? requestId = null;
        string? suggestion = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("message", out var message) &&
                message.ValueKind == JsonValueKind.String)
                detail = message.GetString();
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("detail", out var problemDetail) &&
                problemDetail.ValueKind == JsonValueKind.String)
                detail = problemDetail.GetString();
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("correlationId", out var correlationId) &&
                correlationId.ValueKind == JsonValueKind.String)
                requestId = correlationId.GetString();
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("suggestion", out var suggestionValue) &&
                suggestionValue.ValueKind == JsonValueKind.String)
                suggestion = suggestionValue.GetString();
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array)
            {
                var errorDetails = errors.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(item => !string.IsNullOrWhiteSpace(item));
                var joined = string.Join("; ", errorDetails);
                if (!string.IsNullOrWhiteSpace(joined))
                    detail = string.IsNullOrWhiteSpace(detail) ? joined : $"{detail} {joined}";
            }
        }
        catch (JsonException)
        {
            if (response.Content.Headers.ContentType?.MediaType == "text/plain")
                detail = body;
        }

        detail = string.IsNullOrWhiteSpace(detail) ? response.ReasonPhrase : detail.Trim();
        if (string.IsNullOrWhiteSpace(requestId) &&
            response.Headers.TryGetValues("X-Correlation-ID", out var values))
            requestId = values.FirstOrDefault();
        return $"{operation} thất bại (HTTP {(int)response.StatusCode}): {detail}" +
            (string.IsNullOrWhiteSpace(suggestion) ? string.Empty : $". Cách xử lý: {suggestion}") +
            (string.IsNullOrWhiteSpace(requestId) ? string.Empty : $". Mã yêu cầu: {requestId}");
    }

}
