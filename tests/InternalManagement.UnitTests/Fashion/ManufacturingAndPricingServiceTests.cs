using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Moq;

namespace InternalManagement.UnitTests.Fashion;

public class ManufacturingAndPricingServiceTests
{
    [Fact]
    public async Task ProductionOrder_ShouldCalculateActualCost_ByMaterialLaborOutsideAndOverhead()
    {
        var fixture = await CreateFixtureAsync();
        var manufacturing = new ManufacturingService(fixture.Db, CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id), NullLogger<ManufacturingService>.Instance);

        var create = await manufacturing.CreateProductionOrderAsync(
            new CreateProductionOrderRequest(fixture.Product.Id, fixture.Bom.Id, new[] { new ProductionOutputRequest(fixture.Variant.Id, 2) }, "test batch"),
            CancellationToken.None);

        create.Succeeded.Should().BeTrue();
        create.Value!.StandardCost.Should().Be(250m); // (2*2*100 + 2*50) / 2

        var complete = await manufacturing.CompleteProductionOrderAsync(
            create.Value.Id,
            new CompleteProductionOrderRequest(
                new[]
                {
                    new ProductionMaterialConsumptionRequest(fixture.Fabric.Id, null, 4.4m, "M"),
                    new ProductionMaterialConsumptionRequest(fixture.Lining.Id, null, 2m, "M")
                },
                new[]
                {
                    new ProductionOperationRequest(1, "Cắt", false, "PerPiece", 50m, 2m, null),
                    new ProductionOperationRequest(2, "May", false, "PerPiece", 100m, 2m, null),
                    new ProductionOperationRequest(3, "Thêu ngoài", true, "PerPiece", 25m, 2m, null)
                },
                new[] { new ProductionOutputResultRequest(fixture.Variant.Id, 2, 0, 0) },
                100m,
                60m,
                "close actual cost"),
            CancellationToken.None);

        complete.Succeeded.Should().BeTrue();
        complete.Value!.CostStatus.Should().Be(CostStatus.Actual);
        complete.Value.ActualMaterialCost.Should().Be(540m); // 4.4*100 + 2*50
        complete.Value.ActualLaborCost.Should().Be(300m);
        complete.Value.ActualOutsideProcessingCost.Should().Be(50m);
        complete.Value.ActualOverheadCost.Should().Be(100m);
        complete.Value.ActualScrapReworkCost.Should().Be(60m);
        complete.Value.ActualTotalCost.Should().Be(1050m);
        complete.Value.ActualUnitCost.Should().Be(525m);

        var variant = await fixture.Db.ProductVariants.FirstAsync(x => x.Id == fixture.Variant.Id);
        variant.CostPrice.Should().Be(525m);
        variant.CostStatus.Should().Be(CostStatus.Actual);
        (await fixture.Db.MaterialLots.FirstAsync(x => x.Id == fixture.FabricLot.Id)).QuantityRemaining.Should().Be(5.6m);
        (await fixture.Db.InventoryBalances.FirstAsync(x => x.ProductVariantId == fixture.Variant.Id)).OnHandQuantity.Should().Be(2);
        (await fixture.Db.InventoryMovements.CountAsync(x => x.MovementType == InventoryMovementType.ProductionOutput)).Should().Be(1);
    }

    [Fact]
    public async Task PricingAndOrderSnapshot_ShouldIncludeChannelFeesAndActualCogs()
    {
        var fixture = await CreateFixtureAsync();
        var manufacturing = new ManufacturingService(fixture.Db, CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id), NullLogger<ManufacturingService>.Instance);
        var production = await manufacturing.CreateProductionOrderAsync(
            new CreateProductionOrderRequest(fixture.Product.Id, fixture.Bom.Id, new[] { new ProductionOutputRequest(fixture.Variant.Id, 1) }, null), CancellationToken.None);
        fixture.Db.ChangeTracker.Clear();
        await manufacturing.CompleteProductionOrderAsync(
            production.Value!.Id,
            new CompleteProductionOrderRequest(
                new[] { new ProductionMaterialConsumptionRequest(fixture.Fabric.Id, null, 2m, "M"), new ProductionMaterialConsumptionRequest(fixture.Lining.Id, null, 1m, "M") },
                new[] { new ProductionOperationRequest(1, "May", false, "PerPiece", 100m, 1m, null) },
                new[] { new ProductionOutputResultRequest(fixture.Variant.Id, 1, 0, 0) },
                100m,
                0m,
                null),
            CancellationToken.None);

        var pricing = new PricingService(fixture.Db, CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id), NullLogger<PricingService>.Instance);
        var simulation = await pricing.SimulateAsync(new PricingSimulationRequest(fixture.Variant.Id, "TIKTOK", 1000m, 100m, null, 0.30m, null), CancellationToken.None);

        simulation.Succeeded.Should().BeTrue();
        simulation.Value!.UnitCost.Should().Be(450m);
        simulation.Value.CustomerPaidAmount.Should().Be(900m);
        simulation.Value.Profit.Should().Be(213m);
        simulation.Value.Margin.Should().BeApproximately(213m / 900m, 0.000001m);
        simulation.Value.BreakEvenCustomerPrice.Should().BeApproximately(480m / 0.77m, 0.01m);
        simulation.Value.RequiredListPriceForTargetMargin.Should().BeApproximately(480m / 0.47m + 100m, 0.01m);

        var orders = new OrderCostingService(fixture.Db, CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id), NullLogger<OrderCostingService>.Instance);
        var order = await orders.CreateOrderAndSnapshotAsync(
            new CreateSalesOrderRequest("TIKTOK", "TT-001", "Khách test", null, null, 100m, 0m, 30m, new[] { new CreateSalesOrderItemRequest(fixture.Variant.Id, 1, 1000m) }, 50m, 20m, 10m),
            CancellationToken.None);

        order.Succeeded.Should().BeTrue();
        order.Value!.ActualCogs.Should().Be(450m);
        order.Value.Profit.Should().Be(133m);
        order.Value.AdvertisingCost.Should().Be(50m);
        order.Value.PackagingCost.Should().Be(20m);
        order.Value.OtherSellingExpense.Should().Be(10m);
        order.Value.CostStatus.Should().Be(CostStatus.Actual);
        (await fixture.Db.OrderCostSnapshots.CountAsync()).Should().Be(1);
        (await fixture.Db.InventoryBalances.FirstAsync(x => x.ProductVariantId == fixture.Variant.Id)).ReservedQuantity.Should().Be(1);

        var duplicate = await orders.CreateOrderAndSnapshotAsync(
            new CreateSalesOrderRequest("TIKTOK", "TT-001", "Khách test", null, null, 0m, 0m, 0m, new[] { new CreateSalesOrderItemRequest(fixture.Variant.Id, 1, 1000m) }),
            CancellationToken.None);
        duplicate.Succeeded.Should().BeFalse();
        duplicate.Errors.Should().Contain(x => x.Contains("trùng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OrderLifecycle_ShouldDeliver_ReturnAndIssueVietnameseBusinessDocuments()
    {
        var fixture = await CreateFixtureAsync();
        var manufacturing = new ManufacturingService(fixture.Db, CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id), NullLogger<ManufacturingService>.Instance);
        var production = await manufacturing.CreateProductionOrderAsync(
            new CreateProductionOrderRequest(fixture.Product.Id, fixture.Bom.Id, new[] { new ProductionOutputRequest(fixture.Variant.Id, 1) }, null), CancellationToken.None);
        var completed = await manufacturing.CompleteProductionOrderAsync(
            production.Value!.Id,
            new CompleteProductionOrderRequest(
                new[] { new ProductionMaterialConsumptionRequest(fixture.Fabric.Id, null, 2m, "M"), new ProductionMaterialConsumptionRequest(fixture.Lining.Id, null, 1m, "M") },
                new[] { new ProductionOperationRequest(1, "May", false, "PerPiece", 100m, 1m, null) },
                new[] { new ProductionOutputResultRequest(fixture.Variant.Id, 1, 0, 0) },
                100m, 0m, null), CancellationToken.None);
        completed.Succeeded.Should().BeTrue();

        var orders = new OrderCostingService(fixture.Db, CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id), NullLogger<OrderCostingService>.Instance);
        var created = await orders.CreateOrderAndSnapshotAsync(
            new CreateSalesOrderRequest("TIKTOK", "TT-LIFECYCLE-001", "Khách test", null, null, 0m, 30m, 0m,
                new[] { new CreateSalesOrderItemRequest(fixture.Variant.Id, 1, 1000m) }), CancellationToken.None);
        created.Succeeded.Should().BeTrue();

        var delivered = await orders.DeliverAsync(created.Value!.OrderId,
            new FulfillSalesOrderRequest(new[] { new FulfillSalesOrderItemRequest(fixture.Variant.Id, 1) }), CancellationToken.None);
        delivered.Succeeded.Should().BeTrue();
        delivered.Value!.Status.Should().Be(SalesOrderStatus.Completed);
        delivered.Value.Items.Single().RemainingQuantity.Should().Be(0);

        var returnCreated = await orders.CreateReturnAsync(
            new CreateReturnRequest(created.Value.OrderId, null, "Khách đổi size", new[]
            {
                new CreateReturnItemRequest(fixture.Variant.Id, 1, "Hàng còn tốt", true, 1000m)
            }), CancellationToken.None);
        returnCreated.Succeeded.Should().BeTrue();

        var inspected = await orders.InspectReturnAsync(returnCreated.Value!.Id, new InspectReturnRequest("Duyệt"), CancellationToken.None);
        inspected.Succeeded.Should().BeTrue();
        inspected.Value!.Status.Should().Be("Approved");
        inspected.Value.TotalRefundAmount.Should().Be(1000m);
        (await fixture.Db.InventoryBalances.FirstAsync(x => x.ProductVariantId == fixture.Variant.Id)).OnHandQuantity.Should().Be(1);

        var document = await orders.IssueDocumentAsync(created.Value.OrderId, new IssueSalesDocumentRequest(SalesDocumentType.RetailReceipt), CancellationToken.None);
        document.Succeeded.Should().BeTrue();
        document.Value!.DocumentNumber.Should().StartWith("POS-");
        document.Value.Items.Should().ContainSingle(x => x.Sku == fixture.Variant.Sku);

        using var payload = JsonDocument.Parse($"{{\"orderId\":\"TT-IMPORT-001\",\"customerName\":\"Khách API\",\"items\":[{{\"sku\":\"{fixture.Variant.Sku}\",\"quantity\":1,\"unitPrice\":1000}}]}}");
        var imported = await orders.ImportAsync(new ImportSalesOrderRequest("TIKTOK", payload.RootElement.Clone()), CancellationToken.None);
        imported.Succeeded.Should().BeTrue();
        imported.Value!.Created.Should().BeTrue();
        imported.Value.OrderNumber.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ConfirmedFashionSettlements_ShouldPostExactlyOneIncomeAndOneRefundExpense()
    {
        var fixture = await CreateFixtureAsync();
        var currentUser = CurrentUser(fixture.Company.Id, fixture.BusinessUnit.Id);
        var manufacturing = new ManufacturingService(fixture.Db, currentUser, NullLogger<ManufacturingService>.Instance);
        var production = await manufacturing.CreateProductionOrderAsync(
            new CreateProductionOrderRequest(fixture.Product.Id, fixture.Bom.Id, new[] { new ProductionOutputRequest(fixture.Variant.Id, 1) }, null),
            CancellationToken.None);
        await manufacturing.CompleteProductionOrderAsync(
            production.Value!.Id,
            new CompleteProductionOrderRequest(
                new[] { new ProductionMaterialConsumptionRequest(fixture.Fabric.Id, null, 2m, "M"), new ProductionMaterialConsumptionRequest(fixture.Lining.Id, null, 1m, "M") },
                new[] { new ProductionOperationRequest(1, "May", false, "PerPiece", 100m, 1m, null) },
                new[] { new ProductionOutputResultRequest(fixture.Variant.Id, 1, 0, 0) },
                100m, 0m, null),
            CancellationToken.None);

        var documents = new BusinessDocumentRegistry(fixture.Db);
        var orders = new OrderCostingService(
            fixture.Db,
            currentUser,
            NullLogger<OrderCostingService>.Instance,
            new PartyResolver(fixture.Db),
            documents,
            new FinancePostingService(fixture.Db, documents, currentUser));
        var created = await orders.CreateOrderAndSnapshotAsync(
            new CreateSalesOrderRequest("TIKTOK", "TT-SETTLEMENT-001", "Khách thanh toán", "0901000000", null, 0m, 0m, 0m,
                new[] { new CreateSalesOrderItemRequest(fixture.Variant.Id, 1, 1_000m) }),
            CancellationToken.None);

        var occurredAt = DateTime.UtcNow;
        var paymentRequest = new RecordSalesSettlementRequest(
            SalesSettlementKind.CustomerPayment, 1_000m, "TT-PAYMENT-001", occurredAt);
        var firstPayment = await orders.RecordSettlementAsync(created.Value!.OrderId, paymentRequest, CancellationToken.None);
        var retryPayment = await orders.RecordSettlementAsync(created.Value.OrderId, paymentRequest, CancellationToken.None);

        firstPayment.Succeeded.Should().BeTrue();
        retryPayment.Succeeded.Should().BeTrue();
        retryPayment.Value!.Id.Should().Be(firstPayment.Value!.Id);
        (await fixture.Db.FinanceTransactions.CountAsync(x => x.ReferenceType == nameof(SalesSettlement) && x.TransactionType == TransactionType.Income)).Should().Be(1);

        var delivered = await orders.DeliverAsync(created.Value.OrderId,
            new FulfillSalesOrderRequest(new[] { new FulfillSalesOrderItemRequest(fixture.Variant.Id, 1) }), CancellationToken.None);
        delivered.Succeeded.Should().BeTrue();
        var returned = await orders.CreateReturnAsync(new CreateReturnRequest(created.Value.OrderId, null, "Khách đổi ý", new[]
        {
            new CreateReturnItemRequest(fixture.Variant.Id, 1, "Good", true, 1_000m)
        }), CancellationToken.None);
        var inspected = await orders.InspectReturnAsync(returned.Value!.Id, new InspectReturnRequest("Approved"), CancellationToken.None);
        inspected.Succeeded.Should().BeTrue();

        var refund = await orders.RecordSettlementAsync(created.Value.OrderId, new RecordSalesSettlementRequest(
            SalesSettlementKind.CustomerRefund, 1_000m, "TT-REFUND-001", occurredAt.AddMinutes(1),
            ReturnId: returned.Value.Id), CancellationToken.None);

        refund.Succeeded.Should().BeTrue();
        (await fixture.Db.FinanceTransactions.CountAsync(x => x.ReferenceType == nameof(SalesSettlement))).Should().Be(2);
        (await fixture.Db.FinanceTransactions.CountAsync(x => x.ReferenceType == nameof(SalesSettlement) && x.TransactionType == TransactionType.Expense)).Should().Be(1);
        (await fixture.Db.BusinessDocuments.CountAsync(x => x.DocumentType == BusinessDocumentType.FashionSettlement)).Should().Be(2);
        (await fixture.Db.BusinessDocumentLinks.CountAsync()).Should().Be(2);
    }

    private static ICurrentUserService CurrentUser(Guid companyId, Guid businessUnitId)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.CompanyId).Returns(companyId);
        mock.Setup(x => x.BusinessUnitId).Returns(businessUnitId);
        mock.Setup(x => x.Username).Returns("test");
        return mock.Object;
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"Manufacturing_{Guid.NewGuid():N}")
            .Options;
        var db = new ApplicationDbContext(options);
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var businessUnit = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "MOLI Fashion" };
        var product = new Product { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, Code = "AD-TEST", Name = "Áo Dài Test", Category = "Áo Dài" };
        var variant = new ProductVariant { ProductId = product.Id, Sku = "AD-TEST-M", Size = "M", Color = "Đỏ", SellingPrice = 1000m, SourcingType = SourcingType.Make, CostStatus = CostStatus.Standard };
        var supplier = new Supplier { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, Code = "SUP-TEST", Name = "Supplier Test" };
        var fabric = new Material { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, Code = "FABRIC", Name = "Vải", Unit = "m" };
        var lining = new Material { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, Code = "LINING", Name = "Lót", Unit = "m" };
        var fabricLot = new MaterialLot { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, MaterialId = fabric.Id, SupplierId = supplier.Id, LotNumber = "LOT-FABRIC", QuantityReceived = 10m, QuantityRemaining = 10m, UnitCost = 100m, Currency = "VND" };
        var liningLot = new MaterialLot { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, MaterialId = lining.Id, SupplierId = supplier.Id, LotNumber = "LOT-LINING", QuantityReceived = 10m, QuantityRemaining = 10m, UnitCost = 50m, Currency = "VND" };
        fabric.QuantityOnHand = 10m;
        lining.QuantityOnHand = 10m;
        var bom = new Bom { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, ProductId = product.Id, Code = "BOM-TEST", VersionNumber = 1, Status = "Approved", EffectiveFrom = DateTime.UtcNow.AddDays(-1), Items = new List<BomItem> { new() { MaterialId = fabric.Id, Size = "M", Quantity = 2m, WastePercent = 0, Unit = "m", Sequence = 1 }, new() { MaterialId = lining.Id, Size = "M", Quantity = 1m, WastePercent = 0, Unit = "m", Sequence = 2 } } };
        var policy = new ChannelFeePolicy { CompanyId = company.Id, BusinessUnitId = businessUnit.Id, Code = "TIKTOK-V1", Channel = "TIKTOK", VersionNumber = 1, PlatformFeeRate = .10m, AffiliateFeeRate = .10m, PaymentFeeRate = .02m, FixedPaymentFee = 0m, TaxRate = .01m, DefaultShippingSubsidy = 30m, EffectiveFrom = DateTime.UtcNow.AddDays(-1), IsActive = true };

        product.Variants.Add(variant);
        db.AddRange(company, businessUnit, product, supplier, fabric, lining, fabricLot, liningLot, bom, policy);
        await db.SaveChangesAsync();
        return new Fixture(db, company, businessUnit, product, variant, fabric, lining, fabricLot, liningLot, bom);
    }

    private sealed record Fixture(ApplicationDbContext Db, Company Company, BusinessUnit BusinessUnit, Product Product, ProductVariant Variant, Material Fabric, Material Lining, MaterialLot FabricLot, MaterialLot LiningLot, Bom Bom);
}
