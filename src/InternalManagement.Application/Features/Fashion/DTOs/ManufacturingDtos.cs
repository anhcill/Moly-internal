using System.Text.Json;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Fashion.DTOs;

public record MaterialDto(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    string? Category,
    string Unit,
    decimal QuantityOnHand,
    bool IsActive,
    DateTime CreatedAt);

public record CreateMaterialRequest(
    string Code,
    string Name,
    string? Category,
    string Unit);

public record ReceiveMaterialRequest(
    Guid MaterialId,
    Guid? SupplierId,
    string LotNumber,
    decimal Quantity,
    decimal UnitCost,
    string Currency,
    string? Notes);

public record MaterialLotDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string LotNumber,
    decimal QuantityReceived,
    decimal QuantityRemaining,
    decimal UnitCost,
    string Currency,
    DateTime ReceivedAt);

public record BomItemDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string? Size,
    decimal Quantity,
    decimal WastePercent,
    string Unit,
    int Sequence,
    string? Notes);

public record BomDto(
    Guid Id,
    Guid CompanyId,
    Guid ProductId,
    string ProductName,
    string Code,
    int VersionNumber,
    string Status,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    IReadOnlyList<BomItemDto> Items);

public record CreateBomItemRequest(
    Guid MaterialId,
    string? Size,
    decimal Quantity,
    decimal WastePercent,
    string Unit,
    int Sequence,
    string? Notes);

public record CreateBomRequest(
    Guid ProductId,
    string Code,
    DateTime EffectiveFrom,
    IReadOnlyList<CreateBomItemRequest> Items);

public record ProductionOutputRequest(
    Guid ProductVariantId,
    int PlannedQuantity);

public record CreateProductionOrderRequest(
    Guid ProductId,
    Guid BomId,
    IReadOnlyList<ProductionOutputRequest> Outputs,
    string? Notes);

public record ProductionMaterialConsumptionRequest(
    Guid MaterialId,
    Guid? MaterialLotId,
    decimal Quantity,
    string? Size);

public record ProductionOperationRequest(
    int Sequence,
    string Name,
    bool IsOutsideProcessing,
    string RateType,
    decimal Rate,
    decimal ActualUnits,
    string? Notes);

public record ProductionOutputResultRequest(
    Guid ProductVariantId,
    int GoodQuantity,
    int DefectiveQuantity,
    int ReworkQuantity);

public record CompleteProductionOrderRequest(
    IReadOnlyList<ProductionMaterialConsumptionRequest> Materials,
    IReadOnlyList<ProductionOperationRequest> Operations,
    IReadOnlyList<ProductionOutputResultRequest> Outputs,
    decimal OverheadCost,
    decimal ScrapReworkCost,
    string? Notes);

public record ProductionOrderDto(
    Guid Id,
    Guid CompanyId,
    string OrderNumber,
    Guid ProductId,
    Guid BomId,
    ProductionOrderStatus Status,
    CostStatus CostStatus,
    int PlannedQuantity,
    int GoodQuantity,
    int DefectiveQuantity,
    int ReworkQuantity,
    decimal StandardCost,
    decimal ActualMaterialCost,
    decimal ActualLaborCost,
    decimal ActualOutsideProcessingCost,
    decimal ActualOverheadCost,
    decimal ActualScrapReworkCost,
    decimal ActualTotalCost,
    decimal ActualUnitCost,
    DateTime? CompletedAt,
    DateTime? ClosedAt,
    string ProductName = "")
{
    public string StatusLabel => Status switch
    {
        ProductionOrderStatus.Draft => "Nháp",
        ProductionOrderStatus.Released => "Đã phát hành",
        ProductionOrderStatus.InProgress => "Đang sản xuất",
        ProductionOrderStatus.Completed => "Đã hoàn thành",
        ProductionOrderStatus.Closed => "Đã chốt",
        ProductionOrderStatus.Cancelled => "Đã hủy",
        _ => "Chưa xác định"
    };

    public string CostStatusLabel => CostStatus switch
    {
        CostStatus.Standard => "Giá chuẩn",
        CostStatus.Estimated => "Giá ước tính",
        CostStatus.Actual => "Giá thực tế",
        CostStatus.Provisional => "Giá tạm tính",
        _ => "Chưa xác định"
    };
}

public record ChannelFeePolicyDto(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Channel,
    int VersionNumber,
    decimal PlatformFeeRate,
    decimal AffiliateFeeRate,
    decimal PaymentFeeRate,
    decimal FixedPaymentFee,
    decimal TaxRate,
    decimal DefaultShippingSubsidy,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    bool IsActive);

public record CreateChannelFeePolicyRequest(
    string Code,
    string Channel,
    decimal PlatformFeeRate,
    decimal AffiliateFeeRate,
    decimal PaymentFeeRate,
    decimal FixedPaymentFee,
    decimal TaxRate,
    decimal DefaultShippingSubsidy,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo = null);

public record PricingSimulationRequest(
    Guid ProductVariantId,
    string Channel,
    decimal ListPrice,
    decimal DiscountAmount,
    decimal? ShippingSubsidy,
    decimal? TargetMargin,
    decimal? AffiliateFeeRateOverride,
    decimal? AdvertisingFeeRateOverride = null,
    decimal PackagingCost = 0,
    decimal OtherSellingExpense = 0);

public record PricingSimulationDto(
    Guid ProductVariantId,
    string Sku,
    string Channel,
    CostStatus CostStatus,
    decimal UnitCost,
    decimal ListPrice,
    decimal DiscountAmount,
    decimal CustomerPaidAmount,
    decimal NetRevenue,
    decimal PlatformFee,
    decimal AffiliateFee,
    decimal PaymentFee,
    decimal ShippingSubsidy,
    decimal TaxAmount,
    decimal Profit,
    decimal Margin,
    decimal BreakEvenCustomerPrice,
    decimal RequiredCustomerPriceForTargetMargin,
    decimal RequiredListPriceForTargetMargin,
    decimal? TargetMargin,
    decimal AdvertisingCost = 0,
    decimal PackagingCost = 0,
    decimal OtherSellingExpense = 0)
{
    public decimal GrossProfit => CustomerPaidAmount - UnitCost;
    public decimal ProfitAfterPlatformFees => GrossProfit - PlatformFee - PaymentFee;
    public decimal ProfitAfterMarketing => ProfitAfterPlatformFees - AffiliateFee - AdvertisingCost;
    public decimal NetProfit => Profit;

    public string CostStatusLabel => CostStatus switch
    {
        CostStatus.Standard => "Giá chuẩn",
        CostStatus.Estimated => "Giá ước tính",
        CostStatus.Actual => "Giá thực tế",
        CostStatus.Provisional => "Giá tạm tính",
        _ => "Chưa xác định"
    };
}

public record CreateSalesOrderItemRequest(
    Guid ProductVariantId,
    int Quantity,
    decimal UnitPrice);

public record CreateSalesOrderRequest(
    string SourceSystem,
    string? SourceOrderId,
    string CustomerName,
    string? CustomerPhone,
    string? ShippingAddress,
    decimal DiscountAmount,
    decimal ShippingCustomerPaid,
    decimal ShippingShopSubsidy,
    IReadOnlyList<CreateSalesOrderItemRequest> Items,
    decimal AdvertisingCost = 0,
    decimal PackagingCost = 0,
    decimal OtherSellingExpense = 0,
    Guid? WarehouseId = null);

public record SalesOrderCostDto(
    Guid OrderId,
    string OrderNumber,
    string Channel,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal NetSalesAmount,
    decimal PlatformFee,
    decimal AffiliateFee,
    decimal PaymentFee,
    decimal ShippingSubsidy,
    decimal TaxAmount,
    decimal ActualCogs,
    decimal Profit,
    decimal Margin,
    CostStatus CostStatus,
    DateTime SnapshottedAt,
    decimal RefundAmount = 0,
    decimal AdvertisingCost = 0,
    decimal PackagingCost = 0,
    decimal OtherSellingExpense = 0)
{
    public decimal GrossProfit => NetSalesAmount - ActualCogs;
    public decimal ProfitAfterPlatformFees => GrossProfit - PlatformFee - PaymentFee;
    public decimal ProfitAfterMarketing => ProfitAfterPlatformFees - AffiliateFee - AdvertisingCost;
    public decimal NetProfit => Profit;

    public string CostStatusLabel => CostStatus switch
    {
        CostStatus.Standard => "Giá chuẩn",
        CostStatus.Estimated => "Giá ước tính",
        CostStatus.Actual => "Giá thực tế",
        CostStatus.Provisional => "Giá tạm tính",
        _ => "Chưa xác định"
    };
}

public record SalesOrderSummaryDto(
    Guid Id,
    string OrderNumber,
    string SourceSystem,
    string? SourceOrderId,
    string CustomerName,
    string? CustomerPhone,
    string? ShippingAddress,
    SalesOrderStatus Status,
    decimal TotalAmount,
    decimal NetRevenue,
    decimal Profit,
    int ItemCount,
    DateTime OrderDate,
    Guid? WarehouseId = null,
    string? WarehouseName = null)
{
    public string StatusLabel => Status switch
    {
        SalesOrderStatus.Pending => "Chờ xử lý",
        SalesOrderStatus.Processing => "Đang giao",
        SalesOrderStatus.Completed => "Đã giao đủ",
        SalesOrderStatus.Cancelled => "Đã hủy",
        SalesOrderStatus.Returned => "Đã hoàn trả",
        _ => "Chưa xác định"
    };
}

public record UpdateSalesOrderHeaderRequest(
    string CustomerName,
    string? CustomerPhone,
    string? ShippingAddress,
    string? SourceOrderId);

public record CancelSalesOrderRequest(string? Reason);

public record FulfillSalesOrderItemRequest(
    Guid ProductVariantId,
    int Quantity);

public record FulfillSalesOrderRequest(
    IReadOnlyList<FulfillSalesOrderItemRequest> Items);

public record SalesOrderFulfillmentDto(
    Guid OrderId,
    string OrderNumber,
    SalesOrderStatus Status,
    IReadOnlyList<SalesOrderFulfillmentItemDto> Items)
{
    public string StatusLabel => Status switch
    {
        SalesOrderStatus.Pending => "Chờ xử lý",
        SalesOrderStatus.Processing => "Đang xử lý",
        SalesOrderStatus.Completed => "Đã giao đủ",
        SalesOrderStatus.Cancelled => "Đã hủy",
        SalesOrderStatus.Returned => "Đã hoàn trả",
        _ => "Chưa xác định"
    };
}

public record SalesOrderFulfillmentItemDto(
    Guid ProductVariantId,
    string Sku,
    int OrderedQuantity,
    int DeliveredQuantity,
    int RemainingQuantity);

public record CreateReturnItemRequest(
    Guid ProductVariantId,
    int Quantity,
    string ConditionStatus,
    bool Restockable,
    decimal? RefundAmount);

public record CreateReturnRequest(
    Guid SalesOrderId,
    string? ReturnNumber,
    string? Reason,
    IReadOnlyList<CreateReturnItemRequest> Items);

public record InspectReturnRequest(string Decision);

public record ReturnDto(
    Guid Id,
    Guid SalesOrderId,
    string ReturnNumber,
    string Status,
    decimal TotalRefundAmount,
    DateTime ReturnedAt,
    IReadOnlyList<ReturnItemDto> Items)
{
    public string StatusLabel => Status.ToUpperInvariant() switch
    {
        "INSPECTING" => "Chờ kiểm tra",
        "APPROVED" => "Đã duyệt",
        "REJECTED" => "Từ chối",
        "REFUNDED" => "Đã hoàn tiền",
        _ => Status
    };
}

public record ReturnItemDto(
    Guid ProductVariantId,
    string Sku,
    int Quantity,
    string ConditionStatus,
    bool Restockable,
    decimal RefundAmount);

public record RecordSalesSettlementRequest(
    SalesSettlementKind Kind,
    decimal Amount,
    string PaymentReference,
    DateTime OccurredAt,
    SalesSettlementStatus Status = SalesSettlementStatus.Confirmed,
    string Currency = "VND",
    string? PaymentMethod = null,
    Guid? SalesDocumentId = null,
    Guid? ReturnId = null);

public record SalesSettlementDto(
    Guid Id,
    Guid SalesOrderId,
    Guid? SalesDocumentId,
    Guid? ReturnId,
    SalesSettlementKind Kind,
    SalesSettlementStatus Status,
    string PaymentReference,
    decimal Amount,
    string Currency,
    string? PaymentMethod,
    DateTime OccurredAt,
    Guid? BusinessDocumentId);

public record IssueSalesDocumentRequest(SalesDocumentType DocumentType);

public record SalesDocumentDto(
    Guid Id,
    Guid SalesOrderId,
    SalesDocumentType DocumentType,
    string DocumentNumber,
    string CustomerName,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    DateTime IssuedAt,
    IReadOnlyList<SalesDocumentItemDto> Items)
{
    public string DocumentTypeLabel => DocumentType switch
    {
        SalesDocumentType.Invoice => "Hóa đơn",
        SalesDocumentType.RetailReceipt => "Phiếu bán lẻ",
        _ => "Chứng từ bán hàng"
    };
}

public record SalesDocumentItemDto(
    Guid ProductVariantId,
    string Sku,
    string ProductName,
    string? Color,
    string? Size,
    int Quantity,
    decimal UnitPrice,
    decimal TaxAmount,
    decimal LineTotal);

public record ImportSalesOrderRequest(
    string SourceSystem,
    JsonElement Payload);

public record ImportSalesOrderResult(
    bool Created,
    Guid? OrderId,
    string? OrderNumber,
    string Message);
