using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Compression;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.HrPayroll;

public class PayrollServiceTests
{
    private async Task<ApplicationDbContext> CreateInMemoryDbWithSeedDataAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("PayrollServiceTest_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);

        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);

        var dept = new Department { CompanyId = company.Id, Code = "TECH", Name = "Phòng Công Nghệ" };
        db.Departments.Add(dept);

        var emp1 = new Employee
        {
            CompanyId = company.Id,
            DepartmentId = dept.Id,
            EmployeeCode = "EMP-001",
            FullName = "Nguyễn Văn Một",
            Email = "emp1@moli.local",
            BaseSalary = 22000000m,
            Status = "Active"
        };

        var emp2 = new Employee
        {
            CompanyId = company.Id,
            DepartmentId = dept.Id,
            EmployeeCode = "EMP-002",
            FullName = "Trần Thị Hai",
            Email = "emp2@moli.local",
            BaseSalary = 11000000m,
            Status = "Active"
        };

        db.Employees.AddRange(emp1, emp2);

        // Seed Policy Version
        db.PayrollPolicyVersions.Add(new PayrollPolicyVersion
        {
            CompanyId = company.Id,
            VersionNumber = 1,
            EffectiveDate = new DateOnly(2026, 1, 1),
            ConfigJson = "{\"InsuranceRate\":0.105}",
            IsActive = true
        });

        // Seed 10 days of attendance (80 hours = 10 work days) for emp1
        for (int i = 1; i <= 10; i++)
        {
            db.AttendanceRecords.Add(new AttendanceRecord
            {
                CompanyId = company.Id,
                EmployeeId = emp1.Id,
                Date = new DateOnly(2026, 8, i),
                WorkHours = 8.0m,
                Status = "Present"
            });
        }

        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task CreatePayrollPeriod_WithValidData_ShouldSucceed()
    {
        // Arrange
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new PayrollService(db, currentUser, NullLogger<PayrollService>.Instance);

        var request = new CreatePayrollPeriodRequest(
            "Kỳ Lương Tháng 09/2026",
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 30));

        // Act
        var result = await service.CreatePayrollPeriodAsync(request, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Kỳ Lương Tháng 09/2026");
        result.Value.Status.Should().Be(PayrollStatus.Draft);
    }

    [Fact]
    public async Task CalculatePayroll_WithAttendanceAndAdjustments_ShouldCalculateCorrectGrossAndNet()
    {
        // Arrange
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new PayrollService(db, currentUser, NullLogger<PayrollService>.Instance);

        var periodRes = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Kỳ Lương Tháng 08/2026", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            CancellationToken.None);
        var periodId = periodRes.Value!.Id;

        var emp1 = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-001");
        var emp2 = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-002");
        // A recorded absence is still valid attendance data. It must result in
        // zero salary, rather than incorrectly falling back to 22 standard days.
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            CompanyId = emp2.CompanyId,
            EmployeeId = emp2.Id,
            Date = new DateOnly(2026, 8, 1),
            WorkHours = 0,
            Status = "Absent"
        });
        await db.SaveChangesAsync();

        // Add Bonus 2,000,000 and Deduction 500,000 for emp1
        await service.AddAdjustmentAsync(periodId, new CreatePayrollAdjustmentRequest(emp1.Id, "Bonus", 2000000m, "Thưởng dự án"), CancellationToken.None);
        await service.AddAdjustmentAsync(periodId, new CreatePayrollAdjustmentRequest(emp1.Id, "Deduction", 500000m, "Phạt đi muộn"), CancellationToken.None);

        // Act
        var calcResult = await service.CalculatePayrollAsync(periodId, CancellationToken.None);

        // Assert
        calcResult.Succeeded.Should().BeTrue();
        calcResult.Value.Should().NotBeNull();
        calcResult.Value!.TotalEmployeesProcessed.Should().Be(2);

        // Verify emp1: BaseSalary = 22,000,000. 10 days attendance / 22 days standard = 10,000,000 prorated base.
        // Gross = 10,000,000 + 2,000,000 (Bonus) = 12,000,000.
        // Net = 12,000,000 - 500,000 (Deduction) = 11,500,000.
        var slipEmp1 = calcResult.Value.Payslips.First(ps => ps.EmployeeId == emp1.Id);
        slipEmp1.ActualWorkDays.Should().Be(10.0m);
        slipEmp1.GrossSalary.Should().Be(12000000m);
        slipEmp1.Deductions.Should().Be(500000m);
        slipEmp1.NetSalary.Should().Be(11500000m);
        slipEmp1.Status.Should().Be(PayrollStatus.Calculated);

        // Verify period status updated in db
        var dbPeriod = await db.PayrollPeriods.FindAsync(periodId);
        dbPeriod!.Status.Should().Be(PayrollStatus.Calculated);
        dbPeriod.TotalNetAmount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CalculatePayroll_WhenFullTimeEmployeeHasNoAttendance_ShouldFailWithoutCreatingPayslips()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var service = new PayrollService(db, new CurrentUserService(null!), NullLogger<PayrollService>.Instance);
        var period = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương thiếu công", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            CancellationToken.None);

        var result = await service.CalculatePayrollAsync(period.Value!.Id, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("EMP-002") && error.Contains("chấm công"));
        (await db.Payslips.CountAsync(item => item.PayrollPeriodId == period.Value.Id)).Should().Be(0);
    }

    [Fact]
    public async Task CreateAndCancelDraftPayrollPeriod_ShouldKeepAuditTrailAndAllowReplacementPeriod()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var service = new PayrollService(db, new CurrentUserService(null!), NullLogger<PayrollService>.Instance);
        var request = new CreatePayrollPeriodRequest("Lương tháng 09", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var original = await service.CreatePayrollPeriodAsync(request, CancellationToken.None);

        var overlapping = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương tháng 09 lần hai", new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 30)),
            CancellationToken.None);
        overlapping.Succeeded.Should().BeFalse();
        overlapping.Errors.Should().Contain(error => error.Contains("chồng lấn"));

        var cancelled = await service.CancelPayrollPeriodAsync(
            original.Value!.Id,
            new CancelPayrollPeriodRequest("Tạo nhầm kỳ"),
            CancellationToken.None);

        cancelled.Succeeded.Should().BeTrue();
        cancelled.Value!.Status.Should().Be(PayrollStatus.Cancelled);
        (await db.PayrollPeriods.CountAsync()).Should().Be(1);
        (await db.AuditLogs.SingleAsync()).Action.Should().Be("Cancel");

        var replacement = await service.CreatePayrollPeriodAsync(request, CancellationToken.None);
        replacement.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ExportPayrollPeriodXlsx_ShouldCreateAuditedWorkbookWithSummaryAndRows()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var employee = await db.Employees.FirstAsync(item => item.EmployeeCode == "EMP-001");
        var period = new PayrollPeriod
        {
            CompanyId = employee.CompanyId,
            Name = "Bảng lương xuất Excel",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 31),
            Status = PayrollStatus.Calculated,
            TotalGrossAmount = 10_000_000m,
            TotalNetAmount = 9_000_000m
        };
        db.PayrollPeriods.Add(period);
        db.Payslips.Add(new Payslip
        {
            PayrollPeriodId = period.Id,
            EmployeeId = employee.Id,
            BaseSalary = 22_000_000m,
            StandardWorkDays = 22,
            ActualWorkDays = 10,
            ActualWorkHours = 80,
            GrossSalary = 10_000_000m,
            TotalIncome = 10_000_000m,
            Deductions = 1_000_000m,
            TotalDeductions = 1_000_000m,
            NetSalary = 9_000_000m,
            Status = PayrollStatus.Calculated
        });
        await db.SaveChangesAsync();

        var service = new PayrollExcelExportService(db, new CurrentUserService(null!));
        var result = await service.ExportPeriodXlsxAsync(period.Id, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.ContentType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        result.Value.FileName.Should().EndWith(".xlsx");
        result.Value.Content.Should().NotBeEmpty();
        (await db.AuditLogs.SingleAsync()).Action.Should().Be("Export");

        using var archive = new ZipArchive(new MemoryStream(result.Value.Content), ZipArchiveMode.Read);
        archive.GetEntry("[Content_Types].xml").Should().NotBeNull();
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
        sheet.Should().NotBeNull();
        using var reader = new StreamReader(sheet!.Open());
        var xml = await reader.ReadToEndAsync();
        xml.Should().Contain("BẢNG LƯƠNG");
        xml.Should().Contain("EMP-001");
        xml.Should().Contain("TỔNG THỰC LĨNH");
    }

    [Fact]
    public async Task GetActivePolicy_ShouldReturnLatestActivePolicy()
    {
        // Arrange
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new PayrollService(db, currentUser, NullLogger<PayrollService>.Instance);

        // Act
        var policyRes = await service.GetActivePolicyAsync(CancellationToken.None);

        // Assert
        policyRes.Succeeded.Should().BeTrue();
        policyRes.Value.Should().NotBeNull();
        policyRes.Value!.VersionNumber.Should().Be(1);
        policyRes.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CalculatePayroll_ForPartTimeHourly_ShouldSeparateKpiAllowanceBhytAndTotals()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var employee = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-002");
        employee.EmploymentType = EmploymentType.PART_TIME;
        employee.PartTimeCalculationMethod = PartTimeCalculationMethod.HOURLY;
        employee.PartTimeUnitRate = 100000m;
        db.AttendanceRecords.AddRange(
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 10), WorkHours = 4, Status = "Present"
            },
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 11), WorkHours = 6, Status = "Present"
            });
        await db.SaveChangesAsync();

        var service = new PayrollService(db, new CurrentUserService(null!), NullLogger<PayrollService>.Instance);
        var period = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương tháng 08", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            CancellationToken.None);
        await service.AddAdjustmentAsync(period.Value!.Id,
            new CreatePayrollAdjustmentRequest(employee.Id, "KPI_BONUS", 500000m, "KPI tháng"), CancellationToken.None);
        await service.AddAdjustmentAsync(period.Value.Id,
            new CreatePayrollAdjustmentRequest(employee.Id, "ALLOWANCE", 200000m, "Trợ cấp"), CancellationToken.None);
        await service.AddAdjustmentAsync(period.Value.Id,
            new CreatePayrollAdjustmentRequest(employee.Id, "BHYT", 100000m, "BHYT"), CancellationToken.None);

        var result = await service.CalculatePayrollAsync(period.Value.Id, CancellationToken.None);

        var payslip = result.Value!.Payslips.Single(p => p.EmployeeId == employee.Id);
        payslip.EmploymentType.Should().Be(EmploymentType.PART_TIME);
        payslip.ActualWorkHours.Should().Be(10);
        payslip.KpiBonus.Should().Be(500000m);
        payslip.HealthInsurance.Should().Be(100000m);
        payslip.TotalIncome.Should().Be(1700000m);
        payslip.TotalDeductions.Should().Be(100000m);
        payslip.NetSalary.Should().Be(1600000m);
        payslip.GrossSalary.Should().Be(payslip.TotalIncome);
        payslip.Deductions.Should().Be(payslip.TotalDeductions);
    }

    [Fact]
    public async Task CalculatePayroll_ForPartTimeHourly_ShouldIgnoreAbsentAndLeaveHours()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var employee = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-002");
        employee.EmploymentType = EmploymentType.PART_TIME;
        employee.PartTimeCalculationMethod = PartTimeCalculationMethod.HOURLY;
        employee.PartTimeUnitRate = 100000m;
        db.AttendanceRecords.AddRange(
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 12), WorkHours = 2.5m, Status = "Present"
            },
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 13), WorkHours = 1.5m, Status = "Late"
            },
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 14), WorkHours = 8m, Status = "Absent"
            },
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 15), WorkHours = 8m, Status = "Leave"
            });
        await db.SaveChangesAsync();

        var service = new PayrollService(db, new CurrentUserService(null!), NullLogger<PayrollService>.Instance);
        var period = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương GV tháng 08", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            CancellationToken.None);
        var result = await service.CalculatePayrollAsync(period.Value!.Id, CancellationToken.None);

        var payslip = result.Value!.Payslips.Single(p => p.EmployeeId == employee.Id);
        payslip.ActualWorkHours.Should().Be(4m);
        payslip.TotalIncome.Should().Be(400000m);
        payslip.NetSalary.Should().Be(400000m);
    }

    [Fact]
    public async Task CalculatePayroll_ForBusinessUnit_ShouldNotLeakEmployeesOrAttendanceFromOtherUnit()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var company = await db.Companies.FirstAsync();
        var edtech = new BusinessUnit { CompanyId = company.Id, Code = "EDTECH", Name = "EdTech" };
        var fashion = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "Fashion" };
        db.BusinessUnits.AddRange(edtech, fashion);

        var employeeEdtech = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-001");
        var employeeFashion = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-002");
        employeeEdtech.BusinessUnitId = edtech.Id;
        employeeFashion.BusinessUnitId = fashion.Id;
        foreach (var attendance in db.AttendanceRecords.Where(a => a.EmployeeId == employeeEdtech.Id))
        {
            attendance.BusinessUnitId = edtech.Id;
        }
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            CompanyId = company.Id, BusinessUnitId = fashion.Id, EmployeeId = employeeFashion.Id,
            Date = new DateOnly(2026, 8, 5), WorkHours = 8, Status = "Present"
        });
        await db.SaveChangesAsync();

        var service = new PayrollService(db, new CurrentUserService(null!), NullLogger<PayrollService>.Instance);
        var period = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest(
                "Lương EdTech tháng 08", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), edtech.Id),
            CancellationToken.None);

        var foreignAdjustment = await service.AddAdjustmentAsync(period.Value!.Id,
            new CreatePayrollAdjustmentRequest(employeeFashion.Id, "BONUS", 1000000m, "Sai mảng"),
            CancellationToken.None);
        var result = await service.CalculatePayrollAsync(period.Value.Id, CancellationToken.None);

        foreignAdjustment.Succeeded.Should().BeFalse();
        result.Value!.TotalEmployeesProcessed.Should().Be(1);
        result.Value.Payslips.Should().ContainSingle(p => p.EmployeeId == employeeEdtech.Id);
        result.Value.Payslips.Should().NotContain(p => p.EmployeeId == employeeFashion.Id);

        var technologyPeriods = await service.GetPayrollPeriodsAsync(
            2026, null, 1, 20, CancellationToken.None,
            businessSegment: "TECHNOLOGY_EDUCATION");
        var fashionPeriods = await service.GetPayrollPeriodsAsync(
            2026, null, 1, 20, CancellationToken.None,
            businessSegment: "FASHION");
        technologyPeriods.Items.Should().ContainSingle(p => p.Id == period.Value.Id);
        fashionPeriods.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculatePayroll_ForPartTimeByShift_ShouldUseWorkedShiftCount()
    {
        using var db = await CreateInMemoryDbWithSeedDataAsync();
        var employee = await db.Employees.FirstAsync(e => e.EmployeeCode == "EMP-002");
        employee.EmploymentType = EmploymentType.PART_TIME;
        employee.PartTimeCalculationMethod = PartTimeCalculationMethod.SHIFT;
        employee.PartTimeUnitRate = 350000m;
        db.AttendanceRecords.AddRange(
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 20), WorkHours = 4, Status = "Present"
            },
            new AttendanceRecord
            {
                CompanyId = employee.CompanyId, EmployeeId = employee.Id,
                Date = new DateOnly(2026, 8, 21), WorkHours = 7, Status = "Late"
            });
        await db.SaveChangesAsync();

        var service = new PayrollService(db, new CurrentUserService(null!), NullLogger<PayrollService>.Instance);
        var period = await service.CreatePayrollPeriodAsync(
            new CreatePayrollPeriodRequest("Lương theo ca", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            CancellationToken.None);
        var result = await service.CalculatePayrollAsync(period.Value!.Id, CancellationToken.None);

        var payslip = result.Value!.Payslips.Single(p => p.EmployeeId == employee.Id);
        payslip.PartTimeCalculationMethod.Should().Be(PartTimeCalculationMethod.SHIFT);
        payslip.ActualShifts.Should().Be(2);
        payslip.TotalIncome.Should().Be(700000m);
        payslip.NetSalary.Should().Be(700000m);
    }
}
