using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.HrPayroll;

public class AttendanceServiceTests
{
    private async Task<ApplicationDbContext> CreateInMemoryDbWithEmployeesAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("AttendanceServiceTest_" + Guid.NewGuid())
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
            BaseSalary = 15000000m,
            Status = "Active"
        };

        var emp2 = new Employee
        {
            CompanyId = company.Id,
            DepartmentId = dept.Id,
            EmployeeCode = "EMP-002",
            FullName = "Trần Thị Hai",
            Email = "emp2@moli.local",
            BaseSalary = 18000000m,
            Status = "Active"
        };

        db.Employees.AddRange(emp1, emp2);
        await db.SaveChangesAsync();

        return db;
    }

    [Fact]
    public async Task ImportAttendance_WithValidCsv_ShouldImportAllRecords()
    {
        // Arrange
        using var db = await CreateInMemoryDbWithEmployeesAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new AttendanceService(db, currentUser, NullLogger<AttendanceService>.Instance);

        var csvContent = @"Mã NV,Ngày,Giờ Vào,Giờ Ra,Giờ Công,Trạng Thái
EMP-001,2026-08-10,08:15,17:30,8.0,Present
EMP-002,2026-08-10,08:45,17:30,7.75,Late
EMP-001,2026-08-11,08:00,17:00,8.0,Present";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await service.ImportAttendanceAsync(stream, "attendance_august.csv", CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalRowsProcessed.Should().Be(3);
        result.Value.SuccessCount.Should().Be(3);
        result.Value.ErrorCount.Should().Be(0);
        result.Value.Errors.Should().BeEmpty();
        result.Value.ImportBatchId.Should().StartWith("ATT-");

        var dbRecords = await db.AttendanceRecords.ToListAsync();
        dbRecords.Should().HaveCount(3);
    }

    [Fact]
    public async Task ImportAttendance_WithTemplateXlsx_ShouldReadRealExcelRows()
    {
        using var db = await CreateInMemoryDbWithEmployeesAsync();
        var service = new AttendanceService(db, new CurrentUserService(null!), NullLogger<AttendanceService>.Instance);
        var template = await service.CreateImportTemplateAsync(CancellationToken.None);
        template.Content.Should().NotBeEmpty();

        using var stream = new MemoryStream();
        await stream.WriteAsync(template.Content);
        stream.Position = 0;
        using (var workbook = SpreadsheetDocument.Open(stream, true))
        {
            var workbookPart = workbook.WorkbookPart!;
            var sheet = workbookPart.Workbook.Sheets!.Elements<Sheet>().First();
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id!);
            var data = worksheetPart.Worksheet.GetFirstChild<SheetData>()!;
            data.Append(new Row(
                InlineCell("EMP-001"), InlineCell("2026-08-12"), InlineCell("08:00"),
                InlineCell("17:00"), InlineCell("8"), InlineCell("Present")) { RowIndex = 2U });
            worksheetPart.Worksheet.Save();
        }
        stream.Position = 0;

        var result = await service.ImportAttendanceAsync(stream, "attendance.xlsx", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.SuccessCount.Should().Be(1);
        result.Value.ErrorCount.Should().Be(0);
        (await db.AttendanceRecords.SingleAsync()).WorkHours.Should().Be(8m);
    }

    private static Cell InlineCell(string value) => new()
    {
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value))
    };

    [Fact]
    public async Task ImportAttendance_WithInvalidLines_ShouldReportRowByRowErrors()
    {
        // Arrange
        using var db = await CreateInMemoryDbWithEmployeesAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new AttendanceService(db, currentUser, NullLogger<AttendanceService>.Instance);

        // Line 1: Header
        // Line 2: Valid
        // Line 3: Non-existent employee (EMP-999)
        // Line 4: Invalid date format
        // Line 5: Invalid check-in time
        var csvContent = @"Mã NV,Ngày,Giờ Vào,Giờ Ra,Giờ Công,Trạng Thái
EMP-001,2026-08-10,08:15,17:30,8.0,Present
EMP-999,2026-08-10,08:30,17:30,8.0,Present
EMP-002,99/99/2026,08:30,17:30,8.0,Present
EMP-002,2026-08-11,INVALID_TIME,17:30,8.0,Present";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent));

        // Act
        var result = await service.ImportAttendanceAsync(stream, "invalid_attendance.csv", CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.SuccessCount.Should().Be(1);
        result.Value.ErrorCount.Should().Be(3);

        // Verify line-by-line error reports
        result.Value.Errors.Should().HaveCount(3);

        var err1 = result.Value.Errors.First(e => e.RowNumber == 3);
        err1.EmployeeCode.Should().Be("EMP-999");
        err1.ErrorMessage.Should().Contain("không tồn tại");

        var err2 = result.Value.Errors.First(e => e.RowNumber == 4);
        err2.DateString.Should().Be("99/99/2026");
        err2.ErrorMessage.Should().Contain("không hợp lệ");

        var err3 = result.Value.Errors.First(e => e.RowNumber == 5);
        err3.ErrorMessage.Should().Contain("giờ vào");
    }

    [Fact]
    public async Task RecordAttendance_SingleRecord_ShouldUpsertSuccessfully()
    {
        // Arrange
        using var db = await CreateInMemoryDbWithEmployeesAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new AttendanceService(db, currentUser, NullLogger<AttendanceService>.Instance);
        var emp = await db.Employees.FirstAsync();

        var date = new DateOnly(2026, 8, 15);
        var req1 = new CreateAttendanceRecordRequest(emp.Id, date, new TimeOnly(8, 20), new TimeOnly(17, 30), 8.0m, "Present");
        var req2 = new CreateAttendanceRecordRequest(emp.Id, date, new TimeOnly(8, 45), new TimeOnly(17, 30), 7.75m, "Late");

        // Act - 1st insert
        var res1 = await service.RecordAttendanceAsync(req1, CancellationToken.None);
        res1.Succeeded.Should().BeTrue();

        // Act - 2nd update same employee and date
        var res2 = await service.RecordAttendanceAsync(req2, CancellationToken.None);
        res2.Succeeded.Should().BeTrue();

        // Assert
        var records = await db.AttendanceRecords.Where(a => a.EmployeeId == emp.Id && a.Date == date).ToListAsync();
        records.Should().ContainSingle();
        records[0].Status.Should().Be("Late");
        records[0].WorkHours.Should().Be(7.75m);
    }

    [Fact]
    public async Task GetAttendance_WithFashionSegment_ShouldExcludeTechnologyEducationRecords()
    {
        using var db = await CreateInMemoryDbWithEmployeesAsync();
        var company = await db.Companies.FirstAsync();
        var employees = await db.Employees.OrderBy(e => e.EmployeeCode).ToListAsync();
        var edtech = new BusinessUnit { CompanyId = company.Id, Code = "EDTECH", Name = "EdTech" };
        var fashion = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "Fashion" };
        db.BusinessUnits.AddRange(edtech, fashion);
        employees[0].BusinessUnitId = edtech.Id;
        employees[1].BusinessUnitId = fashion.Id;
        db.AttendanceRecords.AddRange(
            new AttendanceRecord
            {
                CompanyId = company.Id, BusinessUnitId = edtech.Id, EmployeeId = employees[0].Id,
                Date = new DateOnly(2026, 8, 1), WorkHours = 8, Status = "Present"
            },
            new AttendanceRecord
            {
                CompanyId = company.Id, BusinessUnitId = fashion.Id, EmployeeId = employees[1].Id,
                Date = new DateOnly(2026, 8, 1), WorkHours = 8, Status = "Present"
            });
        await db.SaveChangesAsync();

        var service = new AttendanceService(db, new CurrentUserService(null!), NullLogger<AttendanceService>.Instance);
        var result = await service.GetAttendanceRecordsAsync(
            null, null, null, null, null, 1, 20, CancellationToken.None,
            businessSegment: "FASHION");

        result.Items.Should().ContainSingle(r => r.EmployeeCode == "EMP-002");
        result.Items.Should().NotContain(r => r.EmployeeCode == "EMP-001");
    }
}
