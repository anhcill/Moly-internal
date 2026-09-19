using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Integration.DTOs;

// ── Request DTOs ──

public sealed record TriggerSyncRequest(
    string SourceSystem,
    string EntityType,
    bool ForceFullSync = false);

public sealed record WebhookIngestRequest(
    string EventId,
    string EventType,
    string PayloadJson);

// ── Response DTOs ──

public sealed record SyncRunDto
{
    public Guid Id { get; init; }
    public string SourceSystem { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public int RecordsRead { get; init; }
    public int RecordsWritten { get; init; }
    public int RecordsSkipped { get; init; }
    public int RecordsFailed { get; init; }
    public string? Cursor { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record DeadLetterDto
{
    public Guid Id { get; init; }
    public string SourceSystem { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public int RetryCount { get; init; }
    public bool Resolved { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? ResolvedAt { get; init; }
    public string? ResolvedBy { get; init; }
}

public sealed record WebhookIngestResponse(bool Accepted, string Message);

public sealed record SyncTriggerResponse
{
    public Guid RunId { get; init; }
    public string SourceSystem { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int RecordsRead { get; init; }
    public int RecordsWritten { get; init; }
    public int RecordsSkipped { get; init; }
    public int RecordsFailed { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record ConnectorStatusDto
{
    public string SourceSystem { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public string? Message { get; init; }
    public DateTime CheckedAt { get; init; }
    public IReadOnlyList<string> SupportedEntityTypes { get; init; } = [];
}

// ── LMS operations DTOs ──

/// <summary>Operational health snapshot for the Management → CSCA Course LMS flow.</summary>
public sealed record LmsIntegrationOverviewDto
{
    public int TotalCourses { get; init; }
    public int MappedCourses { get; init; }
    public int ReadyCourseMappings { get; init; }
    public int ActiveAccounts { get; init; }
    public int PendingAccounts { get; init; }
    public int ActiveGrants { get; init; }
    public int PendingOutbox { get; init; }
    public int FailedOutbox { get; init; }
    public int DeadLetterOutbox { get; init; }
    public DateTime? LastSuccessfulDispatchAt { get; init; }
}

/// <summary>
/// A tenant-scoped mapping from one InternalManagement course to one course
/// exposed by the CSCA Course LMS integration endpoint.
/// </summary>
public sealed record LmsCourseMappingDto
{
    public Guid CourseId { get; init; }
    public string CourseSourceId { get; init; } = string.Empty;
    public string CourseTitle { get; init; } = string.Empty;
    public string? CourseSlug { get; init; }
    public Guid? MappingId { get; init; }
    public string? ExternalCourseId { get; init; }
    public long? LmsCourseId { get; init; }
    public string? LmsCourseSlug { get; init; }
    public string? Status { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public string? LastSyncError { get; init; }
    public Guid? BusinessUnitId { get; init; }
}

/// <summary>
/// Saving a mapping as enabled deliberately marks it ready for paid access.
/// A numeric LMS course ID or LMS slug is required as a second confirmation.
/// </summary>
public sealed record UpsertLmsCourseMappingRequest(
    string? ExternalCourseId,
    long? LmsCourseId,
    string? LmsCourseSlug,
    bool EnableAccess,
    Guid? BusinessUnitId = null);

/// <summary>Sanitized outbox view. Payloads and idempotency keys stay private.</summary>
public sealed record LmsOutboxItemDto
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string AggregateId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? NextAttemptAt { get; init; }
    public DateTime? PublishedAt { get; init; }
    public string? CorrelationId { get; init; }
    public string? LastError { get; init; }
}
