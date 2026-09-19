using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Moq;

namespace InternalManagement.UnitTests.Finance;

public class ProfitAllocationServiceTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("ProfitAllocationServiceTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task GetProfitSummaryByBusinessUnit_ShouldCalculateIncomeExpenseAndNet()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var buCsca = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "MOLI CSCA" };
        db.BusinessUnits.Add(buCsca);
        await db.SaveChangesAsync();

        var classId = Guid.NewGuid();
        var cscaClass = new CscaClass
        {
            Id = classId,
            CompanyId = company.Id,
            BusinessUnitId = buCsca.Id,
            Code = "CSCA-TEST",
            Name = "Lớp Test"
        };
        db.CscaClasses.Add(cscaClass);

        db.ProfitAllocations.Add(new ProfitAllocation
        {
            CompanyId = company.Id,
            BusinessUnitId = buCsca.Id,
            ReferenceType = "CscaClass",
            ReferenceId = classId,
            IncomeAmount = 10000000m,
            ExpenseAmount = 3000000m,
            AllocatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var service = new ProfitAllocationService(db, NullLogger<ProfitAllocationService>.Instance);

        // Act
        var result = await service.GetProfitSummaryByBusinessUnitAsync("CSCA", CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.BusinessUnitCode.Should().Be("CSCA");
        result.Value.TotalIncome.Should().Be(10000000m);
        result.Value.TotalExpense.Should().Be(3000000m);
        result.Value.NetProfit.Should().Be(7000000m);
        result.Value.Allocations.Count.Should().Be(1);
        result.Value.Allocations[0].ReferenceTitle.Should().Contain("CSCA-TEST");
    }

    [Fact]
    public async Task RecordAllocation_ShouldNotUpdateAnotherTenantWithSameReference()
    {
        await using var db = CreateInMemoryDb();
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var otherCompany = new Company { Code = "OTHER", Name = "Công ty khác" };
        var businessUnit = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "MOLI CSCA" };
        var otherBusinessUnit = new BusinessUnit { CompanyId = otherCompany.Id, Code = "CSCA", Name = "CSCA khác" };
        var sharedReferenceId = Guid.NewGuid();
        var foreignAllocation = new ProfitAllocation
        {
            CompanyId = otherCompany.Id,
            BusinessUnitId = otherBusinessUnit.Id,
            ReferenceType = "CscaClass",
            ReferenceId = sharedReferenceId,
            IncomeAmount = 99_000m,
            ExpenseAmount = 1_000m
        };
        db.AddRange(company, otherCompany, businessUnit, otherBusinessUnit, foreignAllocation);
        await db.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.CompanyId).Returns(company.Id);
        var service = new ProfitAllocationService(
            db, NullLogger<ProfitAllocationService>.Instance, currentUser.Object);

        var result = await service.RecordAllocationAsync(
            new RecordProfitAllocationRequest("CSCA", "CscaClass", sharedReferenceId, 10_000m, 2_000m),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var allocations = await db.ProfitAllocations.OrderBy(x => x.CompanyId).ToListAsync();
        allocations.Should().HaveCount(2);
        allocations.Single(x => x.CompanyId == otherCompany.Id).IncomeAmount.Should().Be(99_000m);
        allocations.Single(x => x.CompanyId == company.Id).IncomeAmount.Should().Be(10_000m);
    }
}
