using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Moq;

namespace InternalManagement.UnitTests.Finance;

public sealed class FinanceAuditTrailTests
{
    [Fact]
    public async Task GetAuditTrail_ShouldReturnActorsAmountsAndNoDuplicateTeacherEntries()
    {
        var actor = "seed";
        var currentUser = CreateUserMock(() => actor);
        await using var db = CreateDb(currentUser.Object);

        var now = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var otherCompany = new Company { Code = "OTHER", Name = "Công ty khác" };
        var teacher = new Employee
        {
            CompanyId = company.Id,
            EmployeeCode = "GV01",
            FullName = "Nguyễn Văn An"
        };
        db.AddRange(company, otherCompany, teacher);
        await db.SaveChangesAsync();

        var manualIncome = new FinanceTransaction
        {
            CompanyId = company.Id,
            TransactionType = TransactionType.Income,
            Amount = 5_000_000m,
            TransactionDate = now.AddHours(-3),
            Description = "Thu học phí đợt 1"
        };
        actor = "ketoan.lan";
        db.FinanceTransactions.Add(manualIncome);
        await db.SaveChangesAsync();

        var manualExpense = new FinanceTransaction
        {
            CompanyId = company.Id,
            TransactionType = TransactionType.Expense,
            Amount = 800_000m,
            TransactionDate = now.AddHours(-2),
            ReferenceType = "CashAccountAdjustment",
            Description = "Chi mua tài liệu giảng dạy"
        };
        actor = "ketoan.minh";
        db.FinanceTransactions.Add(manualExpense);
        await db.SaveChangesAsync();

        var advance = new TeacherAdvance
        {
            CompanyId = company.Id,
            EmployeeId = teacher.Id,
            Amount = 2_000_000m,
            IssuedAt = now.AddHours(-1),
            Description = "Tạm ứng lương tháng 11"
        };
        var duplicateAdvanceTx = new FinanceTransaction
        {
            CompanyId = company.Id,
            TransactionType = TransactionType.Expense,
            Amount = advance.Amount,
            TransactionDate = advance.IssuedAt,
            ReferenceType = nameof(TeacherAdvance),
            ReferenceId = advance.Id,
            Description = advance.Description
        };
        actor = "admin.huy";
        db.TeacherAdvances.Add(advance);
        db.FinanceTransactions.Add(duplicateAdvanceTx);
        await db.SaveChangesAsync();

        var paymentTx = new FinanceTransaction
        {
            CompanyId = company.Id,
            TransactionType = TransactionType.Expense,
            Amount = 3_500_000m,
            TransactionDate = now,
            ReferenceType = nameof(TeacherPayment),
            Description = "Chuyển khoản lương tháng 11"
        };
        var payment = new TeacherPayment
        {
            CompanyId = company.Id,
            EmployeeId = teacher.Id,
            Amount = 3_500_000m,
            PaidAt = now,
            Description = "Chuyển khoản lương tháng 11",
            FinanceTransactionId = paymentTx.Id
        };
        paymentTx.ReferenceId = payment.Id;
        actor = "ketoan.lan";
        db.FinanceTransactions.Add(paymentTx);
        db.TeacherPayments.Add(payment);
        await db.SaveChangesAsync();

        var otherCompanyTx = new FinanceTransaction
        {
            CompanyId = otherCompany.Id,
            TransactionType = TransactionType.Income,
            Amount = 99_000_000m,
            TransactionDate = now,
            Description = "Dữ liệu công ty khác"
        };
        actor = "stranger";
        db.FinanceTransactions.Add(otherCompanyTx);
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id);
        var result = await service.GetAuditTrailAsync(null, null, null, 200, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var entries = result.Value!;
        entries.Should().HaveCount(4);

        entries[0].Id.Should().Be(payment.Id);
        entries[0].ActionLabel.Should().Be("Chuyển lương GV");
        entries[0].Direction.Should().Be("Outflow");
        entries[0].Amount.Should().Be(3_500_000m);
        entries[0].Actor.Should().Be("ketoan.lan");
        entries[0].ReferenceLabel.Should().Be("Nguyễn Văn An");

        entries[1].Id.Should().Be(advance.Id);
        entries[1].ActionLabel.Should().Be("Ứng lương GV");
        entries[1].Direction.Should().Be("Outflow");
        entries[1].Amount.Should().Be(2_000_000m);
        entries[1].Actor.Should().Be("admin.huy");
        entries[1].ReferenceLabel.Should().Be("Nguyễn Văn An");

        entries[2].Id.Should().Be(manualExpense.Id);
        entries[2].ActionLabel.Should().Be("Phiếu Chi");
        entries[2].Direction.Should().Be("Outflow");
        entries[2].Amount.Should().Be(800_000m);
        entries[2].Actor.Should().Be("ketoan.minh");
        entries[2].ReferenceLabel.Should().Be("CashAccountAdjustment");

        entries[3].Id.Should().Be(manualIncome.Id);
        entries[3].ActionLabel.Should().Be("Phiếu Thu");
        entries[3].Direction.Should().Be("Inflow");
        entries[3].Amount.Should().Be(5_000_000m);
        entries[3].Actor.Should().Be("ketoan.lan");
        entries[3].ReferenceLabel.Should().Be("Sổ quỹ");
    }

    [Fact]
    public async Task GetAuditTrail_ShouldFilterBySearchKeywordAcrossFields()
    {
        var actor = "seed";
        var currentUser = CreateUserMock(() => actor);
        await using var db = CreateDb(currentUser.Object);

        var now = new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Utc);
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        var teacher = new Employee
        {
            CompanyId = company.Id,
            EmployeeCode = "GV02",
            FullName = "Trần Thị Bích"
        };
        db.AddRange(company, teacher);
        await db.SaveChangesAsync();

        actor = "ketoan.hoa";
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            CompanyId = company.Id,
            TransactionType = TransactionType.Income,
            Amount = 1_200_000m,
            TransactionDate = now.AddHours(-2),
            Description = "Thu tiền lớp IELTS"
        });
        await db.SaveChangesAsync();

        actor = "admin.nam";
        db.FinanceTransactions.Add(new FinanceTransaction
        {
            CompanyId = company.Id,
            TransactionType = TransactionType.Expense,
            Amount = 300_000m,
            TransactionDate = now.AddHours(-1),
            Description = "Mua mực in văn phòng"
        });
        db.TeacherAdvances.Add(new TeacherAdvance
        {
            CompanyId = company.Id,
            EmployeeId = teacher.Id,
            Amount = 1_500_000m,
            IssuedAt = now,
            Description = "Ứng lương giữa kỳ"
        });
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id);

        var byDescription = await service.GetAuditTrailAsync(null, null, "ielts", 200, CancellationToken.None);
        byDescription.Succeeded.Should().BeTrue();
        byDescription.Value.Should().ContainSingle(x => x.Description.Contains("IELTS"));

        var byTeacher = await service.GetAuditTrailAsync(null, null, "bích", 200, CancellationToken.None);
        byTeacher.Succeeded.Should().BeTrue();
        byTeacher.Value.Should().ContainSingle(x => x.ActionLabel == "Ứng lương GV");

        var byActor = await service.GetAuditTrailAsync(null, null, "ADMIN.NAM", 200, CancellationToken.None);
        byActor.Succeeded.Should().BeTrue();
        byActor.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuditTrail_ShouldFilterByDateRangeAndTakeLimit()
    {
        var actor = "user1";
        var currentUser = CreateUserMock(() => actor);
        await using var db = CreateDb(currentUser.Object);

        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        db.FinanceTransactions.AddRange(
            new FinanceTransaction
            {
                CompanyId = company.Id,
                TransactionType = TransactionType.Income,
                Amount = 100_000m,
                TransactionDate = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc),
                Description = "Giao dịch đầu tháng"
            },
            new FinanceTransaction
            {
                CompanyId = company.Id,
                TransactionType = TransactionType.Income,
                Amount = 200_000m,
                TransactionDate = new DateTime(2026, 11, 10, 14, 30, 0, DateTimeKind.Utc),
                Description = "Giao dịch giữa tháng A"
            },
            new FinanceTransaction
            {
                CompanyId = company.Id,
                TransactionType = TransactionType.Expense,
                Amount = 300_000m,
                TransactionDate = new DateTime(2026, 11, 10, 18, 45, 0, DateTimeKind.Utc),
                Description = "Giao dịch giữa tháng B"
            },
            new FinanceTransaction
            {
                CompanyId = company.Id,
                TransactionType = TransactionType.Expense,
                Amount = 400_000m,
                TransactionDate = new DateTime(2026, 11, 25, 11, 0, 0, DateTimeKind.Utc),
                Description = "Giao dịch cuối tháng"
            });
        await db.SaveChangesAsync();

        var service = CreateService(db, company.Id);

        var inRange = await service.GetAuditTrailAsync(
            new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            null,
            200,
            CancellationToken.None);
        inRange.Succeeded.Should().BeTrue();
        inRange.Value.Should().HaveCount(2);
        inRange.Value![0].Amount.Should().Be(300_000m);
        inRange.Value[1].Amount.Should().Be(200_000m);

        var limited = await service.GetAuditTrailAsync(null, null, null, 2, CancellationToken.None);
        limited.Succeeded.Should().BeTrue();
        limited.Value.Should().HaveCount(2);
        limited.Value![0].Amount.Should().Be(400_000m);
        limited.Value[1].Amount.Should().Be(300_000m);

        var invalidRange = await service.GetAuditTrailAsync(
            new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc),
            null,
            200,
            CancellationToken.None);
        invalidRange.Succeeded.Should().BeFalse();
    }

    private static Mock<ICurrentUserService> CreateUserMock(Func<string> getUsername)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.Setup(x => x.Username).Returns(getUsername);
        return mock;
    }

    private static ApplicationDbContext CreateDb(ICurrentUserService currentUser)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"FinanceAuditTrail_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options, currentUser);
    }

    private static FinanceLedgerService CreateService(ApplicationDbContext db, Guid companyId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.Setup(x => x.CompanyId).Returns(companyId);
        currentUser.Setup(x => x.BusinessUnitId).Returns((Guid?)null);
        currentUser.Setup(x => x.Username).Returns("test");
        return new FinanceLedgerService(db, NullLogger<FinanceLedgerService>.Instance, currentUser.Object);
    }
}
