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
