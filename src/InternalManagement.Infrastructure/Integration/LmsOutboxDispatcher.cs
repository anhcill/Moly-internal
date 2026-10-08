using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Infrastructure.Integration.Connectors;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Delivers committed LMS commands. It records each attempt before sending,
/// retries only transient faults, and moves unrecoverable commands to the
/// existing manual-review queue without leaking credentials or PII to logs.
/// </summary>
public sealed class LmsOutboxDispatcher : ILmsOutboxDispatcher
{
    private const int MaxAttempts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ICscaCourseLmsClient _client;
    private readonly ILogger<LmsOutboxDispatcher> _logger;
    private readonly ILmsAccessLifecycleService? _accessLifecycle;

    public LmsOutboxDispatcher(
        IApplicationDbContext db,
        ICscaCourseLmsClient client,
        ILogger<LmsOutboxDispatcher> logger,
        ILmsAccessLifecycleService? accessLifecycle = null)
    {
        _db = db;
        _client = client;
        _logger = logger;
        _accessLifecycle = accessLifecycle;
    }

    public async Task<LmsOutboxDispatchResult> DispatchPendingAsync(int batchSize, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var safeBatchSize = Math.Clamp(batchSize, 1, 100);
        var items = await _db.IntegrationOutboxes
            .Where(item => item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms
                           && (item.Status == IntegrationStatus.Pending || item.Status == IntegrationStatus.Failed)
                           && (item.NextAttemptAt == null || item.NextAttemptAt <= now))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .Take(safeBatchSize)
            .ToListAsync(ct);

        var processed = 0;
        var succeeded = 0;
        var retrying = 0;
        var deadLettered = 0;
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            if (item.EventType == LmsOutboxEventTypes.ManagementEventRequested)
            {
                // Another dispatcher may be handling the preceding teacher/course
                // event. Do not send a dependent event until its projection is
                // confirmed by the LMS worker.
                var predecessorPending = await _db.IntegrationOutboxes.AsNoTracking().AnyAsync(other =>
                    other.CompanyId == item.CompanyId &&
                    other.CorrelationId == item.CorrelationId &&
                    other.EventType == LmsOutboxEventTypes.ManagementEventRequested &&
                    other.CreatedAt < item.CreatedAt &&
                    other.Status != IntegrationStatus.Success, ct);
                if (predecessorPending) continue;
            }
            if (item.EventType == LmsOutboxEventTypes.StudentAccessRequested)
            {
                var grant = await GetAggregateAsync<LmsAccessGrant>(_db.LmsAccessGrants, item.AggregateId, ct);
                var provisionPending = await _db.IntegrationOutboxes.AsNoTracking().AnyAsync(other =>
                    other.CompanyId == item.CompanyId &&
                    other.EventType == LmsOutboxEventTypes.StudentProvisionRequested &&
                    other.AggregateId == grant.LmsAccountLinkId.ToString("N") &&
                    other.CreatedAt <= item.CreatedAt &&
                    other.Status != IntegrationStatus.Success, ct);
                if (provisionPending) continue;
            }
            processed++;
            item.Status = IntegrationStatus.Processing;
            item.AttemptCount++;
            item.LastError = null;
            await _db.SaveChangesAsync(ct);

            try
            {
                await DispatchAsync(item, ct);
                item.Status = IntegrationStatus.Success;
                item.PublishedAt = DateTime.UtcNow;
                item.NextAttemptAt = null;
                item.LastError = null;
                await ResolveOutboxDeadLettersAsync(item, ct);
                if (item.EventType == LmsOutboxEventTypes.ManagementEventRequested)
                    await UpdateCourseLinkDeliveryAsync(item, ct);
                succeeded++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var maxAttempts = item.EventType == LmsOutboxEventTypes.ManagementEventRequested ? 12 : MaxAttempts;
                var retryable = IsRetryable(ex) && item.AttemptCount < maxAttempts;
                item.LastError = ToSafeErrorMessage(ex);
                if (retryable)
                {
                    item.Status = IntegrationStatus.Failed;
                    item.NextAttemptAt = DateTime.UtcNow.Add(GetBackoff(item.AttemptCount));
                    retrying++;
                    await MarkSyncFailureAsync(item, item.LastError, IntegrationStatus.Failed, ct);
                    _logger.LogWarning(
                        "LMS outbox command {OutboxId} failed transiently on attempt {Attempt}/{MaxAttempts}.",
                        item.Id,
                        item.AttemptCount,
                        maxAttempts);
                }
                else
                {
                    item.Status = IntegrationStatus.DeadLetter;
                    item.NextAttemptAt = null;
                    deadLettered++;
                    await MarkSyncFailureAsync(item, item.LastError, IntegrationStatus.DeadLetter, ct);
                    await AddDeadLetterAsync(item, item.LastError, ct);
                    _logger.LogWarning(
                        "LMS outbox command {OutboxId} requires manual review after attempt {Attempt}.",
                        item.Id,
                        item.AttemptCount);
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        return new LmsOutboxDispatchResult(processed, succeeded, retrying, deadLettered);
    }

    private async Task DispatchAsync(IntegrationOutbox item, CancellationToken ct)
    {
        var context = new LmsOutboundRequestContext(
            item.IdempotencyKey,
            item.CorrelationId ?? item.EventId);

        switch (item.EventType)
        {
            case LmsOutboxEventTypes.ManagementEventRequested:
            {
                var command = Deserialize<LmsManagementEvent>(item.PayloadJson);
                await _client.SendManagementEventAsync(command, context, ct);
                break;
            }
            case LmsOutboxEventTypes.StudentProvisionRequested:
            {
                var command = Deserialize<LmsProvisionCommand>(item.PayloadJson);
                var result = await _client.ProvisionStudentAsync(command, context, ct);
                var account = await GetAggregateAsync<LmsAccountLink>(_db.LmsAccountLinks, item.AggregateId, ct);
                account.LmsUserId ??= result.LmsUserId;
                // A manually retried provision may be the first successful delivery
                // after a prior terminal failure. Restore the intended account state
                // without overwriting a later revoke/suspension from the lifecycle.
                if (account.Status == LmsAccountStatus.ProvisioningFailed &&
                    Enum.TryParse<LmsAccountStatus>(command.AccountStatus, true, out var provisionedStatus))
                {
                    account.Status = provisionedStatus;
                }
                account.LastProvisionedAt = DateTime.UtcNow;
                account.LastSyncedAt = DateTime.UtcNow;
                account.LastCorrelationId = result.CorrelationId ?? context.CorrelationId;
                account.LastSyncError = null;
                await UpsertSyncStatusAsync(item, nameof(LmsAccountLink), command.ExternalStudentId, IntegrationStatus.Success, null, ct);
                break;
            }
            case LmsOutboxEventTypes.StudentAccessRequested:
            {
                var payload = Deserialize<LmsAccessOutboxPayload>(item.PayloadJson);
                await _client.UpdateStudentAccessAsync(payload.ExternalStudentId, payload.Access, context, ct);
                var grant = await GetAggregateAsync<LmsAccessGrant>(_db.LmsAccessGrants, item.AggregateId, ct);
                grant.LastSyncedAt = DateTime.UtcNow;
                grant.LastSyncError = null;
                await UpsertSyncStatusAsync(item, nameof(LmsAccessGrant), grant.ExternalGrantId, IntegrationStatus.Success, null, ct);
                break;
            }
            default:
                throw new LmsIntegrationConfigurationException($"Unsupported LMS outbox event '{item.EventType}'.");
        }
    }

    private async Task MarkSyncFailureAsync(
        IntegrationOutbox item,
        string error,
        IntegrationStatus status,
        CancellationToken ct)
    {
        switch (item.EventType)
        {
            case LmsOutboxEventTypes.ManagementEventRequested:
            {
                var link = await GetAggregateAsync<LmsCourseLink>(_db.LmsCourseLinks, item.AggregateId, ct);
                if (await IsCurrentManagementBatchAsync(item, link, ct))
                {
                    link.Status = status;
                    link.LastSyncError = error;
                }
                break;
            }
            case LmsOutboxEventTypes.StudentProvisionRequested:
            {
                var command = Deserialize<LmsProvisionCommand>(item.PayloadJson);
                var account = await GetAggregateAsync<LmsAccountLink>(_db.LmsAccountLinks, item.AggregateId, ct);
                account.LastSyncError = error;
                if (status == IntegrationStatus.DeadLetter)
                    account.Status = LmsAccountStatus.ProvisioningFailed;
                await UpsertSyncStatusAsync(item, nameof(LmsAccountLink), command.ExternalStudentId, status, error, ct);
                break;
            }
            case LmsOutboxEventTypes.StudentAccessRequested:
            {
                var grant = await GetAggregateAsync<LmsAccessGrant>(_db.LmsAccessGrants, item.AggregateId, ct);
                grant.LastSyncError = error;
                await UpsertSyncStatusAsync(item, nameof(LmsAccessGrant), grant.ExternalGrantId, status, error, ct);
                break;
            }
        }
    }

    private async Task UpdateCourseLinkDeliveryAsync(IntegrationOutbox item, CancellationToken ct)
    {
        var link = await GetAggregateAsync<LmsCourseLink>(_db.LmsCourseLinks, item.AggregateId, ct);
        if (!await IsCurrentManagementBatchAsync(item, link, ct)) return;
        var hasRemaining = await _db.IntegrationOutboxes.AnyAsync(other =>
            other.Id != item.Id &&
            other.CompanyId == item.CompanyId &&
            other.CorrelationId == item.CorrelationId &&
            other.EventType == LmsOutboxEventTypes.ManagementEventRequested &&
            other.Status != IntegrationStatus.Success, ct);
        if (!hasRemaining)
        {
            link.Status = IntegrationStatus.Success;
            link.LastSyncedAt = DateTime.UtcNow;
            link.LastSyncError = null;
            if (_accessLifecycle is not null)
            {
                var classes = await _db.CscaClasses.AsNoTracking()
                    .Include(cls => cls.Students)
                    .Where(cls => cls.CompanyId == link.CompanyId && cls.CourseId == link.CourseId && !cls.IsDeleted)
                    .ToListAsync(ct);
                foreach (var cls in classes)
                foreach (var student in cls.Students)
                {
                    await _accessLifecycle.ReconcileStudentAccessAsync(new LmsAccessEvaluationRequest(
                        cls.CompanyId,
                        cls.BusinessUnitId,
                        student.Id,
                        student.PartyId,
                        cls.Id,
                        link.CourseId,
                        link.ExternalCourseId,
                        student.StudentName,
                        student.Email,
                        student.PhoneNumber,
                        student.PaidAmount,
                        Math.Max(0, cls.TuitionFee - student.DiscountAmount),
                        student.PaymentStatus,
                        student.UpdatedAt ?? student.JoinedAt,
                        student.BusinessDocumentId?.ToString("N")), ct);
                }
            }
        }
    }

    private async Task<bool> IsCurrentManagementBatchAsync(
        IntegrationOutbox item, LmsCourseLink link, CancellationToken ct)
    {
        if (link.Status == IntegrationStatus.Pending && link.UpdatedAt > item.CreatedAt)
            return false;
        var latestCorrelationId = await _db.IntegrationOutboxes.AsNoTracking()
            .Where(other => other.CompanyId == item.CompanyId &&
                other.AggregateType == nameof(LmsCourseLink) &&
                other.AggregateId == item.AggregateId &&
                other.EventType == LmsOutboxEventTypes.ManagementEventRequested)
            .OrderByDescending(other => other.CreatedAt)
            .Select(other => other.CorrelationId)
            .FirstOrDefaultAsync(ct);
        return latestCorrelationId == item.CorrelationId;
    }

    private async Task UpsertSyncStatusAsync(
        IntegrationOutbox item,
        string entityType,
        string externalId,
        IntegrationStatus status,
        string? error,
        CancellationToken ct)
    {
        var syncStatus = await _db.LmsSyncStatuses.FirstOrDefaultAsync(value =>
            value.CompanyId == item.CompanyId &&
            value.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            value.EntityType == entityType &&
            value.ExternalId == externalId,
            ct);
        if (syncStatus is null)
        {
            syncStatus = new LmsSyncStatus
            {
                CompanyId = item.CompanyId,
                BusinessUnitId = item.BusinessUnitId,
                SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
                EntityType = entityType,
                ExternalId = externalId
            };
            _db.LmsSyncStatuses.Add(syncStatus);
        }

        syncStatus.Status = status;
        syncStatus.LastSyncedAt = DateTime.UtcNow;
        syncStatus.LastPayloadHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(item.PayloadJson)));
        syncStatus.LastCorrelationId = item.CorrelationId;
        syncStatus.LastError = error;
    }

    private async Task AddDeadLetterAsync(IntegrationOutbox item, string error, CancellationToken ct)
    {
        var exists = await _db.IntegrationDeadLetters.AnyAsync(deadLetter =>
            deadLetter.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            deadLetter.EntityType == item.EventType &&
            deadLetter.SourceId == item.Id.ToString("N") &&
            !deadLetter.Resolved,
            ct);
        if (exists)
            return;

        _db.IntegrationDeadLetters.Add(new IntegrationDeadLetter
        {
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EntityType = item.EventType,
            SourceId = item.Id.ToString("N"),
            PayloadJson = item.PayloadJson,
            ErrorCode = item.LastError is null ? "LMS_OUTBOX_FAILED" : "LMS_OUTBOX_MANUAL_REVIEW",
            ErrorMessage = error,
            CreatedAt = DateTime.UtcNow
        });
    }

    private async Task ResolveOutboxDeadLettersAsync(IntegrationOutbox item, CancellationToken ct)
    {
        var deadLetters = await _db.IntegrationDeadLetters
            .Where(deadLetter =>
                deadLetter.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                deadLetter.SourceId == item.Id.ToString("N") &&
                !deadLetter.Resolved)
            .ToListAsync(ct);

        if (deadLetters.Count == 0)
            return;

        var resolvedAt = DateTime.UtcNow;
        foreach (var deadLetter in deadLetters)
        {
            deadLetter.Resolved = true;
            deadLetter.ResolvedAt = resolvedAt;
            deadLetter.ResolvedBy = "lms-outbox-dispatcher";
        }
    }

    private static async Task<TEntity> GetAggregateAsync<TEntity>(DbSet<TEntity> set, string aggregateId, CancellationToken ct)
        where TEntity : class
    {
        if (!Guid.TryParse(aggregateId, out var id))
            throw new LmsIntegrationConfigurationException("LMS outbox aggregate ID is invalid.");

        var entity = await set.FindAsync([id], ct);
        return entity ?? throw new LmsIntegrationConfigurationException("LMS outbox aggregate no longer exists.");
    }

    private static T Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                   ?? throw new LmsIntegrationConfigurationException("LMS outbox payload is empty.");
        }
        catch (JsonException ex)
        {
            throw new LmsIntegrationConfigurationException("LMS outbox payload is invalid JSON.", ex);
        }
    }

    private static bool IsRetryable(Exception exception) => exception switch
    {
        LmsIntegrationHttpException { StatusCode: HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout } => true,
        LmsIntegrationHttpException { StatusCode: >= HttpStatusCode.InternalServerError } => true,
        LmsIntegrationHttpException { StatusCode: null } => true,
        HttpRequestException => true,
        _ => false
    };

    private static TimeSpan GetBackoff(int attemptCount) => attemptCount switch
    {
        1 => TimeSpan.FromSeconds(2),
        2 => TimeSpan.FromSeconds(8),
        _ => TimeSpan.FromSeconds(30)
    };

    private static string ToSafeErrorMessage(Exception exception) => exception switch
    {
        LmsIntegrationHttpException http => http.Message,
        LmsIntegrationConfigurationException configuration => configuration.Message,
        _ => $"Lỗi khi gửi sự kiện sang Web ({exception.GetType().Name}): {exception.Message}"
    };
}
