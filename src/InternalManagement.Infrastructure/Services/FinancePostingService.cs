using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Finance;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

/// <summary>
/// Idempotent bridge from an operationally confirmed event to the finance
/// ledger. It deliberately does not change a cash-account balance: bank/cash
/// account attribution is performed later by reconciliation or an explicit
/// cash-account transaction.
/// </summary>
public sealed class FinancePostingService(
    IApplicationDbContext db,
    IBusinessDocumentRegistry documentRegistry,
    ICurrentUserService? currentUser = null) : IFinancePostingService
{
    public async Task<FinancePostingResult> PostAsync(FinancePostingRequest request, CancellationToken ct)
    {
        Validate(request);

        var sourceEntityType = request.SourceEntityType.Trim();
        var description = request.Description.Trim();
        var occurredAt = request.OccurredAt.Kind == DateTimeKind.Utc
            ? request.OccurredAt
            : DateTime.SpecifyKind(request.OccurredAt, DateTimeKind.Utc);
        var transaction = await db.FinanceTransactions.FirstOrDefaultAsync(x =>
            x.CompanyId == request.CompanyId
            && x.ReferenceType == sourceEntityType
            && x.ReferenceId == request.SourceEntityId
            && x.TransactionType == request.TransactionType, ct);

        var outcome = FinancePostingOutcome.Unchanged;
        if (transaction is null)
        {
            transaction = new FinanceTransaction
            {
                CompanyId = request.CompanyId,
                BusinessUnitId = request.BusinessUnitId,
                CategoryId = request.CategoryId,
                TransactionType = request.TransactionType,
                Amount = request.Amount,
                TransactionDate = occurredAt,
                ReferenceType = sourceEntityType,
                ReferenceId = request.SourceEntityId,
                Description = description,
                CreatedBy = currentUser?.Username ?? "system-posting"
            };
            db.FinanceTransactions.Add(transaction);
            outcome = FinancePostingOutcome.Created;
        }
        else if (transaction.Amount != request.Amount
                 || transaction.BusinessUnitId != request.BusinessUnitId
                 || transaction.CategoryId != request.CategoryId
                 || transaction.TransactionDate != occurredAt
                 || transaction.Description != description)
        {
            transaction.BusinessUnitId = request.BusinessUnitId;
            transaction.CategoryId = request.CategoryId;
            transaction.Amount = request.Amount;
            transaction.TransactionDate = occurredAt;
            transaction.Description = description;
            transaction.UpdatedAt = DateTime.UtcNow;
            transaction.UpdatedBy = currentUser?.Username ?? "system-posting";
            outcome = FinancePostingOutcome.Updated;
        }

        var financeDocument = await documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
            request.CompanyId,
            request.BusinessUnitId,
            BusinessDocumentType.FinanceTransaction,
            nameof(FinanceTransaction),
            transaction.Id,
            $"FIN-{transaction.Id:N}",
            transaction.Amount,
            transaction.TransactionDate,
            Status: BusinessDocumentStatus.Settled), ct);
        transaction.BusinessDocumentId = financeDocument.Id;

        if (request.SourceBusinessDocumentId.HasValue
            && request.SourceBusinessDocumentId.Value != financeDocument.Id)
        {
            var settlementLink = await db.BusinessDocumentLinks.FirstOrDefaultAsync(x =>
                x.FromDocumentId == financeDocument.Id
                && x.ToDocumentId == request.SourceBusinessDocumentId.Value
                && x.LinkType == BusinessDocumentLinkType.Settlement, ct);
            if (settlementLink is null)
            {
                db.BusinessDocumentLinks.Add(new BusinessDocumentLink
                {
                    FromDocumentId = financeDocument.Id,
                    ToDocumentId = request.SourceBusinessDocumentId.Value,
                    LinkType = BusinessDocumentLinkType.Settlement,
                    Amount = transaction.Amount,
                    LinkedAt = transaction.TransactionDate,
                    Notes = transaction.Description
                });
            }
            else if (settlementLink.Amount != transaction.Amount
                     || settlementLink.LinkedAt != transaction.TransactionDate
                     || settlementLink.Notes != transaction.Description)
            {
                settlementLink.Amount = transaction.Amount;
                settlementLink.LinkedAt = transaction.TransactionDate;
                settlementLink.Notes = transaction.Description;
                settlementLink.UpdatedAt = DateTime.UtcNow;
                settlementLink.UpdatedBy = currentUser?.Username ?? "system-posting";
            }
        }

        return new FinancePostingResult(transaction.Id, financeDocument.Id, outcome);
    }

    private static void Validate(FinancePostingRequest request)
    {
        if (request.CompanyId == Guid.Empty || request.SourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId and SourceEntityId are required for finance posting.", nameof(request));
        }
        if (request.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Posting amount must be positive.");
        }
        if (string.IsNullOrWhiteSpace(request.SourceEntityType) || string.IsNullOrWhiteSpace(request.Description))
        {
            throw new ArgumentException("SourceEntityType and Description are required for finance posting.", nameof(request));
        }
    }
}
