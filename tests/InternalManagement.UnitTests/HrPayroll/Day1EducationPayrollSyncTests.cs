using FluentAssertions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace InternalManagement.UnitTests.HrPayroll;

public sealed class Day1EducationPayrollSyncTests
{
    private static ApplicationDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("Day1EduPayroll_" + Guid.NewGuid())
            .Options);

    private static ICurrentUserService MockUser(Guid companyId, Guid? buId = null)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(x => x.CompanyId).Returns(companyId);
        mock.SetupGet(x => x.BusinessUnitId).Returns(buId);
        mock.SetupGet(x => x.Username).Returns("day1-tester");
        mock.SetupGet(x => x.UserId).Returns(Guid.NewGuid());
        return mock.Object;
    }

    [Fact]
    public async Task CalculatePayroll_ShouldNotPayScheduledUncompletedSessions_AndShouldPayTaAndMentor()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "Moli" };
        var cscaBu = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "CSCA" };
        var ta = new Employee
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            EmployeeCode = "TA-01",
            FullName = "Trợ giảng A",
            EmploymentType = EmploymentType.PART_TIME,
            PartTimeCalculationMethod = PartTimeCalculationMethod.HOURLY,
            PartTimeUnitRate = 120_000m,
            Status = "Active"
        };
        var mentor = new Employee
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            EmployeeCode = "MN-01",
            FullName = "Mentor B",
            EmploymentType = EmploymentType.PART_TIME,
            PartTimeCalculationMethod = PartTimeCalculationMethod.HOURLY,
            PartTimeUnitRate = 100_000m,
            Status = "Active"
        };
        var cls = new CscaClass
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            CourseId = Guid.NewGuid(),
            Code = "CSCA-DAY1",
            Name = "Lớp chuẩn Day 1",
            Status = "Active"
        };
        db.AddRange(company, cscaBu, ta, mentor, cls);
        db.CscaClassStaffs.AddRange(
            new CscaClassStaff { ClassId = cls.Id, EmployeeId = ta.Id, RoleInClass = "TeachingAssistant", CompensationRate = 0m },
            new CscaClassStaff { ClassId = cls.Id, EmployeeId = mentor.Id, RoleInClass = "Mentor", CompensationRate = 150_000m });
        db.CscaLessonSessions.AddRange(
            new CscaLessonSession
            {
                ClassId = cls.Id, LessonDate = new DateOnly(2026, 9, 5),
                StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(21, 0, 0),
                Status = "Completed", IsPayable = true
            },
            new CscaLessonSession
            {
                ClassId = cls.Id, LessonDate = new DateOnly(2026, 9, 12),
                StartTime = new TimeSpan(19, 0, 0), EndTime = new TimeSpan(21, 0, 0),
                Status = "Scheduled", IsPayable = true
            });
        await db.SaveChangesAsync();

        var service = new PayrollService(db, MockUser(company.Id, cscaBu.Id), NullLogger<PayrollService>.Instance);
        var period = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương T09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), cscaBu.Id),
            default);
        var calc = await service.CalculatePayrollAsync(period.Value!.Id, default);

        calc.Succeeded.Should().BeTrue();
        var taSlip = calc.Value!.Payslips.Single(p => p.EmployeeId == ta.Id);
        taSlip.ActualWorkHours.Should().Be(2m, "only the Completed 2h session is payable; Scheduled is excluded");
        taSlip.NetSalary.Should().Be(240_000m);

        var mentorSlip = calc.Value.Payslips.Single(p => p.EmployeeId == mentor.Id);
        mentorSlip.WorkEarnings.Should().Be(300_000m, "custom class CompensationRate (150k x 2h) applies to Mentor");
        mentorSlip.NetSalary.Should().Be(300_000m);
    }

    [Fact]
    public async Task EndToEnd_LmsSnapshotToPayroll_DeductsAdvance_PreventsDoublePay_AndPushesPaidToLms()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "Moli" };
        var cscaBu = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "CSCA" };
        var hqBu = new BusinessUnit { CompanyId = company.Id, Code = "HQ", Name = "HQ" };
        var teacher = new Employee
        {
            CompanyId = company.Id,
            BusinessUnitId = hqBu.Id, // Cross-BU teacher!
            EmployeeCode = "GV-CROSS",
            FullName = "Giáo viên Cross-BU",
            Email = "gv.cross@moli.vn",
            EmploymentType = EmploymentType.PART_TIME,
            PartTimeCalculationMethod = PartTimeCalculationMethod.HOURLY,
            PartTimeUnitRate = 200_000m,
            Status = "Active"
        };
        var cls = new CscaClass
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            CourseId = Guid.NewGuid(),
            Code = "CSCA-LMS-01",
            Name = "Lớp nối LMS",
            Status = "Active"
        };
        db.AddRange(company, cscaBu, hqBu, teacher, cls);
        db.CscaClassStaffs.Add(new CscaClassStaff { ClassId = cls.Id, EmployeeId = teacher.Id, RoleInClass = "Teacher" });
        // Local completed session ALSO exists for the same class — must NOT be double-counted with LMS snapshot!
        db.CscaLessonSessions.Add(new CscaLessonSession
        {
            ClassId = cls.Id, LessonDate = new DateOnly(2026, 9, 10),
            StartTime = new TimeSpan(18, 0, 0), EndTime = new TimeSpan(20, 0, 0),
            Status = "Completed", IsPayable = true
        });
        // Authoritative LMS snapshot with hyphenated "D" GUID
        var snapshot = new CscaTeacherCostSnapshot
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            EmployeeId = teacher.Id,
            SourceSystem = "CSCA_COURSE",
            SourceKey = "77:88:99",
            TeacherSourceId = teacher.Id.ToString("N"),
            TeacherLmsId = 77,
            TeacherName = teacher.FullName,
            ClassSourceId = cls.Id.ToString("D"),
            ClassLmsId = 88,
            ClassTitle = cls.Name,
            SessionSourceId = "sess-99",
            SessionLmsId = 99,
            StartTime = new DateTime(2026, 9, 10, 11, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 9, 10, 13, 0, 0, DateTimeKind.Utc),
            Status = "completed",
            IsCompleted = true,
            DurationHours = 2m,
            EffectiveHourlyRate = 500_000m,
            AccruedAmount = 1_000_000m,
            PaymentStatus = "pending",
            IsCurrent = true
        };
        db.CscaTeacherCostSnapshots.Add(snapshot);
        // Open teacher advance of 300k
        db.TeacherAdvances.Add(new TeacherAdvance
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            EmployeeId = teacher.Id,
            Amount = 300_000m,
            IssuedAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            Description = "Ứng lương giữa tháng",
            IdempotencyKey = "adv-cross-1"
        });
        await db.SaveChangesAsync();

        var lmsClient = new Mock<ICscaCourseLmsClient>();
        lmsClient.Setup(x => x.UpdateTeacherSettlementAsync(
                It.Is<LmsTeacherSettlementUpdate>(u => u.TeacherLmsId == 77 && u.PaymentStatus == "paid"),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var docRegistry = new BusinessDocumentRegistry(db);
        var financePosting = new FinancePostingService(db, docRegistry);
        var payroll = new PayrollService(db, MockUser(company.Id, cscaBu.Id), NullLogger<PayrollService>.Instance,
            documentRegistry: docRegistry, financePostingService: financePosting, lmsClient: lmsClient.Object);

        var period = await payroll.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương CSCA T09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), cscaBu.Id),
            default);
        var calc = await payroll.CalculatePayrollAsync(period.Value!.Id, default);
        calc.Succeeded.Should().BeTrue();
        var slip = calc.Value!.Payslips.Single(p => p.EmployeeId == teacher.Id);
        slip.WorkEarnings.Should().Be(1_000_000m, "LMS snapshot is authoritative without double-counting local session");
        slip.TotalIncome.Should().Be(1_000_000m);
        slip.TotalDeductions.Should().Be(300_000m, "open TeacherAdvance is automatically offset");
        slip.NetSalary.Should().Be(700_000m);

        (await payroll.SubmitForReviewAsync(period.Value.Id, new SubmitPayrollForReviewRequest(null), default)).Succeeded.Should().BeTrue();
        (await payroll.ApprovePayrollAsync(period.Value.Id, new ApprovePayrollRequest(null), default)).Succeeded.Should().BeTrue();

        // Finance Workspace must refuse double-paying while period is Approved!
        var financeWorkspace = new FinanceWorkspaceService(db, MockUser(company.Id, cscaBu.Id), lmsClient: lmsClient.Object);
        var duplicatePay = await financeWorkspace.PayTeacherAsync(
            new PayTeacherRequest(teacher.Id, 700_000m, IdempotencyKey: "dup-pay-1"), default);
        duplicatePay.Succeeded.Should().BeFalse();

        // Mark Payroll as Paid -> posts 1 FinanceTransaction, settles snapshot + advance, pushes 'paid' to LMS
        var paid = await payroll.MarkAsPaidAsync(period.Value.Id, new MarkPayrollPaidRequest(null), default);
        paid.Succeeded.Should().BeTrue();
        (await db.FinanceTransactions.CountAsync()).Should().Be(1);
        (await db.FinanceTransactions.SingleAsync()).Amount.Should().Be(700_000m);
        (await db.CscaTeacherCostSnapshots.SingleAsync()).PaymentStatus.Should().Be("paid");
        (await db.TeacherAdvances.SingleAsync()).Status.Should().Be("Settled");
        lmsClient.Verify(x => x.UpdateTeacherSettlementAsync(
            It.Is<LmsTeacherSettlementUpdate>(u => u.TeacherLmsId == 77 && u.PaymentStatus == "paid"),
            It.IsAny<LmsOutboundRequestContext>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Finance Workspace now sees 0 unpaid earned!
        var workspaceAfter = await financeWorkspace.GetTeacherCostsAsync(default);
        workspaceAfter.Value!.Single(t => t.EmployeeId == teacher.Id).UnpaidEarned.Should().Be(0m);
    }

    [Fact]
    public async Task ClassFinancialSummary_AndSync_ShouldMatchHyphenatedGuid_AndMapEmployeeByCodeOrEmail()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "Moli" };
        var cscaBu = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "CSCA" };
        var course = new Course { CompanyId = company.Id, Title = "CSCA Pro", CourseSourceId = "csca-pro" };
        var cls = new CscaClass
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            Course = course,
            Code = "CSCA-GUID-D",
            Name = "Lớp GUID chuẩn D",
            TuitionFee = 2_000_000m
        };
        var emp = new Employee
        {
            CompanyId = company.Id,
            BusinessUnitId = cscaBu.Id,
            EmployeeCode = "MOLY-GV99",
            FullName = "Cô Lan",
            Email = "lan@moli.vn",
            Status = "Active"
        };
        db.AddRange(company, cscaBu, course, cls, emp);
        await db.SaveChangesAsync();

        var lmsClient = new Mock<ICscaCourseLmsClient>();
        lmsClient.Setup(x => x.QueryTeacherCostsAsync(It.IsAny<LmsTeacherCostQuery>(), It.IsAny<LmsOutboundRequestContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LmsTeacherCostReport(
                "CSCA_COURSE",
                DateTime.UtcNow,
                true,
                [
                    new LmsTeacherCostItem(
                        TeacherSourceId: "MOLY-GV99", // EmployeeCode instead of GUID!
                        TeacherLmsId: 10,
                        TeacherName: "Cô Lan",
                        TeacherUsername: "lan@moli.vn",
                        CourseSourceId: "csca-pro",
                        CourseTitle: "CSCA Pro",
                        ClassSourceId: cls.Id.ToString("D"), // Hyphenated GUID!
                        ClassLmsId: 20,
                        ClassTitle: cls.Name,
                        TeachingRole: "lead",
                        SessionSourceId: "s-1",
                        SessionLmsId: 30,
                        SessionTitle: "Buổi 1",
                        StartTime: new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
                        EndTime: new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                        Status: "completed",
                        IsCompleted: true,
                        DurationHours: 2m,
                        EffectiveHourlyRate: 400_000m,
                        AccruedAmount: 800_000m,
                        ProjectedAmount: 800_000m,
                        BonusAmount: 0m,
                        DeductionAmount: 0m,
                        PaymentStatus: "pending",
                        PaidAt: null,
                        SourceUpdatedAt: DateTime.UtcNow),
                    new LmsTeacherCostItem(
                        TeacherSourceId: "UNKNOWN-ID",
                        TeacherLmsId: 11,
                        TeacherName: "GV Chưa Map",
                        TeacherUsername: "ghost@moli.vn",
                        CourseSourceId: "csca-pro",
                        CourseTitle: "CSCA Pro",
                        ClassSourceId: cls.Id.ToString("D"),
                        ClassLmsId: 20,
                        ClassTitle: cls.Name,
                        TeachingRole: "assistant",
                        SessionSourceId: "s-2",
                        SessionLmsId: 31,
                        SessionTitle: "Buổi 1 TA",
                        StartTime: new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
                        EndTime: new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
                        Status: "completed",
                        IsCompleted: true,
                        DurationHours: 2m,
                        EffectiveHourlyRate: 150_000m,
                        AccruedAmount: 300_000m,
                        ProjectedAmount: 300_000m,
                        BonusAmount: 0m,
                        DeductionAmount: 0m,
                        PaymentStatus: "pending",
                        PaidAt: null,
                        SourceUpdatedAt: DateTime.UtcNow)
                ]));

        var snapshotService = new CscaTeacherCostSnapshotService(db, MockUser(company.Id, cscaBu.Id), lmsClient.Object, NullLogger<CscaTeacherCostSnapshotService>.Instance);
        var synced = await snapshotService.SyncAsync(new SyncCscaTeacherCostsRequest(), default);
        synced.Succeeded.Should().BeTrue();
        synced.Value!.UnmappedTeacherCount.Should().Be(1);
        synced.Value.UnmappedTeachers.Should().Contain(x => x.Contains("GV Chưa Map"));

        var mappedSnapshot = await db.CscaTeacherCostSnapshots.FirstAsync(x => x.TeacherLmsId == 10);
        mappedSnapshot.EmployeeId.Should().Be(emp.Id);

        // Class financial summary MUST match hyphenated "D" GUID snapshots instead of returning 0!
        var cscaService = new CscaService(db, MockUser(company.Id, cscaBu.Id), NullLogger<CscaService>.Instance, integrationDb: db);
        var summary = await cscaService.GetClassFinancialSummaryAsync(cls.Id, default);
        summary.Succeeded.Should().BeTrue();
        summary.Value!.TotalStaffExpense.Should().Be(1_100_000m);
    }

    [Fact]
    public async Task PayTeacherAsync_WithNullBuSnapshotUnderScopedUnit_ShouldCreateAllocations()
    {
        await using var db = CreateDb();
        var company = new Company { Code = "MOLI", Name = "Moli" };
        var cscaBu = new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "CSCA" };
        var emp = new Employee { CompanyId = company.Id, BusinessUnitId = cscaBu.Id, EmployeeCode = "GV-NULL-BU", FullName = "GV Null BU" };
        var snapshot = new CscaTeacherCostSnapshot
        {
            CompanyId = company.Id,
            BusinessUnitId = null, // Null BU!
            EmployeeId = emp.Id,
            SourceSystem = "CSCA_COURSE",
            SourceKey = "1:2:3",
            TeacherLmsId = 1,
            TeacherName = emp.FullName,
            ClassSourceId = "cls-1",
            SessionSourceId = "s-1",
            StartTime = DateTime.UtcNow.AddDays(-2),
            EndTime = DateTime.UtcNow.AddDays(-2).AddHours(2),
            Status = "completed",
            IsCompleted = true,
            DurationHours = 2m,
            AccruedAmount = 500_000m,
            PaymentStatus = "pending",
            IsCurrent = true
        };
        db.AddRange(company, cscaBu, emp, snapshot);
        await db.SaveChangesAsync();

        var finance = new FinanceWorkspaceService(db, MockUser(company.Id, cscaBu.Id));
        var pay = await finance.PayTeacherAsync(new PayTeacherRequest(emp.Id, 500_000m, IdempotencyKey: "null-bu-pay-1"), default);
        pay.Succeeded.Should().BeTrue();
        (await db.TeacherPaymentAllocations.CountAsync()).Should().Be(1);
        (await db.TeacherPaymentAllocations.SingleAsync()).Amount.Should().Be(500_000m);
    }
}
