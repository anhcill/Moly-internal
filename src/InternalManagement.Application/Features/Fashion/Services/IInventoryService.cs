using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;

namespace InternalManagement.Application.Features.Fashion.Services;

public interface IInventoryService
{
    Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(bool includeInactive, CancellationToken ct);
    Task<Result<WarehouseDto>> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken ct);
    Task<Result<WarehouseDto>> UpdateWarehouseAsync(Guid id, UpdateWarehouseRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteWarehouseAsync(Guid id, CancellationToken ct);
    Task<PaginatedResult<InventoryBalanceDto>> GetBalancesAsync(string? search, Guid? warehouseId, int pageIndex, int pageSize, CancellationToken ct);
    Task<PaginatedResult<InventoryMovementDto>> GetMovementsAsync(Guid? variantId, Guid? warehouseId, int pageIndex, int pageSize, CancellationToken ct);
    Task<PaginatedResult<PurchaseReceiptDto>> GetPurchaseReceiptsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<PurchaseReceiptDto>> CreatePurchaseReceiptAsync(CreatePurchaseReceiptRequest request, CancellationToken ct);
    Task<PurchaseReceiptDetailDto?> GetPurchaseReceiptByIdAsync(Guid id, CancellationToken ct);
    Task<Result<PurchaseReceiptDto>> UpdatePurchaseReceiptHeaderAsync(Guid id, UpdatePurchaseReceiptHeaderRequest request, CancellationToken ct);
    Task<Result<PurchaseReceiptDto>> CancelPurchaseReceiptAsync(Guid id, CancelPurchaseReceiptRequest request, CancellationToken ct);
    Task<Result<PurchaseReceiptAttachmentDto>> UploadPurchaseReceiptAttachmentAsync(
        Guid receiptId,
        Stream content,
        string fileName,
        string? contentType,
        long length,
        CancellationToken ct);
    Task<Result<InventoryMovementDto>> AdjustStockAsync(StockAdjustmentRequest request, CancellationToken ct);
}
