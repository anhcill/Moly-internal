using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.HrPayroll;

public class PayrollWorkflowTests
{
    private async Task<(ApplicationDbContext Db, Guid PeriodId, Guid EmpUserId)> SetupDatabaseAndPeriodAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("PayrollWorkflowTest_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);

        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);

        var dept = new Department { CompanyId = company.Id, Code = "TECH", Name = "Phòng Công Nghệ" };
        db.Departments.Add(dept);

        var user = new User
        {
            Username = "emp.user",
            Email = "emp.user@moli.local",
            FullName = "Nguyễn Văn Nhân Viên"
        };
        db.Users.Add(user);

        var emp = new Employee
        {
            CompanyId = company.Id,
            DepartmentId = dept.Id,
            UserId = user.Id,
            EmployeeCode = "EMP-001",
            FullName = "Nguyễn Văn Nhân Viên",
            Email = "emp.user@moli.local",
            BaseSalary = 20000000m,
            Status = "Active"
        };
        db.Employees.Add(emp);
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            CompanyId = company.Id,
            EmployeeId = emp.Id,
            Date = new DateOnly(2026, 8, 4),
            CheckInTime = new TimeOnly(8, 0),
            CheckOutTime = new TimeOnly(17, 0),
            WorkHours = 8,
            Status = "Present"
        });

        var period = new PayrollPeriod
        {
            CompanyId = company.Id,
            Name = "Kỳ Lương Tháng 08/2026",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            Status = PayrollStatus.Draft
        };
        db.PayrollPeriods.Add(period);

        await db.SaveChangesAsync();
        return (db, period.Id, user.Id);
    }

    private CurrentUserService CreateCurrentUserService(Guid userId, string username)
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, userId.ToString()),
            new(System.Security.Claims.ClaimTypes.Name, username)
        };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "Test"));
        var httpContextAccessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };
        return new CurrentUserService(httpContextAccessor);
    }

    [Fact]
    public async Task FullWorkflow_FromDraftToPublished_ShouldTransitionAllStatesSuccessfully()
    {
        // Arrange
        var (db, periodId, _) = await SetupDatabaseAndPeriodAsync();
        var adminUser = new User { Username = "admin", Email = "admin@moli.local", FullName = "System Admin" };
        db.Users.Add(adminUser);
        await db.SaveChangesAsync();

        var currentUser = CreateCurrentUserService(adminUser.Id, "admin");
        var service = new PayrollService(db, currentUser, NullLogger<PayrollService>.Instance);

        // Step 1: Calculate Payroll (Draft -> Calculated)
        var calcRes = await service.CalculatePayrollAsync(periodId, CancellationToken.None);
        calcRes.Succeeded.Should().BeTrue();
        var periodAfterCalc = await db.PayrollPeriods.FindAsync(periodId);
        periodAfterCalc!.Status.Should().Be(PayrollStatus.Calculated);

        // Step 2: Submit for review (Calculated -> Reviewing)
        var submitRes = await service.SubmitForReviewAsync(periodId, new SubmitPayrollForReviewRequest("Gửi duyệt"), CancellationToken.None);
        submitRes.Succeeded.Should().BeTrue();
        submitRes.Value!.Status.Should().Be(PayrollStatus.Reviewing);

        // Step 3: Approve payroll (Reviewing -> Approved)
        var approveRes = await service.ApprovePayrollAsync(periodId, new ApprovePayrollRequest("Đã duyệt bảng lương"), CancellationToken.None);
        approveRes.Succeeded.Should().BeTrue();
        approveRes.Value!.Status.Should().Be(PayrollStatus.Approved);
        approveRes.Value.ApprovedAt.Should().NotBeNull();

        // Step 4: Mark as Paid (Approved -> Paid)
        var paidRes = await service.MarkAsPaidAsync(periodId, new MarkPayrollPaidRequest("Đã chi lương ngân hàng"), CancellationToken.None);
        paidRes.Succeeded.Should().BeTrue();
        paidRes.Value!.Status.Should().Be(PayrollStatus.Paid);

        // Step 5: Publish Payroll (Paid -> Published)
        var publishRes = await service.PublishPayrollAsync(periodId, new PublishPayrollRequest("Phát hành phiếu lương"), CancellationToken.None);
        publishRes.Succeeded.Should().BeTrue();
        publishRes.Value!.Status.Should().Be(PayrollStatus.Published);

        // Verify child payslips are all marked as Published
        var payslips = await db.Payslips.Where(ps => ps.PayrollPeriodId == periodId).ToListAsync();
        payslips.Should().NotBeEmpty();
        payslips.Should().OnlyContain(ps => ps.Status == PayrollStatus.Published && ps.PublishedAt != null);

        // Verify approval logs
        var approvals = await service.GetApprovalsAsync(periodId, CancellationToken.None);
        approvals.Succeeded.Should().BeTrue();
        approvals.Value.Should().HaveCount(4); // Reviewing, Approved, Paid, Published
    }

    [Fact]
    public async Task MarkAsPaid_WithFinancePostingEnabled_CreatesOnePayrollExpenseAndSettlementLink()
    {
        var (db, periodId, _) = await SetupDatabaseAndPeriodAsync();
        var registry = new BusinessDocumentRegistry(db);
        var service = new PayrollService(
            db,
            new CurrentUserService(null!),
            NullLogger<PayrollService>.Instance,
            registry,
            new FinancePostingService(db, registry));

        (await service.CalculatePayrollAsync(periodId, CancellationToken.None)).Succeeded.Should().BeTrue();
        (await service.ApprovePayrollAsync(periodId, new ApprovePayrollRequest(null), CancellationToken.None)).Succeeded.Should().BeTrue();

        var paid = await service.MarkAsPaidAsync(periodId, new MarkPayrollPaidRequest("Đã chuyển khoản"), CancellationToken.None);

        paid.Succeeded.Should().BeTrue();
        var transaction = await db.FinanceTransactions.SingleAsync();
        transaction.TransactionType.Should().Be(TransactionType.Expense);
        transaction.ReferenceType.Should().Be(nameof(PayrollPeriod));
        transaction.ReferenceId.Should().Be(periodId);
        transaction.Amount.Should().BeGreaterThan(0);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task InvalidTransition_PublishingFromDraft_ShouldFail()
    {
        // Arrange
        var (db, periodId, _) = await SetupDatabaseAndPeriodAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new PayrollService(db, currentUser, NullLogger<PayrollService>.Instance);

        // Act - Attempt to publish directly from Draft without calculation or approval
        var publishRes = await service.PublishPayrollAsync(periodId, new PublishPayrollRequest("Lỗi phát hành"), CancellationToken.None);

        // Assert
        publishRes.Succeeded.Should().BeFalse();
        publishRes.Errors.Should().Contain(e => e.Contains("Không thể phát hành"));
    }

    [Fact]
    public async Task GetPersonalPayslip_BeforePublishing_ShouldFail_AfterPublishing_ShouldSucceed()
    {
        // Arrange
        var (db, periodId, empUserId) = await SetupDatabaseAndPeriodAsync();

        // Create a mock HttpContext accessor for the employee user
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, empUserId.ToString()),
            new(System.Security.Claims.ClaimTypes.Name, "emp.user")
        };
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(claims, "Test"));
        var httpContextAccessor = new Microsoft.AspNetCore.Http.HttpContextAccessor { HttpContext = httpContext };

        var userCurrentUserService = new CurrentUserService(httpContextAccessor);
        var adminCurrentUserService = new CurrentUserService(null!);

        var adminService = new PayrollService(db, adminCurrentUserService, NullLogger<PayrollService>.Instance);
        var empService = new PayrollService(db, userCurrentUserService, NullLogger<PayrollService>.Instance);

        // Step 1: Calculate payroll
        await adminService.CalculatePayrollAsync(periodId, CancellationToken.None);

        // Act 1: Employee tries to view personal payslip while period is Calculated (not published yet)
        var beforePublishRes = await empService.GetPersonalPayslipAsync(periodId, CancellationToken.None);
        beforePublishRes.Succeeded.Should().BeFalse();

        // Step 2: Approve and Publish
        await adminService.ApprovePayrollAsync(periodId, new ApprovePayrollRequest(null), CancellationToken.None);
        await adminService.PublishPayrollAsync(periodId, new PublishPayrollRequest(null), CancellationToken.None);

        // Act 2: Employee views personal payslip after publishing
        var afterPublishRes = await empService.GetPersonalPayslipAsync(periodId, CancellationToken.None);

        // Assert 2
        afterPublishRes.Succeeded.Should().BeTrue();
        afterPublishRes.Value.Should().NotBeNull();
        afterPublishRes.Value!.EmployeeCode.Should().Be("EMP-001");
        afterPublishRes.Value.Status.Should().Be(PayrollStatus.Published);
    }
}
