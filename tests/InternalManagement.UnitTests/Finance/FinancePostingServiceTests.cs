using System.Text.Json;
using FluentAssertions;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration.Mappers;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InternalManagement.UnitTests.Finance;

public sealed class FinancePostingServiceTests
{
    [Fact]
    public async Task PostAsync_RetryAndCorrection_UsesOneFinanceEntryAndOneSettlementLink()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var businessUnitId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var registry = new BusinessDocumentRegistry(db);
        var sourceDocument = await registry.RegisterAsync(new BusinessDocumentRegistration(
            companyId, businessUnitId, BusinessDocumentType.EdTechPayment, nameof(Payment), paymentId,
            "PAY-001", 500_000m, DateTime.UtcNow), CancellationToken.None);
        await db.SaveChangesAsync();

        var postingService = new FinancePostingService(db, registry);
        var request = new FinancePostingRequest(
            companyId, businessUnitId, TransactionType.Income, 500_000m, DateTime.UtcNow,
            nameof(Payment), paymentId, "Thu học phí", sourceDocument.Id);

        var first = await postingService.PostAsync(request, CancellationToken.None);
        await db.SaveChangesAsync();
        var retry = await postingService.PostAsync(request, CancellationToken.None);
        await db.SaveChangesAsync();

        first.Outcome.Should().Be(FinancePostingOutcome.Created);
        retry.Outcome.Should().Be(FinancePostingOutcome.Unchanged);
        (await db.FinanceTransactions.CountAsync()).Should().Be(1);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);

        var corrected = await postingService.PostAsync(request with { Amount = 650_000m }, CancellationToken.None);
        await db.SaveChangesAsync();

        corrected.Outcome.Should().Be(FinancePostingOutcome.Updated);
        (await db.FinanceTransactions.SingleAsync()).Amount.Should().Be(650_000m);
        (await db.BusinessDocuments.SingleAsync(x => x.Id == corrected.BusinessDocumentId)).TotalAmount.Should().Be(650_000m);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PaymentMapper_ConfirmedPayment_PostsIncomeAndDoesNotDuplicateOnRetry()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var businessUnitId = Guid.NewGuid();
        db.EdTechCustomers.Add(new EdTechCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SourceSystem = "WEBSITE_EDTECH",
            SourceId = "customer-01",
            FullName = "Lê Minh",
            Email = "le.minh@example.com",
            PhoneNumber = "0901234567"
        });
        await db.SaveChangesAsync();

        var registry = new BusinessDocumentRegistry(db);
        var mapper = new PaymentMapper(
            db,
            NullLogger<PaymentMapper>.Instance,
            new PartyResolver(db),
            registry,
            new FinancePostingService(db, registry));
        var payload = JsonSerializer.SerializeToElement(new
        {
            id = "payment-01",
            customerId = "customer-01",
            amount = 750000m,
            currency = "VND",
            status = "Paid",
            paidAt = DateTime.UtcNow,
            paymentMethod = "VNPAY"
        });

        var first = await mapper.MapAndUpsertAsync(payload, "WEBSITE_EDTECH", companyId, businessUnitId, CancellationToken.None);
        var retry = await mapper.MapAndUpsertAsync(payload, "WEBSITE_EDTECH", companyId, businessUnitId, CancellationToken.None);

        first.Status.Should().Be(InternalManagement.Application.Features.Integration.Interfaces.MapResultStatus.Written);
        retry.Status.Should().Be(InternalManagement.Application.Features.Integration.Interfaces.MapResultStatus.Skipped);
        var payment = await db.Payments.SingleAsync();
        payment.BusinessDocumentId.Should().NotBeNull();
        (await db.FinanceTransactions.CountAsync()).Should().Be(1);
        var transaction = await db.FinanceTransactions.SingleAsync();
        transaction.TransactionType.Should().Be(TransactionType.Income);
        transaction.ReferenceType.Should().Be(nameof(Payment));
        transaction.ReferenceId.Should().Be(payment.Id);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);
    }

    private static ApplicationDbContext CreateDb() => new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"FinancePosting_{Guid.NewGuid():N}")
            .Options);
}
