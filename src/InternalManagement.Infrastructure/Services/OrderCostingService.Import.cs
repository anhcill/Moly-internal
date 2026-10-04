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

}
