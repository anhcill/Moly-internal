using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── Manufacturing / Pricing / Orders ──

    public async Task<PaginatedData<MaterialItem>?> GetManufacturingMaterialsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/materials?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<MaterialItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateManufacturingMaterialAsync(string code, string name, string? category, string unit, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Category = category, Unit = unit };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/manufacturing/materials", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<MaterialLotItem>?> GetManufacturingMaterialLotsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/material-lots?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<MaterialLotItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<BomItemModel>?> GetManufacturingBomsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/boms?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<BomItemModel>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<ProductionOrderItem>?> GetProductionOrdersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/manufacturing/production-orders?pageSize=100";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<ProductionOrderItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PricingSimulationItem?> SimulatePricingAsync(Guid variantId, string channel, decimal listPrice, decimal discountAmount, decimal targetMargin, CancellationToken ct = default)
    {
        var req = new { ProductVariantId = variantId, Channel = channel, ListPrice = listPrice, DiscountAmount = discountAmount, ShippingSubsidy = (decimal?)null, TargetMargin = (decimal?)targetMargin, AffiliateFeeRateOverride = (decimal?)null };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/pricing/simulate", req, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PricingSimulationItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<SalesOrderCostItem?> CreateSalesOrderAsync(
        string sourceSystem,
        string? sourceOrderId,
        string customerName,
        decimal discountAmount,
        decimal shippingCustomerPaid,
        decimal shippingShopSubsidy,
        IReadOnlyList<CreateSalesOrderItemReq> items,
        string? customerPhone = null,
        string? shippingAddress = null,
        decimal advertisingCost = 0,
        decimal packagingCost = 0,
        decimal otherSellingExpense = 0,
        Guid? warehouseId = null,
        CancellationToken ct = default)
    {
        var req = new { SourceSystem = sourceSystem, SourceOrderId = sourceOrderId, CustomerName = customerName, CustomerPhone = customerPhone, ShippingAddress = shippingAddress, DiscountAmount = discountAmount, ShippingCustomerPaid = shippingCustomerPaid, ShippingShopSubsidy = shippingShopSubsidy, Items = items, AdvertisingCost = advertisingCost, PackagingCost = packagingCost, OtherSellingExpense = otherSellingExpense, WarehouseId = warehouseId };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/orders", req, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SalesOrderCostItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<SalesOrderSummaryItem>?> GetSalesOrdersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/orders?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<SalesOrderSummaryItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<SalesOrderFulfillmentItem?> GetSalesOrderFulfillmentAsync(Guid orderId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/orders/{orderId}/fulfillment", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SalesOrderFulfillmentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> UpdateSalesOrderHeaderAsync(Guid orderId, string customerName, string? customerPhone, string? shippingAddress, string? sourceOrderId, CancellationToken ct = default)
    {
        var req = new { CustomerName = customerName, CustomerPhone = customerPhone, ShippingAddress = shippingAddress, SourceOrderId = sourceOrderId };
        using var response = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/orders/{orderId}", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> CancelSalesOrderAsync(Guid orderId, string? reason, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/cancel", new { Reason = reason }, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> DeliverSalesOrderAsync(Guid orderId, IReadOnlyList<FulfillSalesOrderItemReq> items, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/deliver", new { Items = items }, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

    public async Task<SalesDocumentItem?> IssueSalesDocumentAsync(Guid orderId, int documentType, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/documents", new { DocumentType = documentType }, _jsonOptions, ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SalesDocumentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<SalesSettlementItem>?> GetSalesOrderSettlementsAsync(Guid orderId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/orders/{orderId}/settlements", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<SalesSettlementItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<SalesDocumentItem>?> GetSalesOrderDocumentsAsync(Guid orderId, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/orders/{orderId}/documents", ct), ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<SalesDocumentItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> RecordCustomerPaymentAsync(Guid orderId, decimal amount, string paymentReference, string? paymentMethod, CancellationToken ct = default)
    {
        var req = new { Kind = 0, Amount = amount, PaymentReference = paymentReference, OccurredAt = DateTime.UtcNow, Status = 1, Currency = "VND", PaymentMethod = paymentMethod };
        using var response = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/settlements", req, _jsonOptions, ct), ct);
        return response.IsSuccessStatusCode;
    }

}
