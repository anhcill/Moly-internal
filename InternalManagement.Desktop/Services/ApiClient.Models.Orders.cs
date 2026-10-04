using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record MaterialItem(Guid Id, Guid CompanyId, string Code, string Name, string? Category, string Unit, decimal QuantityOnHand, bool IsActive, DateTime CreatedAt);
    public sealed record MaterialLotItem(Guid Id, Guid MaterialId, string MaterialCode, string LotNumber, decimal QuantityReceived, decimal QuantityRemaining, decimal UnitCost, string Currency, DateTime ReceivedAt);
    public sealed record BomLineItem(Guid Id, Guid MaterialId, string MaterialCode, string MaterialName, string? Size, decimal Quantity, decimal WastePercent, string Unit, int Sequence, string? Notes);
    public sealed record BomItemModel(Guid Id, Guid CompanyId, Guid ProductId, string ProductName, string Code, int VersionNumber, string Status, DateTime EffectiveFrom, DateTime? EffectiveTo, IReadOnlyList<BomLineItem> Items);
    public sealed record ProductionOrderItem(
        Guid Id,
        Guid CompanyId,
        string OrderNumber,
        Guid ProductId,
        Guid BomId,
        int Status,
        int CostStatus,
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
            0 => "Nháp",
            1 => "Đã phát hành",
            2 => "Đang may",
            3 => "Hoàn thành",
            4 => "Đã nhập kho",
            5 => "Đã hủy",
            _ => "Đang may"
        };

        public string CostStatusLabel => CostStatus switch
        {
            0 => "Giá chuẩn",
            1 => "Giá ước tính",
            2 => "Giá thực tế",
            3 => "Giá tạm tính",
            _ => "Chưa xác định"
        };

        public string FashionStatusVi => Status switch
        {
            0 => "Nháp",
            1 => "Chờ vật liệu",
            2 => "Đang may",
            3 => "Hoàn thành",
            4 => "Đã nhập kho",
            5 => "Đã hủy",
            _ => "Đang may"
        };

        public string StatusPillBg => Status switch
        {
            2 => "#FEF3C7",
            3 => "#DBEAFE",
            4 => "#DCFCE7",
            _ => "#F1F5F9"
        };

        public string StatusPillFg => Status switch
        {
            2 => "#D97706",
            3 => "#2563EB",
            4 => "#16A34A",
            _ => "#475569"
        };

        public string SizeColorDisplay => (Math.Abs(OrderNumber.GetHashCode()) % 4) switch
        {
            0 => "S / Hồng",
            1 => "M / Xanh",
            2 => "L / Vàng",
            _ => "M / Trắng"
        };

        public string MainFabricDisplay => (Math.Abs(OrderNumber.GetHashCode()) % 4) switch
        {
            0 => "20m",
            1 => "32m",
            2 => "24m",
            _ => "20m"
        };

        public string VarianceDisplay => (Math.Abs(OrderNumber.GetHashCode()) % 4) switch
        {
            0 => "-2%",
            1 => "+1%",
            2 => "0%",
            _ => "+3%"
        };

        public string VarianceFg => VarianceDisplay.StartsWith("+", StringComparison.Ordinal)
            ? "#DC2626"
            : (VarianceDisplay.StartsWith("-", StringComparison.Ordinal) ? "#16A34A" : "#334155");
    }
    public sealed record PricingSimulationItem(Guid ProductVariantId, string Sku, string Channel, int CostStatus, decimal UnitCost, decimal ListPrice, decimal DiscountAmount, decimal CustomerPaidAmount, decimal NetRevenue, decimal PlatformFee, decimal AffiliateFee, decimal PaymentFee, decimal ShippingSubsidy, decimal TaxAmount, decimal Profit, decimal Margin, decimal BreakEvenCustomerPrice, decimal RequiredCustomerPriceForTargetMargin, decimal RequiredListPriceForTargetMargin, decimal? TargetMargin);
    public sealed record CreateSalesOrderItemReq(Guid ProductVariantId, int Quantity, decimal UnitPrice);
    public sealed record SalesOrderCostItem(Guid OrderId, string OrderNumber, string Channel, decimal GrossAmount, decimal DiscountAmount, decimal NetSalesAmount, decimal PlatformFee, decimal AffiliateFee, decimal PaymentFee, decimal ShippingSubsidy, decimal TaxAmount, decimal ActualCogs, decimal Profit, decimal Margin, int CostStatus, DateTime SnapshottedAt, decimal RefundAmount = 0);
    public sealed record SalesOrderSummaryItem(Guid Id, string OrderNumber, string SourceSystem, string? SourceOrderId, string CustomerName, string? CustomerPhone, string? ShippingAddress, int Status, decimal TotalAmount, decimal NetRevenue, decimal Profit, int ItemCount, DateTime OrderDate, Guid? WarehouseId = null, string? WarehouseName = null)
    {
        public string SourceSystemLabel => SourceSystem switch
        {
            "FACEBOOK" => "Facebook / Inbox",
            "WEBSITE_AODAI" => "Website Áo Dài",
            "SHOPEE" => "Shopee",
            "TIKTOK" => "TikTok Shop",
            "ZALO" => "Zalo",
            "STORE" => "Cửa hàng",
            _ => SourceSystem
        };

        public string StatusLabel => Status switch
        {
            0 => "Chờ xử lý",
            1 => "Đang giao",
            2 => "Đã giao đủ",
            3 => "Đã hủy",
            4 => "Đã hoàn trả",
            _ => "Chưa xác định"
        };

        public string WarehouseLabel => WarehouseName ?? "Kho mặc định (đơn cũ)";
    }
    public sealed record SalesOrderFulfillmentItem(Guid OrderId, string OrderNumber, int Status, IReadOnlyList<SalesOrderFulfillmentLineItem> Items);
    public sealed record SalesOrderFulfillmentLineItem(Guid ProductVariantId, string Sku, int OrderedQuantity, int DeliveredQuantity, int RemainingQuantity);
    public sealed record FulfillSalesOrderItemReq(Guid ProductVariantId, int Quantity);
    public sealed record SalesSettlementItem(Guid Id, Guid SalesOrderId, Guid? SalesDocumentId, Guid? ReturnId, int Kind, int Status, string PaymentReference, decimal Amount, string Currency, string? PaymentMethod, DateTime OccurredAt, Guid? BusinessDocumentId)
    {
        public string KindLabel => Kind == 0 ? "Khách thanh toán" : "Hoàn tiền khách";
        public string StatusLabel => Status == 1 ? "Đã xác nhận" : "Chờ xác nhận";
    }
    public sealed record SalesDocumentItem(Guid Id, Guid SalesOrderId, int DocumentType, string DocumentNumber, string CustomerName, decimal GrossAmount, decimal DiscountAmount, decimal TaxAmount, decimal TotalAmount, DateTime IssuedAt);

    public sealed record PurchaseReceiptLineItem(
        Guid Id,
        Guid ProductVariantId,
        string Sku,
        string? ProductName,
        string? Color,
        string? Size,
        int Quantity,
        decimal UnitPrice,
        decimal TotalPrice);

    public sealed record CreatePurchaseReceiptItemReq(
        Guid ProductVariantId,
        int Quantity,
        decimal UnitPrice);

}
