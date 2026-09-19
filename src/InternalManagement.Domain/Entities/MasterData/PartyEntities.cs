using InternalManagement.Domain.Common;

namespace InternalManagement.Domain.Entities.MasterData;

/// <summary>
/// Canonical person or organization record shared by every business module.
/// Transaction tables keep their own snapshots, while this entity provides a
/// stable identity for cross-module reporting and deduplication.
/// </summary>
public sealed class Party : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public PartyType Type { get; set; } = PartyType.Individual;
    public PartyStatus Status { get; set; } = PartyStatus.Active;
    public string DisplayName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<PartyContact> Contacts { get; set; } = new List<PartyContact>();
    public ICollection<PartyExternalIdentity> ExternalIdentities { get; set; } = new List<PartyExternalIdentity>();
    public ICollection<PartyBusinessProfile> BusinessProfiles { get; set; } = new List<PartyBusinessProfile>();
}

public enum PartyType
{
    Individual,
    Organization
}

public enum PartyStatus
{
    Active,
    Inactive,
    Archived
}

public enum PartyRole
{
    Customer,
    Student,
    Supplier,
    Partner
}

public enum PartyContactType
{
    Email,
    Phone,
    Address
}

public sealed class PartyContact : BaseEntity, IAuditableEntity
{
    public Guid PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public PartyContactType Type { get; set; }
    public string Value { get; set; } = string.Empty;
    public string NormalizedValue { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime? VerifiedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class PartyExternalIdentity : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public string SourceSystem { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed class PartyBusinessProfile : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid PartyId { get; set; }
    public Party Party { get; set; } = null!;
    public PartyRole Role { get; set; }
    public PartyStatus Status { get; set; } = PartyStatus.Active;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
