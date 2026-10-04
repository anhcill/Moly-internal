using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record FashionProductItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? Category,
        string? Description,
        bool IsActive,
        int VariantCount,
        DateTime CreatedAt);

    public sealed record FashionProductDetailItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? Category,
        string? Description,
        bool IsActive,
        DateTime CreatedAt,
        IReadOnlyList<FashionVariantItem> Variants);

    public sealed record FashionVariantItem(
        Guid Id,
        Guid ProductId,
        string ProductName,
        string Sku,
        string? Barcode,
        string? Color,
        string? Size,
        decimal CostPrice,
        decimal SellingPrice,
        bool IsActive,
        int OnHandQuantity,
        DateTime CreatedAt,
        int SourcingType = 0,
        int CostStatus = 0)
    {
        public string SourcingTypeLabel => SourcingType switch
        {
            0 => "Tự sản xuất",
            1 => "Mua ngoài",
            _ => "Chưa xác định"
        };

        public string CostStatusLabel => CostStatus switch
        {
            0 => "Giá chuẩn",
            1 => "Giá ước tính",
            2 => "Giá thực tế",
            3 => "Giá tạm tính",
            _ => "Chưa xác định"
        };
    }

    public sealed record SupplierItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? ContactName,
        string? Phone,
        string? PhoneNumbers,
        string? Email,
        string? Address,
        string? BankAccounts,
        DateTime CreatedAt)
    {
        public string? PhoneNumbersDisplay => string.IsNullOrWhiteSpace(PhoneNumbers) ? Phone : PhoneNumbers;
        public string? BankAccountsDisplay => BankAccounts;
    }

    public sealed record InventoryBalanceItem(
        Guid Id,
        Guid CompanyId,
        Guid ProductVariantId,
        string Sku,
        string? ProductName,
        string? Color,
        string? Size,
        int OnHandQuantity,
        int ReservedQuantity,
        int AvailableQuantity,
        DateTime LastUpdated,
        Guid WarehouseId = default,
        string WarehouseName = "");

    public sealed record InventoryMovementItem(
        Guid Id,
        Guid CompanyId,
        Guid ProductVariantId,
        string Sku,
        string? ProductName,
        int MovementType,
        int QuantityDelta,
        decimal UnitCost,
        string? ReferenceType,
        Guid? ReferenceId,
        DateTime MovementDate,
        string? Notes,
        Guid WarehouseId = default,
        string WarehouseName = "")
    {
        public string MovementTypeLabel => MovementType switch
        {
            0 => "Nhập hàng",
            1 => "Xuất bán",
            2 => "Khách trả hàng",
            3 => "Trả nhà cung cấp",
            4 => "Điều chỉnh tồn kho",
            5 => "Hàng hỏng / loại bỏ",
            6 => "Nhập thành phẩm sản xuất",
            7 => "Xuất nguyên vật liệu sản xuất",
            8 => "Hao hụt sản xuất",
            _ => "Khác"
        };
    }

    public sealed record PurchaseReceiptItemModel(
        Guid Id,
        Guid CompanyId,
        string ReceiptNumber,
        Guid SupplierId,
        string SupplierName,
        decimal TotalAmount,
        DateTime ReceivedAt,
        string Status,
        string? Notes,
        int ItemCount,
        DateTime CreatedAt,
        Guid WarehouseId = default,
        string WarehouseName = "",
        Guid? AttachmentId = null,
        string? AttachmentFileName = null,
        string? AttachmentContentType = null,
        long? AttachmentSizeBytes = null,
        string? AttachmentUrl = null)
    {
        public string AttachmentLabel => string.IsNullOrWhiteSpace(AttachmentFileName) ? "Chưa có" : "Mở chứng từ";
    }

    public sealed record ReceiptAttachmentItem(
        Guid Id,
        Guid ReceiptId,
        string FileName,
        string ContentType,
        long FileSizeBytes,
        string Url,
        DateTime CreatedAt);

    public sealed record PurchaseReceiptDetailModel(
        Guid Id,
        Guid CompanyId,
        string ReceiptNumber,
        Guid SupplierId,
        string SupplierName,
        decimal TotalAmount,
        DateTime ReceivedAt,
        string Status,
        string? Notes,
        DateTime CreatedAt,
        IReadOnlyList<PurchaseReceiptLineItem> Items,
        Guid WarehouseId = default,
        string WarehouseName = "",
        Guid? AttachmentId = null,
        string? AttachmentFileName = null,
        string? AttachmentContentType = null,
        long? AttachmentSizeBytes = null,
        string? AttachmentUrl = null);

    public sealed record WarehouseItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Code,
        string Name,
        string? Address,
        bool IsActive,
        bool IsDefault)
    {
        public string StatusLabel => IsActive ? "Đang hoạt động" : "Ngừng dùng";
        public string DefaultLabel => IsDefault ? "Kho mặc định" : "";
    }

}
