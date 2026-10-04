using InternalManagement.Application.Features.Fashion.Costing;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Entities.Fashion;

namespace InternalManagement.UnitTests.Fashion;

public sealed class OrderCostCalculatorTests
{
    [Fact]
    public void Calculate_IncludesActualCogsAndAllChannelFees()
    {
        var variantId = Guid.NewGuid();
        var request = new CreateSalesOrderRequest(
            "SHOPEE", "order-1", "Khách hàng", null, null,
            DiscountAmount: 20m,
            ShippingCustomerPaid: 0m,
            ShippingShopSubsidy: 5m,
            Items: [new CreateSalesOrderItemRequest(variantId, 2, 100m)],
            AdvertisingCost: 3m,
            PackagingCost: 2m,
            OtherSellingExpense: 1m);
        var policy = new ChannelFeePolicy
        {
            PlatformFeeRate = 0.10m,
            AffiliateFeeRate = 0.05m,
            PaymentFeeRate = 0.02m,
            FixedPaymentFee = 2m,
            TaxRate = 0.01m
        };

        var result = OrderCostCalculator.Calculate(request, policy,
            new Dictionary<Guid, decimal> { [variantId] = 40m });

        Assert.Equal(200m, result.Gross);
        Assert.Equal(180m, result.NetSales);
        Assert.Equal(80m, result.Cogs);
        Assert.Equal(18m, result.PlatformFee);
        Assert.Equal(9m, result.AffiliateFee);
        Assert.Equal(5.6m, result.PaymentFee);
        Assert.Equal(1.8m, result.Tax);
        Assert.Equal(54.6m, result.Profit);
        Assert.Equal(54.6m / 180m, result.Margin);
    }
}
