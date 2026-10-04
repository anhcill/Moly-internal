using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class OrderCostingService
{
    public async Task<SalesOrderCostDto?> GetLatestSnapshotAsync(Guid orderId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var snapshot = await _db.OrderCostSnapshots.AsNoTracking()
            .Include(x => x.SalesOrder)
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.SalesOrderId == orderId)
            .OrderByDescending(x => x.SnapshottedAt)
            .FirstOrDefaultAsync(ct);
        return snapshot == null ? null : ToDto(snapshot.SalesOrder, snapshot);
    }

    public async Task<PaginatedResult<SalesOrderSummaryDto>> GetOrdersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.SalesOrders.AsNoTracking()
            .Where(o => o.CompanyId == companyId && o.BusinessUnitId == businessUnitId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.OrderNumber.Contains(term) || o.CustomerName.Contains(term) ||
                (o.CustomerPhone != null && o.CustomerPhone.Contains(term)) ||
                (o.SourceOrderId != null && o.SourceOrderId.Contains(term)));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(o => o.OrderDate)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize)
            .Select(o => new SalesOrderSummaryDto(o.Id, o.OrderNumber, o.SourceSystem, o.SourceOrderId,
                o.CustomerName, o.CustomerPhone, o.ShippingAddress, o.Status, o.TotalAmount,
                o.NetRevenue, o.Profit, o.Items.Count, o.OrderDate, o.WarehouseId,
                o.Warehouse == null ? null : o.Warehouse.Name))
            .ToListAsync(ct);
        return new PaginatedResult<SalesOrderSummaryDto>(items, total, pageIndex, pageSize);
    }

    public async Task<SalesOrderFulfillmentDto?> GetFulfillmentAsync(Guid orderId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var order = await _db.SalesOrders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId && o.BusinessUnitId == businessUnitId, ct);
        return order == null ? null : ToFulfillmentDto(order);
    }

    public async Task<Result<SalesOrderSummaryDto>> UpdateOrderHeaderAsync(Guid orderId, UpdateSalesOrderHeaderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (string.IsNullOrWhiteSpace(request.CustomerName))
            return Result<SalesOrderSummaryDto>.Failure("Tên khách hàng không được để trống.");

        var order = await _db.SalesOrders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId && o.BusinessUnitId == businessUnitId, ct);
        if (order == null)
            return Result<SalesOrderSummaryDto>.Failure("Không tìm thấy đơn hàng.");
        if (order.Status != SalesOrderStatus.Pending)
            return Result<SalesOrderSummaryDto>.Failure("Chỉ được sửa thông tin liên hệ khi đơn còn chờ xử lý. Đơn đã giao phải dùng chứng từ điều chỉnh/đổi trả.");

        var externalId = NormalizeOptional(request.SourceOrderId);
        if (externalId != null && await _db.SalesOrders.AnyAsync(o =>
                o.Id != orderId && o.CompanyId == companyId && o.BusinessUnitId == businessUnitId &&
                o.SourceSystem == order.SourceSystem && o.SourceOrderId == externalId, ct))
            return Result<SalesOrderSummaryDto>.Failure("Mã đơn ngoài đã tồn tại trong cùng kênh bán.");

        order.CustomerName = request.CustomerName.Trim();
        order.CustomerPhone = NormalizeOptional(request.CustomerPhone);
        order.ShippingAddress = NormalizeOptional(request.ShippingAddress);
        order.SourceOrderId = externalId;
        await _db.SaveChangesAsync(ct);
        return Result<SalesOrderSummaryDto>.Success(ToSummaryDto(order));
    }

    public async Task<Result<SalesOrderSummaryDto>> CancelOrderAsync(Guid orderId, CancelSalesOrderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var order = await _db.SalesOrders.Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CompanyId == companyId && o.BusinessUnitId == businessUnitId, ct);
        if (order == null)
            return Result<SalesOrderSummaryDto>.Failure("Không tìm thấy đơn hàng.");
        if (order.Status != SalesOrderStatus.Pending)
            return Result<SalesOrderSummaryDto>.Failure("Chỉ hủy trực tiếp đơn chưa giao. Đơn đang/đã giao phải dùng quy trình đổi trả để giữ đúng sổ kho và doanh thu.");

        var warehouse = await GetOrderWarehouseAsync(order, companyId, businessUnitId, ct);
        foreach (var item in order.Items)
        {
            var balance = await _db.InventoryBalances.FirstOrDefaultAsync(b =>
                b.CompanyId == companyId && b.BusinessUnitId == businessUnitId &&
                b.WarehouseId == warehouse.Id && b.ProductVariantId == item.ProductVariantId, ct);
            if (balance == null || balance.ReservedQuantity < item.Quantity)
                return Result<SalesOrderSummaryDto>.Failure($"Không thể hủy {order.OrderNumber}: số lượng giữ kho của SKU {item.SkuSnapshot} không còn khớp. Hãy kiểm tra lịch sử giao hàng.");
            balance.ReservedQuantity -= item.Quantity;
            balance.LastUpdated = DateTime.UtcNow;
        }

        order.Status = SalesOrderStatus.Cancelled;
        if (order.BusinessDocumentId.HasValue)
        {
            var document = await _db.BusinessDocuments.FirstOrDefaultAsync(d => d.Id == order.BusinessDocumentId.Value, ct);
            if (document != null)
                document.Status = BusinessDocumentStatus.Voided;
        }
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cancelled pending sales order {OrderNumber}. Reason: {Reason}", order.OrderNumber, request.Reason);
        return Result<SalesOrderSummaryDto>.Success(ToSummaryDto(order));
    }

    public async Task<Result<SalesOrderFulfillmentDto>> DeliverAsync(Guid orderId, FulfillSalesOrderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Items == null || request.Items.Count == 0)
        {
            return Result<SalesOrderFulfillmentDto>.Failure("Phiếu giao hàng phải có ít nhất một SKU.");
        }
        if (request.Items.GroupBy(x => x.ProductVariantId).Any(x => x.Count() > 1))
        {
            return Result<SalesOrderFulfillmentDto>.Failure("Mỗi SKU chỉ được xuất hiện một lần trong phiếu giao hàng.");
        }

        var order = await _db.SalesOrders
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == orderId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order == null)
        {
            return Result<SalesOrderFulfillmentDto>.Failure("Không tìm thấy đơn hàng.");
        }
        if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Returned)
        {
            return Result<SalesOrderFulfillmentDto>.Failure("Đơn hàng đã hủy hoặc đã hoàn tất đổi trả, không thể giao thêm.");
        }

        var warehouse = await GetOrderWarehouseAsync(order, companyId, businessUnitId, ct);
        var warehouseId = warehouse.Id;

        foreach (var requestItem in request.Items)
        {
            if (requestItem.Quantity <= 0)
            {
                return Result<SalesOrderFulfillmentDto>.Failure("Số lượng giao phải lớn hơn 0.");
            }

            var orderItem = order.Items.FirstOrDefault(x => x.ProductVariantId == requestItem.ProductVariantId);
            if (orderItem == null)
            {
                return Result<SalesOrderFulfillmentDto>.Failure("SKU giao hàng không thuộc đơn hàng.");
            }

            var remaining = orderItem.Quantity - orderItem.DeliveredQuantity - orderItem.ReturnedQuantity;
            if (requestItem.Quantity > remaining)
            {
                return Result<SalesOrderFulfillmentDto>.Failure($"SKU {orderItem.SkuSnapshot} chỉ còn {remaining} sản phẩm chưa giao.");
            }

            var balance = await _db.InventoryBalances.FirstOrDefaultAsync(
                x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.WarehouseId == warehouseId && x.ProductVariantId == requestItem.ProductVariantId, ct);
            if (balance == null || balance.OnHandQuantity < requestItem.Quantity || balance.ReservedQuantity < requestItem.Quantity)
            {
                return Result<SalesOrderFulfillmentDto>.Failure($"Tồn kho giữ cho SKU {orderItem.SkuSnapshot} không đủ để giao.");
            }

            orderItem.DeliveredQuantity += requestItem.Quantity;
            balance.OnHandQuantity -= requestItem.Quantity;
            balance.ReservedQuantity -= requestItem.Quantity;
            balance.LastUpdated = DateTime.UtcNow;

            _db.InventoryMovements.Add(new InventoryMovement
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                WarehouseId = warehouseId,
                Warehouse = warehouse,
                ProductVariantId = requestItem.ProductVariantId,
                MovementType = InventoryMovementType.SalesOrderDelivery,
                QuantityDelta = -requestItem.Quantity,
                UnitCost = orderItem.UnitCostSnapshot,
                ReferenceType = "SalesOrder",
                ReferenceId = order.Id,
                MovementDate = DateTime.UtcNow,
                Notes = $"Giao hàng cho đơn {order.OrderNumber}"
            });
        }

        order.Status = order.Items.All(x => x.DeliveredQuantity >= x.Quantity - x.ReturnedQuantity)
            ? SalesOrderStatus.Completed
            : SalesOrderStatus.Processing;

        await _db.SaveChangesAsync(ct);
        return Result<SalesOrderFulfillmentDto>.Success(ToFulfillmentDto(order));
    }

}
