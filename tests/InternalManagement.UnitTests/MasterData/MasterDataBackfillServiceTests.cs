using FluentAssertions;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.UnitTests.MasterData;

public sealed class MasterDataBackfillServiceTests
{
    [Fact]
    public async Task BusinessDocumentRegistry_NormalizesLegacyUnspecifiedTimestampToUtc()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"BusinessDocumentTimestamp_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var registry = new BusinessDocumentRegistry(db);
        var issuedAt = new DateTime(2026, 8, 30, 9, 0, 0, DateTimeKind.Unspecified);

        var document = await registry.RegisterAsync(new BusinessDocumentRegistration(
            Guid.NewGuid(), null, BusinessDocumentType.PayrollPeriod, "PayrollPeriod", Guid.NewGuid(),
            "PAYROLL-LEGACY", 1_000_000m, issuedAt), CancellationToken.None);
        await db.SaveChangesAsync();

        document.IssuedAt.Kind.Should().Be(DateTimeKind.Utc);
        document.IssuedAt.Should().Be(DateTime.SpecifyKind(issuedAt, DateTimeKind.Utc));
    }

    [Fact]
    public async Task BackfillAsync_LinksLegacyCustomerPaymentAndFinanceTransaction_Idempotently()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"MasterDataBackfill_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var companyId = Guid.NewGuid();
        var businessUnitId = Guid.NewGuid();
        var customer = new EdTechCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SourceSystem = "WEBSITE_EDTECH",
            SourceId = "customer-01",
            FullName = "Nguyễn Minh Anh",
            Email = "minh.anh@example.com",
            PhoneNumber = "0901 234 567"
        };
        var payment = new Payment
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            CustomerId = customer.Id,
            Customer = customer,
            SourceSystem = "WEBSITE_EDTECH",
            SourcePaymentId = "payment-01",
            Amount = 1_250_000m,
            Currency = "VND",
            Status = PaymentStatus.Paid,
            PaidAt = DateTime.UtcNow
        };
        var financeTransaction = new FinanceTransaction
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            TransactionType = TransactionType.Income,
            Amount = payment.Amount,
            TransactionDate = payment.PaidAt,
            ReferenceType = nameof(Payment),
            ReferenceId = payment.Id,
            Description = "Thu học phí"
        };
        db.AddRange(customer, payment, financeTransaction);
        await db.SaveChangesAsync();

        var service = new MasterDataBackfillService(
            db,
            new PartyResolver(db),
            new BusinessDocumentRegistry(db));

        var firstRun = await service.BackfillAsync(50, CancellationToken.None);

        firstRun.PartiesLinked.Should().Be(1);
        firstRun.DocumentsLinked.Should().Be(2);
        firstRun.DocumentLinksCreated.Should().Be(1);

        var linkedCustomer = await db.EdTechCustomers.SingleAsync();
        var linkedPayment = await db.Payments.SingleAsync();
        var linkedTransaction = await db.FinanceTransactions.SingleAsync();
        linkedCustomer.PartyId.Should().NotBeNull();
        linkedPayment.BusinessDocumentId.Should().NotBeNull();
        linkedTransaction.BusinessDocumentId.Should().NotBeNull();
        (await db.PartyContacts.CountAsync()).Should().Be(2);
        (await db.PartyExternalIdentities.CountAsync()).Should().Be(1);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);

        var secondRun = await service.BackfillAsync(50, CancellationToken.None);

        secondRun.Should().Be(new InternalManagement.Application.Features.MasterData.Services.MasterDataBackfillResult(0, 0, 0));
        (await db.Parties.CountAsync()).Should().Be(1);
        (await db.BusinessDocuments.CountAsync()).Should().Be(2);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task BackfillAsync_RegistersLegacyCscaAndInterviewPaymentsInTheFinanceLedger()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"MasterDataBackfillEducation_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var companyId = Guid.NewGuid();
        var businessUnitId = Guid.NewGuid();
        var cls = new CscaClass
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            CourseId = Guid.NewGuid(),
            Code = "CSCA-LEGACY",
            Name = "Lớp dữ liệu cũ"
        };
        var student = new CscaClassStudent
        {
            Class = cls,
            ClassId = cls.Id,
            StudentName = "Học viên cũ",
            Email = "student.legacy@example.com",
            PhoneNumber = "0901000001",
            PaidAmount = 1_500_000m,
            PaymentStatus = PaymentStatus.Paid
        };
        var interviewCustomer = new InterviewCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SourceSystem = "WEBSITE_INTERVIEW",
            SourceId = "INT-LEGACY",
            FullName = "Khách Mock cũ",
            Email = "mock.legacy@example.com",
            Phone = "0901000002",
            PackageName = "Gói Mock",
            PaidAmount = 2_000_000m,
            Status = PaymentStatus.Paid
        };
        db.AddRange(cls, student, interviewCustomer);
        await db.SaveChangesAsync();

        var documents = new BusinessDocumentRegistry(db);
        var service = new MasterDataBackfillService(
            db,
            new PartyResolver(db),
            documents,
            new FinancePostingService(db, documents));

        var firstRun = await service.BackfillAsync(50, CancellationToken.None);

        firstRun.PartiesLinked.Should().Be(2);
        firstRun.DocumentsLinked.Should().Be(2);
        firstRun.DocumentLinksCreated.Should().Be(2);
        firstRun.FinanceTransactionsPosted.Should().Be(2);
        (await db.FinanceTransactions.CountAsync()).Should().Be(2);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(2);

        var secondRun = await service.BackfillAsync(50, CancellationToken.None);
        secondRun.Should().Be(new InternalManagement.Application.Features.MasterData.Services.MasterDataBackfillResult(0, 0, 0, 0));
    }
}
