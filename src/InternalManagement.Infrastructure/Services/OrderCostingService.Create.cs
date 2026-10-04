using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Costing;
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
    public async Task<Result<SalesOrderCostDto>> CreateOrderAndSnapshotAsync(CreateSalesOrderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Items == null || request.Items.Count == 0)
        {
            return Result<SalesOrderCostDto>.Failure("Đơn hàng phải có ít nhất một SKU.");
        }
        if (string.IsNullOrWhiteSpace(request.CustomerName))
        {
            return Result<SalesOrderCostDto>.Failure("Tên khách hàng không được để trống.");
        }
        if (request.Items.GroupBy(x => x.ProductVariantId).Any(x => x.Count() > 1))
        {
            return Result<SalesOrderCostDto>.Failure("Mỗi SKU chỉ được xuất hiện một lần trong đơn hàng.");
        }
        if (request.Items.Any(x => x.Quantity <= 0 || x.UnitPrice < 0) || request.DiscountAmount < 0 || request.ShippingShopSubsidy < 0 || request.ShippingCustomerPaid < 0 || request.AdvertisingCost < 0 || request.PackagingCost < 0 || request.OtherSellingExpense < 0)
        {
            return Result<SalesOrderCostDto>.Failure("Số lượng, giá bán, voucher và phí vận chuyển không hợp lệ.");
        }

        var channel = (string.IsNullOrWhiteSpace(request.SourceSystem) ? "STORE" : request.SourceSystem).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(request.SourceOrderId) && await _db.SalesOrders.AnyAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.SourceSystem == channel && x.SourceOrderId == request.SourceOrderId, ct))
        {
            return Result<SalesOrderCostDto>.Failure("duplicate: đơn hàng từ nguồn này đã tồn tại; không tạo trùng đơn hàng.");
        }

        Party? customerParty = null;
        if (_partyResolver is not null && (!string.IsNullOrWhiteSpace(request.SourceOrderId) || !string.IsNullOrWhiteSpace(request.CustomerPhone)))
        {
            customerParty = await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                companyId,
                businessUnitId,
                PartyType.Individual,
                PartyRole.Customer,
                request.CustomerName,
                Phone: request.CustomerPhone,
                SourceSystem: channel,
                SourceId: request.SourceOrderId), ct);
        }

        var policy = await _db.ChannelFeePolicies
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.Channel == channel && x.IsActive && x.EffectiveFrom <= DateTime.UtcNow && (x.EffectiveTo == null || x.EffectiveTo >= DateTime.UtcNow))
            .OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (policy == null)
        {
            return Result<SalesOrderCostDto>.Failure($"Chưa có chính sách phí hiệu lực cho kênh {channel}.");
        }

        var variantIds = request.Items.Select(x => x.ProductVariantId).Distinct().ToList();
        var variants = await _db.ProductVariants.Include(x => x.Product)
            .Where(x => variantIds.Contains(x.Id) && x.Product.CompanyId == companyId && x.Product.BusinessUnitId == businessUnitId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, ct);
        if (variants.Count != variantIds.Count)
        {
            return Result<SalesOrderCostDto>.Failure("Đơn hàng có SKU không tồn tại trong phạm vi công ty.");
        }
        if (variants.Values.Any(x => x.CostPrice <= 0 || x.CostStatus != CostStatus.Actual))
        {
            return Result<SalesOrderCostDto>.Failure("Không thể chốt lợi nhuận: một SKU chưa có giá vốn thực tế từ lệnh sản xuất đã đóng.");
        }

        var warehouse = await GetWarehouseForNewOrderAsync(request.WarehouseId, companyId, businessUnitId, ct);
        if (warehouse == null)
        {
            return Result<SalesOrderCostDto>.Failure("Kho xuất hàng không hợp lệ hoặc đã ngừng hoạt động.");
        }
        var warehouseId = warehouse.Id;

        foreach (var item in request.Items)
        {
            var balance = await _db.InventoryBalances.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.WarehouseId == warehouseId && x.ProductVariantId == item.ProductVariantId, ct);
            if (balance == null || balance.OnHandQuantity - balance.ReservedQuantity < item.Quantity)
            {
                return Result<SalesOrderCostDto>.Failure($"Tồn kho khả dụng không đủ cho SKU {variants[item.ProductVariantId].Sku}.");
            }
        }

        var breakdown = OrderCostCalculator.Calculate(request, policy,
            variants.ToDictionary(item => item.Key, item => item.Value.CostPrice));
        var gross = breakdown.Gross;
        if (request.DiscountAmount > gross)
        {
            return Result<SalesOrderCostDto>.Failure("Voucher không được lớn hơn tổng giá bán.");
        }
        var netSales = breakdown.NetSales;
        var platformFee = breakdown.PlatformFee;
        var affiliateFee = breakdown.AffiliateFee;
        var paymentFee = breakdown.PaymentFee;
        var tax = breakdown.Tax;
        var cogs = breakdown.Cogs;
        var profit = breakdown.Profit;
        var margin = breakdown.Margin;

        var order = new SalesOrder
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            WarehouseId = warehouseId,
            Warehouse = warehouse,
            CustomerPartyId = customerParty?.Id,
            OrderNumber = $"SO-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(100, 999)}",
            SourceSystem = channel,
            SourceOrderId = request.SourceOrderId,
            CustomerName = request.CustomerName,
            CustomerPhone = request.CustomerPhone,
            ShippingAddress = request.ShippingAddress,
            TotalAmount = netSales + request.ShippingCustomerPaid,
            GrossAmount = gross,
            DiscountAmount = request.DiscountAmount,
            ShippingCustomerPaid = request.ShippingCustomerPaid,
            ShippingShopSubsidy = request.ShippingShopSubsidy,
            TaxAmount = tax,
            PlatformFee = platformFee,
            AffiliateFee = affiliateFee,
            PaymentFee = paymentFee,
            AdvertisingCost = request.AdvertisingCost,
            PackagingCost = request.PackagingCost,
            OtherSellingExpense = request.OtherSellingExpense,
            ActualCogs = cogs,
            NetRevenue = netSales,
            Profit = profit,
            CostStatus = CostStatus.Actual,
            Status = SalesOrderStatus.Pending,
            OrderDate = DateTime.UtcNow
        };

        var snapshot = new OrderCostSnapshot
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SalesOrderId = order.Id,
            Channel = channel,
            ChannelFeePolicyVersion = policy.VersionNumber,
            GrossAmount = gross,
            DiscountAmount = request.DiscountAmount,
            RefundAmount = 0,
            NetSalesAmount = netSales,
            PlatformFee = platformFee,
            AffiliateFee = affiliateFee,
            PaymentFee = paymentFee,
            AdvertisingCost = request.AdvertisingCost,
            PackagingCost = request.PackagingCost,
            OtherSellingExpense = request.OtherSellingExpense,
            ShippingSubsidy = request.ShippingShopSubsidy,
            TaxAmount = tax,
            ActualCogs = cogs,
            Profit = profit,
            Margin = margin,
            CostStatus = CostStatus.Actual,
            SnapshottedAt = DateTime.UtcNow
        };

        foreach (var item in request.Items)
        {
            var variant = variants[item.ProductVariantId];
            order.Items.Add(new SalesOrderItem
            {
                SalesOrderId = order.Id,
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                UnitCostSnapshot = variant.CostPrice,
                CostStatus = CostStatus.Actual,
                SkuSnapshot = variant.Sku,
                ProductNameSnapshot = variant.Product?.Name ?? string.Empty
            });
            snapshot.Items.Add(new OrderCostSnapshotItem
            {
                OrderCostSnapshotId = snapshot.Id,
                ProductVariantId = item.ProductVariantId,
                Quantity = item.Quantity,
                UnitSellingPrice = item.UnitPrice,
                UnitCost = variant.CostPrice,
                CostStatus = CostStatus.Actual,
                TotalCogs = item.Quantity * variant.CostPrice
            });
            var balance = await _db.InventoryBalances.FirstAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.WarehouseId == warehouseId && x.ProductVariantId == item.ProductVariantId, ct);
            balance.ReservedQuantity += item.Quantity;
        }

        order.CostSnapshots.Add(snapshot);
        if (_documentRegistry is not null)
        {
            var businessDocument = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId,
                BusinessDocumentType.FashionSalesOrder,
                nameof(SalesOrder),
                order.Id,
                order.OrderNumber,
                order.TotalAmount,
                order.OrderDate,
                customerParty?.Id,
                ExternalSourceSystem: channel,
                ExternalSourceId: request.SourceOrderId), ct);
            order.BusinessDocumentId = businessDocument.Id;
        }
        _db.SalesOrders.Add(order);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex) when (ex.Entries.Any(entry => entry.Entity is InventoryBalance))
        {
            _logger.LogWarning(ex, "Inventory reservation conflicted while creating an order in warehouse {WarehouseId}.", warehouseId);
            return Result<SalesOrderCostDto>.Failure("Tồn kho vừa thay đổi bởi đơn hàng khác; vui lòng tải lại và thử lại.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result<SalesOrderCostDto>.Failure("duplicate: đơn hàng đã được tạo bởi một yêu cầu khác.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Could not persist sales order {OrderNumber}.", order.OrderNumber);
            return Result<SalesOrderCostDto>.Failure("Không thể lưu đơn hàng; vui lòng thử lại hoặc liên hệ quản trị.");
        }
        _logger.LogInformation("Order {OrderNumber} created with actual COGS {Cogs} and profit {Profit}.", order.OrderNumber, cogs, profit);
        return Result<SalesOrderCostDto>.Success(ToDto(order, snapshot));
    }

}
