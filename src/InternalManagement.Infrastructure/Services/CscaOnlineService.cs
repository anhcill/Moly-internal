using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;

namespace InternalManagement.Infrastructure.Services;

/// <summary>
/// Read-only bridge for public CSCA-MOLI.STUDIO content.
/// Private admin resources still require the configured integration key/token.
/// </summary>
public sealed class CscaOnlineService : ICscaOnlineService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CscaOnlineService> _logger;
    private readonly string? _bearerToken;
    private readonly string? _integrationKey;
    private readonly string _materialsPath;
    private readonly string _vocabularyPath;
    private readonly string _postsPath;

    public CscaOnlineService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CscaOnlineService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;

        var section = configuration.GetSection("Integrations:CscaMoliStudio");
        var baseUrl = section["BaseUrl"] ?? "https://csca-molistudio-production.up.railway.app";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl))
            throw new InvalidOperationException("Invalid CSCA-MOLI.STUDIO BaseUrl.");

        _httpClient.BaseAddress = parsedBaseUrl;
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        _bearerToken = section["BearerToken"] ?? configuration["CSCA_MOLI_STUDIO_BEARER_TOKEN"];
        _integrationKey = section["IntegrationKey"] ?? configuration["CSCA_MOLI_STUDIO_INTEGRATION_KEY"];
        _materialsPath = section["MaterialsPath"] ?? "/api/materials";
        _vocabularyPath = section["VocabularyPath"] ?? "/api/vocabulary";
        _postsPath = section["PostsPath"] ?? "/api/posts";
    }

    public async Task<IReadOnlyList<CscaOnlineMaterialDto>> GetMaterialsAsync(
        string? search, int pageSize, CancellationToken ct)
    {
        var items = await GetItemsAsync<ExternalMaterial>(_materialsPath, pageSize, ct);
        return items
            .Where(item => Matches(search, item.Title, item.Description, item.Subject, item.Topic))
            .Select(item => new CscaOnlineMaterialDto
            {
                Id = item.Id,
                Title = item.Title ?? string.Empty,
                Description = item.Description,
                Subject = item.Subject,
                Topic = item.Topic,
                FileType = item.FileType,
                FileUrl = item.FileUrl,
                ThumbnailUrl = item.ThumbnailUrl,
                ViewCount = item.ViewCount,
                DownloadCount = item.DownloadCount,
                IsPremium = item.IsPremium ?? false,
                VipTier = item.VipTier,
                UpdatedAt = item.UpdatedAt
            })
            .ToList();
    }

    public async Task<IReadOnlyList<CscaOnlineVocabularyDto>> GetVocabularyAsync(
        string? search, int pageSize, CancellationToken ct)
    {
        var items = await GetItemsAsync<ExternalVocabulary>(_vocabularyPath, pageSize, ct);
        return items
            .Where(item => Matches(search, item.WordChinese, item.WordVietnamese, item.WordEnglish, item.Subject, item.Topic))
            .Select(item => new CscaOnlineVocabularyDto
            {
                Id = item.Id,
                WordChinese = item.WordChinese ?? string.Empty,
                Pinyin = item.Pinyin,
                WordVietnamese = item.WordVietnamese,
                WordEnglish = item.WordEnglish,
                Subject = item.Subject,
                Topic = item.Topic,
                ExampleChinese = item.ExampleChinese,
                ExampleVietnamese = item.ExampleVietnamese,
                IsPremium = item.IsPremium,
                VipTier = item.VipTier
            })
            .ToList();
    }

    public async Task<IReadOnlyList<CscaOnlinePostDto>> GetPostsAsync(
        string? search, int pageSize, CancellationToken ct)
    {
        var items = await GetItemsAsync<ExternalPost>(_postsPath, pageSize, ct);
        return items
            .Where(item => Matches(search, item.Content, item.AuthorName, item.PostType, item.ModerationStatus))
            .Select(item => new CscaOnlinePostDto
            {
                Id = item.Id,
                Content = item.Content ?? string.Empty,
                ImageUrl = item.ImageUrl,
                PostType = item.PostType,
                IsOfficial = item.IsOfficial,
                ModerationStatus = item.ModerationStatus,
                AuthorName = item.AuthorName,
                AuthorRole = item.AuthorRole,
                LikeCount = item.LikeCount,
                CommentCount = item.CommentCount,
                CreatedAt = item.CreatedAt
            })
            .ToList();
    }

    private async Task<IReadOnlyList<T>> GetItemsAsync<T>(string path, int pageSize, CancellationToken ct)
    {
        var limit = Math.Clamp(pageSize, 1, 100);
        var separator = path.Contains('?') ? '&' : '?';
        var requestPath = $"{path}{separator}page=1&limit={limit}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestPath);
        if (!string.IsNullOrWhiteSpace(_bearerToken))
        {
            var token = _bearerToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? _bearerToken[7..]
                : _bearerToken;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (!string.IsNullOrWhiteSpace(_integrationKey))
            request.Headers.TryAddWithoutValidation("X-Integration-Key", _integrationKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("CSCA online endpoint {Path} returned HTTP {StatusCode}", requestPath, (int)response.StatusCode);
            throw new HttpRequestException($"CSCA online endpoint returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);
        if (!TryFindDataArray(document.RootElement, out var data))
            return [];

        return JsonSerializer.Deserialize<List<T>>(data.GetRawText(), JsonOptions) ?? [];
    }

    private static bool TryFindDataArray(JsonElement root, out JsonElement data)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            data = root;
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in new[] { "data", "items", "results" })
            {
                if (!root.TryGetProperty(name, out var value))
                    continue;
                if (value.ValueKind == JsonValueKind.Array)
                {
                    data = value;
                    return true;
                }
                if (value.ValueKind == JsonValueKind.Object)
                    return TryFindDataArray(value, out data);
            }
        }

        data = default;
        return false;
    }

    private static bool Matches(string? search, params string?[] values)
        => string.IsNullOrWhiteSpace(search)
           || values.Any(value => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class ExternalMaterial
    {
        public int Id { get; init; }
        public string? Title { get; init; }
        public string? Description { get; init; }
        public string? Subject { get; init; }
        public string? Topic { get; init; }
        [JsonPropertyName("file_type")] public string? FileType { get; init; }
        [JsonPropertyName("file_url")] public string? FileUrl { get; init; }
        [JsonPropertyName("thumbnail_url")] public string? ThumbnailUrl { get; init; }
        [JsonPropertyName("view_count")] public int ViewCount { get; init; }
        [JsonPropertyName("download_count")] public int DownloadCount { get; init; }
        [JsonPropertyName("is_premium")] public bool? IsPremium { get; init; }
        [JsonPropertyName("vip_tier")] public string? VipTier { get; init; }
        [JsonPropertyName("updated_at")] public DateTime? UpdatedAt { get; init; }
    }

    private sealed class ExternalVocabulary
    {
        public int Id { get; init; }
        [JsonPropertyName("word_cn")] public string? WordChinese { get; init; }
        public string? Pinyin { get; init; }
        [JsonPropertyName("word_vn")] public string? WordVietnamese { get; init; }
        [JsonPropertyName("word_en")] public string? WordEnglish { get; init; }
        public string? Subject { get; init; }
        public string? Topic { get; init; }
        [JsonPropertyName("example_cn")] public string? ExampleChinese { get; init; }
        [JsonPropertyName("example_vn")] public string? ExampleVietnamese { get; init; }
        [JsonPropertyName("is_premium")] public bool IsPremium { get; init; }
        [JsonPropertyName("vip_tier")] public string? VipTier { get; init; }
    }

    private sealed class ExternalPost
    {
        public int Id { get; init; }
        public string? Content { get; init; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; init; }
        [JsonPropertyName("post_type")] public string? PostType { get; init; }
        [JsonPropertyName("is_official")] public bool IsOfficial { get; init; }
        [JsonPropertyName("moderation_status")] public string? ModerationStatus { get; init; }
        [JsonPropertyName("author_name")] public string? AuthorName { get; init; }
        [JsonPropertyName("author_role")] public string? AuthorRole { get; init; }
        [JsonPropertyName("like_count")] public int LikeCount { get; init; }
        [JsonPropertyName("comment_count")] public int CommentCount { get; init; }
        [JsonPropertyName("created_at")] public DateTime? CreatedAt { get; init; }
    }
}
