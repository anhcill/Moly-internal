using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Integration.DTOs;

public sealed record ApplicationOrchestrationOverviewDto
{
    public int TotalApplications { get; init; }
    public int ActiveApplications { get; init; }
    public int ActiveMemberships { get; init; }
    public int ActiveRoleAssignments { get; init; }
    public int ReadyCourseMaps { get; init; }
    public int ReadyClassMaps { get; init; }
    public int ActiveEntitlements { get; init; }
    public int PendingEntitlements { get; init; }
    public int PendingDelivery { get; init; }
    public int FailedDelivery { get; init; }
    public int OpenDeadLetters { get; init; }
}

public sealed record ManagedApplicationDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string Status { get; init; } = string.Empty;
    public Guid? BusinessUnitId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record UpsertManagedApplicationRequest(
    string Code,
    string Name,
    string BaseUrl,
    string? Description,
    ManagedApplicationStatus Status = ManagedApplicationStatus.Active,
    Guid? BusinessUnitId = null);

public sealed record ApplicationMembershipDto
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public string ApplicationCode { get; init; } = string.Empty;
    public Guid PartyId { get; init; }
    public string PartyName { get; init; } = string.Empty;
    public string? ExternalUserId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? ActivatedAt { get; init; }
    public DateTime? RevokedAt { get; init; }
    public string? RevocationReason { get; init; }
    public Guid? BusinessUnitId { get; init; }
}

public sealed record UpsertApplicationMembershipRequest(
    Guid PartyId,
    string? ExternalUserId,
    ApplicationMembershipStatus Status,
    string? RevocationReason = null,
    Guid? BusinessUnitId = null);

public sealed record ApplicationRoleAssignmentDto
{
    public Guid Id { get; init; }
    public Guid MembershipId { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public string ScopeType { get; init; } = string.Empty;
    public Guid? ScopeId { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public DateTime? RevokedAt { get; init; }
    public string? RevocationReason { get; init; }
}

public sealed record UpsertApplicationRoleAssignmentRequest(
    string RoleCode,
    ApplicationScopeType ScopeType,
    Guid? ScopeId,
    ApplicationRoleAssignmentStatus Status = ApplicationRoleAssignmentStatus.Active,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null,
    string? RevocationReason = null);

public sealed record ApplicationCourseMapDto
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid CourseId { get; init; }
    public string CourseSourceId { get; init; } = string.Empty;
    public string CourseTitle { get; init; } = string.Empty;
    public string ExternalCourseId { get; init; } = string.Empty;
    public long? ExternalCourseNumericId { get; init; }
    public string? ExternalCourseSlug { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTime? ActivatedAt { get; init; }
    public string? LastSyncError { get; init; }
}

public sealed record UpsertApplicationCourseMapRequest(
    string ExternalCourseId,
    long? ExternalCourseNumericId,
    string? ExternalCourseSlug,
    bool Activate);

public sealed record ApplicationClassMapDto
{
    public Guid Id { get; init; }
    public Guid ApplicationId { get; init; }
    public Guid CscaClassId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public Guid? CourseMapId { get; init; }
    public string ExternalClassId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? ActivatedAt { get; init; }
    public string? LastSyncError { get; init; }
}

public sealed record UpsertApplicationClassMapRequest(
    Guid? CourseMapId,
    string ExternalClassId,
    bool Activate);

public sealed record ApplicationEntitlementDto
{
    public Guid Id { get; init; }
    public Guid MembershipId { get; init; }
    public Guid CourseMapId { get; init; }
    public Guid? ClassMapId { get; init; }
    public Guid? CscaClassStudentId { get; init; }
    public string ApplicationCode { get; init; } = string.Empty;
    public string ExternalCourseId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string? SourcePaymentId { get; init; }
    public string? Reason { get; init; }
    public DateTime ValidFrom { get; init; }
    public DateTime? ValidUntil { get; init; }
    public DateTime? RevokedAt { get; init; }
    public DateTime? LastSyncedAt { get; init; }
    public string? LastSyncError { get; init; }
}

public sealed record UpsertApplicationEntitlementRequest(
    Guid CourseMapId,
    Guid? ClassMapId,
    Guid? CscaClassStudentId,
    ApplicationEntitlementStatus Status,
    string? SourcePaymentId,
    string? Reason,
    DateTime? ValidFrom = null,
    DateTime? ValidUntil = null);

/// <summary>Safe operator view; event payloads and idempotency keys are never exposed.</summary>
public sealed record ApplicationDeliveryItemDto
{
    public Guid Id { get; init; }
    public string ApplicationCode { get; init; } = string.Empty;
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
