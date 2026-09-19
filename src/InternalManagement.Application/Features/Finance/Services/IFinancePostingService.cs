using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Finance.Services;

/// <summary>
/// Command used by an operational module to post a confirmed business event
/// into the finance ledger. The source entity + entry type is the idempotency
/// key; callers may safely retry the same command.
/// </summary>
public sealed record FinancePostingRequest(
    Guid CompanyId,
    Guid? BusinessUnitId,
    TransactionType TransactionType,
    decimal Amount,
    DateTime OccurredAt,
    string SourceEntityType,
    Guid SourceEntityId,
    string Description,
    Guid? SourceBusinessDocumentId = null,
    Guid? CategoryId = null);

public enum FinancePostingOutcome
{
    Created,
    Updated,
    Unchanged
}

public sealed record FinancePostingResult(
    Guid FinanceTransactionId,
    Guid BusinessDocumentId,
    FinancePostingOutcome Outcome);

public interface IFinancePostingService
{
    /// <summary>Creates or updates one un-reconciled finance entry for a confirmed source event.</summary>
    Task<FinancePostingResult> PostAsync(FinancePostingRequest request, CancellationToken ct);
}
