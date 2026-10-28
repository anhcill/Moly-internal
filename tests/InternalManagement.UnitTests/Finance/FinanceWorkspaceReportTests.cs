using FluentAssertions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace InternalManagement.UnitTests.Finance;

public sealed class FinanceWorkspaceReportTests
{
    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static FinanceWorkspaceService Service(ApplicationDbContext db, Guid companyId, Guid? unitId = null)
    {
        var user = new Mock<ICurrentUserService>();
        user.SetupGet(x => x.CompanyId).Returns(companyId);
        user.SetupGet(x => x.BusinessUnitId).Returns(unitId);
        return new FinanceWorkspaceService(db, user.Object);
    }

    [Fact]
    public async Task CurrentMonth_KeepsActualsAndRespectsZeroScenarioWithoutWriting()
    {
        await using var db = CreateDb();
        var company = Guid.NewGuid();
        var now = DateTime.UtcNow;
        db.AddRange(new FinanceTransaction { CompanyId = company, TransactionDate = now.AddSeconds(-1), TransactionType = TransactionType.Income, Amount = 50 },
            new FinanceTransaction { CompanyId = company, TransactionDate = now.AddSeconds(-1), TransactionType = TransactionType.Expense, Amount = 20 },
            new CashAccount { CompanyId = company, Code = "BANK", CurrentBalance = 999_999 },
            new PlannedCashMovement { CompanyId = company, PlannedDate = now.AddDays(1), TransactionType = TransactionType.Expense, Amount = 100, Title = "Rent" });
        var cls = new CscaClass { CompanyId = company, Code = "REPORT", Name = "Class", TuitionFee = 1000 };
        db.Add(new CscaClassStudent { Class = cls, StudentName = "Student", PaidAmount = 200, DebtDueDate = now.AddDays(1), PaymentStatus = PaymentStatus.Partial });
        await db.SaveChangesAsync();

        var result = await Service(db, company).GetWorkspaceAsync(null, 2, 0, 0, 100, default);

        result.Succeeded.Should().BeTrue();
        var report = result.Value!;
        report.CurrentCash.Should().Be(130);
        report.StudentDebt.Should().Be(800);
        report.Months.Sum(x => x.ActualInflow).Should().Be(50);
        report.Months.Sum(x => x.ActualOutflow).Should().Be(20);
        report.Months.Sum(x => x.ForecastInflow).Should().Be(0);
        report.Months.Sum(x => x.ForecastOutflow).Should().Be(0);
        report.Months.First().Kind.Should().Be("Mixed");
        report.ForecastClosingCash.Should().Be(130);
        report.CurrentCashIsReconciled.Should().BeFalse();
        (await db.StudentReceivables.CountAsync()).Should().Be(0, "loading a report must be read-only");
    }

    [Fact]
    public async Task Forecast_CollectsOverdueDebtAndOffsetsAdvanceOnlyOnceAcrossTeacherSchedule()
    {
        await using var db = CreateDb();
        var company = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var employee = new Employee { CompanyId = company, EmployeeCode = "TEACHER", FullName = "Teacher" };
        var cls = new CscaClass { CompanyId = company, Code = "DEBT", Name = "Class", TuitionFee = 1000 };
        db.AddRange(employee, new CscaClassStudent { Class = cls, StudentName = "Partial", PaidAmount = 200, DebtDueDate = now.AddDays(-5), PaymentStatus = PaymentStatus.Partial },
            new CscaClassStudent { Class = cls, StudentName = "Cancelled", PaidAmount = 0, PaymentStatus = PaymentStatus.Cancelled },
            new TeacherAdvance { CompanyId = company, EmployeeId = employee.Id, Amount = 100, IssuedAt = now.AddDays(-1) },
            new PlannedCashMovement { CompanyId = company, Title = "Other expense", PlannedDate = now.AddDays(1), TransactionType = TransactionType.Expense, Amount = 20 });
        db.AddRange(Snapshot(company, employee.Id, "done", now.AddDays(-2), 300, 300, true),
            Snapshot(company, employee.Id, "next", now.AddDays(1), 0, 200, false));
        await db.SaveChangesAsync();

        var result = await Service(db, company).GetWorkspaceAsync(null, 2, 50, 1, 0, default);

        result.Succeeded.Should().BeTrue();
        result.Value!.StudentDebt.Should().Be(800);
        result.Value.Months.Sum(x => x.StudentCollections).Should().Be(400);
        result.Value.Months.Sum(x => x.TeacherPayments).Should().Be(400, "300 earned + 200 scheduled - 100 already advanced");
        result.Value.ForecastClosingCash.Should().Be(-20);
        result.Value.Warnings.Should().Contain(x => x.Contains("thiếu tiền"));
    }

    [Fact]
    public async Task HistoricalMonths_UseVietnamBoundaryAndIgnoreScenarioAndOtherTenants()
    {
        await using var db = CreateDb();
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);
        db.AddRange(new FinanceTransaction { CompanyId = company, BusinessUnitId = unit, TransactionDate = first.AddTicks(-1), TransactionType = TransactionType.Income, Amount = 999 },
            new FinanceTransaction { CompanyId = company, BusinessUnitId = unit, TransactionDate = first, TransactionType = TransactionType.Income, Amount = 40 },
            new FinanceTransaction { CompanyId = Guid.NewGuid(), BusinessUnitId = unit, TransactionDate = first, TransactionType = TransactionType.Income, Amount = 999 },
            new FinanceTransaction { CompanyId = company, BusinessUnitId = Guid.NewGuid(), TransactionDate = first, TransactionType = TransactionType.Income, Amount = 999 },
            new FinanceTransaction { CompanyId = company, BusinessUnitId = unit, TransactionDate = first.AddMonths(1), TransactionType = TransactionType.Income, Amount = 200 });
        await db.SaveChangesAsync();

        var report = (await Service(db, company, unit).GetWorkspaceAsync(new DateTime(2026, 1, 1), 1, 0, 10, 0, default)).Value!;

        report.Months.Should().ContainSingle();
        report.Months[0].Kind.Should().Be("Actual");
        report.Months[0].TotalInflow.Should().Be(40);
        report.Months[0].Month.Month.Should().Be(1);
        report.ForecastClosingCash.Should().Be(40);
    }

    [Fact]
    public async Task SettledPlansAndPaidLmsSessions_AreNotForecastAgain()
    {
        await using var db = CreateDb();
        var company = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var paid = Snapshot(company, null, "paid-unmapped", now.AddDays(-1), 500, 500, true);
        paid.PaymentStatus = "paid";
        db.AddRange(paid,
            new PlannedCashMovement { CompanyId = company, PlannedDate = now.AddDays(1), TransactionType = TransactionType.Expense, Amount = 100, Status = "Settled", Title = "Settled" },
            new PlannedCashMovement { CompanyId = company, PlannedDate = now.AddDays(1), TransactionType = TransactionType.Expense, Amount = 200, Status = "Cancelled", Title = "Cancelled" });
        await db.SaveChangesAsync();

        var report = (await Service(db, company).GetWorkspaceAsync(null, 2, 100, 1, 0, default)).Value!;

        report.TeacherPayable.Should().Be(0);
        report.Months.Sum(x => x.ForecastOutflow).Should().Be(0);
    }

    private static CscaTeacherCostSnapshot Snapshot(Guid company, Guid? employee, string key, DateTime date, decimal accrued, decimal projected, bool completed) => new()
    {
        CompanyId = company, EmployeeId = employee, SourceKey = key, TeacherLmsId = 5,
        TeacherName = "Teacher", ClassSourceId = "class", SessionSourceId = key,
        StartTime = date, EndTime = date.AddHours(1), AccruedAmount = accrued,
        ProjectedAmount = projected, IsCompleted = completed, IsCurrent = true,
        Status = completed ? "ended" : "scheduled", PaymentStatus = "pending", SyncedAt = DateTime.UtcNow
    };
}
