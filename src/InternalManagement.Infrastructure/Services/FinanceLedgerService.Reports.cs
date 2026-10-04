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

}
