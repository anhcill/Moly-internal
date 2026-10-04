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
    public async Task<Result<ReturnDto>> CreateReturnAsync(CreateReturnRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Items == null || request.Items.Count == 0)
        {
            return Result<ReturnDto>.Failure("Phiếu đổi trả phải có ít nhất một SKU.");
        }
        if (request.Items.GroupBy(x => x.ProductVariantId).Any(x => x.Count() > 1))
        {
            return Result<ReturnDto>.Failure("Mỗi SKU chỉ được xuất hiện một lần trong phiếu đổi trả.");
        }

        var order = await _db.SalesOrders
            .Include(x => x.Items)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == request.SalesOrderId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order == null)
        {
            return Result<ReturnDto>.Failure("Không tìm thấy đơn hàng.");
        }

        var previousReturns = await _db.ReturnItems
            .Where(x => x.Return.SalesOrderId == order.Id && x.Return.Status != "Rejected")
            .GroupBy(x => x.ProductVariantId)
            .Select(x => new { ProductVariantId = x.Key, Quantity = x.Sum(y => y.Quantity) })
            .ToDictionaryAsync(x => x.ProductVariantId, x => x.Quantity, ct);

        var returnEntity = new Return
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SalesOrderId = order.Id,
            ReturnNumber = string.IsNullOrWhiteSpace(request.ReturnNumber)
                ? $"RT-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(100, 999)}"
                : request.ReturnNumber.Trim(),
            Reason = request.Reason,
            Status = "Inspecting",
            ReturnedAt = DateTime.UtcNow
        };

        foreach (var requestItem in request.Items)
        {
            if (requestItem.Quantity <= 0 || requestItem.RefundAmount is < 0)
            {
                return Result<ReturnDto>.Failure("Số lượng đổi trả phải lớn hơn 0 và tiền hoàn không được âm.");
            }

            var orderItem = order.Items.FirstOrDefault(x => x.ProductVariantId == requestItem.ProductVariantId);
            if (orderItem == null)
            {
                return Result<ReturnDto>.Failure("SKU đổi trả không thuộc đơn hàng.");
            }

            var alreadyReturned = previousReturns.GetValueOrDefault(requestItem.ProductVariantId);
            var remaining = orderItem.DeliveredQuantity - orderItem.ReturnedQuantity - alreadyReturned;
            if (requestItem.Quantity > remaining)
            {
                return Result<ReturnDto>.Failure($"SKU {orderItem.SkuSnapshot} chỉ còn {remaining} sản phẩm đủ điều kiện đổi trả.");
            }

            var refund = requestItem.RefundAmount ?? decimal.Round(requestItem.Quantity * orderItem.UnitPrice, 2);
            returnEntity.Items.Add(new ReturnItem
            {
                ReturnId = returnEntity.Id,
                ProductVariantId = requestItem.ProductVariantId,
                ProductVariant = orderItem.ProductVariant,
                Quantity = requestItem.Quantity,
                ConditionStatus = string.IsNullOrWhiteSpace(requestItem.ConditionStatus) ? "Good" : requestItem.ConditionStatus.Trim(),
                Restockable = requestItem.Restockable,
                UnitPriceSnapshot = orderItem.UnitPrice,
                UnitCostSnapshot = orderItem.UnitCostSnapshot,
                RefundAmount = refund
            });
        }

        returnEntity.TotalRefundAmount = returnEntity.Items.Sum(x => x.RefundAmount);
        await SyncReturnDocumentAsync(returnEntity, order, BusinessDocumentStatus.Draft, ct);
        _db.Returns.Add(returnEntity);
        await _db.SaveChangesAsync(ct);
        return Result<ReturnDto>.Success(ToReturnDto(returnEntity));
    }

    public async Task<Result<ReturnDto>> InspectReturnAsync(Guid returnId, InspectReturnRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var decision = request.Decision?.Trim().ToUpperInvariant();
        if (decision is not ("APPROVE" or "APPROVED" or "DUYỆT" or "REJECT" or "REJECTED" or "TỪ CHỐI"))
        {
            return Result<ReturnDto>.Failure("Kết quả kiểm tra phải là Duyệt hoặc Từ chối.");
        }

        var returnEntity = await _db.Returns
            .Include(x => x.Items)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .Include(x => x.SalesOrder)
                .ThenInclude(x => x.Items)
            .FirstOrDefaultAsync(x => x.Id == returnId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (returnEntity == null)
        {
            return Result<ReturnDto>.Failure("Không tìm thấy phiếu đổi trả.");
        }
        if (!returnEntity.Status.Equals("Inspecting", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ReturnDto>.Failure("Phiếu đổi trả đã được xử lý trước đó.");
        }

        var approve = decision is "APPROVE" or "APPROVED" or "DUYỆT";
        if (!approve)
        {
            returnEntity.Status = "Rejected";
            await SyncReturnDocumentAsync(returnEntity, returnEntity.SalesOrder, BusinessDocumentStatus.Voided, ct);
            await _db.SaveChangesAsync(ct);
            return Result<ReturnDto>.Success(ToReturnDto(returnEntity));
        }

        var warehouse = await GetOrderWarehouseAsync(returnEntity.SalesOrder, companyId, businessUnitId, ct);
        var warehouseId = warehouse.Id;

        var restockedCost = 0m;
        foreach (var returnItem in returnEntity.Items)
        {
            var orderItem = returnEntity.SalesOrder.Items.FirstOrDefault(x => x.ProductVariantId == returnItem.ProductVariantId);
            if (orderItem == null)
            {
                return Result<ReturnDto>.Failure("Dữ liệu SKU trong phiếu đổi trả không còn khớp với đơn hàng.");
            }
            if (orderItem.ReturnedQuantity + returnItem.Quantity > orderItem.DeliveredQuantity)
            {
                return Result<ReturnDto>.Failure("Số lượng đổi trả vượt quá số lượng đã giao.");
            }

            orderItem.ReturnedQuantity += returnItem.Quantity;
            if (!returnItem.Restockable)
            {
                continue;
            }

            var balance = await _db.InventoryBalances.FirstOrDefaultAsync(
                x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.WarehouseId == warehouseId && x.ProductVariantId == returnItem.ProductVariantId, ct);
            if (balance == null)
            {
                balance = new InventoryBalance
                {
                    CompanyId = companyId,
                    BusinessUnitId = businessUnitId,
                    WarehouseId = warehouseId,
                    Warehouse = warehouse,
                    ProductVariantId = returnItem.ProductVariantId,
                    OnHandQuantity = 0,
                    ReservedQuantity = 0,
                    LastUpdated = DateTime.UtcNow
                };
                _db.InventoryBalances.Add(balance);
            }

            balance.OnHandQuantity += returnItem.Quantity;
            balance.LastUpdated = DateTime.UtcNow;
            restockedCost += returnItem.Quantity * returnItem.UnitCostSnapshot;
            _db.InventoryMovements.Add(new InventoryMovement
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                WarehouseId = warehouseId,
                Warehouse = warehouse,
                ProductVariantId = returnItem.ProductVariantId,
                MovementType = InventoryMovementType.CustomerReturn,
                QuantityDelta = returnItem.Quantity,
                UnitCost = returnItem.UnitCostSnapshot,
                ReferenceType = "Return",
                ReferenceId = returnEntity.Id,
                MovementDate = DateTime.UtcNow,
                Notes = $"Nhập lại hàng từ phiếu {returnEntity.ReturnNumber}"
            });
        }

        returnEntity.Status = "Approved";
        await SyncReturnDocumentAsync(returnEntity, returnEntity.SalesOrder, BusinessDocumentStatus.Open, ct);
        var order = returnEntity.SalesOrder;
        order.Status = order.Items.All(x => x.ReturnedQuantity >= x.Quantity)
            ? SalesOrderStatus.Returned
            : SalesOrderStatus.Completed;

        var snapshot = await _db.OrderCostSnapshots
            .Where(x => x.SalesOrderId == order.Id && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId)
            .OrderByDescending(x => x.SnapshottedAt)
            .FirstOrDefaultAsync(ct);
        if (snapshot != null)
        {
            snapshot.RefundAmount += returnEntity.TotalRefundAmount;
            snapshot.NetSalesAmount = Math.Max(0, snapshot.NetSalesAmount - returnEntity.TotalRefundAmount);
            snapshot.ActualCogs = Math.Max(0, snapshot.ActualCogs - restockedCost);
            snapshot.Profit = snapshot.NetSalesAmount
                - snapshot.ActualCogs
                - snapshot.PlatformFee
                - snapshot.AffiliateFee
                - snapshot.PaymentFee
                - snapshot.AdvertisingCost
                - snapshot.PackagingCost
                - snapshot.OtherSellingExpense
                - snapshot.ShippingSubsidy
                - snapshot.TaxAmount;
            snapshot.Margin = snapshot.NetSalesAmount == 0 ? 0 : snapshot.Profit / snapshot.NetSalesAmount;
            order.NetRevenue = snapshot.NetSalesAmount;
            order.ActualCogs = snapshot.ActualCogs;
            order.Profit = snapshot.Profit;
        }

        await _db.SaveChangesAsync(ct);
        return Result<ReturnDto>.Success(ToReturnDto(returnEntity));
    }

}
