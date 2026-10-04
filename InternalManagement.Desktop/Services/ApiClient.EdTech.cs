using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── EdTech API Calls (Ngày 8 & 9) ──

    public async Task<PaginatedData<CourseItem>?> GetCoursesAsync(string? search = null, string? status = null, CancellationToken ct = default)
    {
        var url = $"api/v1/edtech/courses?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CourseItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateCourseAsync(string title, decimal price, string description, string? status = "Published", string? slug = null, CancellationToken ct = default)
    {
        var req = new { Title = title, Price = price, Description = description, Status = status ?? "Published", Slug = slug };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/edtech/courses", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateCourseAsync(Guid id, string title, decimal price, string description, string status, string? slug = null, CancellationToken ct = default)
    {
        var req = new { Title = title, Price = price, Description = description, Status = status, Slug = slug };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/edtech/courses/{id}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteCourseAsync(Guid id, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/edtech/courses/{id}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<QuestionItem>?> GetQuestionsAsync(string? search = null, string? difficulty = null, string? status = null, CancellationToken ct = default)
    {
        var url = $"api/v1/edtech/questions?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(difficulty)) url += $"&difficulty={Uri.EscapeDataString(difficulty)}";
        if (!string.IsNullOrWhiteSpace(status)) url += $"&status={Uri.EscapeDataString(status)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<QuestionItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> PublishQuestionVersionAsync(Guid versionId, string notes, CancellationToken ct = default)
    {
        var req = new { Notes = notes };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/edtech/questions/versions/{versionId}/publish", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<CustomerItem>?> GetCustomersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = $"api/v1/edtech/customers?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<CustomerItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

}
