using FluentAssertions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace InternalManagement.UnitTests.Finance;

public sealed class FinanceWorkspaceCommandTests
{
    [Fact]
    public async Task Receivable_ReadIsPure_AndReceiptsAndRefundsKeepNetDebtAndLedgerHistory()
    {
        await using var db = NewDb();
        var (company, student) = await SeedStudent(db);
        var classAllocation = new ProfitAllocation { CompanyId = company.Id, ReferenceType = nameof(CscaClass), ReferenceId = student.ClassId, IncomeAmount = 200, ExpenseAmount = 50 };
        db.ProfitAllocations.Add(classAllocation); await db.SaveChangesAsync();
        var service = Service(db, company.Id);
        var initial = await service.GetReceivablesAsync(default);
        initial.Value!.Single().OutstandingAmount.Should().Be(800);
        (await db.StudentReceivables.CountAsync()).Should().Be(0);

        var receipt = new RecordStudentReceiptRequest(300, IdempotencyKey: "receipt-1");
        var first = await service.RecordReceiptAsync(student.Id, receipt, default);
        var retry = await service.RecordReceiptAsync(student.Id, receipt, default);
        first.Succeeded.Should().BeTrue(); retry.Succeeded.Should().BeTrue();
        first.Value!.PaidAmount.Should().Be(500);
        first.Value.OutstandingAmount.Should().Be(500);
        (await db.StudentReceipts.CountAsync()).Should().Be(1);
        var storedReceipt = await db.StudentReceipts.SingleAsync();
        (await db.FinanceTransactions.SingleAsync()).Id.Should().Be(storedReceipt.FinanceTransactionId!.Value);
        (await db.BusinessDocuments.CountAsync()).Should().Be(1);

        var refundRequest = new RecordStudentRefundRequest(100, IdempotencyKey: "refund-1");
        var refund = await service.RecordRefundAsync(student.Id, refundRequest, default);
        (await service.RecordRefundAsync(student.Id, refundRequest, default)).Succeeded.Should().BeTrue();
        refund.Value!.PaidAmount.Should().Be(400);
        refund.Value.OutstandingAmount.Should().Be(600);
        student.PaidAmount.Should().Be(400);
        classAllocation.IncomeAmount.Should().Be(400);
        classAllocation.ExpenseAmount.Should().Be(50);
        (await db.FinanceTransactions.CountAsync()).Should().Be(2);
        (await service.RecordReceiptAsync(student.Id, receipt with { Amount = 301 }, default)).Succeeded.Should().BeFalse();
        (await service.RecordRefundAsync(student.Id, new(401, IdempotencyKey: "too-much"), default)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task AdvanceApplicationIsNonCash_AndConsumedAdvanceCannotBeReturned()
    {
        await using var db = NewDb();
        var (company, employee, snapshot) = await SeedTeacher(db);
        var service = Service(db, company.Id);
        var advanceRequest = new IssueTeacherAdvanceRequest(employee.Id, 600, IdempotencyKey: "advance-1");
        (await service.IssueAdvanceAsync(advanceRequest, default)).Succeeded.Should().BeTrue();
        (await service.IssueAdvanceAsync(advanceRequest, default)).Succeeded.Should().BeTrue();
        var payRequest = new PayTeacherRequest(employee.Id, 400, IdempotencyKey: "pay-1", ApplyAdvanceAmount: 600);
        var paid = await service.PayTeacherAsync(payRequest, default);
        paid.Succeeded.Should().BeTrue();
        paid.Value!.SettledAmount.Should().Be(1000);
        paid.Value.UnpaidEarned.Should().Be(0);
        paid.Value.RemainingAdvance.Should().Be(0);
        paid.Value.AdvancesApplied.Should().Be(600);
        (await service.PayTeacherAsync(payRequest, default)).Succeeded.Should().BeTrue();
        (await db.FinanceTransactions.CountAsync()).Should().Be(2);
        (await db.FinanceTransactions.SumAsync(x => x.Amount)).Should().Be(1000);
        (await db.TeacherPaymentAllocations.SumAsync(x => x.Amount)).Should().Be(1000);
        var advance = await db.TeacherAdvances.SingleAsync();
        (await service.ReturnAdvanceAsync(advance.Id, new(1, IdempotencyKey: "return-consumed"), default)).Succeeded.Should().BeFalse();

        snapshot.PaymentStatus = "paid"; snapshot.PaidAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        var afterSync = await service.GetTeacherCostsAsync(default);
        afterSync.Value!.Single().SettledAmount.Should().Be(1000, "later LMS paid flags cover the already allocated local settlement");
        (await service.PayTeacherAsync(new(employee.Id, 1, IdempotencyKey: "overpay"), default)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task SourcePaidTeacherCostCannotBePaidAgain()
    {
        await using var db = NewDb();
        var (company, employee, snapshot) = await SeedTeacher(db);
        snapshot.PaymentStatus = "paid";
        await db.SaveChangesAsync();
        var service = Service(db, company.Id);
        (await service.GetTeacherCostsAsync(default)).Value!.Single().UnpaidEarned.Should().Be(0);
        (await service.PayTeacherAsync(new(employee.Id, 1000, IdempotencyKey: "duplicate-source"), default)).Succeeded.Should().BeFalse();
        (await db.FinanceTransactions.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task AdvanceReturnRetriesDoNotCreateExtraIncome()
    {
        await using var db = NewDb();
        var (company, employee, _) = await SeedTeacher(db);
        var service = Service(db, company.Id);
        await service.IssueAdvanceAsync(new(employee.Id, 500, IdempotencyKey: "advance"), default);
        var advance = await db.TeacherAdvances.SingleAsync();
        var request = new ReturnTeacherAdvanceRequest(500, IdempotencyKey: "return");
        (await service.ReturnAdvanceAsync(advance.Id, request, default)).Succeeded.Should().BeTrue();
        (await service.ReturnAdvanceAsync(advance.Id, request, default)).Succeeded.Should().BeTrue();
        (await db.FinanceTransactions.CountAsync()).Should().Be(2);
        (await db.TeacherAdvanceReturns.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task MonthlyPlansMaterializeBoundedDates_AndSettlementPostsOnlyOnce()
    {
        await using var db = NewDb();
        var company = new Company { Name = "Company", Code = "A" }; db.Companies.Add(company); await db.SaveChangesAsync();
        var service = Service(db, company.Id);
        var request = new CreatePlannedCashMovementRequest(new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), "Expense", 100, "Rent", Recurrence: "Monthly", IdempotencyKey: "rent", RecurrenceCount: 3);
        var created = await service.CreatePlannedMovementAsync(request, default);
        (await service.CreatePlannedMovementAsync(request, default)).Succeeded.Should().BeTrue();
        var rows = await db.PlannedCashMovements.OrderBy(x => x.PlannedDate).ToListAsync();
        rows.Select(x => x.PlannedDate.Day).Should().Equal(31, 28, 31);
        (await service.SettlePlannedMovementAsync(created.Value!.Id, default)).Succeeded.Should().BeTrue();
        (await service.SettlePlannedMovementAsync(created.Value.Id, default)).Succeeded.Should().BeTrue();
        (await db.FinanceTransactions.CountAsync()).Should().Be(1);
        (await service.CancelPlannedMovementAsync(created.Value.Id, default)).Succeeded.Should().BeFalse();
        (await service.CancelPlannedMovementAsync(rows[1].Id, default)).Succeeded.Should().BeTrue();
        (await service.SettlePlannedMovementAsync(rows[1].Id, default)).Succeeded.Should().BeFalse();
        (await service.CreatePlannedMovementAsync(request with { RecurrenceCount = 25, IdempotencyKey = "too-many" }, default)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task CrossTenantAndBusinessUnitReferencesAreRejected()
    {
        await using var db = NewDb();
        var (company, student) = await SeedStudent(db);
        var (other, employee, _) = await SeedTeacher(db);
        var unitA = new BusinessUnit { CompanyId = company.Id, Code = "A", Name = "A" };
        var unitB = new BusinessUnit { CompanyId = company.Id, Code = "B", Name = "B" };
        db.AddRange(unitA, unitB); student.Class.BusinessUnitId = unitB.Id; await db.SaveChangesAsync();
        var service = Service(db, company.Id, unitA.Id);
        (await service.GetReceivablesAsync(default)).Value.Should().BeEmpty();
        (await service.RecordReceiptAsync(student.Id, new(1, IdempotencyKey: "cross-unit"), default)).Succeeded.Should().BeFalse();
        (await service.IssueAdvanceAsync(new(employee.Id, 1, IdempotencyKey: "cross-company"), default)).Succeeded.Should().BeFalse();
        (await service.CreatePlannedMovementAsync(new(DateTime.UtcNow, "Income", 1, "Wrong BU", IdempotencyKey: "cross-plan", BusinessUnitId: unitB.Id), default)).Succeeded.Should().BeFalse();
        (await db.FinanceTransactions.CountAsync()).Should().Be(0);
    }

    private static ApplicationDbContext NewDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static FinanceWorkspaceService Service(ApplicationDbContext db, Guid company, Guid? unit = null)
    {
        var user = new Mock<ICurrentUserService>(); user.SetupGet(x => x.CompanyId).Returns(company); user.SetupGet(x => x.BusinessUnitId).Returns(unit);
        return new(db, user.Object);
    }

    private static async Task<(Company Company, CscaClassStudent Student)> SeedStudent(ApplicationDbContext db)
    {
        var company = new Company { Name = "School", Code = Guid.NewGuid().ToString("N") };
        var course = new Course { CompanyId = company.Id, Title = "Course", CourseSourceId = "course" };
        var cls = new CscaClass { CompanyId = company.Id, Course = course, Name = "Class", Code = "CLS", TuitionFee = 1000 };
        var student = new CscaClassStudent { Class = cls, StudentName = "Student", PaidAmount = 200, PaymentStatus = PaymentStatus.Partial };
        db.AddRange(company, course, cls, student); await db.SaveChangesAsync();
        return (company, student);
    }

    private static async Task<(Company Company, Employee Employee, CscaTeacherCostSnapshot Snapshot)> SeedTeacher(ApplicationDbContext db)
    {
        var company = new Company { Name = "School", Code = Guid.NewGuid().ToString("N") };
        var employee = new Employee { CompanyId = company.Id, EmployeeCode = "T", FullName = "Teacher" };
        var snapshot = new CscaTeacherCostSnapshot { CompanyId = company.Id, Employee = employee, TeacherName = employee.FullName, SourceKey = "teacher-session", AccruedAmount = 1000, ProjectedAmount = 1000, IsCompleted = true, StartTime = DateTime.UtcNow.AddDays(-1) };
        db.AddRange(company, employee, snapshot); await db.SaveChangesAsync();
        return (company, employee, snapshot);
    }
}
