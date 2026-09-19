using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;

namespace InternalManagement.Infrastructure.Storage;

public sealed class CloudinaryFileStorageService : IFileStorageService
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf"
    };

    private readonly HttpClient _httpClient;
    private readonly CloudinarySettings _settings;
    private readonly ILogger<CloudinaryFileStorageService> _logger;

    public CloudinaryFileStorageService(
        HttpClient httpClient,
        IOptions<CloudinarySettings> options,
        ILogger<CloudinaryFileStorageService> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public bool IsConfigured => _settings.IsConfigured;

    public async Task<Result<StoredFileResult>> UploadAsync(
        Stream content,
        string fileName,
        string? contentType,
        long length,
        string folder,
        CancellationToken ct)
    {
        if (!IsConfigured)
            return Result<StoredFileResult>.Failure("Cloudinary chưa được cấu hình trên API. Hãy thêm Cloudinary:CloudName, ApiKey và ApiSecret trong secret store.");

        var extension = Path.GetExtension(fileName);
        if (!SupportedExtensions.Contains(extension))
            return Result<StoredFileResult>.Failure("Chỉ hỗ trợ ảnh JPG, PNG, WEBP, GIF và tệp PDF cho chứng từ.");

        if (length <= 0)
            return Result<StoredFileResult>.Failure("Tệp chứng từ không có dữ liệu.");

        if (length > _settings.MaxFileSizeBytes)
            return Result<StoredFileResult>.Failure($"Tệp chứng từ vượt quá giới hạn {FormatBytes(_settings.MaxFileSizeBytes)}.");

        var resourceType = extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? "raw" : "image";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var publicId = $"receipt-{Guid.NewGuid():N}";
        var normalizedFolder = string.IsNullOrWhiteSpace(folder) ? _settings.Folder : folder.Trim('/');
        var signedParameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["folder"] = normalizedFolder,
            ["public_id"] = publicId,
            ["timestamp"] = timestamp
        };
        var signature = CreateSignature(signedParameters, _settings.ApiSecret);
        var endpoint = $"https://api.cloudinary.com/v1_1/{Uri.EscapeDataString(_settings.CloudName)}/{resourceType}/upload";

        try
        {
            using var requestContent = new MultipartFormDataContent();
            var fileContent = new StreamContent(content);
            if (!string.IsNullOrWhiteSpace(contentType))
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

            requestContent.Add(fileContent, "file", Path.GetFileName(fileName));
            requestContent.Add(new StringContent(_settings.ApiKey), "api_key");
            requestContent.Add(new StringContent(timestamp), "timestamp");
            requestContent.Add(new StringContent(signature), "signature");
            requestContent.Add(new StringContent(normalizedFolder), "folder");
            requestContent.Add(new StringContent(publicId), "public_id");

            using var response = await _httpClient.PostAsync(endpoint, requestContent, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Cloudinary upload failed with status {StatusCode}: {ResponseBody}", response.StatusCode, body);
                return Result<StoredFileResult>.Failure("Cloudinary không nhận được tệp chứng từ. Vui lòng thử lại.");
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var secureUrl = root.GetProperty("secure_url").GetString();
            var uploadedPublicId = root.GetProperty("public_id").GetString();
            if (string.IsNullOrWhiteSpace(secureUrl) || string.IsNullOrWhiteSpace(uploadedPublicId))
                return Result<StoredFileResult>.Failure("Cloudinary trả về kết quả không hợp lệ.");

            var format = root.TryGetProperty("format", out var formatElement) ? formatElement.GetString() : extension.TrimStart('.');
            var bytes = root.TryGetProperty("bytes", out var bytesElement) && bytesElement.TryGetInt64(out var uploadedBytes)
                ? uploadedBytes
                : length;
            var returnedResourceType = root.TryGetProperty("resource_type", out var resourceTypeElement)
                ? resourceTypeElement.GetString() ?? resourceType
                : resourceType;

            return Result<StoredFileResult>.Success(new StoredFileResult(
                secureUrl,
                uploadedPublicId,
                returnedResourceType,
                format,
                bytes));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while uploading a document to Cloudinary.");
            return Result<StoredFileResult>.Failure("Không thể tải chứng từ lên Cloudinary. Vui lòng thử lại.");
        }
    }

    private static string CreateSignature(IReadOnlyDictionary<string, string> parameters, string apiSecret)
    {
        var canonical = string.Join("&", parameters
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
        var payload = Encoding.UTF8.GetBytes(canonical + apiSecret);
#pragma warning disable CA5350 // Cloudinary's signed-upload protocol specifies SHA-1 for this request signature.
        var hash = SHA1.HashData(payload);
#pragma warning restore CA5350
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / (1024d * 1024d);
        return $"{megabytes:0.#} MB";
    }
}
