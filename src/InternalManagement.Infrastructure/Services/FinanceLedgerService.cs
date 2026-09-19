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

public sealed class FinanceLedgerService : IFinanceLedgerService
{
    private const string TechnologyEducationAreaCode = "TECHNOLOGY_EDUCATION";
    private const string FashionAreaCode = "FASHION";
    private static readonly string[] TechnologyEducationBusinessUnitCodes = ["EDTECH", "CSCA", "INTERVIEW"];
    private static readonly string[] FashionBusinessUnitCodes = ["FASHION"];

    private readonly IApplicationDbContext _db;
    private readonly ILogger<FinanceLedgerService> _logger;
    private readonly ICurrentUserService? _currentUser;
    private readonly IBusinessDocumentRegistry? _documentRegistry;

    public FinanceLedgerService(
        IApplicationDbContext db,
        ILogger<FinanceLedgerService> logger,
        ICurrentUserService? currentUser = null,
        IBusinessDocumentRegistry? documentRegistry = null)
    {
        _db = db;
        _logger = logger;
        _currentUser = currentUser;
        _documentRegistry = documentRegistry;
    }

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

    public async Task<Result<CashFlowReportDto>> GetCashFlowReportAsync(
        DateTime? from,
        DateTime? to,
        Guid? businessUnitId,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<CashFlowReportDto>.Failure("Chưa xác định được công ty hiện tại.");

        var range = NormalizeRange(from, to);
        if (!range.IsValid)
            return Result<CashFlowReportDto>.Failure("Khoảng thời gian không hợp lệ.");

        var selectedBusinessUnitId = businessUnitId ?? _currentUser?.BusinessUnitId;
        var query = _db.FinanceTransactions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value
                && x.TransactionDate >= range.From
                && x.TransactionDate <= range.To);

        if (selectedBusinessUnitId.HasValue)
            query = query.Where(x => x.BusinessUnitId == selectedBusinessUnitId.Value);

        var transactions = await query.ToListAsync(ct);
        var byDay = transactions
            .GroupBy(x => x.TransactionDate.Date)
            .Select(group => new CashFlowDayDto(
                group.Key,
                group.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount),
                group.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount),
                group.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount)
                    - group.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount)))
            .OrderBy(x => x.Date)
            .ToList();

        var totalIncome = transactions
            .Where(x => x.TransactionType == TransactionType.Income)
            .Sum(x => x.Amount);
        var totalExpense = transactions
            .Where(x => x.TransactionType == TransactionType.Expense)
            .Sum(x => x.Amount);

        return Result<CashFlowReportDto>.Success(new CashFlowReportDto(
            range.From,
            range.To,
            totalIncome,
            totalExpense,
            totalIncome - totalExpense,
            byDay));
    }

    public async Task<Result<CompanyFinancialOverviewDto>> GetCompanyFinancialOverviewAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<CompanyFinancialOverviewDto>.Failure(
                "Chưa xác định được công ty hiện tại; không thể lập bảng tổng an toàn giữa các tenant.");

        var range = NormalizeRange(from, to);
        if (!range.IsValid)
            return Result<CompanyFinancialOverviewDto>.Failure("Khoảng thời gian không hợp lệ.");

        var recognizedCodes = TechnologyEducationBusinessUnitCodes
            .Concat(FashionBusinessUnitCodes)
            .ToArray();
        var businessUnits = await _db.BusinessUnits
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value && recognizedCodes.Contains(x.Code.ToUpper()))
            .Select(x => new { x.Id, Code = x.Code.ToUpper() })
            .ToListAsync(ct);

        var technologyEducationIds = businessUnits
            .Where(x => TechnologyEducationBusinessUnitCodes.Contains(x.Code))
            .Select(x => x.Id)
            .ToList();
        var fashionIds = businessUnits
            .Where(x => FashionBusinessUnitCodes.Contains(x.Code))
            .Select(x => x.Id)
            .ToList();
        var recognizedIds = technologyEducationIds.Concat(fashionIds).ToHashSet();

        // FinanceTransaction là nguồn duy nhất cho thu, chi và dòng tiền.
        var transactions = await _db.FinanceTransactions
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value
                && x.TransactionDate >= range.From
                && x.TransactionDate <= range.To)
            .ToListAsync(ct);

        var technologyEducationCash = SummarizeCash(
            transactions.Where(x => x.BusinessUnitId.HasValue
                && technologyEducationIds.Contains(x.BusinessUnitId.Value)));
        var fashionCash = SummarizeCash(
            transactions.Where(x => x.BusinessUnitId.HasValue
                && fashionIds.Contains(x.BusinessUnitId.Value)));
        var unclassifiedCash = SummarizeCash(
            transactions.Where(x => !x.BusinessUnitId.HasValue
                || !recognizedIds.Contains(x.BusinessUnitId.Value)));

        // ProfitAllocation là snapshot lợi nhuận của EDTECH/CSCA/INTERVIEW.
        // Chọn bản mới nhất cho mỗi chứng từ để dữ liệu lịch sử/trùng không bị cộng hai lần.
        var technologyEducationAllocations = await _db.ProfitAllocations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value
                && x.BusinessUnitId.HasValue
                && technologyEducationIds.Contains(x.BusinessUnitId.Value)
                && x.AllocatedAt >= range.From
                && x.AllocatedAt <= range.To)
            .ToListAsync(ct);
        var latestAllocations = technologyEducationAllocations
            .GroupBy(x => new { x.BusinessUnitId, x.ReferenceType, x.ReferenceId })
            .Select(x => x.OrderByDescending(y => y.AllocatedAt).ThenByDescending(y => y.Id).First())
            .ToList();
        var technologyEducationConfirmedProfit = latestAllocations.Sum(x => x.NetAmount);

        // OrderCostSnapshot là nguồn duy nhất cho lợi nhuận Fashion. Một đơn có thể
        // được snapshot nhiều lần; chỉ snapshot mới nhất trong kỳ được sử dụng.
        var fashionSnapshots = await _db.OrderCostSnapshots
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value
                && x.BusinessUnitId.HasValue
                && fashionIds.Contains(x.BusinessUnitId.Value)
                && x.SnapshottedAt >= range.From
                && x.SnapshottedAt <= range.To)
            .ToListAsync(ct);
        var latestFashionSnapshots = fashionSnapshots
            .GroupBy(x => x.SalesOrderId)
            .Select(x => x.OrderByDescending(y => y.SnapshottedAt).ThenByDescending(y => y.Id).First())
            .ToList();
        var fashionConfirmedProfit = latestFashionSnapshots
            .Where(x => x.CostStatus == CostStatus.Actual)
            .Sum(x => x.Profit);
        var fashionProvisionalProfit = latestFashionSnapshots
            .Where(x => x.CostStatus != CostStatus.Actual)
            .Sum(x => x.Profit);

        var technologyEducation = CreateAreaSummary(
            TechnologyEducationAreaCode,
            "Công nghệ - Giáo dục",
            TechnologyEducationBusinessUnitCodes,
            technologyEducationCash,
            technologyEducationConfirmedProfit,
            0,
            latestAllocations.Count,
            0,
            "Phân bổ lợi nhuận EDTECH/CSCA/INTERVIEW");
        var fashion = CreateAreaSummary(
            FashionAreaCode,
            "Thời trang",
            FashionBusinessUnitCodes,
            fashionCash,
            fashionConfirmedProfit,
            fashionProvisionalProfit,
            latestFashionSnapshots.Count(x => x.CostStatus == CostStatus.Actual),
            latestFashionSnapshots.Count(x => x.CostStatus != CostStatus.Actual),
            "Snapshot giá vốn và lợi nhuận đơn hàng thời trang");
        var areas = new[] { technologyEducation, fashion };
        var companyTotal = CreateCompanyTotal(areas);

        return Result<CompanyFinancialOverviewDto>.Success(new CompanyFinancialOverviewDto(
            range.From,
            range.To,
            areas,
            companyTotal,
            new UnclassifiedCashFlowDto(
                unclassifiedCash.TotalIncome,
                unclassifiedCash.TotalExpense,
                unclassifiedCash.NetCashFlow,
                unclassifiedCash.TransactionCount),
            unclassifiedCash.TransactionCount > 0,
            "Thu/chi và dòng tiền ròng lấy duy nhất từ sổ giao dịch. Lợi nhuận lấy duy nhất từ snapshot nghiệp vụ mới nhất của mỗi chứng từ/đơn hàng; chỉ giá vốn Actual được xem là đã chốt."));
    }

    public async Task<Result<FinanceProfitReportDto>> GetProfitReportAsync(
        DateTime? from,
        DateTime? to,
        Guid? businessUnitId,
        string? channel,
        Guid? productId,
        Guid? productVariantId,
        string? size,
        string? productionBatchCode,
        CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<FinanceProfitReportDto>.Failure("Chưa xác định được công ty hiện tại.");

        var range = NormalizeRange(from, to);
        if (!range.IsValid)
            return Result<FinanceProfitReportDto>.Failure("Khoảng thời gian không hợp lệ.");

        var selectedBusinessUnitId = businessUnitId ?? _currentUser?.BusinessUnitId;
        var snapshotsQuery = _db.OrderCostSnapshots
            .AsNoTracking()
            .Include(x => x.Items)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .Where(x => x.CompanyId == companyId.Value
                && x.SnapshottedAt >= range.From
                && x.SnapshottedAt <= range.To);

        if (selectedBusinessUnitId.HasValue)
            snapshotsQuery = snapshotsQuery.Where(x => x.BusinessUnitId == selectedBusinessUnitId.Value);

        var snapshots = await snapshotsQuery.ToListAsync(ct);
        var variantIds = snapshots
            .SelectMany(x => x.Items)
            .Select(x => x.ProductVariantId)
            .Distinct()
            .ToList();

        var batches = await _db.ProductionOrderOutputs
            .AsNoTracking()
            .Where(x => x.ProductionOrder.CompanyId == companyId.Value
                && variantIds.Contains(x.ProductVariantId))
            .Select(x => new
            {
                x.ProductVariantId,
                BatchCode = x.ProductionOrder.OrderNumber,
                x.ProductionOrder.CreatedAt
            })
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
        var batchByVariant = batches
            .GroupBy(x => x.ProductVariantId)
            .ToDictionary(x => x.Key, x => x.First().BatchCode);

        var businessUnitIds = snapshots
            .Where(x => x.BusinessUnitId.HasValue)
            .Select(x => x.BusinessUnitId!.Value)
            .Distinct()
            .ToList();
        var businessUnits = await _db.BusinessUnits
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId.Value && businessUnitIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);

        var lines = new List<ProfitLine>();
        foreach (var snapshot in snapshots)
        {
            if (!string.IsNullOrWhiteSpace(channel)
                && !string.Equals(snapshot.Channel, channel.Trim(), StringComparison.OrdinalIgnoreCase))
                continue;

            var items = snapshot.Items.ToList();
            if (items.Count == 0)
                continue;

            var totalItemGross = items.Sum(x => Math.Max(0, x.Quantity) * x.UnitSellingPrice);
            foreach (var item in items)
            {
                var variant = item.ProductVariant;
                var itemGross = Math.Max(0, item.Quantity) * item.UnitSellingPrice;
                var ratio = totalItemGross > 0
                    ? itemGross / totalItemGross
                    : 1m / items.Count;
                var batchCode = batchByVariant.GetValueOrDefault(item.ProductVariantId);

                if (productVariantId.HasValue && item.ProductVariantId != productVariantId.Value)
                    continue;
                if (productId.HasValue && (variant == null || variant.ProductId != productId.Value))
                    continue;
                if (!string.IsNullOrWhiteSpace(size)
                    && !string.Equals(variant?.Size, size.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.IsNullOrWhiteSpace(productionBatchCode)
                    && !string.Equals(batchCode, productionBatchCode.Trim(), StringComparison.OrdinalIgnoreCase))
                    continue;

                var isProvisional = snapshot.CostStatus is CostStatus.Estimated or CostStatus.Provisional
                    || item.CostStatus is CostStatus.Estimated or CostStatus.Provisional;
                var costStatus = isProvisional
                    ? (snapshot.CostStatus is CostStatus.Estimated or CostStatus.Provisional
                        ? snapshot.CostStatus
                        : item.CostStatus)
                    : snapshot.CostStatus;
                var cogs = item.TotalCogs != 0
                    ? item.TotalCogs
                    : snapshot.ActualCogs * ratio;

                lines.Add(new ProfitLine(
                    snapshot.Id,
                    snapshot.BusinessUnitId,
                    businessUnits.GetValueOrDefault(snapshot.BusinessUnitId ?? Guid.Empty)?.Code ?? "CHUA_PHAN_BO",
                    businessUnits.GetValueOrDefault(snapshot.BusinessUnitId ?? Guid.Empty)?.Name ?? "Chưa phân bổ",
                    string.IsNullOrWhiteSpace(snapshot.Channel) ? "Khác" : snapshot.Channel,
                    variant?.ProductId,
                    variant?.Product?.Name,
                    item.ProductVariantId,
                    variant?.Sku,
                    variant?.Size,
                    batchCode,
                    snapshot.GrossAmount * ratio,
                    snapshot.DiscountAmount * ratio,
                    snapshot.RefundAmount * ratio,
                    snapshot.NetSalesAmount * ratio,
                    cogs,
                    snapshot.PlatformFee * ratio,
                    snapshot.AffiliateFee * ratio,
                    snapshot.PaymentFee * ratio,
                    snapshot.AdvertisingCost * ratio,
                    snapshot.PackagingCost * ratio,
                    snapshot.OtherSellingExpense * ratio,
                    snapshot.ShippingSubsidy * ratio,
                    snapshot.TaxAmount * ratio,
                    snapshot.Profit * ratio,
                    isProvisional,
                    costStatus));
            }
        }

        var reportItems = lines
            .GroupBy(x => new
            {
                x.BusinessUnitId,
                x.BusinessUnitCode,
                x.BusinessUnitName,
                x.Channel,
                x.ProductId,
                x.ProductName,
                x.ProductVariantId,
                x.Sku,
                x.Size,
                x.ProductionBatchCode
            })
            .Select(group => new FinanceProfitReportItemDto(
                group.Key.BusinessUnitId,
                group.Key.BusinessUnitCode,
                group.Key.BusinessUnitName,
                group.Key.Channel,
                group.Key.ProductId,
                group.Key.ProductName,
                group.Key.ProductVariantId,
                group.Key.Sku,
                group.Key.Size,
                group.Key.ProductionBatchCode,
                group.Select(x => x.SnapshotId).Distinct().Count(),
                group.Sum(x => x.GrossSales),
                group.Sum(x => x.DiscountAmount),
                group.Sum(x => x.RefundAmount),
                group.Sum(x => x.NetSales),
                group.Sum(x => x.Cogs),
                group.Sum(x => x.PlatformFee),
                group.Sum(x => x.AffiliateFee),
                group.Sum(x => x.PaymentFee),
                group.Sum(x => x.AdvertisingCost),
                group.Sum(x => x.PackagingCost),
                group.Sum(x => x.OtherSellingExpense),
                group.Sum(x => x.ShippingSubsidy),
                group.Sum(x => x.TaxAmount),
                group.Where(x => !x.HasProvisionalCost).Sum(x => x.Profit),
                group.Where(x => x.HasProvisionalCost).Sum(x => x.Profit),
                group.Any(x => x.HasProvisionalCost),
                GetRepresentativeCostStatus(group).ToString(),
                GetCostStatusName(GetRepresentativeCostStatus(group))))
            .OrderBy(x => x.BusinessUnitCode)
            .ThenBy(x => x.Channel)
            .ThenBy(x => x.ProductName)
            .ThenBy(x => x.Size)
            .ToList();

        return Result<FinanceProfitReportDto>.Success(new FinanceProfitReportDto(
            range.From,
            range.To,
            reportItems,
            reportItems.Sum(x => x.GrossSales),
            reportItems.Sum(x => x.NetSales),
            reportItems.Sum(x => x.Cogs),
            reportItems.Sum(x => x.ConfirmedProfit),
            reportItems.Sum(x => x.ProvisionalProfit),
            reportItems.Any(x => x.HasProvisionalCost)));
    }

    public async Task<Result<FinanceReferenceDto>> ResolveReferenceAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referenceType) || referenceId == Guid.Empty)
            return Result<FinanceReferenceDto>.Failure("ReferenceType và ReferenceId là bắt buộc.");

        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<FinanceReferenceDto>.Failure("Chưa xác định được công ty hiện tại.");

        var result = await ResolveReferenceInternalAsync(
            NormalizeReferenceType(referenceType), referenceId, companyId.Value, ct);
        return Result<FinanceReferenceDto>.Success(result);
    }

    private async Task<Guid?> ResolveCompanyIdAsync(CancellationToken ct)
    {
        if (_currentUser?.CompanyId is { } currentCompanyId && currentCompanyId != Guid.Empty)
            return currentCompanyId;

        // Chỉ tự suy ra tenant khi database có đúng một công ty. Việc lấy công ty
        // đầu tiên khi có nhiều tenant có thể làm rò rỉ dữ liệu chéo công ty.
        var companyIds = await _db.Companies
            .AsNoTracking()
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync(ct);
        return companyIds.Count == 1 ? companyIds[0] : null;
    }

    private static CashTotals SummarizeCash(IEnumerable<FinanceTransaction> transactions)
    {
        var items = transactions.ToList();
        var income = items.Where(x => x.TransactionType == TransactionType.Income).Sum(x => x.Amount);
        var expense = items.Where(x => x.TransactionType == TransactionType.Expense).Sum(x => x.Amount);
        return new CashTotals(income, expense, income - expense, items.Count);
    }

    private static FinancialAreaSummaryDto CreateAreaSummary(
        string areaCode,
        string areaName,
        IReadOnlyList<string> businessUnitCodes,
        CashTotals cash,
        decimal confirmedProfit,
        decimal provisionalProfit,
        int confirmedSnapshotCount,
        int provisionalSnapshotCount,
        string profitSourceName)
    {
        var profitSnapshotCount = confirmedSnapshotCount + provisionalSnapshotCount;
        var hasProvisional = provisionalSnapshotCount > 0;
        var status = profitSnapshotCount == 0
            ? (Code: "NO_DATA", Name: "Chưa có dữ liệu lợi nhuận")
            : provisionalSnapshotCount == 0
                ? (Code: "ACTUAL", Name: "Đã chốt")
                : confirmedSnapshotCount == 0
                    ? (Code: "PROVISIONAL", Name: "Tạm tính")
                    : (Code: "MIXED", Name: "Một phần đã chốt, một phần tạm tính");

        return new FinancialAreaSummaryDto(
            areaCode,
            areaName,
            businessUnitCodes,
            cash.TotalIncome,
            cash.TotalExpense,
            cash.NetCashFlow,
            confirmedProfit + provisionalProfit,
            confirmedProfit,
            provisionalProfit,
            hasProvisional,
            status.Code,
            status.Name,
            profitSourceName,
            cash.TransactionCount,
            profitSnapshotCount);
    }

    private static FinancialAreaSummaryDto CreateCompanyTotal(
        IReadOnlyCollection<FinancialAreaSummaryDto> areas)
    {
        var confirmed = areas.Sum(x => x.ConfirmedProfit);
        var provisional = areas.Sum(x => x.ProvisionalProfit);
        var snapshotCount = areas.Sum(x => x.ProfitSnapshotCount);
        var hasProvisionalData = areas.Any(x => x.HasProvisionalData);
        var hasConfirmedData = areas.Any(x => x.ProfitDataStatus is "ACTUAL" or "MIXED");
        var cash = new CashTotals(
            areas.Sum(x => x.TotalIncome),
            areas.Sum(x => x.TotalExpense),
            areas.Sum(x => x.NetCashFlow),
            areas.Sum(x => x.CashTransactionCount));

        var total = CreateAreaSummary(
            "COMPANY_TOTAL",
            "Tổng toàn công ty",
            TechnologyEducationBusinessUnitCodes.Concat(FashionBusinessUnitCodes).ToArray(),
            cash,
            confirmed,
            provisional,
            hasConfirmedData ? 1 : 0,
            hasProvisionalData ? 1 : 0,
            "Tổng snapshot của hai mảng");
        return total with { ProfitSnapshotCount = snapshotCount };
    }

    private async Task<string?> ValidateBusinessUnitAsync(
        Guid companyId,
        Guid? businessUnitId,
        CancellationToken ct)
    {
        if (!businessUnitId.HasValue)
            return null;

        var exists = await _db.BusinessUnits.AnyAsync(
            x => x.CompanyId == companyId && x.Id == businessUnitId.Value, ct);
        return exists ? null : "Business Unit không thuộc công ty hiện tại.";
    }

    private async Task<Result<FinanceCategory?>> ValidateCategoryAsync(
        Guid companyId,
        Guid? categoryId,
        TransactionType transactionType,
        CancellationToken ct)
    {
        if (!categoryId.HasValue)
            return Result<FinanceCategory?>.Success(null);

        var category = await _db.FinanceCategories.FirstOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == categoryId.Value && x.IsActive, ct);
        if (category == null)
            return Result<FinanceCategory?>.Failure("Khoản mục thu/chi không tồn tại hoặc đã bị khóa.");
        if (category.Type != transactionType)
            return Result<FinanceCategory?>.Failure("Loại giao dịch không khớp với loại của khoản mục.");

        return Result<FinanceCategory?>.Success(category);
    }

    private async Task<FinanceTransaction?> FindIdempotentTransactionAsync(
        Guid companyId,
        string? referenceType,
        Guid? referenceId,
        TransactionType transactionType,
        CancellationToken ct)
    {
        if (referenceType == null || !referenceId.HasValue)
            return null;

        return await _db.FinanceTransactions
            .Include(x => x.Category)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId
                && x.ReferenceType == referenceType
                && x.ReferenceId == referenceId.Value
                && x.TransactionType == transactionType, ct);
    }

    private async Task<FinanceTransactionDto> ToTransactionDtoAsync(
        FinanceTransaction transaction,
        CancellationToken ct)
    {
        CashAccount? cashAccount = null;
        if (transaction.ReferenceId.HasValue && IsCashReference(transaction.ReferenceType))
        {
            cashAccount = await _db.CashAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == transaction.CompanyId
                    && x.Id == transaction.ReferenceId.Value, ct);
        }

        var referenceTitle = transaction.ReferenceType != null && transaction.ReferenceId.HasValue
            ? (await ResolveReferenceInternalAsync(
                transaction.ReferenceType,
                transaction.ReferenceId.Value,
                transaction.CompanyId,
                ct)).ReferenceTitle
            : null;

        return new FinanceTransactionDto(
            transaction.Id,
            transaction.CompanyId,
            transaction.BusinessUnitId,
            transaction.CategoryId,
            transaction.Category?.Code,
            transaction.Category?.Name,
            TypeCode(transaction.TransactionType),
            TypeName(transaction.TransactionType),
            transaction.Amount,
            transaction.TransactionDate,
            transaction.ReferenceType,
            transaction.ReferenceId,
            referenceTitle,
            cashAccount?.Id,
            cashAccount?.Code,
            cashAccount?.Name,
            transaction.Description,
            transaction.CreatedAt);
    }

    private async Task<FinanceReferenceDto> ResolveReferenceInternalAsync(
        string referenceType,
        Guid referenceId,
        Guid companyId,
        CancellationToken ct)
    {
        var normalized = NormalizeReferenceType(referenceType);
        switch (normalized)
        {
            case "CashAccount":
            case "CashAccountAdjustment":
            {
                var account = await _db.CashAccounts.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, account != null,
                    account == null ? $"Tài khoản tiền {referenceId}" : $"{account.Code} - {account.Name}");
            }
            case "SalesOrder":
            {
                var order = await _db.SalesOrders.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, order != null,
                    order == null ? $"Đơn hàng {referenceId}" : $"Đơn hàng {order.OrderNumber}");
            }
            case "Return":
            {
                var item = await _db.Returns.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Phiếu trả hàng {referenceId}" : $"Phiếu trả hàng {item.ReturnNumber}");
            }
            case "SalesSettlement":
            {
                var settlement = await _db.SalesSettlements.AsNoTracking()
                    .Include(x => x.SalesOrder)
                    .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, settlement != null,
                    settlement == null
                        ? $"Giao dịch Fashion {referenceId}"
                        : $"{settlement.Kind} {settlement.PaymentReference} - đơn {settlement.SalesOrder.OrderNumber}");
            }
            case "ProductionOrder":
            {
                var order = await _db.ProductionOrders.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, order != null,
                    order == null ? $"Lệnh sản xuất {referenceId}" : $"Lệnh sản xuất {order.OrderNumber}");
            }
            case "CscaClass":
            {
                var item = await _db.CscaClasses.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Lớp CSCA {referenceId}" : $"{item.Code} - {item.Name}");
            }
            case "Payment":
            {
                var payment = await _db.Payments.AsNoTracking()
                    .Include(x => x.Customer)
                    .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, payment != null,
                    payment == null
                        ? $"Thanh toán {referenceId}"
                        : $"Thanh toán {payment.SourcePaymentId} - {payment.Customer.FullName}");
            }
            case "InterviewCustomer":
            {
                var item = await _db.InterviewCustomers.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Khách Interview {referenceId}" : $"{item.FullName} ({item.PackageName})");
            }
            case "PayrollPeriod":
            {
                var item = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Kỳ lương {referenceId}" : item.Name);
            }
            case "FinanceCategory":
            {
                var item = await _db.FinanceCategories.AsNoTracking().FirstOrDefaultAsync(
                    x => x.CompanyId == companyId && x.Id == referenceId, ct);
                return new FinanceReferenceDto(normalized, referenceId, item != null,
                    item == null ? $"Khoản mục {referenceId}" : $"{item.Code} - {item.Name}");
            }
            default:
                return new FinanceReferenceDto(normalized, referenceId, false, $"{normalized} - {referenceId}");
        }
    }

    private static FinanceCategoryDto ToCategoryDto(FinanceCategory category) => new(
        category.Id,
        category.CompanyId,
        category.BusinessUnitId,
        category.Code,
        category.Name,
        TypeCode(category.Type),
        TypeName(category.Type),
        category.IsActive);

    private static CashAccountDto ToCashAccountDto(CashAccount account) => new(
        account.Id,
        account.CompanyId,
        account.Code,
        account.Name,
        account.AccountNumber,
        account.BankName,
        account.CurrentBalance,
        account.CreatedAt);

    private static void ApplyCashDelta(CashAccount account, TransactionType type, decimal amount)
    {
        account.CurrentBalance += type == TransactionType.Income ? amount : -amount;
    }

    private static bool IsCashReference(string? referenceType) =>
        string.Equals(referenceType, "CashAccount", StringComparison.OrdinalIgnoreCase)
        || string.Equals(referenceType, "CashAccountAdjustment", StringComparison.OrdinalIgnoreCase);

    private static string TypeCode(TransactionType type) =>
        type == TransactionType.Income ? "Income" : "Expense";

    private static string TypeName(TransactionType type) =>
        type == TransactionType.Income ? "Thu" : "Chi";

    private static bool TryParseTransactionType(string? value, out TransactionType type)
    {
        type = TransactionType.Expense;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "income":
            case "thu":
            case "thu nhập":
            case "thu_nhap":
            case "1":
                type = TransactionType.Income;
                return true;
            case "expense":
            case "chi":
            case "chi phí":
            case "chi_phi":
            case "0":
                type = TransactionType.Expense;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeReferenceType(string value)
    {
        var normalized = value.Trim().Replace(" ", string.Empty).Replace("_", string.Empty);
        return normalized.ToLowerInvariant() switch
        {
            "cashaccount" => "CashAccount",
            "cashaccountadjustment" => "CashAccountAdjustment",
            "salesorder" => "SalesOrder",
            "return" => "Return",
            "salessettlement" => "SalesSettlement",
            "productionorder" => "ProductionOrder",
            "cscaclass" => "CscaClass",
            "payment" => "Payment",
            "interviewcustomer" => "InterviewCustomer",
            "payrollperiod" => "PayrollPeriod",
            "financecategory" => "FinanceCategory",
            _ => value.Trim()
        };
    }

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeDate(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateRange NormalizeRange(DateTime? from, DateTime? to)
    {
        var fromValue = NormalizeDate(from ?? DateTime.UtcNow.Date.AddDays(-30));
        var rawTo = to ?? DateTime.UtcNow;
        // Query UI thường gửi ngày kết thúc ở 00:00. Xem đó là toàn bộ ngày để
        // giao dịch phát sinh buổi trưa/tối không bị mất khỏi báo cáo.
        if (to.HasValue && rawTo.TimeOfDay == TimeSpan.Zero && rawTo.Date < DateTime.MaxValue.Date)
            rawTo = rawTo.Date.AddDays(1).AddTicks(-1);
        var toValue = NormalizeDate(rawTo);
        return new DateRange(fromValue, toValue, toValue >= fromValue);
    }

    private static string GetCostStatusName(CostStatus status) => status switch
    {
        CostStatus.Actual => "Đã chốt",
        CostStatus.Standard => "Theo định mức",
        CostStatus.Estimated => "Tạm tính",
        CostStatus.Provisional => "Tạm thời",
        _ => status.ToString()
    };

    private static CostStatus GetRepresentativeCostStatus(IEnumerable<ProfitLine> lines)
    {
        var statuses = lines.Select(x => x.CostStatus).ToHashSet();
        if (statuses.Contains(CostStatus.Provisional))
            return CostStatus.Provisional;
        if (statuses.Contains(CostStatus.Estimated))
            return CostStatus.Estimated;
        if (statuses.Contains(CostStatus.Actual))
            return CostStatus.Actual;
        return CostStatus.Standard;
    }

    private sealed record DateRange(DateTime From, DateTime To, bool IsValid);

    private sealed record CashTotals(
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetCashFlow,
        int TransactionCount);

    private sealed record ProfitLine(
        Guid SnapshotId,
        Guid? BusinessUnitId,
        string BusinessUnitCode,
        string BusinessUnitName,
        string Channel,
        Guid? ProductId,
        string? ProductName,
        Guid ProductVariantId,
        string? Sku,
        string? Size,
        string? ProductionBatchCode,
        decimal GrossSales,
        decimal DiscountAmount,
        decimal RefundAmount,
        decimal NetSales,
        decimal Cogs,
        decimal PlatformFee,
        decimal AffiliateFee,
        decimal PaymentFee,
        decimal AdvertisingCost,
        decimal PackagingCost,
        decimal OtherSellingExpense,
        decimal ShippingSubsidy,
        decimal TaxAmount,
        decimal Profit,
        bool HasProvisionalCost,
        CostStatus CostStatus);
}
