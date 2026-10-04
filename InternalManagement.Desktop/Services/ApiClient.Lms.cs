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
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsIntegrationOverviewItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<LmsCourseMappingItem>?> GetLmsCourseMappingsAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/course-mappings?pageSize=100", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<LmsCourseMappingItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<LmsCourseMappingItem?> UpsertLmsCourseMappingAsync(
        Guid courseId,
        LmsCourseMappingRequest request,
        CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PutAsJsonAsync($"api/v1/lms-integration/course-mappings/{courseId}", request, _jsonOptions, ct),
            ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsCourseMappingItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<LmsOutboxItem>?> GetLmsOutboxAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/lms-integration/outbox?pageSize=100", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<LmsOutboxItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RetryLmsOutboxAsync(Guid outboxId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsync($"api/v1/lms-integration/outbox/{outboxId}/retry", null, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<LmsOutboxDispatchResultItem?> DispatchLmsOutboxAsync(int batchSize = 20, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.PostAsync($"api/v1/lms-integration/outbox/dispatch?batchSize={Math.Clamp(batchSize, 1, 100)}", null, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<LmsOutboxDispatchResultItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

}
