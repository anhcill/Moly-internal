using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Moq;

namespace InternalManagement.UnitTests.Fashion;

public class ProductServiceTests
{
    private async Task<ApplicationDbContext> CreateInMemoryDbAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ProductServiceTest_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);

        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);

        var bu = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "MOLI Fashion" };
        db.BusinessUnits.Add(bu);

        await db.SaveChangesAsync();
        return db;
    }

    private static ICurrentUserService MockCurrentUser(Guid companyId, Guid? buId = null)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(c => c.CompanyId).Returns(companyId);
        mock.Setup(c => c.BusinessUnitId).Returns(buId);
        mock.Setup(c => c.Username).Returns("admin");
        return mock.Object;
    }

    [Fact]
    public async Task CreateProductAsync_ShouldCreateProductSuccessfully()
    {
        // Arrange
        var db = await CreateInMemoryDbAsync();
        var company = await db.Companies.FirstAsync();
        var service = new ProductService(db, MockCurrentUser(company.Id), NullLogger<ProductService>.Instance);

        var req = new CreateProductRequest("PROD-001", "Áo Thun Cotton", "Áo Nam", "Mô tả áo thun");

        // Act
        var result = await service.CreateProductAsync(req, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Code.Should().Be("PROD-001");
        result.Value.Name.Should().Be("Áo Thun Cotton");

        var inDb = await db.Products.FirstOrDefaultAsync(p => p.Code == "PROD-001");
        inDb.Should().NotBeNull();
    }

    [Fact]
    public async Task AddVariantAsync_ShouldAddVariantToProduct()
    {
        // Arrange
        var db = await CreateInMemoryDbAsync();
        var company = await db.Companies.FirstAsync();
        var service = new ProductService(db, MockCurrentUser(company.Id), NullLogger<ProductService>.Instance);

        var prodResult = await service.CreateProductAsync(new CreateProductRequest("PROD-002", "Áo Polo", "Áo Nam", "Mô tả"), CancellationToken.None);
        var productId = prodResult.Value!.Id;

        var variantReq = new CreateProductVariantRequest("POLO-BLK-L", "8938500001", "Đen", "L", 120000m, 300000m);

        // Act
        var result = await service.AddVariantAsync(productId, variantReq, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Sku.Should().Be("POLO-BLK-L");
        result.Value.SellingPrice.Should().Be(300000m);

        var variantInDb = await db.ProductVariants.FirstOrDefaultAsync(v => v.Sku == "POLO-BLK-L");
        variantInDb.Should().NotBeNull();
        variantInDb!.ProductId.Should().Be(productId);
    }

    [Fact]
    public async Task GetProductsAsync_ShouldFilterAndPaginate()
    {
        // Arrange
        var db = await CreateInMemoryDbAsync();
        var company = await db.Companies.FirstAsync();
        var service = new ProductService(db, MockCurrentUser(company.Id), NullLogger<ProductService>.Instance);

        await service.CreateProductAsync(new CreateProductRequest("P-01", "Áo Sơ Mi Trắng", "Áo Sơ Mi", ""), CancellationToken.None);
        await service.CreateProductAsync(new CreateProductRequest("P-02", "Quần Jean Nam", "Quần Nam", ""), CancellationToken.None);

        // Act
        var result = await service.GetProductsAsync("Sơ Mi", 1, 10, CancellationToken.None);

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Should().ContainSingle(p => p.Code == "P-01");
    }

    [Fact]
    public async Task GetProductByIdAsync_ShouldReturnProductWithVariants()
    {
        // Arrange
        var db = await CreateInMemoryDbAsync();
        var company = await db.Companies.FirstAsync();
        var service = new ProductService(db, MockCurrentUser(company.Id), NullLogger<ProductService>.Instance);

        var prodResult = await service.CreateProductAsync(new CreateProductRequest("PROD-003", "Váy Nữ", "Váy", ""), CancellationToken.None);
        await service.AddVariantAsync(prodResult.Value!.Id, new CreateProductVariantRequest("VAY-RED-S", null, "Đỏ", "S", 150000m, 350000m), CancellationToken.None);

        // Act
        var detail = await service.GetProductByIdAsync(prodResult.Value!.Id, CancellationToken.None);

        // Assert
        detail.Should().NotBeNull();
        detail!.Code.Should().Be("PROD-003");
        detail.Variants.Should().HaveCount(1);
        detail.Variants[0].Sku.Should().Be("VAY-RED-S");
    }

    [Fact]
    public async Task CreateSupplierAsync_And_GetSuppliersAsync_ShouldWork()
    {
        // Arrange
        var db = await CreateInMemoryDbAsync();
        var company = await db.Companies.FirstAsync();
        var service = new ProductService(db, MockCurrentUser(company.Id), NullLogger<ProductService>.Instance);

        var req = new CreateSupplierRequest("SUP-TEST", "Xưởng May Thăng Long", "Bác Thăng", "0912999888", "thanglong@moly.local", "Hà Nội");

        // Act
        var createResult = await service.CreateSupplierAsync(req, CancellationToken.None);
        var listResult = await service.GetSuppliersAsync("Thăng Long", 1, 10, CancellationToken.None);

        // Assert
        createResult.Succeeded.Should().BeTrue();
        createResult.Value!.Code.Should().Be("SUP-TEST");
        listResult.TotalCount.Should().Be(1);
        listResult.Items.First().ContactName.Should().Be("Bác Thăng");
    }
}
