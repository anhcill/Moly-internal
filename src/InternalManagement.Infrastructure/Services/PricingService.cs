using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Domain.Entities.Fashion;

namespace InternalManagement.Infrastructure.Services;

public sealed class PricingService : IPricingService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PricingService> _logger;

    public PricingService(IApplicationDbContext db, ICurrentUserService currentUser, ILogger<PricingService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetTenantAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            companyId = (await _db.Companies.FirstOrDefaultAsync(x => x.Code == "MOLI", ct))?.Id ?? Guid.Empty;
        }
        var businessUnitId = (await _db.BusinessUnits.FirstOrDefaultAsync(
            x => x.CompanyId == companyId && x.Code == "FASHION" && x.IsActive && !x.IsDeleted, ct))?.Id
            ?? _currentUser.BusinessUnitId;
        return (companyId.Value, businessUnitId);
    }

    public async Task<Result<ChannelFeePolicyDto>> CreatePolicyAsync(CreateChannelFeePolicyRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var effectiveFrom = NormalizeUtc(request.EffectiveFrom);
        DateTime? effectiveTo = request.EffectiveTo.HasValue ? NormalizeUtc(request.EffectiveTo.Value) : null;
        if (string.IsNullOrWhiteSpace(request.Channel) || effectiveFrom == default)
        {
            return Result<ChannelFeePolicyDto>.Failure("Kênh và ngày hiệu lực là bắt buộc.");
        }
        if (new[] { request.PlatformFeeRate, request.AffiliateFeeRate, request.PaymentFeeRate, request.TaxRate }.Any(x => x < 0 || x > 1) || request.FixedPaymentFee < 0 || request.DefaultShippingSubsidy < 0)
        {
            return Result<ChannelFeePolicyDto>.Failure("Tỷ lệ phí phải nằm trong khoảng 0..1 và chi phí cố định không được âm.");
        }
        if (effectiveTo.HasValue && effectiveTo.Value <= effectiveFrom)
        {
            return Result<ChannelFeePolicyDto>.Failure("Ngày kết thúc phải sau ngày bắt đầu hiệu lực.");
        }

        var channel = request.Channel.Trim().ToUpperInvariant();
        var currentPolicy = await _db.ChannelFeePolicies
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.Channel == channel && x.IsActive)
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (currentPolicy != null && effectiveFrom < currentPolicy.EffectiveFrom &&
            (!currentPolicy.EffectiveTo.HasValue || effectiveFrom <= currentPolicy.EffectiveTo.Value))
        {
            return Result<ChannelFeePolicyDto>.Failure("Không được tạo chính sách có ngày hiệu lực lùi vào khoảng đã có.");
        }

        // Close the previous open-ended period at the boundary instead of
        // rewriting its rates. This keeps historical orders reproducible.
        if (currentPolicy != null && currentPolicy.EffectiveFrom < effectiveFrom &&
            (!currentPolicy.EffectiveTo.HasValue || currentPolicy.EffectiveTo.Value >= effectiveFrom))
        {
            currentPolicy.EffectiveTo = effectiveFrom.AddTicks(-1);
        }

        var version = (await _db.ChannelFeePolicies.Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.Channel == channel).Select(x => (int?)x.VersionNumber).MaxAsync(ct) ?? 0) + 1;
        var policy = new ChannelFeePolicy
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = string.IsNullOrWhiteSpace(request.Code) ? $"{channel}-V{version}" : request.Code.Trim().ToUpperInvariant(),
            Channel = channel,
            VersionNumber = version,
            PlatformFeeRate = request.PlatformFeeRate,
            AffiliateFeeRate = request.AffiliateFeeRate,
            PaymentFeeRate = request.PaymentFeeRate,
            FixedPaymentFee = request.FixedPaymentFee,
            TaxRate = request.TaxRate,
            DefaultShippingSubsidy = request.DefaultShippingSubsidy,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            IsActive = true
        };

        _db.ChannelFeePolicies.Add(policy);
        await _db.SaveChangesAsync(ct);
        return Result<ChannelFeePolicyDto>.Success(ToDto(policy));
    }

    public async Task<Result<PricingSimulationDto>> SimulateAsync(PricingSimulationRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.ListPrice <= 0 || request.DiscountAmount < 0 || request.DiscountAmount >= request.ListPrice)
        {
            return Result<PricingSimulationDto>.Failure("Giá niêm yết phải lớn hơn 0 và voucher không được lớn hơn giá bán.");
        }

        var targetMargin = request.TargetMargin ?? 0.30m;
        if (targetMargin < 0 || targetMargin >= 1)
        {
            return Result<PricingSimulationDto>.Failure("Biên lợi nhuận mục tiêu phải từ 0 đến dưới 100%.");
        }

        var variant = await _db.ProductVariants.Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == request.ProductVariantId && x.Product.CompanyId == companyId && x.Product.BusinessUnitId == businessUnitId && x.IsActive, ct);
        if (variant == null)
        {
            return Result<PricingSimulationDto>.Failure("SKU không tồn tại trong phạm vi công ty.");
        }
        if (variant.CostPrice <= 0)
        {
            return Result<PricingSimulationDto>.Failure("SKU chưa có giá vốn hợp lệ; không được mô phỏng lợi nhuận bằng giá vốn bằng 0.");
        }

        var channel = request.Channel.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        var policy = await _db.ChannelFeePolicies.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.Channel == channel && x.IsActive && x.EffectiveFrom <= now && (x.EffectiveTo == null || x.EffectiveTo >= now))
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (policy == null)
        {
            return Result<PricingSimulationDto>.Failure($"Chưa có chính sách phí hiệu lực cho kênh {channel}.");
        }

        var shipping = request.ShippingSubsidy ?? policy.DefaultShippingSubsidy;
        var affiliateRate = request.AffiliateFeeRateOverride ?? policy.AffiliateFeeRate;
        var advertisingRate = request.AdvertisingFeeRateOverride ?? 0m;
        if (shipping < 0 || affiliateRate < 0 || affiliateRate > 1 || advertisingRate < 0 || advertisingRate > 1 || request.PackagingCost < 0 || request.OtherSellingExpense < 0)
        {
            return Result<PricingSimulationDto>.Failure("Phí vận chuyển, tiếp thị, quảng cáo và chi phí bán hàng không hợp lệ.");
        }

        var customerPaid = request.ListPrice - request.DiscountAmount;
        var platformFee = customerPaid * policy.PlatformFeeRate;
        var affiliateFee = customerPaid * affiliateRate;
        var advertisingCost = customerPaid * advertisingRate;
        var paymentFee = customerPaid * policy.PaymentFeeRate + policy.FixedPaymentFee;
        var tax = customerPaid * policy.TaxRate;
        var profit = customerPaid - variant.CostPrice - platformFee - affiliateFee - advertisingCost - paymentFee - shipping - tax - request.PackagingCost - request.OtherSellingExpense;
        var margin = customerPaid == 0 ? 0 : profit / customerPaid;
        var variableRate = policy.PlatformFeeRate + affiliateRate + advertisingRate + policy.PaymentFeeRate + policy.TaxRate;
        var fixedCost = variant.CostPrice + policy.FixedPaymentFee + shipping + request.PackagingCost + request.OtherSellingExpense;
        var breakEvenCustomerPrice = variableRate >= 1 ? 0 : fixedCost / (1 - variableRate);
        var targetDenominator = 1 - variableRate - targetMargin;
        var requiredCustomerPrice = targetDenominator <= 0 ? 0 : fixedCost / targetDenominator;

        var result = new PricingSimulationDto(
            variant.Id,
            variant.Sku,
            channel,
            variant.CostStatus,
            variant.CostPrice,
            request.ListPrice,
            request.DiscountAmount,
            decimal.Round(customerPaid, 2),
            decimal.Round(customerPaid, 2),
            decimal.Round(platformFee, 2),
            decimal.Round(affiliateFee, 2),
            decimal.Round(paymentFee, 2),
            decimal.Round(shipping, 2),
            decimal.Round(tax, 2),
            decimal.Round(profit, 2),
            decimal.Round(margin, 6),
            decimal.Round(breakEvenCustomerPrice, 2),
            decimal.Round(requiredCustomerPrice, 2),
            decimal.Round(requiredCustomerPrice + request.DiscountAmount, 2),
            request.TargetMargin,
            decimal.Round(advertisingCost, 2),
            decimal.Round(request.PackagingCost, 2),
            decimal.Round(request.OtherSellingExpense, 2));

        _logger.LogInformation("Pricing simulation for {Sku} on {Channel}: profit {Profit}, margin {Margin}, cost status {CostStatus}.", variant.Sku, channel, result.Profit, result.Margin, result.CostStatus);
        return Result<PricingSimulationDto>.Success(result);
    }

    private static ChannelFeePolicyDto ToDto(ChannelFeePolicy x) => new(x.Id, x.CompanyId, x.Code, x.Channel, x.VersionNumber, x.PlatformFeeRate, x.AffiliateFeeRate, x.PaymentFeeRate, x.FixedPaymentFee, x.TaxRate, x.DefaultShippingSubsidy, x.EffectiveFrom, x.EffectiveTo, x.IsActive);

    private static DateTime NormalizeUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
