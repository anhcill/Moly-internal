using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.HrPayroll;

public class EmployeeServiceTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("EmployeeServiceTest_" + Guid.NewGuid())
            .Options;
        var db = new ApplicationDbContext(options);

        // Seed base company and department
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);

        var dept = new Department { CompanyId = company.Id, Code = "TECH", Name = "Phòng Công Nghệ" };
        db.Departments.Add(dept);

        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task CreateEmployee_WithValidData_ShouldSucceed()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var company = await db.Companies.FirstAsync();
        var dept = await db.Departments.FirstAsync();
        var currentUser = new CurrentUserService(null!);
        var service = new EmployeeService(db, currentUser, NullLogger<EmployeeService>.Instance);

        var request = new CreateEmployeeRequest(
            "EMP-TEST01",
            "Nguyễn Văn Test",
            "test@moli.local",
            "0911223344",
            "Backend Engineer",
            20000000m,
            dept.Id);

        // Act
        var result = await service.CreateEmployeeAsync(request, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.EmployeeCode.Should().Be("EMP-TEST01");
        result.Value.FullName.Should().Be("Nguyễn Văn Test");
        result.Value.BaseSalary.Should().Be(20000000m);
        result.Value.DepartmentName.Should().Be("Phòng Công Nghệ");
    }

    [Fact]
    public async Task CreateEmployee_WithDuplicateCode_ShouldFail()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EmployeeService(db, currentUser, NullLogger<EmployeeService>.Instance);

        var req1 = new CreateEmployeeRequest("EMP-DUP", "NV Một", "nv1@moli.local", null, "Dev", 10000000m);
        var req2 = new CreateEmployeeRequest("EMP-DUP", "NV Hai", "nv2@moli.local", null, "Dev", 12000000m);

        await service.CreateEmployeeAsync(req1, CancellationToken.None);

        // Act
        var result = await service.CreateEmployeeAsync(req2, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("đã tồn tại"));
    }

    [Fact]
    public async Task GetEmployees_WithSearch_ShouldReturnMatchedResults()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EmployeeService(db, currentUser, NullLogger<EmployeeService>.Instance);

        await service.CreateEmployeeAsync(new CreateEmployeeRequest("EMP-001", "Trần Thu Hà", "ha.tran@moli.local", null, "Designer", 15000000m), CancellationToken.None);
        await service.CreateEmployeeAsync(new CreateEmployeeRequest("EMP-002", "Hoàng Nam", "nam.hoang@moli.local", null, "DevOps", 22000000m), CancellationToken.None);

        // Act
        var searchResult = await service.GetEmployeesAsync("Thu Hà", null, null, 1, 20, CancellationToken.None);

        // Assert
        searchResult.TotalCount.Should().Be(1);
        searchResult.Items.Should().ContainSingle(e => e.EmployeeCode == "EMP-001");
    }

    [Fact]
    public async Task DeleteEmployee_ShouldSoftDelete()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EmployeeService(db, currentUser, NullLogger<EmployeeService>.Instance);

        var createRes = await service.CreateEmployeeAsync(new CreateEmployeeRequest("EMP-DEL", "NV Cần Xóa", "del@moli.local", null, "Staff", 10000000m), CancellationToken.None);
        var empId = createRes.Value!.Id;

        // Act
        var deleteRes = await service.DeleteEmployeeAsync(empId, CancellationToken.None);

        // Assert
        deleteRes.Succeeded.Should().BeTrue();
        var getRes = await service.GetEmployeeByIdAsync(empId, CancellationToken.None);
        getRes.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task CreatePartTimeEmployee_WithCvProfile_ShouldPersistCompensationAndCv()
    {
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EmployeeService(db, currentUser, NullLogger<EmployeeService>.Instance);

        var request = new CreateEmployeeRequest(
            "PT-001",
            "Lê Minh Part-time",
            "parttime@moli.local",
            null,
            "Trợ giảng",
            0,
            EmploymentType: EmploymentType.PART_TIME,
            PartTimeCalculationMethod: PartTimeCalculationMethod.HOURLY,
            PartTimeUnitRate: 120000m,
            CvUrlOrPath: "/cv/pt-001.pdf",
            ProfessionalSummary: "Trợ giảng công nghệ",
            Skills: "C#, giao tiếp",
            Experience: "2 năm trợ giảng");

        var result = await service.CreateEmployeeAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.EmploymentType.Should().Be(EmploymentType.PART_TIME);
        result.Value.EmploymentTypeNameVi.Should().Be("Bán thời gian");
        result.Value.PartTimeCalculationMethod.Should().Be(PartTimeCalculationMethod.HOURLY);
        result.Value.PartTimeUnitRate.Should().Be(120000m);
        result.Value.CvUrlOrPath.Should().Be("/cv/pt-001.pdf");
        result.Value.Skills.Should().Contain("C#");
    }

    [Fact]
    public async Task GetEmployees_WithTechnologyEducationSegment_ShouldExcludeFashion()
    {
        using var db = CreateInMemoryDb();
        var company = await db.Companies.FirstAsync();
        var edtech = new BusinessUnit { CompanyId = company.Id, Code = "EDTECH", Name = "Công nghệ - Giáo dục" };
        var fashion = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "Thời trang" };
        db.BusinessUnits.AddRange(edtech, fashion);
        db.Employees.AddRange(
            new Employee
            {
                CompanyId = company.Id, BusinessUnitId = edtech.Id, EmployeeCode = "EDU-001",
                FullName = "Nhân sự Giáo dục", Email = "edu@moli.local", Status = "Active"
            },
            new Employee
            {
                CompanyId = company.Id, BusinessUnitId = fashion.Id, EmployeeCode = "FAS-001",
                FullName = "Nhân sự Thời trang", Email = "fashion@moli.local", Status = "Active"
            });
        await db.SaveChangesAsync();

        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);
        var result = await service.GetEmployeesAsync(
            null, null, null, 1, 20, CancellationToken.None,
            businessSegment: "TECHNOLOGY_EDUCATION");

        result.Items.Should().ContainSingle(e => e.EmployeeCode == "EDU-001");
        result.Items.Single().BusinessUnitName.Should().Be("Công nghệ - Giáo dục");
        result.Items.Should().NotContain(e => e.EmployeeCode == "FAS-001");

        var detail = await service.GetEmployeeByIdAsync(result.Items.Single().Id, CancellationToken.None);
        detail.Value!.BusinessUnitName.Should().Be("Công nghệ - Giáo dục");
    }
}
