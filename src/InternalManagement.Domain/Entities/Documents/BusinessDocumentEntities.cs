using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.MasterData;

namespace InternalManagement.Domain.Entities.Documents;

/// <summary>
/// A stable registry entry for a business document. It links operational
/// records to finance, files and audit trails without replacing the detailed
/// module-specific document tables.
/// </summary>
public sealed class BusinessDocument : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }
    public BusinessDocumentType DocumentType { get; set; }
    public BusinessDocumentStatus Status { get; set; } = BusinessDocumentStatus.Open;
    public string DocumentNumber { get; set; } = string.Empty;
    public string SourceEntityType { get; set; } = string.Empty;
    public Guid SourceEntityId { get; set; }
    public string? ExternalSourceSystem { get; set; }
    public string? ExternalSourceId { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = "VND";
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<BusinessDocumentLink> OutgoingLinks { get; set; } = new List<BusinessDocumentLink>();
    public ICollection<BusinessDocumentLink> IncomingLinks { get; set; } = new List<BusinessDocumentLink>();
}

public enum BusinessDocumentType
{
    EdTechPayment,
    CscaEnrollment,
    InterviewService,
    FashionSalesOrder,
    FashionSalesDocument,
    FashionReturn,
    FashionSettlement,
    FashionPurchaseReceipt,
    PayrollPeriod,
    FinanceTransaction
}

public enum BusinessDocumentStatus
{
    Draft,
    Open,
    Settled,
    Voided
}

public enum BusinessDocumentLinkType
{
    Settlement,
    Fulfillment,
    Reversal,
    Adjustment
}

public sealed class BusinessDocumentLink : BaseEntity, IAuditableEntity
{
    public Guid FromDocumentId { get; set; }
    public BusinessDocument FromDocument { get; set; } = null!;
    public Guid ToDocumentId { get; set; }
    public BusinessDocument ToDocument { get; set; } = null!;
    public BusinessDocumentLinkType LinkType { get; set; }
    public decimal? Amount { get; set; }
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
