using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── System Sync API Calls (Ngày 6 & 8) ──

    public async Task<SyncTriggerResult?> TriggerSyncAsync(string sourceSystem, string entityType, CancellationToken ct = default)
    {
        var req = new { SourceSystem = sourceSystem, EntityType = entityType, ForceFullSync = false };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/system/sync/trigger", req, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;

        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<SyncTriggerResult>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<SyncRunItem>?> GetSyncRunsAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync("api/v1/system/sync/runs?pageSize=50", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<SyncRunItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<DeadLetterItem>?> GetDeadLettersAsync(bool? resolved = false, CancellationToken ct = default)
    {
        var resolvedQuery = resolved.HasValue ? $"&resolved={resolved.Value.ToString().ToLowerInvariant()}" : string.Empty;
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/system/sync/dead-letters?pageSize=50{resolvedQuery}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<DeadLetterItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RetryDeadLetterAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsync($"api/v1/system/sync/dead-letters/{id}/retry", null, ct), ct);
        return response.IsSuccessStatusCode;
    }

}
