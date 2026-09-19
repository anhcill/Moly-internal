using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Moq;

namespace InternalManagement.UnitTests.Finance;

public sealed class FinanceLedgerServiceTests
{
    [Fact]
    public async Task CreateTransaction_ShouldUpdateCashBalance_AndIgnoreDuplicateReference()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var businessUnit = new BusinessUnit
        {
            CompanyId = company.Id,
            Code = "FASHION",
            Name = "MOLI Fashion"
        };
        db.AddRange(company, businessUnit);
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id, businessUnit.Id);
        var account = await service.CreateCashAccountAsync(
            new CreateCashAccountRequest("VCB_MAIN", "Vietcombank", OpeningBalance: 1_000_000m),
            CancellationToken.None);

        account.Succeeded.Should().BeTrue();
        var request = new CreateFinanceTransactionRequest(
            "Thu",
            250_000m,
            DateTime.UtcNow,
            null,
            businessUnit.Id,
            null,
            null,
            "Thu tiền đơn hàng",
            account.Value!.Id);

        var first = await service.CreateTransactionAsync(request, CancellationToken.None);
        var duplicate = await service.CreateTransactionAsync(request, CancellationToken.None);

        first.Succeeded.Should().BeTrue();
        duplicate.Succeeded.Should().BeTrue();
        duplicate.Value!.Id.Should().Be(first.Value!.Id);
        (await db.FinanceTransactions.CountAsync()).Should().Be(1);
        (await db.CashAccounts.SingleAsync()).CurrentBalance.Should().Be(1_250_000m);
    }

    [Fact]
    public async Task AdjustCashAccount_ShouldCreateLedgerEntry_AndCashFlowReportInVietnameseShape()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Add(company);
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id, null);
        var account = await service.CreateCashAccountAsync(
            new CreateCashAccountRequest("CASH_VND", "Quỹ tiền mặt"),
            CancellationToken.None);

        var adjustment = await service.AdjustCashAccountAsync(
            account.Value!.Id,
            new AdjustCashAccountRequest("Chi", 100_000m, DateTime.UtcNow, null, null, "Chi mua văn phòng phẩm"),
            CancellationToken.None);
        var report = await service.GetCashFlowReportAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), null, CancellationToken.None);

        adjustment.Succeeded.Should().BeTrue();
        adjustment.Value!.TransactionTypeName.Should().Be("Chi");
        report.Succeeded.Should().BeTrue();
        report.Value!.TotalExpense.Should().Be(100_000m);
        report.Value.NetCashFlow.Should().Be(-100_000m);
        report.Value.ByDay.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ProfitReport_ShouldSeparateConfirmedAndProvisionalProfit()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var businessUnit = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "MOLI Fashion" };
        var product = new Product
        {
            CompanyId = company.Id,
            BusinessUnitId = businessUnit.Id,
            Code = "AO-001",
            Name = "Áo test"
        };
        var variant = new ProductVariant
        {
            Product = product,
            Sku = "AO-001-M",
            Size = "M",
            SellingPrice = 1_000m,
            CostPrice = 400m,
            CostStatus = CostStatus.Estimated
        };
        var order = new SalesOrder
        {
            CompanyId = company.Id,
            BusinessUnitId = businessUnit.Id,
            OrderNumber = "SO-001",
            SourceSystem = "WEBSITE",
            GrossAmount = 1_000m,
            NetRevenue = 1_000m
        };
        var snapshot = new OrderCostSnapshot
        {
            CompanyId = company.Id,
            BusinessUnitId = businessUnit.Id,
            SalesOrder = order,
            Channel = "WEBSITE",
            GrossAmount = 1_000m,
            NetSalesAmount = 1_000m,
            ActualCogs = 400m,
            Profit = 600m,
            CostStatus = CostStatus.Provisional,
            SnapshottedAt = DateTime.UtcNow,
            Items = new List<OrderCostSnapshotItem>
            {
                new()
                {
                    ProductVariant = variant,
                    Quantity = 1,
                    UnitSellingPrice = 1_000m,
                    UnitCost = 400m,
                    TotalCogs = 400m,
                    CostStatus = CostStatus.Provisional
                }
            }
        };
        db.AddRange(company, businessUnit, product, order, snapshot);
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id, businessUnit.Id);
        var result = await service.GetProfitReportAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), null, "WEBSITE", null, null, "M", null,
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle();
        result.Value.Items[0].ProvisionalProfit.Should().Be(600m);
        result.Value.Items[0].ConfirmedProfit.Should().Be(0m);
        result.Value.HasProvisionalCost.Should().BeTrue();
        result.Value.Items[0].CostStatusName.Should().Be("Tạm thời");
    }

    [Fact]
    public async Task CompanyOverview_ShouldSeparateAreas_IsolateTenant_AndUseLatestSnapshotsOnly()
    {
        await using var db = CreateDb();
        var now = DateTime.UtcNow;
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var otherCompany = new Company { Code = "OTHER", Name = "Công ty khác" };
        var edtech = new BusinessUnit { CompanyId = company.Id, Code = "EDTECH", Name = "MOLI EdTech" };
        var csca = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "MOLI CSCA" };
        var interview = new BusinessUnit { CompanyId = company.Id, Code = "INTERVIEW", Name = "MOLI Interview" };
        var fashion = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "MOLI Fashion" };
        var otherFashion = new BusinessUnit { CompanyId = otherCompany.Id, Code = "FASHION", Name = "Fashion khác" };
        db.AddRange(company, otherCompany, edtech, csca, interview, fashion, otherFashion);

        db.FinanceTransactions.AddRange(
            NewTransaction(company.Id, edtech.Id, TransactionType.Income, 1_000m, now),
            NewTransaction(company.Id, csca.Id, TransactionType.Expense, 300m, now),
            NewTransaction(company.Id, fashion.Id, TransactionType.Income, 2_000m, now),
            NewTransaction(company.Id, fashion.Id, TransactionType.Expense, 800m, now),
            NewTransaction(company.Id, null, TransactionType.Expense, 50m, now),
            NewTransaction(company.Id, interview.Id, TransactionType.Income, 9_999m, now.AddDays(-20)),
            NewTransaction(otherCompany.Id, otherFashion.Id, TransactionType.Income, 999_999m, now));

        var techReferenceId = Guid.NewGuid();
        db.ProfitAllocations.AddRange(
            new ProfitAllocation
            {
                CompanyId = company.Id,
                BusinessUnitId = edtech.Id,
                ReferenceType = "Course",
                ReferenceId = techReferenceId,
                IncomeAmount = 800m,
                ExpenseAmount = 300m,
                AllocatedAt = now.AddHours(-2)
            },
            new ProfitAllocation
            {
                CompanyId = company.Id,
                BusinessUnitId = edtech.Id,
                ReferenceType = "Course",
                ReferenceId = techReferenceId,
                IncomeAmount = 1_000m,
                ExpenseAmount = 400m,
                AllocatedAt = now.AddHours(-1)
            },
            new ProfitAllocation
            {
                CompanyId = otherCompany.Id,
                BusinessUnitId = otherFashion.Id,
                ReferenceType = "Course",
                ReferenceId = Guid.NewGuid(),
                IncomeAmount = 999_999m,
                AllocatedAt = now
            });

        var firstOrder = NewSalesOrder(company.Id, fashion.Id, "SO-OVERVIEW-1", now);
        var secondOrder = NewSalesOrder(company.Id, fashion.Id, "SO-OVERVIEW-2", now);
        var foreignOrder = NewSalesOrder(otherCompany.Id, otherFashion.Id, "SO-FOREIGN", now);
        db.SalesOrders.AddRange(firstOrder, secondOrder, foreignOrder);
        db.OrderCostSnapshots.AddRange(
            NewSnapshot(company.Id, fashion.Id, firstOrder, 200m, CostStatus.Provisional, now.AddHours(-2)),
            NewSnapshot(company.Id, fashion.Id, firstOrder, 400m, CostStatus.Actual, now.AddHours(-1)),
            NewSnapshot(company.Id, fashion.Id, secondOrder, 300m, CostStatus.Standard, now),
            NewSnapshot(otherCompany.Id, otherFashion.Id, foreignOrder, 999_999m, CostStatus.Actual, now));
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id, null);
        var result = await service.GetCompanyFinancialOverviewAsync(
            now.AddDays(-1), now.AddDays(1), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var report = result.Value!;
        report.Areas.Should().HaveCount(2);

        var technologyEducation = report.Areas.Single(x => x.AreaCode == "TECHNOLOGY_EDUCATION");
        technologyEducation.BusinessUnitCodes.Should().BeEquivalentTo("EDTECH", "CSCA", "INTERVIEW");
        technologyEducation.TotalIncome.Should().Be(1_000m);
        technologyEducation.TotalExpense.Should().Be(300m);
        technologyEducation.NetCashFlow.Should().Be(700m);
        technologyEducation.ConfirmedProfit.Should().Be(600m);
        technologyEducation.ProfitSnapshotCount.Should().Be(1);
        technologyEducation.ProfitDataStatus.Should().Be("ACTUAL");

        var fashionSummary = report.Areas.Single(x => x.AreaCode == "FASHION");
        fashionSummary.TotalIncome.Should().Be(2_000m);
        fashionSummary.TotalExpense.Should().Be(800m);
        fashionSummary.ConfirmedProfit.Should().Be(400m);
        fashionSummary.ProvisionalProfit.Should().Be(300m);
        fashionSummary.OperatingProfit.Should().Be(700m);
        fashionSummary.ProfitSnapshotCount.Should().Be(2);
        fashionSummary.ProfitDataStatus.Should().Be("MIXED");

        report.CompanyTotal.TotalIncome.Should().Be(3_000m);
        report.CompanyTotal.TotalExpense.Should().Be(1_100m);
        report.CompanyTotal.NetCashFlow.Should().Be(1_900m);
        report.CompanyTotal.ConfirmedProfit.Should().Be(1_000m);
        report.CompanyTotal.ProvisionalProfit.Should().Be(300m);
        report.HasUnclassifiedTransactions.Should().BeTrue();
        report.UnclassifiedCashFlow.TotalExpense.Should().Be(50m);
        report.UnclassifiedCashFlow.TransactionCount.Should().Be(1);
    }

    [Fact]
    public async Task CompanyOverview_WithoutTenantContext_ShouldFailWhenMultipleCompaniesExist()
    {
        await using var db = CreateDb();
        db.Companies.AddRange(
            new Company { Code = "ONE", Name = "Công ty một" },
            new Company { Code = "TWO", Name = "Công ty hai" });
        await db.SaveChangesAsync();

        var service = new FinanceLedgerService(db, NullLogger<FinanceLedgerService>.Instance);
        var result = await service.GetCompanyFinancialOverviewAsync(null, null, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("tenant"));
    }

    [Fact]
    public async Task CompanyOverview_ShouldRejectInvertedDateRange()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id, null);
        var result = await service.GetCompanyFinancialOverviewAsync(
            DateTime.UtcNow, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("Khoảng thời gian"));
    }

    private static FinanceTransaction NewTransaction(
        Guid companyId,
        Guid? businessUnitId,
        TransactionType type,
        decimal amount,
        DateTime transactionDate) => new()
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            TransactionType = type,
            Amount = amount,
            TransactionDate = transactionDate,
            Description = "Dữ liệu kiểm thử bảng tổng"
        };

    private static SalesOrder NewSalesOrder(
        Guid companyId,
        Guid businessUnitId,
        string orderNumber,
        DateTime orderDate) => new()
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            OrderNumber = orderNumber,
            OrderDate = orderDate,
            SourceSystem = "TEST"
        };

    private static OrderCostSnapshot NewSnapshot(
        Guid companyId,
        Guid businessUnitId,
        SalesOrder order,
        decimal profit,
        CostStatus costStatus,
        DateTime snapshottedAt) => new()
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SalesOrder = order,
            Channel = "TEST",
            Profit = profit,
            CostStatus = costStatus,
            SnapshottedAt = snapshottedAt
        };

    private static ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"FinanceLedger_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FinanceLedgerService CreateService(
        ApplicationDbContext db,
        Guid companyId,
        Guid? businessUnitId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.CompanyId).Returns(companyId);
        currentUser.Setup(x => x.BusinessUnitId).Returns(businessUnitId);
        currentUser.Setup(x => x.Username).Returns("test");
        return new FinanceLedgerService(db, NullLogger<FinanceLedgerService>.Instance, currentUser.Object);
    }
}
