using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class FinanceLedgerService
{
    public async Task<Result<PaginatedResult<FinanceTransactionDto>>> GetTransactionsAsync(
        DateTime? from,
        DateTime? to,
        TransactionType? type,
        Guid? businessUnitId,
        string? referenceType,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<PaginatedResult<FinanceTransactionDto>>.Failure("Chưa xác định được công ty hiện tại.");

        var range = NormalizeRange(from, to);
        if (!range.IsValid)
            return Result<PaginatedResult<FinanceTransactionDto>>.Failure("Khoảng thời gian không hợp lệ.");

        var selectedBusinessUnitId = businessUnitId ?? _currentUser?.BusinessUnitId;
        var normalizedReferenceType = string.IsNullOrWhiteSpace(referenceType)
            ? null
            : NormalizeReferenceType(referenceType);

        var query = _db.FinanceTransactions
            .AsNoTracking()
            .Include(x => x.Category)
            .Where(x => x.CompanyId == companyId.Value
                && x.TransactionDate >= range.From
                && x.TransactionDate <= range.To);

        if (type.HasValue)
            query = query.Where(x => x.TransactionType == type.Value);
        if (selectedBusinessUnitId.HasValue)
            query = query.Where(x => x.BusinessUnitId == selectedBusinessUnitId.Value);
        if (normalizedReferenceType != null)
            query = query.Where(x => x.ReferenceType == normalizedReferenceType);

        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var totalCount = await query.CountAsync(ct);
        var transactions = await query
            .OrderByDescending(x => x.TransactionDate)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = new List<FinanceTransactionDto>(transactions.Count);
        foreach (var transaction in transactions)
            items.Add(await ToTransactionDtoAsync(transaction, ct));

        return Result<PaginatedResult<FinanceTransactionDto>>.Success(
            new PaginatedResult<FinanceTransactionDto>(items, totalCount, pageIndex, pageSize));
    }

    public async Task<Result<FinanceTransactionDto>> CreateTransactionAsync(
        CreateFinanceTransactionRequest request,
        CancellationToken ct)
    {
        if (!TryParseTransactionType(request.TransactionType, out var transactionType))
            return Result<FinanceTransactionDto>.Failure("Loại giao dịch không hợp lệ. Hãy dùng Thu/Chi hoặc Income/Expense.");
        if (request.Amount <= 0)
            return Result<FinanceTransactionDto>.Failure("Số tiền giao dịch phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return Result<FinanceTransactionDto>.Failure("Nội dung giao dịch không được để trống.");

        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<FinanceTransactionDto>.Failure("Chưa xác định được công ty hiện tại.");

        var businessUnitId = request.BusinessUnitId ?? _currentUser?.BusinessUnitId;
        var businessUnitError = await ValidateBusinessUnitAsync(companyId.Value, businessUnitId, ct);
        if (businessUnitError != null)
            return Result<FinanceTransactionDto>.Failure(businessUnitError);

        var categoryResult = await ValidateCategoryAsync(
            companyId.Value, request.CategoryId, transactionType, ct);
        if (!categoryResult.Succeeded)
            return Result<FinanceTransactionDto>.Failure(categoryResult.Errors);

        string? referenceType = null;
        Guid? referenceId = null;
        CashAccount? cashAccount = null;

        if (request.CashAccountId.HasValue)
        {
            if (request.ReferenceId.HasValue || !string.IsNullOrWhiteSpace(request.ReferenceType))
                return Result<FinanceTransactionDto>.Failure("Không gửi đồng thời CashAccountId và tham chiếu giao dịch khác.");

            cashAccount = await _db.CashAccounts.FirstOrDefaultAsync(
                x => x.CompanyId == companyId.Value && x.Id == request.CashAccountId.Value, ct);
            if (cashAccount == null)
                return Result<FinanceTransactionDto>.Failure("Không tìm thấy tài khoản tiền thuộc công ty hiện tại.");

            referenceType = "CashAccount";
            referenceId = cashAccount.Id;
        }
        else if (request.ReferenceId.HasValue || !string.IsNullOrWhiteSpace(request.ReferenceType))
        {
            if (!request.ReferenceId.HasValue || string.IsNullOrWhiteSpace(request.ReferenceType))
                return Result<FinanceTransactionDto>.Failure("ReferenceType và ReferenceId phải được gửi cùng nhau.");

            referenceType = NormalizeReferenceType(request.ReferenceType);
            referenceId = request.ReferenceId.Value;
            if (referenceId == Guid.Empty)
                return Result<FinanceTransactionDto>.Failure("ReferenceId không hợp lệ.");
        }

        var existing = await FindIdempotentTransactionAsync(
            companyId.Value, referenceType, referenceId, transactionType, ct);
        if (existing != null)
        {
            if (existing.Amount != request.Amount)
                return Result<FinanceTransactionDto>.Failure(
                    "Tham chiếu giao dịch đã tồn tại nhưng số tiền khác; dữ liệu không được ghi đè.");

            _logger.LogInformation("Bỏ qua giao dịch Finance trùng tham chiếu {ReferenceType}/{ReferenceId}.",
                referenceType, referenceId);
            return Result<FinanceTransactionDto>.Success(await ToTransactionDtoAsync(existing, ct));
        }

        var transaction = new FinanceTransaction
        {
            CompanyId = companyId.Value,
            BusinessUnitId = businessUnitId,
            CategoryId = request.CategoryId,
            Category = categoryResult.Value,
            TransactionType = transactionType,
            Amount = request.Amount,
            TransactionDate = NormalizeDate(request.TransactionDate ?? DateTime.UtcNow),
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Description = request.Description.Trim(),
            CreatedBy = _currentUser?.Username
        };

        if (cashAccount != null)
            ApplyCashDelta(cashAccount, transactionType, request.Amount);

        if (_documentRegistry is not null)
        {
            var businessDocument = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId.Value,
                businessUnitId,
                BusinessDocumentType.FinanceTransaction,
                nameof(FinanceTransaction),
                transaction.Id,
                $"FIN-{transaction.Id:N}",
                transaction.Amount,
                transaction.TransactionDate,
                Status: BusinessDocumentStatus.Settled), ct);
            transaction.BusinessDocumentId = businessDocument.Id;

            if (referenceType is not null && referenceId.HasValue)
            {
                var sourceDocument = await _db.BusinessDocuments.FirstOrDefaultAsync(x =>
                    x.CompanyId == companyId.Value
                    && x.SourceEntityType == referenceType
                    && x.SourceEntityId == referenceId.Value, ct);
                if (sourceDocument is not null && sourceDocument.Id != businessDocument.Id)
                {
                    _db.BusinessDocumentLinks.Add(new BusinessDocumentLink
                    {
                        FromDocumentId = businessDocument.Id,
                        ToDocumentId = sourceDocument.Id,
                        LinkType = BusinessDocumentLinkType.Settlement,
                        Amount = transaction.Amount,
                        LinkedAt = transaction.TransactionDate,
                        Notes = transaction.Description
                    });
                }
            }
        }

        _db.FinanceTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
        return Result<FinanceTransactionDto>.Success(await ToTransactionDtoAsync(transaction, ct));
    }

    public async Task<Result<FinanceTransactionDto>> AdjustCashAccountAsync(
        Guid cashAccountId,
        AdjustCashAccountRequest request,
        CancellationToken ct)
    {
        if (!TryParseTransactionType(request.TransactionType, out var transactionType))
            return Result<FinanceTransactionDto>.Failure("Loại giao dịch không hợp lệ. Hãy dùng Thu/Chi hoặc Income/Expense.");
        if (request.Amount <= 0)
            return Result<FinanceTransactionDto>.Failure("Số tiền điều chỉnh phải lớn hơn 0.");
        if (string.IsNullOrWhiteSpace(request.Description))
            return Result<FinanceTransactionDto>.Failure("Nội dung điều chỉnh không được để trống.");

        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<FinanceTransactionDto>.Failure("Chưa xác định được công ty hiện tại.");

        var account = await _db.CashAccounts.FirstOrDefaultAsync(
            x => x.CompanyId == companyId.Value && x.Id == cashAccountId, ct);
        if (account == null)
            return Result<FinanceTransactionDto>.Failure("Không tìm thấy tài khoản tiền thuộc công ty hiện tại.");

        var businessUnitId = request.BusinessUnitId ?? _currentUser?.BusinessUnitId;
        var businessUnitError = await ValidateBusinessUnitAsync(companyId.Value, businessUnitId, ct);
        if (businessUnitError != null)
            return Result<FinanceTransactionDto>.Failure(businessUnitError);

        var categoryResult = await ValidateCategoryAsync(
            companyId.Value, request.CategoryId, transactionType, ct);
        if (!categoryResult.Succeeded)
            return Result<FinanceTransactionDto>.Failure(categoryResult.Errors);

        var transaction = new FinanceTransaction
        {
            CompanyId = companyId.Value,
            BusinessUnitId = businessUnitId,
            CategoryId = request.CategoryId,
            Category = categoryResult.Value,
            TransactionType = transactionType,
            Amount = request.Amount,
            TransactionDate = NormalizeDate(request.TransactionDate ?? DateTime.UtcNow),
            ReferenceType = "CashAccountAdjustment",
            ReferenceId = account.Id,
            Description = request.Description.Trim(),
            CreatedBy = _currentUser?.Username
        };

        ApplyCashDelta(account, transactionType, request.Amount);
        if (_documentRegistry is not null)
        {
            var businessDocument = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId.Value,
                businessUnitId,
                BusinessDocumentType.FinanceTransaction,
                nameof(FinanceTransaction),
                transaction.Id,
                $"FIN-{transaction.Id:N}",
                transaction.Amount,
                transaction.TransactionDate,
                Status: BusinessDocumentStatus.Settled), ct);
            transaction.BusinessDocumentId = businessDocument.Id;
        }
        _db.FinanceTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
        return Result<FinanceTransactionDto>.Success(await ToTransactionDtoAsync(transaction, ct));
    }

}
