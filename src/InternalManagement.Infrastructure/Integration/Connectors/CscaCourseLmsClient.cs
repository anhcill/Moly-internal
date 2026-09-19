using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using Microsoft.Extensions.Configuration;

namespace InternalManagement.Infrastructure.Integration.Connectors;

/// <summary>
/// Machine-to-machine client for the CSCA Course LMS. Credentials are read
/// from configuration/secret storage at dispatch time and are never persisted
/// in Management tables, outbox payloads, or application logs.
/// </summary>
public sealed class CscaCourseLmsClient : ICscaCourseLmsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public CscaCourseLmsClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<LmsProvisionResult> ProvisionStudentAsync(
        LmsProvisionCommand command,
        LmsOutboundRequestContext context,
        CancellationToken ct)
    {
        using var response = await SendAsync(
            HttpMethod.Post,
            "/api/integrations/v1/students/provision",
            command,
            context,
            ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
            return new LmsProvisionResult(false, null, null, null);

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            return new LmsProvisionResult(
                ReadBool(root, "alreadyExists") ?? false,
                ReadLong(root, "lmsUserId"),
                ReadString(root, "provisionStatus"),
                ReadString(root, "correlationId"));
        }
        catch (JsonException ex)
        {
            throw new LmsIntegrationHttpException(
                "LMS provision response is not valid JSON.",
                response.StatusCode,
                ex);
        }
    }

    public async Task UpdateStudentAccessAsync(
        string externalStudentId,
        LmsAccessCommand command,
        LmsOutboundRequestContext context,
        CancellationToken ct)
    {
        var path = $"/api/integrations/v1/students/{Uri.EscapeDataString(externalStudentId)}/access";
        using var response = await SendAsync(HttpMethod.Patch, path, command, context, ct);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object payload,
        LmsOutboundRequestContext context,
        CancellationToken ct)
    {
        var options = GetRequiredOptions();
        var payloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        var timestamp = DateTime.UtcNow.ToString("O");
        var signature = ComputeSignature(options.HmacSecret, timestamp, payloadJson);

        using var request = new HttpRequestMessage(method, new Uri(options.BaseUri, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", NormalizeBearerToken(options.ServiceToken));
        request.Headers.TryAddWithoutValidation("X-Integration-Key", options.IntegrationKey);
        request.Headers.TryAddWithoutValidation("X-Event-Timestamp", timestamp);
        request.Headers.TryAddWithoutValidation("X-Signature", signature);
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", context.CorrelationId);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", context.IdempotencyKey);
        request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new LmsIntegrationHttpException("Unable to reach CSCA Course LMS.", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new LmsIntegrationHttpException("CSCA Course LMS request timed out.", HttpStatusCode.RequestTimeout, ex);
        }

        if (response.IsSuccessStatusCode)
            return response;

        var statusCode = response.StatusCode;
        response.Dispose();
        throw new LmsIntegrationHttpException(
            $"CSCA Course LMS returned HTTP {(int)statusCode}.",
            statusCode);
    }

    private LmsClientOptions GetRequiredOptions()
    {
        var section = _configuration.GetSection("Integrations:CscaCourseLms");
        var baseUrl = section["BaseUrl"];
        var serviceToken = section["ServiceToken"];
        var integrationKey = section["IntegrationKey"];
        var hmacSecret = section["HmacSecret"] ?? section["WebhookSecret"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new LmsIntegrationConfigurationException("CSCA Course LMS BaseUrl is not configured.");
        if (string.IsNullOrWhiteSpace(serviceToken) || string.IsNullOrWhiteSpace(integrationKey) || string.IsNullOrWhiteSpace(hmacSecret))
            throw new LmsIntegrationConfigurationException(
                "CSCA Course LMS requires ServiceToken, IntegrationKey and HmacSecret from secret configuration.");

        var timeoutSeconds = Math.Clamp(section.GetValue<int?>("TimeoutSeconds") ?? 30, 1, 120);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return new LmsClientOptions(baseUri, serviceToken, integrationKey, hmacSecret);
    }

    private static string NormalizeBearerToken(string token) =>
        token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? token[7..] : token;

    private static string ComputeSignature(string secret, string timestamp, string payloadJson)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payloadJson}"));
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return null;
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        var value = ReadString(element, propertyName);
        return long.TryParse(value, out var parsed) ? parsed : null;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
            return null;
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return value.GetBoolean();
        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private sealed record LmsClientOptions(Uri BaseUri, string ServiceToken, string IntegrationKey, string HmacSecret);
}

public sealed class LmsIntegrationHttpException : Exception
{
    public LmsIntegrationHttpException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}

public sealed class LmsIntegrationConfigurationException : Exception
{
    public LmsIntegrationConfigurationException(string message) : base(message)
    {
    }

    public LmsIntegrationConfigurationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
