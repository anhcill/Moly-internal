using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Xử lý webhook: nhận event, xác thực HMAC SHA-256, kiểm tra idempotency, lưu inbox, xử lý pending.
/// </summary>
public sealed class WebhookProcessor : IWebhookProcessor
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<WebhookProcessor> _logger;

    public WebhookProcessor(IApplicationDbContext db, ILogger<WebhookProcessor> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(bool Accepted, string Message)> IngestAsync(
        string sourceSystem, string eventId, string eventType,
        string payloadJson, string? signature, CancellationToken ct)
    {
        // Idempotency check: kiểm tra eventId đã tồn tại chưa
        var exists = await _db.IntegrationInboxes
            .AnyAsync(i => i.SourceSystem == sourceSystem && i.EventId == eventId, ct);

        if (exists)
        {
            _logger.LogInformation(
                "Webhook duplicate bị bỏ qua: {SourceSystem}/{EventId}",
                sourceSystem, eventId);
            return (true, "Event đã được nhận trước đó (idempotent skip).");
        }

        // Xác thực HMAC SHA-256 nếu có signature
        if (!string.IsNullOrWhiteSpace(signature))
        {
            var isValid = await ValidateSignatureAsync(sourceSystem, payloadJson, signature, ct);
            if (!isValid)
            {
                _logger.LogWarning(
                    "Webhook HMAC không hợp lệ: {SourceSystem}/{EventId}",
                    sourceSystem, eventId);
                return (false, "Chữ ký HMAC không hợp lệ.");
            }
        }

        // Lưu vào inbox
        var inboxEntry = new IntegrationInbox
        {
            SourceSystem = sourceSystem,
            EventId = eventId,
            EventType = eventType,
            PayloadJson = payloadJson,
            Signature = signature,
            Status = IntegrationStatus.Pending,
            ReceivedAt = DateTime.UtcNow
        };

        _db.IntegrationInboxes.Add(inboxEntry);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Webhook đã nhận: {SourceSystem}/{EventId}/{EventType}",
            sourceSystem, eventId, eventType);

        return (true, "Webhook đã được tiếp nhận thành công.");
    }

    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct)
    {
        var pendingItems = await _db.IntegrationInboxes
            .Where(i => i.Status == IntegrationStatus.Pending)
            .OrderBy(i => i.ReceivedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (pendingItems.Count == 0)
            return 0;

        int processed = 0;

        foreach (var item in pendingItems)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                item.Status = IntegrationStatus.Processing;
                await _db.SaveChangesAsync(ct);

                // TODO (Ngày 8+): Entity mapper sẽ xử lý payload ở đây.
                // Hiện tại chỉ đánh dấu đã xử lý thành công.
                item.Status = IntegrationStatus.Success;
                item.ProcessedAt = DateTime.UtcNow;
                processed++;

                _logger.LogInformation(
                    "Inbox processed: {SourceSystem}/{EventId}",
                    item.SourceSystem, item.EventId);
            }
            catch (Exception ex)
            {
                item.Status = IntegrationStatus.Failed;
                item.ErrorMessage = ex.Message;
                item.ProcessedAt = DateTime.UtcNow;

                _logger.LogError(ex,
                    "Inbox processing failed: {SourceSystem}/{EventId}",
                    item.SourceSystem, item.EventId);
            }

            await _db.SaveChangesAsync(ct);
        }

        return processed;
    }

    /// <summary>
    /// Xác thực chữ ký HMAC SHA-256.
    /// Lấy secret từ IntegrationSource.CredentialReference.
    /// </summary>
    private async Task<bool> ValidateSignatureAsync(
        string sourceSystem, string payloadJson, string signature, CancellationToken ct)
    {
        var source = await _db.IntegrationSources
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Code == sourceSystem && s.IsActive, ct);

        if (source == null)
        {
            _logger.LogWarning("Không tìm thấy IntegrationSource cho {SourceSystem}", sourceSystem);
            return false;
        }

        var secret = source.CredentialReference;
        if (string.IsNullOrWhiteSpace(secret))
        {
            // Nếu chưa cấu hình secret → bỏ qua kiểm tra (dev mode)
            _logger.LogWarning("IntegrationSource {SourceSystem} chưa cấu hình CredentialReference — bỏ qua HMAC", sourceSystem);
            return true;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadJson));
        var computedSignature = $"sha256={Convert.ToHexStringLower(computedHash)}";

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature),
            Encoding.UTF8.GetBytes(signature));
    }
}
