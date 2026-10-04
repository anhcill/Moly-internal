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
    public async Task<Result<IReadOnlyList<FinanceCategoryDto>>> GetCategoriesAsync(
        TransactionType? type,
        bool includeInactive,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<IReadOnlyList<FinanceCategoryDto>>.Failure("Chưa xác định được công ty hiện tại.");

        var query = _db.FinanceCategories
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value);

        if (type.HasValue)
            query = query.Where(x => x.Type == type.Value);
        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var categories = await query
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Code)
            .ToListAsync(ct);

        return Result<IReadOnlyList<FinanceCategoryDto>>.Success(
            categories.Select(ToCategoryDto).ToList());
    }

    public async Task<Result<FinanceCategoryDto>> CreateCategoryAsync(
        CreateFinanceCategoryRequest request,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<FinanceCategoryDto>.Failure("Chưa xác định được công ty hiện tại.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return Result<FinanceCategoryDto>.Failure("Mã và tên khoản mục không được để trống.");
        if (!TryParseTransactionType(request.TransactionType, out var transactionType))
            return Result<FinanceCategoryDto>.Failure("Loại giao dịch không hợp lệ. Hãy dùng Thu/Chi hoặc Income/Expense.");

        var businessUnitId = request.BusinessUnitId ?? _currentUser?.BusinessUnitId;
        var businessUnitError = await ValidateBusinessUnitAsync(companyId.Value, businessUnitId, ct);
        if (businessUnitError != null)
            return Result<FinanceCategoryDto>.Failure(businessUnitError);

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.FinanceCategories.AnyAsync(
            x => x.CompanyId == companyId.Value && x.Code == code, ct);
        if (exists)
            return Result<FinanceCategoryDto>.Failure($"Mã khoản mục '{code}' đã tồn tại trong công ty.");

        var category = new FinanceCategory
        {
            CompanyId = companyId.Value,
            BusinessUnitId = businessUnitId,
            Code = code,
            Name = request.Name.Trim(),
            Type = transactionType,
            IsActive = true,
            CreatedBy = _currentUser?.Username
        };

        _db.FinanceCategories.Add(category);
        await _db.SaveChangesAsync(ct);
        return Result<FinanceCategoryDto>.Success(ToCategoryDto(category));
    }

    public async Task<Result<IReadOnlyList<CashAccountDto>>> GetCashAccountsAsync(CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<IReadOnlyList<CashAccountDto>>.Failure("Chưa xác định được công ty hiện tại.");

        var accounts = await _db.CashAccounts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value)
            .OrderBy(x => x.Code)
            .ToListAsync(ct);

        return Result<IReadOnlyList<CashAccountDto>>.Success(accounts.Select(ToCashAccountDto).ToList());
    }

    public async Task<Result<CashAccountDto>> CreateCashAccountAsync(
        CreateCashAccountRequest request,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<CashAccountDto>.Failure("Chưa xác định được công ty hiện tại.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
            return Result<CashAccountDto>.Failure("Mã và tên tài khoản tiền không được để trống.");
        if (request.OpeningBalance < 0)
            return Result<CashAccountDto>.Failure("Số dư đầu kỳ không được âm.");

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.CashAccounts.AnyAsync(
            x => x.CompanyId == companyId.Value && x.Code == code, ct);
        if (exists)
            return Result<CashAccountDto>.Failure($"Mã tài khoản tiền '{code}' đã tồn tại.");

        var account = new CashAccount
        {
            CompanyId = companyId.Value,
            Code = code,
            Name = request.Name.Trim(),
            AccountNumber = NullIfWhiteSpace(request.AccountNumber),
            BankName = NullIfWhiteSpace(request.BankName),
            CurrentBalance = request.OpeningBalance,
            CreatedBy = _currentUser?.Username
        };

        _db.CashAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
        return Result<CashAccountDto>.Success(ToCashAccountDto(account));
    }

}
