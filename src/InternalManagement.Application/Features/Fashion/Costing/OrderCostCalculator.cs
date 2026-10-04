using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Entities.Fashion;

namespace InternalManagement.Application.Features.Fashion.Costing;

public static class OrderCostCalculator
{
    public static OrderCostBreakdown Calculate(
        CreateSalesOrderRequest request,
        ChannelFeePolicy policy,
        IReadOnlyDictionary<Guid, decimal> unitCosts)
    {
        var gross = request.Items.Sum(item => item.Quantity * item.UnitPrice);
        var netSales = gross - request.DiscountAmount;
        var platformFee = netSales * policy.PlatformFeeRate;
        var affiliateFee = netSales * policy.AffiliateFeeRate;
        var paymentFee = netSales * policy.PaymentFeeRate + policy.FixedPaymentFee;
        var tax = netSales * policy.TaxRate;
        var cogs = request.Items.Sum(item => item.Quantity * unitCosts[item.ProductVariantId]);
        var profit = netSales - cogs - platformFee - affiliateFee - paymentFee
            - request.ShippingShopSubsidy - tax - request.AdvertisingCost
            - request.PackagingCost - request.OtherSellingExpense;
        var margin = netSales == 0 ? 0 : profit / netSales;

        return new OrderCostBreakdown(gross, netSales, platformFee, affiliateFee,
            paymentFee, tax, cogs, profit, margin);
    }
}

public sealed record OrderCostBreakdown(
    decimal Gross,
    decimal NetSales,
    decimal PlatformFee,
    decimal AffiliateFee,
    decimal PaymentFee,
    decimal Tax,
    decimal Cogs,
    decimal Profit,
    decimal Margin);
