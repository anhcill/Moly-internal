using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Domain.Entities.Integration;

/// <summary>
/// A website or application governed by InternalManagement. It is deliberately
/// separate from IntegrationSource: one application can have more than one
/// technical connection over its lifetime, but users, roles and entitlements
/// always belong to the application code.
/// </summary>
public sealed class ManagedApplication : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ManagedApplicationStatus Status { get; set; } = ManagedApplicationStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<ApplicationMembership> Memberships { get; set; } = new List<ApplicationMembership>();
    public ICollection<ApplicationCourseMap> CourseMaps { get; set; } = new List<ApplicationCourseMap>();
    public ICollection<ApplicationClassMap> ClassMaps { get; set; } = new List<ApplicationClassMap>();
}

/// <summary>
/// A durable statement that one canonical Party belongs to one managed
/// application. It does not itself grant a role or learning entitlement.
/// </summary>
public sealed class ApplicationMembership : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid ManagedApplicationId { get; set; }
    public ManagedApplication ManagedApplication { get; set; } = null!;
    public Guid PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public string? ExternalUserId { get; set; }
    public ApplicationMembershipStatus Status { get; set; } = ApplicationMembershipStatus.Pending;
    public DateTime? ActivatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<ApplicationRoleAssignment> RoleAssignments { get; set; } = new List<ApplicationRoleAssignment>();
    public ICollection<ApplicationEntitlement> Entitlements { get; set; } = new List<ApplicationEntitlement>();
}

/// <summary>
/// Role assignment scoped to an application, course, or class. A Party may
/// hold both Student and Teacher roles, but only within explicitly assigned
/// scopes.
/// </summary>
public sealed class ApplicationRoleAssignment : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid ApplicationMembershipId { get; set; }
    public ApplicationMembership ApplicationMembership { get; set; } = null!;
    public string RoleCode { get; set; } = string.Empty;
    public ApplicationScopeType ScopeType { get; set; } = ApplicationScopeType.Application;
    public Guid? ScopeId { get; set; }
    public ApplicationRoleAssignmentStatus Status { get; set; } = ApplicationRoleAssignmentStatus.Active;
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidUntil { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Mapping from a Management course to exactly one target course in an
/// application. Multiple target courses are represented by multiple mapping
/// rows instead of serialising IDs into one field.
/// </summary>
public sealed class ApplicationCourseMap : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid ManagedApplicationId { get; set; }
    public ManagedApplication ManagedApplication { get; set; } = null!;
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string ExternalCourseId { get; set; } = string.Empty;
    public long? ExternalCourseNumericId { get; set; }
    public string? ExternalCourseSlug { get; set; }
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;
    public DateTime? ActivatedAt { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<ApplicationClassMap> ClassMaps { get; set; } = new List<ApplicationClassMap>();
    public ICollection<ApplicationEntitlement> Entitlements { get; set; } = new List<ApplicationEntitlement>();
}

/// <summary>Maps a Management class/cohort to its optional representation in an application.</summary>
public sealed class ApplicationClassMap : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid ManagedApplicationId { get; set; }
    public ManagedApplication ManagedApplication { get; set; } = null!;
    public Guid CscaClassId { get; set; }
    public CscaClass CscaClass { get; set; } = null!;
    public Guid? ApplicationCourseMapId { get; set; }
    public ApplicationCourseMap? ApplicationCourseMap { get; set; }
    public string ExternalClassId { get; set; } = string.Empty;
    public IntegrationStatus Status { get; set; } = IntegrationStatus.Pending;
    public DateTime? ActivatedAt { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<ApplicationEntitlement> Entitlements { get; set; } = new List<ApplicationEntitlement>();
}

/// <summary>
/// The central, payment-aware access decision for a Party in an application.
/// Connectors project this record to a website; websites do not invent a paid
/// entitlement of their own.
/// </summary>
public sealed class ApplicationEntitlement : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid ApplicationMembershipId { get; set; }
    public ApplicationMembership ApplicationMembership { get; set; } = null!;
    public Guid ApplicationCourseMapId { get; set; }
    public ApplicationCourseMap ApplicationCourseMap { get; set; } = null!;
    public Guid? ApplicationClassMapId { get; set; }
    public ApplicationClassMap? ApplicationClassMap { get; set; }
    public Guid? CscaClassStudentId { get; set; }
    public CscaClassStudent? CscaClassStudent { get; set; }
    public string? SourcePaymentId { get; set; }
    public ApplicationEntitlementStatus Status { get; set; } = ApplicationEntitlementStatus.PendingPayment;
    public string? Reason { get; set; }
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime? ValidUntil { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? LastCorrelationId { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public string? LastSyncError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
