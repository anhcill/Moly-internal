using FluentAssertions;
using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Enums;

namespace InternalManagement.UnitTests.Domain;

public class DomainEntitiesTests
{
    [Fact]
    public void Company_ShouldInitializeWithDefaultValues()
    {
        // Act
        var company = new Company
        {
            Code = "MOLI",
            Name = "MOLI Group"
        };

        // Assert
        company.Id.Should().NotBeEmpty();
        company.IsActive.Should().BeTrue();
        company.IsDeleted.Should().BeFalse();
        company.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void InventoryBalance_AvailableQuantity_ShouldCalculateCorrectly()
    {
        // Arrange
        var balance = new InventoryBalance
        {
            OnHandQuantity = 100,
            ReservedQuantity = 30
        };

        // Act & Assert
        balance.AvailableQuantity.Should().Be(70);
    }

    [Fact]
    public void ProfitAllocation_NetAmount_ShouldBeIncomeMinusExpense()
    {
        // Arrange
        var allocation = new ProfitAllocation
        {
            IncomeAmount = 50000000m,
            ExpenseAmount = 15000000m
        };

        // Act & Assert
        allocation.NetAmount.Should().Be(35000000m);
    }
}
