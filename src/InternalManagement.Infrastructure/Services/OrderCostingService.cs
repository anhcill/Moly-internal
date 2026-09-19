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

public sealed class OrderCostingService : IOrderCostingService
{
    private static readonly SemaphoreSlim OrderCreationGate = new(1, 1);
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<OrderCostingService> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public OrderCostingService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<OrderCostingService> logger,
        IPartyResolver? partyResolver = null,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _partyResolver = partyResolver;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
    }

    public async Task<Result<SalesOrderCostDto>> CreateOrderAndSnapshotAsync(CreateSalesOrderRequest request, CancellationToken ct)
    {
        await OrderCreationGate.WaitAsync(ct);
        try
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

        var gross = request.Items.Sum(x => x.Quantity * x.UnitPrice);
        if (request.DiscountAmount > gross)
        {
            return Result<SalesOrderCostDto>.Failure("Voucher không được lớn hơn tổng giá bán.");
        }
        var netSales = gross - request.DiscountAmount;
        var platformFee = netSales * policy.PlatformFeeRate;
        var affiliateFee = netSales * policy.AffiliateFeeRate;
        var paymentFee = netSales * policy.PaymentFeeRate + policy.FixedPaymentFee;
        var tax = netSales * policy.TaxRate;
        var cogs = request.Items.Sum(x => x.Quantity * variants[x.ProductVariantId].CostPrice);
        var profit = netSales - cogs - platformFee - affiliateFee - paymentFee - request.ShippingShopSubsidy - tax - request.AdvertisingCost - request.PackagingCost - request.OtherSellingExpense;
        var margin = netSales == 0 ? 0 : profit / netSales;

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
        catch (DbUpdateException)
        {
            return Result<SalesOrderCostDto>.Failure("duplicate: đơn hàng đã được tạo bởi một yêu cầu khác.");
        }
        _logger.LogInformation("Order {OrderNumber} created with actual COGS {Cogs} and profit {Profit}.", order.OrderNumber, cogs, profit);
        return Result<SalesOrderCostDto>.Success(ToDto(order, snapshot));
        }
        finally
        {
            OrderCreationGate.Release();
        }
    }

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

    public async Task<IReadOnlyList<SalesSettlementDto>> GetSettlementsAsync(Guid orderId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        return await _db.SalesSettlements
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.SalesOrderId == orderId)
            .OrderByDescending(x => x.OccurredAt)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => ToSettlementDto(x))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SalesDocumentDto>> GetDocumentsAsync(Guid orderId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var documents = await _db.SalesDocuments
            .AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.SalesOrderId == orderId)
            .OrderByDescending(x => x.IssuedAt)
            .ToListAsync(ct);
        return documents.Select(ToDocumentDto).ToList();
    }

    public async Task<Result<SalesSettlementDto>> RecordSettlementAsync(
        Guid orderId,
        RecordSalesSettlementRequest request,
        CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Amount <= 0)
        {
            return Result<SalesSettlementDto>.Failure("Số tiền thu/hoàn phải lớn hơn 0.");
        }
        if (string.IsNullOrWhiteSpace(request.PaymentReference))
        {
            return Result<SalesSettlementDto>.Failure("Mã tham chiếu thanh toán là bắt buộc để chống tạo trùng.");
        }
        if (!Enum.IsDefined(request.Kind) || !Enum.IsDefined(request.Status))
        {
            return Result<SalesSettlementDto>.Failure("Loại hoặc trạng thái thanh toán không hợp lệ.");
        }
        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return Result<SalesSettlementDto>.Failure("Loại tiền tệ là bắt buộc.");
        }
        var occurredAt = NormalizeUtc(request.OccurredAt);

        var order = await _db.SalesOrders.FirstOrDefaultAsync(x =>
            x.Id == orderId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order is null)
        {
            return Result<SalesSettlementDto>.Failure("Không tìm thấy đơn hàng.");
        }
        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return Result<SalesSettlementDto>.Failure("Không thể ghi nhận thu/hoàn tiền cho đơn đã hủy.");
        }

        SalesDocument? salesDocument = null;
        if (request.SalesDocumentId.HasValue)
        {
            salesDocument = await _db.SalesDocuments.FirstOrDefaultAsync(x =>
                x.Id == request.SalesDocumentId.Value && x.CompanyId == companyId && x.SalesOrderId == order.Id, ct);
            if (salesDocument is null)
            {
                return Result<SalesSettlementDto>.Failure("Chứng từ bán hàng không thuộc đơn hàng này.");
            }
        }

        Return? returnEntity = null;
        if (request.Kind == SalesSettlementKind.CustomerRefund)
        {
            if (!request.ReturnId.HasValue)
            {
                return Result<SalesSettlementDto>.Failure("Hoàn tiền phải tham chiếu một phiếu đổi trả đã được duyệt.");
            }
            returnEntity = await _db.Returns.FirstOrDefaultAsync(x =>
                x.Id == request.ReturnId.Value && x.CompanyId == companyId && x.SalesOrderId == order.Id, ct);
            if (returnEntity is null || !string.Equals(returnEntity.Status, "Approved", StringComparison.OrdinalIgnoreCase))
            {
                return Result<SalesSettlementDto>.Failure("Chỉ được hoàn tiền cho phiếu đổi trả đã được duyệt.");
            }
        }
        else if (request.ReturnId.HasValue)
        {
            return Result<SalesSettlementDto>.Failure("Khoản thu khách hàng không được tham chiếu phiếu đổi trả.");
        }

        var paymentReference = request.PaymentReference.Trim();
        var settlement = await _db.SalesSettlements.FirstOrDefaultAsync(x =>
            x.CompanyId == companyId && x.PaymentReference == paymentReference, ct);
        if (settlement is not null && settlement.SalesOrderId != order.Id)
        {
            return Result<SalesSettlementDto>.Failure("Mã tham chiếu thanh toán đã thuộc một đơn hàng khác.");
        }
        if (settlement is not null && settlement.Status == SalesSettlementStatus.Confirmed)
        {
            var identical = settlement.Kind == request.Kind
                            && settlement.Status == request.Status
                            && settlement.Amount == request.Amount
                            && settlement.Currency == request.Currency.Trim().ToUpperInvariant()
                            && settlement.PaymentMethod == NormalizeOptional(request.PaymentMethod)
                            && settlement.SalesDocumentId == request.SalesDocumentId
                            && settlement.ReturnId == request.ReturnId
                            && settlement.OccurredAt == occurredAt;
            return identical
                ? Result<SalesSettlementDto>.Success(ToSettlementDto(settlement))
                : Result<SalesSettlementDto>.Failure("Giao dịch đã xác nhận không được sửa. Hãy lập giao dịch điều chỉnh hoặc hoàn tiền riêng.");
        }

        var excludedId = settlement?.Id;
        if (request.Kind == SalesSettlementKind.CustomerPayment)
        {
            var recorded = await _db.SalesSettlements
                .Where(x => x.CompanyId == companyId && x.SalesOrderId == order.Id
                    && x.Kind == SalesSettlementKind.CustomerPayment
                    && (x.Status == SalesSettlementStatus.Pending || x.Status == SalesSettlementStatus.Confirmed)
                    && (!excludedId.HasValue || x.Id != excludedId.Value))
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            if (recorded + request.Amount > order.TotalAmount)
            {
                return Result<SalesSettlementDto>.Failure("Tổng tiền thu vượt quá giá trị đơn hàng.");
            }
        }
        else
        {
            var recorded = await _db.SalesSettlements
                .Where(x => x.CompanyId == companyId && x.ReturnId == returnEntity!.Id
                    && x.Kind == SalesSettlementKind.CustomerRefund
                    && (x.Status == SalesSettlementStatus.Pending || x.Status == SalesSettlementStatus.Confirmed)
                    && (!excludedId.HasValue || x.Id != excludedId.Value))
                .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
            if (recorded + request.Amount > returnEntity!.TotalRefundAmount)
            {
                return Result<SalesSettlementDto>.Failure("Tổng tiền hoàn vượt quá số tiền đã duyệt trên phiếu đổi trả.");
            }
        }

        if (settlement is null)
        {
            settlement = new SalesSettlement
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                SalesOrderId = order.Id,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username ?? "system"
            };
            _db.SalesSettlements.Add(settlement);
        }

        settlement.SalesDocumentId = salesDocument?.Id;
        settlement.ReturnId = returnEntity?.Id;
        settlement.Kind = request.Kind;
        settlement.Status = request.Status;
        settlement.PaymentReference = paymentReference;
        settlement.Amount = request.Amount;
        settlement.Currency = request.Currency.Trim().ToUpperInvariant();
        settlement.PaymentMethod = NormalizeOptional(request.PaymentMethod);
        settlement.OccurredAt = occurredAt;
        settlement.UpdatedAt = DateTime.UtcNow;
        settlement.UpdatedBy = _currentUser.Username ?? "system";

        if (_documentRegistry is not null)
        {
            var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId,
                BusinessDocumentType.FashionSettlement,
                nameof(SalesSettlement),
                settlement.Id,
                $"SET-{paymentReference}",
                settlement.Amount,
                settlement.OccurredAt,
                order.CustomerPartyId,
                settlement.Currency,
                ToDocumentStatus(settlement.Status),
                order.SourceSystem,
                paymentReference), ct);
            settlement.BusinessDocumentId = document.Id;
        }

        if (settlement.Status == SalesSettlementStatus.Confirmed && _financePostingService is not null)
        {
            var sourceBusinessDocumentId = settlement.Kind == SalesSettlementKind.CustomerRefund
                ? returnEntity!.BusinessDocumentId
                : salesDocument?.BusinessDocumentId ?? order.BusinessDocumentId;
            var transactionType = settlement.Kind == SalesSettlementKind.CustomerPayment
                ? TransactionType.Income
                : TransactionType.Expense;
            var description = transactionType == TransactionType.Income
                ? $"Thu đơn Fashion {order.OrderNumber} ({settlement.PaymentReference})"
                : $"Hoàn tiền đơn Fashion {order.OrderNumber} ({settlement.PaymentReference})";
            await _financePostingService.PostAsync(new FinancePostingRequest(
                companyId,
                businessUnitId,
                transactionType,
                settlement.Amount,
                settlement.OccurredAt,
                nameof(SalesSettlement),
                settlement.Id,
                description,
                sourceBusinessDocumentId), ct);
        }

        await _db.SaveChangesAsync(ct);
        return Result<SalesSettlementDto>.Success(ToSettlementDto(settlement));
    }

    public async Task<Result<SalesDocumentDto>> IssueDocumentAsync(Guid orderId, IssueSalesDocumentRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.DocumentType is not (SalesDocumentType.Invoice or SalesDocumentType.RetailReceipt))
        {
            return Result<SalesDocumentDto>.Failure("Loại chứng từ bán hàng không hợp lệ.");
        }

        var order = await _db.SalesOrders
            .Include(x => x.Items)
                .ThenInclude(x => x.ProductVariant)
                    .ThenInclude(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == orderId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order == null)
        {
            return Result<SalesDocumentDto>.Failure("Không tìm thấy đơn hàng.");
        }
        if (order.Status == SalesOrderStatus.Cancelled)
        {
            return Result<SalesDocumentDto>.Failure("Không thể phát hành chứng từ cho đơn hàng đã hủy.");
        }
        if (await _db.SalesDocuments.AnyAsync(x => x.CompanyId == companyId && x.SalesOrderId == order.Id && x.DocumentType == request.DocumentType, ct))
        {
            return Result<SalesDocumentDto>.Failure("Đơn hàng đã có chứng từ cùng loại.");
        }

        var prefix = request.DocumentType == SalesDocumentType.Invoice ? "INV" : "POS";
        var document = new SalesDocument
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SalesOrderId = order.Id,
            CustomerPartyId = order.CustomerPartyId,
            DocumentType = request.DocumentType,
            DocumentNumber = $"{prefix}-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Random.Shared.Next(100, 999)}",
            CustomerName = order.CustomerName,
            CustomerPhone = order.CustomerPhone,
            ShippingAddress = order.ShippingAddress,
            GrossAmount = order.GrossAmount,
            DiscountAmount = order.DiscountAmount,
            TaxAmount = order.TaxAmount,
            TotalAmount = order.TotalAmount,
            IssuedAt = DateTime.UtcNow
        };

        var gross = order.Items.Sum(x => x.TotalPrice);
        var remainingTax = order.TaxAmount;
        for (var index = 0; index < order.Items.Count; index++)
        {
            var orderItem = order.Items.ElementAt(index);
            var lineTotal = orderItem.TotalPrice;
            var lineTax = index == order.Items.Count - 1 || gross <= 0
                ? remainingTax
                : decimal.Round(order.TaxAmount * lineTotal / gross, 2);
            remainingTax -= lineTax;
            var variant = orderItem.ProductVariant;
            document.Items.Add(new SalesDocumentItem
            {
                SalesDocumentId = document.Id,
                ProductVariantId = orderItem.ProductVariantId,
                SkuSnapshot = string.IsNullOrWhiteSpace(orderItem.SkuSnapshot) ? variant.Sku : orderItem.SkuSnapshot,
                ProductNameSnapshot = string.IsNullOrWhiteSpace(orderItem.ProductNameSnapshot) ? variant.Product.Name : orderItem.ProductNameSnapshot,
                ColorSnapshot = variant.Color,
                SizeSnapshot = variant.Size,
                Quantity = orderItem.Quantity,
                UnitPrice = orderItem.UnitPrice,
                TaxAmount = lineTax,
                LineTotal = lineTotal
            });
        }

        if (_documentRegistry is not null)
        {
            var businessDocument = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId,
                BusinessDocumentType.FashionSalesDocument,
                nameof(SalesDocument),
                document.Id,
                document.DocumentNumber,
                document.TotalAmount,
                document.IssuedAt,
                document.CustomerPartyId,
                ExternalSourceSystem: order.SourceSystem,
                ExternalSourceId: order.SourceOrderId), ct);
            document.BusinessDocumentId = businessDocument.Id;
        }
        _db.SalesDocuments.Add(document);
        await _db.SaveChangesAsync(ct);
        return Result<SalesDocumentDto>.Success(ToDocumentDto(document));
    }

    public async Task<Result<ImportSalesOrderResult>> ImportAsync(ImportSalesOrderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (string.IsNullOrWhiteSpace(request.SourceSystem))
        {
            return Result<ImportSalesOrderResult>.Failure("Nguồn đơn hàng là bắt buộc.");
        }
        if (request.Payload.ValueKind != JsonValueKind.Object)
        {
            return Result<ImportSalesOrderResult>.Failure("Dữ liệu đơn hàng nhập vào phải là một đối tượng JSON.");
        }

        var sourceSystem = request.SourceSystem.Trim().ToUpperInvariant();
        var sourceOrderId = ReadString(request.Payload, "sourceOrderId", "source_order_id", "orderId", "order_id", "id", "orderNumber", "order_number");
        if (string.IsNullOrWhiteSpace(sourceOrderId))
        {
            return Result<ImportSalesOrderResult>.Failure("Dữ liệu đơn hàng phải có mã đơn từ nguồn.");
        }

        if (!TryReadArray(request.Payload, out var rawItems, "items", "orderItems", "order_items", "lineItems", "line_items") || rawItems.Count == 0)
        {
            return Result<ImportSalesOrderResult>.Failure("Dữ liệu đơn hàng phải có danh sách sản phẩm.");
        }

        var requestedItems = new List<(Guid? VariantId, string? Sku, int Quantity, decimal UnitPrice)>();
        foreach (var rawItem in rawItems)
        {
            if (rawItem.ValueKind != JsonValueKind.Object)
            {
                return Result<ImportSalesOrderResult>.Failure("Một dòng sản phẩm trong đơn hàng không hợp lệ.");
            }

            var variantId = ReadGuid(rawItem, "productVariantId", "product_variant_id", "variantId", "variant_id");
            var sku = ReadString(rawItem, "sku", "SKU", "productSku", "product_sku");
            var quantity = ReadInt(rawItem, "quantity", "qty", "amount");
            var unitPrice = ReadDecimal(rawItem, "unitPrice", "unit_price", "price", "sellingPrice", "selling_price");
            if (!variantId.HasValue && string.IsNullOrWhiteSpace(sku))
            {
                return Result<ImportSalesOrderResult>.Failure("Mỗi dòng sản phẩm phải có productVariantId hoặc SKU.");
            }
            if (quantity <= 0 || unitPrice < 0)
            {
                return Result<ImportSalesOrderResult>.Failure("Số lượng phải lớn hơn 0 và giá bán không được âm.");
            }
            requestedItems.Add((variantId, sku, quantity, unitPrice));
        }

        var variantIds = requestedItems.Where(x => x.VariantId.HasValue).Select(x => x.VariantId!.Value).ToList();
        var skus = requestedItems.Where(x => !string.IsNullOrWhiteSpace(x.Sku)).Select(x => x.Sku!).ToList();
        var variants = await _db.ProductVariants.Include(x => x.Product)
            .Where(x => x.Product.CompanyId == companyId && x.Product.BusinessUnitId == businessUnitId && x.IsActive && (variantIds.Contains(x.Id) || skus.Contains(x.Sku)))
            .ToListAsync(ct);

        var items = new List<CreateSalesOrderItemRequest>();
        foreach (var item in requestedItems)
        {
            var variant = item.VariantId.HasValue
                ? variants.FirstOrDefault(x => x.Id == item.VariantId.Value)
                : variants.FirstOrDefault(x => x.Sku == item.Sku);
            if (variant == null)
            {
                return Result<ImportSalesOrderResult>.Failure($"Không tìm thấy SKU/biến thể {item.Sku ?? item.VariantId?.ToString()}.");
            }
            items.Add(new CreateSalesOrderItemRequest(variant.Id, item.Quantity, item.UnitPrice));
        }

        var createRequest = new CreateSalesOrderRequest(
            sourceSystem,
            sourceOrderId,
            ReadString(request.Payload, "customerName", "customer_name", "buyerName", "buyer_name") ?? "Khách lẻ",
            ReadString(request.Payload, "customerPhone", "customer_phone", "phone"),
            ReadString(request.Payload, "shippingAddress", "shipping_address", "address"),
            ReadDecimal(request.Payload, "discountAmount", "discount_amount", "voucher") ,
            ReadDecimal(request.Payload, "shippingCustomerPaid", "shipping_customer_paid", "customerShippingFee"),
            ReadDecimal(request.Payload, "shippingShopSubsidy", "shipping_shop_subsidy", "shopShippingSubsidy"),
            items,
            ReadDecimal(request.Payload, "advertisingCost", "advertising_cost", "ads", "adSpend"),
            ReadDecimal(request.Payload, "packagingCost", "packaging_cost", "packaging"),
            ReadDecimal(request.Payload, "otherSellingExpense", "other_selling_expense", "otherFee"));

        var created = await CreateOrderAndSnapshotAsync(createRequest, ct);
        if (!created.Succeeded)
        {
            return Result<ImportSalesOrderResult>.Failure(created.Errors.ToArray());
        }

        return Result<ImportSalesOrderResult>.Success(new ImportSalesOrderResult(
            true,
            created.Value!.OrderId,
            created.Value.OrderNumber,
            $"Đã nhập đơn {sourceOrderId} từ kênh {sourceSystem} và chốt snapshot lợi nhuận."));
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

    private async Task<Warehouse?> GetWarehouseForNewOrderAsync(Guid? warehouseId, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        if (!warehouseId.HasValue)
            return await GetOrCreateWarehouseAsync(companyId, businessUnitId, ct);

        return await _db.Warehouses.FirstOrDefaultAsync(x =>
            x.Id == warehouseId.Value && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.IsActive, ct);
    }

    private async Task<Warehouse> GetOrderWarehouseAsync(SalesOrder order, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        if (order.WarehouseId.HasValue)
        {
            var assignedWarehouse = await _db.Warehouses.FirstOrDefaultAsync(x =>
                x.Id == order.WarehouseId.Value && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
            if (assignedWarehouse != null)
                return assignedWarehouse;
        }

        // Đơn cũ chưa gắn kho: dùng kho mặc định hiện hành một lần và lưu lại để các thao tác sau không bị lệch kho.
        var fallbackWarehouse = await GetOrCreateWarehouseAsync(companyId, businessUnitId, ct);
        order.WarehouseId = fallbackWarehouse.Id;
        return fallbackWarehouse;
    }

    private async Task<Warehouse> GetOrCreateWarehouseAsync(Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        var query = _db.Warehouses.Where(x => x.CompanyId == companyId && x.IsActive && x.BusinessUnitId == businessUnitId);
        var warehouse = await query.OrderByDescending(x => x.IsDefault).ThenBy(x => x.Code).FirstOrDefaultAsync(ct);
        if (warehouse != null)
            return warehouse;

        warehouse = new Warehouse
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = businessUnitId.HasValue ? "FASHION-MAIN" : "MAIN",
            Name = businessUnitId.HasValue ? "Kho Thời trang chính" : "Kho chính",
            IsActive = true,
            IsDefault = true
        };
        _db.Warehouses.Add(warehouse);
        return warehouse;
    }

    private async Task SyncReturnDocumentAsync(
        Return returnEntity,
        SalesOrder order,
        BusinessDocumentStatus status,
        CancellationToken ct)
    {
        if (_documentRegistry is null)
        {
            return;
        }

        var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
            returnEntity.CompanyId,
            returnEntity.BusinessUnitId,
            BusinessDocumentType.FashionReturn,
            nameof(Return),
            returnEntity.Id,
            returnEntity.ReturnNumber,
            returnEntity.TotalRefundAmount,
            returnEntity.UpdatedAt ?? returnEntity.ReturnedAt,
            order.CustomerPartyId,
            Status: status,
            ExternalSourceSystem: order.SourceSystem,
            ExternalSourceId: order.SourceOrderId), ct);
        returnEntity.BusinessDocumentId = document.Id;
    }

    private static BusinessDocumentStatus ToDocumentStatus(SalesSettlementStatus status) => status switch
    {
        SalesSettlementStatus.Confirmed => BusinessDocumentStatus.Settled,
        SalesSettlementStatus.Failed or SalesSettlementStatus.Voided => BusinessDocumentStatus.Voided,
        _ => BusinessDocumentStatus.Open
    };

    private static SalesSettlementDto ToSettlementDto(SalesSettlement settlement) => new(
        settlement.Id,
        settlement.SalesOrderId,
        settlement.SalesDocumentId,
        settlement.ReturnId,
        settlement.Kind,
        settlement.Status,
        settlement.PaymentReference,
        settlement.Amount,
        settlement.Currency,
        settlement.PaymentMethod,
        settlement.OccurredAt,
        settlement.BusinessDocumentId);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static SalesOrderCostDto ToDto(SalesOrder order, OrderCostSnapshot snapshot)
        => new(order.Id, order.OrderNumber, snapshot.Channel, snapshot.GrossAmount, snapshot.DiscountAmount, snapshot.NetSalesAmount, snapshot.PlatformFee, snapshot.AffiliateFee, snapshot.PaymentFee, snapshot.ShippingSubsidy, snapshot.TaxAmount, snapshot.ActualCogs, snapshot.Profit, snapshot.Margin, snapshot.CostStatus, snapshot.SnapshottedAt, snapshot.RefundAmount, snapshot.AdvertisingCost, snapshot.PackagingCost, snapshot.OtherSellingExpense);

    private static SalesOrderSummaryDto ToSummaryDto(SalesOrder order) => new(
        order.Id, order.OrderNumber, order.SourceSystem, order.SourceOrderId, order.CustomerName,
        order.CustomerPhone, order.ShippingAddress, order.Status, order.TotalAmount, order.NetRevenue,
        order.Profit, order.Items.Count, order.OrderDate, order.WarehouseId, order.Warehouse?.Name);

    private static SalesOrderFulfillmentDto ToFulfillmentDto(SalesOrder order)
        => new(order.Id, order.OrderNumber, order.Status, order.Items.Select(x => new SalesOrderFulfillmentItemDto(
            x.ProductVariantId,
            x.SkuSnapshot,
            x.Quantity,
            x.DeliveredQuantity,
            Math.Max(0, x.Quantity - x.DeliveredQuantity - x.ReturnedQuantity))).ToList());

    private static ReturnDto ToReturnDto(Return returnEntity)
        => new(returnEntity.Id, returnEntity.SalesOrderId, returnEntity.ReturnNumber, returnEntity.Status,
            returnEntity.TotalRefundAmount, returnEntity.ReturnedAt,
            returnEntity.Items.Select(x => new ReturnItemDto(
                x.ProductVariantId,
                x.ProductVariant?.Sku ?? string.Empty,
                x.Quantity,
                x.ConditionStatus,
                x.Restockable,
                x.RefundAmount)).ToList());

    private static SalesDocumentDto ToDocumentDto(SalesDocument document)
        => new(document.Id, document.SalesOrderId, document.DocumentType, document.DocumentNumber,
            document.CustomerName, document.GrossAmount, document.DiscountAmount, document.TaxAmount, document.TotalAmount,
            document.IssuedAt, document.Items.Select(x => new SalesDocumentItemDto(
                x.ProductVariantId,
                x.SkuSnapshot,
                x.ProductNameSnapshot,
                x.ColorSnapshot,
                x.SizeSnapshot,
                x.Quantity,
                x.UnitPrice,
                x.TaxAmount,
                x.LineTotal)).ToList());

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                var match = element.EnumerateObject().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match.Equals(default(JsonProperty))) continue;
                property = match.Value;
            }
            if (property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
            if (property.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
            {
                return property.ToString();
            }
        }
        return null;
    }

    private static Guid? ReadGuid(JsonElement element, params string[] names)
        => Guid.TryParse(ReadString(element, names), out var value) ? value : null;

    private static int ReadInt(JsonElement element, params string[] names)
    {
        var text = ReadString(element, names);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static decimal ReadDecimal(JsonElement element, params string[] names)
    {
        var text = ReadString(element, names);
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
    }

    private static bool TryReadArray(JsonElement element, out IReadOnlyList<JsonElement> values, params string[] names)
    {
        foreach (var name in names)
        {
            var property = element.EnumerateObject().FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (!property.Equals(default(JsonProperty)) && property.Value.ValueKind == JsonValueKind.Array)
            {
                values = property.Value.EnumerateArray().Select(x => x.Clone()).ToList();
                return true;
            }
        }
        values = Array.Empty<JsonElement>();
        return false;
    }
}
