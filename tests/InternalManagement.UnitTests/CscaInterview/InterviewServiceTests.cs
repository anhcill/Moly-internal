using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.CscaInterview;

public class InterviewServiceTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("InterviewServiceTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateCustomer_WithValidData_ShouldCreateCustomerAndProfitAllocation()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new InterviewService(db, currentUser, NullLogger<InterviewService>.Instance);

        var request = new CreateInterviewCustomerRequest(
            "Nguyễn Văn Ứng Viên",
            "ungvien@gmail.com",
            "0911222333",
            "Gói Mock Interview FAANG VIP",
            3,
            4500000m,
            PaymentStatus.Paid,
            "INT_TEST_001");

        // Act
        var result = await service.CreateCustomerAsync(request, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.FullName.Should().Be("Nguyễn Văn Ứng Viên");
        result.Value.SessionCount.Should().Be(3);
        result.Value.PaidAmount.Should().Be(4500000m);

        // Verify Profit Allocation was created
        var allocation = await db.ProfitAllocations.FirstOrDefaultAsync(p => p.ReferenceId == result.Value.Id);
        allocation.Should().NotBeNull();
        allocation!.IncomeAmount.Should().Be(4500000m);
        allocation.ExpenseAmount.Should().Be(0);
    }

    [Fact]
    public async Task GetFinancialSummary_ShouldAggregateTotalsCorrectly()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new InterviewService(db, currentUser, NullLogger<InterviewService>.Instance);

        await service.CreateCustomerAsync(new CreateInterviewCustomerRequest(
            "Khách 1", "k1@test.com", "0901", "Gói 1", 2, 2000000m, PaymentStatus.Paid), CancellationToken.None);

        await service.CreateCustomerAsync(new CreateInterviewCustomerRequest(
            "Khách 2", "k2@test.com", "0902", "Gói 2", 1, 1000000m, PaymentStatus.Paid), CancellationToken.None);

        await service.CreateCustomerAsync(new CreateInterviewCustomerRequest(
            "Khách 3", "k3@test.com", "0903", "Gói 3", 3, 3000000m, PaymentStatus.Pending), CancellationToken.None);

        // Act
        var summary = await service.GetFinancialSummaryAsync(CancellationToken.None);

        // Assert
        summary.Succeeded.Should().BeTrue();
        summary.Value.Should().NotBeNull();
        summary.Value!.TotalCustomers.Should().Be(3);
        summary.Value.TotalSessions.Should().Be(6);
        summary.Value.TotalRevenue.Should().Be(3000000m);
        summary.Value.PendingRevenue.Should().Be(3000000m);
    }

    [Fact]
    public async Task CreateCustomer_WithDuplicateSourceId_ShouldBeRejected()
    {
        using var db = CreateInMemoryDb();
        var service = new InterviewService(db, new CurrentUserService(null!), NullLogger<InterviewService>.Instance);
        var request = new CreateInterviewCustomerRequest(
            "Khách hàng", "customer@example.com", null, "Gói 1", 1, 1000000m, PaymentStatus.Paid, "SRC_DUP");

        (await service.CreateCustomerAsync(request, CancellationToken.None)).Succeeded.Should().BeTrue();
        var duplicate = await service.CreateCustomerAsync(request with { Email = "other@example.com" }, CancellationToken.None);
        duplicate.Succeeded.Should().BeFalse();
        duplicate.Errors.Should().Contain(e => e.Contains("đã tồn tại"));
    }

    [Fact]
    public async Task CreateAndUpdateCustomer_WithSharedDataServices_ShouldKeepOnePartyDocumentAndIncomePosting()
    {
        using var db = CreateInMemoryDb();
        db.Companies.Add(new Company { Code = "MOLI", Name = "MOLI Test" });
        await db.SaveChangesAsync();
        var currentUser = new CurrentUserService(null!);
        var documents = new BusinessDocumentRegistry(db);
        var service = new InterviewService(
            db,
            currentUser,
            NullLogger<InterviewService>.Instance,
            new PartyResolver(db),
            documents,
            new FinancePostingService(db, documents, currentUser));

        var created = await service.CreateCustomerAsync(new CreateInterviewCustomerRequest(
            "Khách Mock Interview", "mock@example.com", "0909 111 222", "Gói Premium", 2, 2_000_000m, PaymentStatus.Paid, "INT-LINK"),
            CancellationToken.None);
        var updated = await service.UpdateCustomerAsync(created.Value!.Id, new UpdateInterviewCustomerRequest(
            "Khách Mock Interview", "mock@example.com", "0909 111 222", "Gói Premium", 2, 2_500_000m, PaymentStatus.Paid),
            CancellationToken.None);

        created.Succeeded.Should().BeTrue();
        updated.Succeeded.Should().BeTrue();
        var customer = await db.InterviewCustomers.SingleAsync();
        customer.PartyId.Should().NotBeNull();
        customer.BusinessDocumentId.Should().NotBeNull();
        (await db.Parties.CountAsync()).Should().Be(1);
        (await db.BusinessDocuments.CountAsync(x => x.DocumentType == BusinessDocumentType.InterviewService)).Should().Be(1);
        var posting = await db.FinanceTransactions.SingleAsync(x => x.ReferenceType == nameof(InternalManagement.Domain.Entities.CscaInterview.InterviewCustomer));
        posting.TransactionType.Should().Be(TransactionType.Income);
        posting.Amount.Should().Be(2_500_000m);
        var settlement = await db.BusinessDocumentLinks.SingleAsync();
        settlement.Amount.Should().Be(2_500_000m);
    }
}
