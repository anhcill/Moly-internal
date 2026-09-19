using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.IntegrationTests;

public class FashionAndInventoryApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public FashionAndInventoryApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var loginReq = new LoginRequest("admin", "Admin@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginData!.Data!.AccessToken;
    }

    [Fact]
    public async Task GetProducts_WithoutAuth_ShouldReturnUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/fashion/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetProducts_AsAdmin_ShouldReturnSeededProducts()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/fashion/products");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<ProductDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateProduct_And_AddVariant_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var random = Guid.NewGuid().ToString("N")[..6];
        var createReq = new CreateProductRequest($"PROD-API-{random}", $"Áo Khoác Gió API {random}", "Áo Khoác", "Áo gió cản nước");

        // 1. Create Product
        var createRes = await client.PostAsJsonAsync("/api/v1/fashion/products", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdProd = await createRes.Content.ReadFromJsonAsync<ApiResponse<ProductDto>>();
        createdProd!.Data.Should().NotBeNull();
        var productId = createdProd.Data!.Id;

        // 2. Add Variant
        var variantReq = new CreateProductVariantRequest($"WIND-BLK-L-{random}", "8939000101", "Đen", "L", 180000m, 390000m);
        var variantRes = await client.PostAsJsonAsync($"/api/v1/fashion/products/{productId}/variants", variantReq);
        variantRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var variantData = await variantRes.Content.ReadFromJsonAsync<ApiResponse<ProductVariantDto>>();
        variantData!.Data!.Sku.Should().Be($"WIND-BLK-L-{random}");

        // 3. Get Details
        var detailRes = await client.GetAsync($"/api/v1/fashion/products/{productId}");
        detailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var detailData = await detailRes.Content.ReadFromJsonAsync<ApiResponse<ProductDetailDto>>();
        detailData!.Data!.Variants.Should().ContainSingle(v => v.Sku == $"WIND-BLK-L-{random}");
    }

    [Fact]
    public async Task CreateSupplier_And_GetSuppliers_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var random = Guid.NewGuid().ToString("N")[..6];
        var req = new CreateSupplierRequest($"SUP-{random}", $"Nhà May API {random}", "Đại Diện", "0909112233", $"ncc{random}@moly.local", "Hà Nội");

        // Act
        var createRes = await client.PostAsJsonAsync("/api/v1/fashion/suppliers", req);
        createRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var listRes = await client.GetAsync("/api/v1/fashion/suppliers");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var listData = await listRes.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<SupplierDto>>>();
        listData!.Data!.Items.Should().Contain(s => s.Code == $"SUP-{random}");
    }

    [Fact]
    public async Task PurchaseReceipt_And_InventoryBalances_Movements_ShouldWorkEndToEnd()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Create a Dedicated Supplier and Product with Variant for this test
        var rand = Guid.NewGuid().ToString("N")[..6];
        var supReq = new CreateSupplierRequest($"SUP-E2E-{rand}", $"Nhà Cung Cấp E2E {rand}", "Đại Diện", "0911223344", $"e2e_{rand}@moly.local", "Hà Nội");
        var supRes = await client.PostAsJsonAsync("/api/v1/fashion/suppliers", supReq);
        supRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var supData = await supRes.Content.ReadFromJsonAsync<ApiResponse<SupplierDto>>();
        var supplierId = supData!.Data!.Id;

        var prodReq = new CreateProductRequest($"PROD-E2E-{rand}", $"Sản Phẩm E2E {rand}", "Thời Trang", "Mô tả");
        var prodRes = await client.PostAsJsonAsync("/api/v1/fashion/products", prodReq);
        prodRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var prodData = await prodRes.Content.ReadFromJsonAsync<ApiResponse<ProductDto>>();
        var prodId = prodData!.Data!.Id;

        var sku = $"SKU-E2E-{rand}";
        var varReq = new CreateProductVariantRequest(sku, null, "Xanh", "L", 80000m, 190000m, SourcingType.Buy);
        var varRes = await client.PostAsJsonAsync($"/api/v1/fashion/products/{prodId}/variants", varReq);
        varRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var varData = await varRes.Content.ReadFromJsonAsync<ApiResponse<ProductVariantDto>>();
        var variantId = varData!.Data!.Id;

        // 2. Create Purchase Receipt
        var receiptReq = new CreatePurchaseReceiptRequest(
            supplierId,
            "Nhập hàng kiểm thử Integration Test",
            new[] { new CreatePurchaseReceiptItemRequest(variantId, 30, 80000m) }
        );

        var receiptRes = await client.PostAsJsonAsync("/api/v1/inventory/receipts", receiptReq);
        receiptRes.StatusCode.Should().Be(HttpStatusCode.Created, await receiptRes.Content.ReadAsStringAsync());
        var receiptData = await receiptRes.Content.ReadFromJsonAsync<ApiResponse<PurchaseReceiptDto>>();
        receiptData!.Data!.ReceiptNumber.Should().StartWith("PR-");
        receiptData.Data.TotalAmount.Should().Be(30 * 80000m);

        // 3. Verify Balances
        var balanceRes = await client.GetAsync("/api/v1/inventory/balances");
        balanceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceData = await balanceRes.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<InventoryBalanceDto>>>();
        balanceData!.Data!.Items.Should().Contain(b => b.ProductVariantId == variantId && b.OnHandQuantity >= 30);

        // 4. Verify Movements
        var moveRes = await client.GetAsync($"/api/v1/inventory/movements?variantId={variantId}");
        moveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var moveData = await moveRes.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<InventoryMovementDto>>>();
        moveData!.Data!.Items.Should().Contain(m => m.ReferenceId == receiptData.Data.Id);

        // 5. Test Stock Adjustment
        var adjustReq = new StockAdjustmentRequest(variantId, 5, "Điều chỉnh kiểm kê test");
        var adjustRes = await client.PostAsJsonAsync("/api/v1/inventory/adjust", adjustReq);
        adjustRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify Balance after adjustment
        var balanceAfterRes = await client.GetAsync("/api/v1/inventory/balances");
        var balanceAfterData = await balanceAfterRes.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<InventoryBalanceDto>>>();
        balanceAfterData!.Data!.Items.Should().Contain(b => b.ProductVariantId == variantId && b.OnHandQuantity == 35);
    }
}
