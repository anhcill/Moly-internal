using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── Fashion & Inventory API Calls (Ngày 14) ──

    public async Task<PaginatedData<FashionProductItem>?> GetFashionProductsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/fashion/products?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<FashionProductItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<FashionProductDetailItem?> GetFashionProductDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/fashion/products/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<FashionProductDetailItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<FashionVariantItem>?> GetActiveFashionVariantsAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.GetAsync("api/v1/fashion/variants/active-buy", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<FashionVariantItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<FashionVariantItem>?> GetActiveSellableFashionVariantsAsync(CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            () => _httpClient.GetAsync("api/v1/fashion/variants/active", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<FashionVariantItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateFashionProductAsync(string code, string name, string? category, string? description, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Category = category, Description = description };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/fashion/products", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateFashionProductAsync(Guid productId, string code, string name, string? category, string? description, bool isActive, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Category = category, Description = description, IsActive = isActive };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/fashion/products/{productId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteFashionProductAsync(Guid productId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/fashion/products/{productId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> AddProductVariantAsync(Guid productId, string sku, string? barcode, string? color, string? size, decimal costPrice, decimal sellingPrice, int sourcingType = 0, CancellationToken ct = default)
    {
        var req = new { Sku = sku, Barcode = barcode, Color = color, Size = size, CostPrice = costPrice, SellingPrice = sellingPrice, SourcingType = sourcingType };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/fashion/products/{productId}/variants", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateProductVariantAsync(Guid variantId, string sku, string? barcode, string? color, string? size, decimal costPrice, decimal sellingPrice, int sourcingType, bool isActive, CancellationToken ct = default)
    {
        var req = new { Sku = sku, Barcode = barcode, Color = color, Size = size, CostPrice = costPrice, SellingPrice = sellingPrice, SourcingType = sourcingType, IsActive = isActive };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/fashion/variants/{variantId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteProductVariantAsync(Guid variantId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/fashion/variants/{variantId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<SupplierItem>?> GetSuppliersAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/fashion/suppliers?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<SupplierItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateSupplierAsync(string code, string name, string? contactName, string? phoneNumbers, string? email, string? address, string? bankAccounts, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, ContactName = contactName, Phone = FirstLine(phoneNumbers), PhoneNumbers = phoneNumbers, Email = email, Address = address, BankAccounts = bankAccounts };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/fashion/suppliers", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateSupplierAsync(Guid supplierId, string code, string name, string? contactName, string? phoneNumbers, string? email, string? address, string? bankAccounts, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, ContactName = contactName, Phone = FirstLine(phoneNumbers), PhoneNumbers = phoneNumbers, Email = email, Address = address, BankAccounts = bankAccounts };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/fashion/suppliers/{supplierId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteSupplierAsync(Guid supplierId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/fashion/suppliers/{supplierId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InventoryBalanceItem>?> GetInventoryBalancesAsync(string? search = null, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var url = "api/v1/inventory/balances?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";
        if (warehouseId.HasValue) url += $"&warehouseId={warehouseId.Value}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InventoryBalanceItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<IReadOnlyList<WarehouseItem>?> GetWarehousesAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var url = includeInactive ? "api/v1/inventory/warehouses?includeInactive=true" : "api/v1/inventory/warehouses";
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<WarehouseItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> CreateWarehouseAsync(string code, string name, string? address, bool isActive, bool isDefault, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Address = address, IsActive = isActive, IsDefault = isDefault };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/inventory/warehouses", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> UpdateWarehouseAsync(Guid warehouseId, string code, string name, string? address, bool isActive, bool isDefault, CancellationToken ct = default)
    {
        var req = new { Code = code, Name = name, Address = address, IsActive = isActive, IsDefault = isDefault };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/inventory/warehouses/{warehouseId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteWarehouseAsync(Guid warehouseId, CancellationToken ct = default)
    {
        using var res = await SendWithRefreshAsync(() => _httpClient.DeleteAsync($"api/v1/inventory/warehouses/{warehouseId}", ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<PaginatedData<InventoryMovementItem>?> GetInventoryMovementsAsync(Guid? variantId = null, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var url = "api/v1/inventory/movements?pageSize=50";
        if (variantId.HasValue) url += $"&variantId={variantId.Value}";
        if (warehouseId.HasValue) url += $"&warehouseId={warehouseId.Value}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<InventoryMovementItem>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PaginatedData<PurchaseReceiptItemModel>?> GetPurchaseReceiptsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = "api/v1/inventory/receipts?pageSize=50";
        if (!string.IsNullOrWhiteSpace(search)) url += $"&search={Uri.EscapeDataString(search)}";

        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync(url, ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PaginatedData<PurchaseReceiptItemModel>>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PurchaseReceiptDetailModel?> GetPurchaseReceiptDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(() => _httpClient.GetAsync($"api/v1/inventory/receipts/{id}", ct), ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PurchaseReceiptDetailModel>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<PurchaseReceiptItemModel?> CreatePurchaseReceiptAsync(Guid supplierId, string? notes, IReadOnlyList<CreatePurchaseReceiptItemReq> items, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var req = new { SupplierId = supplierId, Notes = notes, Items = items, WarehouseId = warehouseId };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/inventory/receipts", req, _jsonOptions, ct), ct);
        if (!res.IsSuccessStatusCode) return null;

        var envelope = await res.Content.ReadFromJsonAsync<ApiEnvelope<PurchaseReceiptItemModel>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> UpdatePurchaseReceiptHeaderAsync(Guid receiptId, Guid supplierId, string? notes, CancellationToken ct = default)
    {
        var req = new { SupplierId = supplierId, Notes = notes };
        using var res = await SendWithRefreshAsync(() => _httpClient.PutAsJsonAsync($"api/v1/inventory/receipts/{receiptId}", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> CancelPurchaseReceiptAsync(Guid receiptId, string? reason, CancellationToken ct = default)
    {
        var req = new { Reason = reason };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync($"api/v1/inventory/receipts/{receiptId}/cancel", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<ReceiptAttachmentItem?> UploadPurchaseReceiptAttachmentAsync(Guid receiptId, string filePath, CancellationToken ct = default)
    {
        using var response = await SendWithRefreshAsync(
            async () =>
            {
                // Build a fresh multipart body for a possible 401/refresh retry.
                using var form = new MultipartFormDataContent();
                await using var fileStream = File.OpenRead(filePath);
                using var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
                form.Add(streamContent, "file", Path.GetFileName(filePath));
                return await _httpClient.PostAsync($"api/v1/inventory/receipts/{receiptId}/attachment", form, ct);
            }, ct);
        if (!response.IsSuccessStatusCode) return null;

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReceiptAttachmentItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> AdjustStockAsync(Guid variantId, int quantityDelta, string? notes, Guid? warehouseId = null, CancellationToken ct = default)
    {
        var req = new { ProductVariantId = variantId, QuantityDelta = quantityDelta, Notes = notes, WarehouseId = warehouseId };
        using var res = await SendWithRefreshAsync(() => _httpClient.PostAsJsonAsync("api/v1/inventory/adjust", req, _jsonOptions, ct), ct);
        return res.IsSuccessStatusCode;
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

}
