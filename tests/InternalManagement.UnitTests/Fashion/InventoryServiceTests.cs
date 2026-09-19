using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Moq;

namespace InternalManagement.UnitTests.Fashion;

public class InventoryServiceTests
{
    private async Task<(ApplicationDbContext Db, Guid CompanyId, Guid SupplierId, Guid VariantId)> CreateSeedContextAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("InventoryServiceTest_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);

        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);

        var bu = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "MOLI Fashion" };
        db.BusinessUnits.Add(bu);

        var supplier = new Supplier
        {
            CompanyId = company.Id,
            BusinessUnitId = bu.Id,
            Code = "SUP-VPHUC",
            Name = "Xưởng May Vĩnh Phúc",
            ContactName = "Nguyễn Văn Hùng",
            Phone = "0987111222"
        };
        db.Suppliers.Add(supplier);

        var product = new Product
        {
            CompanyId = company.Id,
            BusinessUnitId = bu.Id,
            Code = "PROD-TSHIRT",
            Name = "Áo Thun MOLY",
            Category = "Áo"
        };
        db.Products.Add(product);

        var variant = new ProductVariant
        {
            ProductId = product.Id,
            Product = product,
            Sku = "TSHIRT-BLK-M",
            Color = "Đen",
            Size = "M",
            CostPrice = 95000m,
            SellingPrice = 220000m,
            SourcingType = SourcingType.Buy
        };
        db.ProductVariants.Add(variant);

        await db.SaveChangesAsync();
        return (db, company.Id, supplier.Id, variant.Id);
    }

    private static ICurrentUserService MockCurrentUser(Guid companyId)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(c => c.CompanyId).Returns(companyId);
        mock.Setup(c => c.Username).Returns("warehouse_mgr");
        return mock.Object;
    }

    [Fact]
    public async Task CreatePurchaseReceiptAsync_ShouldCreateReceipt_AndRecordMovement_AndUpdateBalance()
    {
        // Arrange
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        var items = new List<CreatePurchaseReceiptItemRequest>
        {
            new(variantId, 100, 95000m)
        };
        var req = new CreatePurchaseReceiptRequest(supplierId, "Nhập 100 áo thun size M", items);

        // Act
        var result = await service.CreatePurchaseReceiptAsync(req, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalAmount.Should().Be(9500000m);
        result.Value.ItemCount.Should().Be(1);
        result.Value.ReceiptNumber.Should().StartWith("PR-");

        // Verify InventoryMovement
        var movements = await db.InventoryMovements.Where(m => m.ProductVariantId == variantId).ToListAsync();
        movements.Should().ContainSingle();
        movements[0].MovementType.Should().Be(InventoryMovementType.PurchaseReceipt);
        movements[0].QuantityDelta.Should().Be(100);
        movements[0].UnitCost.Should().Be(95000m);

        // Verify InventoryBalance
        var balance = await db.InventoryBalances.FirstOrDefaultAsync(b => b.ProductVariantId == variantId);
        balance.Should().NotBeNull();
        balance!.OnHandQuantity.Should().Be(100);
        balance.AvailableQuantity.Should().Be(100);
    }

    [Fact]
    public async Task CreatePurchaseReceiptAsync_WhenSupplierNotFound_ShouldFail()
    {
        // Arrange
        var (db, companyId, _, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        var items = new List<CreatePurchaseReceiptItemRequest> { new(variantId, 50, 90000m) };
        var req = new CreatePurchaseReceiptRequest(Guid.NewGuid(), "Non-existent supplier", items);

        // Act
        var result = await service.CreatePurchaseReceiptAsync(req, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Nhà cung cấp không tồn tại"));
    }

    [Fact]
    public async Task CreatePurchaseReceiptAsync_WhenUnitPriceIsNegative_ShouldFailWithoutWritingMovement()
    {
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        var result = await service.CreatePurchaseReceiptAsync(
            new CreatePurchaseReceiptRequest(supplierId, "Đơn giá không hợp lệ", new[]
            {
                new CreatePurchaseReceiptItemRequest(variantId, 10, -1m)
            }),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Đơn giá nhập"));
        (await db.InventoryMovements.CountAsync()).Should().Be(0);
        (await db.PurchaseReceipts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreatePurchaseReceiptAsync_WhenVariantIsMake_ShouldFailWithoutWritingMovement()
    {
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var variant = await db.ProductVariants.FirstAsync(v => v.Id == variantId);
        variant.SourcingType = SourcingType.Make;
        await db.SaveChangesAsync();

        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);
        var result = await service.CreatePurchaseReceiptAsync(
            new CreatePurchaseReceiptRequest(supplierId, "Không nhập hàng MAKE", new[]
            {
                new CreatePurchaseReceiptItemRequest(variantId, 10, 95000m)
            }),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("MAKE"));
        (await db.InventoryMovements.CountAsync()).Should().Be(0);
        (await db.PurchaseReceipts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task InventoryCommands_WhenVariantBelongsToAnotherCompany_ShouldFail()
    {
        var (db, companyId, supplierId, _) = await CreateSeedContextAsync();
        var otherCompany = new Company { Code = "OTHER", Name = "Other Company" };
        var otherBusinessUnit = new BusinessUnit { CompanyId = otherCompany.Id, Code = "FASHION", Name = "Other Fashion" };
        var otherProduct = new Product
        {
            CompanyId = otherCompany.Id,
            BusinessUnitId = otherBusinessUnit.Id,
            Code = "OTHER-PRODUCT",
            Name = "Sản phẩm tenant khác",
            Category = "Áo"
        };
        var otherVariant = new ProductVariant
        {
            ProductId = otherProduct.Id,
            Product = otherProduct,
            Sku = "OTHER-SKU",
            CostPrice = 100m,
            SellingPrice = 200m
        };
        db.Companies.Add(otherCompany);
        db.BusinessUnits.Add(otherBusinessUnit);
        db.Products.Add(otherProduct);
        db.ProductVariants.Add(otherVariant);
        await db.SaveChangesAsync();

        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        var receiptResult = await service.CreatePurchaseReceiptAsync(
            new CreatePurchaseReceiptRequest(supplierId, "Cross tenant", new[]
            {
                new CreatePurchaseReceiptItemRequest(otherVariant.Id, 1, 100m)
            }),
            CancellationToken.None);
        var adjustResult = await service.AdjustStockAsync(
            new StockAdjustmentRequest(otherVariant.Id, 1, "Cross tenant"),
            CancellationToken.None);

        receiptResult.Succeeded.Should().BeFalse();
        adjustResult.Succeeded.Should().BeFalse();
        (await db.InventoryMovements.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AdjustStockAsync_ShouldAdjustOnHandQuantity_AndRecordMovement()
    {
        // Arrange
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        // First receive 50 items
        await service.CreatePurchaseReceiptAsync(new CreatePurchaseReceiptRequest(supplierId, "Init", new[] { new CreatePurchaseReceiptItemRequest(variantId, 50, 95000m) }), CancellationToken.None);

        // Act: Adjust +10 items
        var adjustResult = await service.AdjustStockAsync(new StockAdjustmentRequest(variantId, 10, "Kiểm kê kho phát hiện dư 10 cái"), CancellationToken.None);

        // Assert
        adjustResult.Succeeded.Should().BeTrue();
        adjustResult.Value!.QuantityDelta.Should().Be(10);
        adjustResult.Value.MovementType.Should().Be(InventoryMovementType.StockAdjustment);

        var balance = await db.InventoryBalances.FirstAsync(b => b.ProductVariantId == variantId);
        balance.OnHandQuantity.Should().Be(60);

        // Verify total movements
        var allMovements = await db.InventoryMovements.Where(m => m.ProductVariantId == variantId).ToListAsync();
        allMovements.Should().HaveCount(2);
    }

    [Fact]
    public async Task AdjustStockAsync_WhenReducingBeyondOnHand_ShouldFail()
    {
        // Arrange
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        // Receive 20 items
        await service.CreatePurchaseReceiptAsync(new CreatePurchaseReceiptRequest(supplierId, "Init", new[] { new CreatePurchaseReceiptItemRequest(variantId, 20, 95000m) }), CancellationToken.None);

        // Act: Try reducing -30 items
        var adjustResult = await service.AdjustStockAsync(new StockAdjustmentRequest(variantId, -30, "Xuất hủy quá số lượng tồn"), CancellationToken.None);

        // Assert
        adjustResult.Succeeded.Should().BeFalse();
        adjustResult.Errors.Should().Contain(e => e.Contains("không đủ để giảm"));
    }

    [Fact]
    public async Task AdjustStockAsync_WhenReducingBelowReservedQuantity_ShouldFail()
    {
        // Arrange
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);
        await service.CreatePurchaseReceiptAsync(new CreatePurchaseReceiptRequest(
            supplierId,
            "Init",
            new[] { new CreatePurchaseReceiptItemRequest(variantId, 20, 95000m) }),
            CancellationToken.None);

        var balance = await db.InventoryBalances.FirstAsync(b => b.ProductVariantId == variantId);
        balance.ReservedQuantity = 15;
        await db.SaveChangesAsync();

        // Act: on-hand would remain positive, but available stock would become negative.
        var adjustResult = await service.AdjustStockAsync(
            new StockAdjustmentRequest(variantId, -10, "Không được giảm phần hàng đang giữ"),
            CancellationToken.None);

        // Assert
        adjustResult.Succeeded.Should().BeFalse();
        adjustResult.Errors.Should().Contain(e => e.Contains("khả dụng"));
        balance.OnHandQuantity.Should().Be(20);
        balance.ReservedQuantity.Should().Be(15);
    }

    [Fact]
    public async Task GetBalancesAsync_And_GetMovementsAsync_ShouldReturnPaginatedData()
    {
        // Arrange
        var (db, companyId, supplierId, variantId) = await CreateSeedContextAsync();
        var service = new InventoryService(db, MockCurrentUser(companyId), NullLogger<InventoryService>.Instance);

        await service.CreatePurchaseReceiptAsync(new CreatePurchaseReceiptRequest(supplierId, "Init", new[] { new CreatePurchaseReceiptItemRequest(variantId, 40, 95000m) }), CancellationToken.None);

        // Act
        var balances = await service.GetBalancesAsync(null, null, 1, 10, CancellationToken.None);
        var movements = await service.GetMovementsAsync(variantId, null, 1, 10, CancellationToken.None);

        // Assert
        balances.TotalCount.Should().Be(1);
        balances.Items.First().OnHandQuantity.Should().Be(40);
        movements.TotalCount.Should().Be(1);
        movements.Items.First().QuantityDelta.Should().Be(40);
    }
}
