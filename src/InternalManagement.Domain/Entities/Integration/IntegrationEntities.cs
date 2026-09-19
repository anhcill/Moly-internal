using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Domain.Entities.Integration;

public class IntegrationSource : BaseEntity, IAuditableEntity
{
    public Guid CompanyId { get; set; }
    public string Code { get; set; } = string.Empty; // WEBSITE_EDTECH, WEBSITE_INTERVIEW, SHOPEE, TIKTOK
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "ApiKey"; // ApiKey, OAuth2, HMAC
    public string CredentialReference { get; set; } = string.Empty; // Reference key to Secret Store
    public bool IsActive { get; set; } = true;
    public DateTime? LastSyncAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class IntegrationRun : BaseEntity
{
    public string SourceSystem { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty; // Courses, Questions, Customers, Payments, Orders
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;

    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsFailed { get; set; }

    public string? Cursor { get; set; }
    public string? ErrorMessage { get; set; }
}

public class IntegrationInbox : BaseEntity
{
    public string SourceSystem { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string? Signature { get; set; }
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class IntegrationDeadLetter : BaseEntity
{
    public string SourceSystem { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public bool Resolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
}

/// <summary>
/// Stable source identifiers used by the CSCA Course LMS integration.
/// Keeping them in the domain prevents ad-hoc string variants in services,
/// migrations and future connector code.
/// </summary>
public static class LmsIntegrationSourceSystems
{
    public const string CscaCourseLms = "CSCA_COURSE_LMS";
    public const string CscaInternalManagement = "CSCA_INTERNAL_MANAGEMENT";
}

/// <summary>
/// Links one Management student identity to one account on a given LMS.
/// PartyId is the canonical person link when available; CscaClassStudentId
/// records the first class-enrollment that established this external mapping.
/// </summary>
public sealed class LmsAccountLink : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }
    public Guid? CscaClassStudentId { get; set; }
    public CscaClassStudent? CscaClassStudent { get; set; }

    public string SourceSystem { get; set; } = LmsIntegrationSourceSystems.CscaCourseLms;
    public string ExternalStudentId { get; set; } = string.Empty;
    public long? LmsUserId { get; set; }
    public string? LmsEmail { get; set; }
    public LmsAccountStatus Status { get; set; } = LmsAccountStatus.PendingPayment;

    public DateTime? LastProvisionedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastCorrelationId { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<LmsAccessGrant> AccessGrants { get; set; } = new List<LmsAccessGrant>();
}

/// <summary>
/// Maps an InternalManagement course to its immutable source ID and optional
/// numeric CSCA Course LMS ID. Course content remains owned by the LMS.
/// </summary>
public sealed class LmsCourseLink : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string SourceSystem { get; set; } = LmsIntegrationSourceSystems.CscaCourseLms;
    public string ExternalCourseId { get; set; } = string.Empty;
    public long? LmsCourseId { get; set; }
    public string? LmsCourseSlug { get; set; }
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<LmsAccessGrant> AccessGrants { get; set; } = new List<LmsAccessGrant>();
}

/// <summary>
/// Per-course learning entitlement. Management creates the record from the
/// class/payment workflow; a later connector will synchronise it to LMS.
/// </summary>
public sealed class LmsAccessGrant : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid LmsAccountLinkId { get; set; }
    public LmsAccountLink LmsAccountLink { get; set; } = null!;
    public Guid LmsCourseLinkId { get; set; }
    public LmsCourseLink LmsCourseLink { get; set; } = null!;
    public Guid CscaClassStudentId { get; set; }
    public CscaClassStudent CscaClassStudent { get; set; } = null!;

    public string SourceSystem { get; set; } = LmsIntegrationSourceSystems.CscaCourseLms;
    public string ExternalGrantId { get; set; } = string.Empty;
    public string? SourcePaymentId { get; set; }
    public long? LmsEnrollmentId { get; set; }
    public LmsAccessGrantStatus Status { get; set; } = LmsAccessGrantStatus.PendingPayment;
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Transactional outbound event. The payment/class workflow will enqueue here
/// in Phase 3, ensuring a committed business change cannot lose its LMS call.
/// </summary>
public sealed class IntegrationOutbox : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public string SourceSystem { get; set; } = LmsIntegrationSourceSystems.CscaCourseLms;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string? CorrelationId { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Stores the latest observed result for one remote LMS entity. It is a
/// durable reconciliation/read-model status, distinct from IntegrationRun.
/// </summary>
public sealed class LmsSyncStatus : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public string SourceSystem { get; set; } = LmsIntegrationSourceSystems.CscaCourseLms;
    public string EntityType { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;
    public DateTime? LastSyncedAt { get; set; }
    public string? LastPayloadHash { get; set; }
    public string? LastCorrelationId { get; set; }
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
