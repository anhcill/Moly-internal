using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.MasterData;

namespace InternalManagement.Domain.Entities.InternalData;

public enum BusinessSegment
{
    TECHNOLOGY_EDUCATION,
    FASHION
}

public enum InternalCustomerStatus
{
    LEAD,
    ACTIVE,
    INACTIVE,
    ARCHIVED
}

public enum InternalResourceType
{
    EXAM,
    DOCUMENT,
    PLAN,
    DESIGN_SAMPLE
}

public enum InternalResourceStatus
{
    DRAFT,
    ACTIVE,
    ARCHIVED
}

public sealed class InternalCustomer : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }
    public BusinessSegment BusinessSegment { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Source { get; set; }
    public InternalCustomerStatus Status { get; set; } = InternalCustomerStatus.ACTIVE;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}

/// <summary>
/// Chỉ lưu metadata và vị trí tệp. Nội dung binary phải nằm ở object storage/file server bên ngoài DB.
/// </summary>
public sealed class InternalResource : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public BusinessSegment BusinessSegment { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public InternalResourceType ResourceType { get; set; }
    public string StorageUri { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ChecksumSha256 { get; set; }
    public string Version { get; set; } = "1.0";
    public InternalResourceStatus Status { get; set; } = InternalResourceStatus.DRAFT;
    public string TagsCsv { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
