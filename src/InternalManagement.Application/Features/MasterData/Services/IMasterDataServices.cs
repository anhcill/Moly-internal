using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.MasterData;

namespace InternalManagement.Application.Features.MasterData.Services;

public sealed record PartyResolutionRequest(
    Guid CompanyId,
    Guid? BusinessUnitId,
    PartyType Type,
    PartyRole Role,
    string DisplayName,
    string? Email = null,
    string? Phone = null,
    string? SourceSystem = null,
    string? SourceId = null);

public interface IPartyResolver
{
    Task<Party> ResolveAsync(PartyResolutionRequest request, CancellationToken ct);
}

public sealed record BusinessDocumentRegistration(
    Guid CompanyId,
    Guid? BusinessUnitId,
    BusinessDocumentType DocumentType,
    string SourceEntityType,
    Guid SourceEntityId,
    string DocumentNumber,
    decimal TotalAmount,
    DateTime IssuedAt,
    Guid? PartyId = null,
    string Currency = "VND",
    BusinessDocumentStatus Status = BusinessDocumentStatus.Open,
    string? ExternalSourceSystem = null,
    string? ExternalSourceId = null);

public interface IBusinessDocumentRegistry
{
    Task<BusinessDocument> RegisterAsync(BusinessDocumentRegistration registration, CancellationToken ct);
}
