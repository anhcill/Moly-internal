using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.HrPayroll;

[Collection("Lms teacher takeover sync")]
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
    public async Task CreateAndUpdateEmployee_WithLocalCalendarDate_ShouldStoreUtcWithoutChangingTheDay()
    {
        using var db = CreateInMemoryDb();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);
        var joined = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Local);
        var created = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "EMP-DATE", "Nhân sự ngày vào làm", "date@moli.local", null, null, 0,
            JoinedDate: joined), CancellationToken.None);

        created.Succeeded.Should().BeTrue();
        var employee = await db.Employees.SingleAsync(x => x.Id == created.Value!.Id);
        employee.JoinedDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
        employee.JoinedDate.Value.Date.Should().Be(joined.Date);

        var changed = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Unspecified);
        var updated = await service.UpdateEmployeeAsync(employee.Id, new UpdateEmployeeRequest(
            employee.FullName, employee.Email, null, null, 0, JoinedDate: changed), CancellationToken.None);
        updated.Succeeded.Should().BeTrue();
        employee.JoinedDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
        employee.JoinedDate.Value.Date.Should().Be(changed.Date);
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
    public async Task CreateEmployee_WithDuplicateEmailInCompany_ShouldFailCaseInsensitively()
    {
        using var db = CreateInMemoryDb();
        var company = await db.Companies.SingleAsync();
        db.Employees.Add(new Employee
        {
            CompanyId = company.Id,
            EmployeeCode = "EMP-EMAIL-1",
            FullName = "Nhân viên một",
            Email = "teacher@example.com"
        });
        await db.SaveChangesAsync();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "EMP-EMAIL-2", "Nhân viên hai", " Teacher@Example.com ", null, null, 0), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("Email nhân viên"));
    }

    [Fact]
    public async Task CreateEmployee_WithEmailUsedByAnotherCompany_ShouldSucceed()
    {
        using var db = CreateInMemoryDb();
        var otherCompany = new Company { Code = "OTHER", Name = "Công ty khác" };
        db.AddRange(otherCompany, new Employee
        {
            CompanyId = otherCompany.Id,
            EmployeeCode = "OTHER-1",
            FullName = "Nhân viên công ty khác",
            Email = "shared@example.com"
        });
        await db.SaveChangesAsync();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "EMP-SHARED", "Nhân viên MOLI", " Shared@Example.com ", null, null, 0), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.Email.Should().Be("shared@example.com");
    }

    [Fact]
    public async Task UpdateEmployee_WithAnotherEmployeesEmail_ShouldFailCaseInsensitively()
    {
        using var db = CreateInMemoryDb();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);
        var first = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "EMP-UPD-1", "Nhân viên một", "one@example.com", null, null, 0), CancellationToken.None);
        var second = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "EMP-UPD-2", "Nhân viên hai", "two@example.com", null, null, 0), CancellationToken.None);

        var result = await service.UpdateEmployeeAsync(second.Value!.Id, new UpdateEmployeeRequest(
            second.Value.FullName, " ONE@EXAMPLE.COM ", null, null, 0), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        (await db.Employees.SingleAsync(employee => employee.Id == first.Value!.Id)).Email.Should().Be("one@example.com");
        (await db.Employees.SingleAsync(employee => employee.Id == second.Value.Id)).Email.Should().Be("two@example.com");
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
    public async Task UpdateLinkedTeacherIdentityAndStatus_ShouldQueueTeacherAndClassUpdatesWithNewLead()
    {
        using var db = CreateInMemoryDb();
        var setup = await SeedLinkedClassWithTwoTeachersAsync(db);
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.UpdateEmployeeAsync(setup.Lead.Id, new UpdateEmployeeRequest(
            "Giáo viên đã đổi", " Changed.Teacher@Example.com ", null, null, 0, Status: "Resigned"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var events = await ReadManagementEventsAsync(db);
        events.Select(item => item.EventType).Should().Contain("teacher.upserted");
        events.Select(item => item.EventType).Should().Contain("class.upserted");
        var teacherPayload = events
            .Where(item => item.EventType == "teacher.upserted")
            .Select(item => item.Payload)
            .Single(payload => payload.GetProperty("teacherSourceId").GetString() == setup.Lead.Id.ToString("N"));
        teacherPayload.GetProperty("fullName").GetString().Should().Be("Giáo viên đã đổi");
        teacherPayload.GetProperty("email").GetString().Should().Be("changed.teacher@example.com");
        teacherPayload.GetProperty("accountStatus").GetString().Should().Be("suspended");
        events.Single(item => item.EventType == "class.upserted").Payload
            .GetProperty("leadTeacherSourceId").GetString().Should().Be(setup.Replacement.Id.ToString("N"));
        events.Where(item => item.EventType == "class.teacher.assigned")
            .Should().Contain(item =>
                item.Payload.GetProperty("teacherSourceId").GetString() == setup.Lead.Id.ToString("N") &&
                item.Payload.GetProperty("status").GetString() == "revoked");
    }

    [Fact]
    public async Task UpdateLinkedTeacher_WhenDeletedEmployeeUsedSameEmail_ShouldQueueLegacyTeacherSourceId()
    {
        using var db = CreateInMemoryDb();
        var setup = await SeedLinkedClassWithTwoTeachersAsync(db);
        var legacyTeacher = new Employee
        {
            CompanyId = setup.Lead.CompanyId,
            EmployeeCode = "TEACHER-LEGACY",
            FullName = "Phạm Khánh Ly cũ",
            Email = " LEAD@EXAMPLE.COM ",
            Status = "Active",
            IsDeleted = true
        };
        db.Employees.Add(legacyTeacher);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.UpdateEmployeeAsync(setup.Lead.Id, new UpdateEmployeeRequest(
            "Phạm Khánh Ly mới", setup.Lead.Email, null, null, 0, Status: "Active"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var payload = (await ReadManagementEventsAsync(db))
            .Where(item => item.EventType == "teacher.upserted")
            .Select(item => item.Payload)
            .Single(item => item.GetProperty("teacherSourceId").GetString() == setup.Lead.Id.ToString("N"));
        payload.GetProperty("legacyTeacherSourceIds").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Equal(legacyTeacher.Id.ToString("N"));
    }

    [Fact]
    public async Task DeleteLinkedTeacher_ShouldQueueSuspensionAndClassRosterUpdate()
    {
        using var db = CreateInMemoryDb();
        var setup = await SeedLinkedClassWithTwoTeachersAsync(db);
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.DeleteEmployeeAsync(setup.Lead.Id, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var events = await ReadManagementEventsAsync(db);
        var deletedTeacher = events
            .Where(item => item.EventType == "teacher.upserted")
            .Select(item => item.Payload)
            .Single(payload => payload.GetProperty("teacherSourceId").GetString() == setup.Lead.Id.ToString("N"));
        deletedTeacher.GetProperty("accountStatus").GetString().Should().Be("suspended");
        events.Single(item => item.EventType == "class.upserted").Payload
            .GetProperty("leadTeacherSourceId").GetString().Should().Be(setup.Replacement.Id.ToString("N"));
    }

    [Fact]
    public async Task ResignLinkedCoTeacher_ShouldRevokeCoTeacherWithoutChangingLead()
    {
        using var db = CreateInMemoryDb();
        var setup = await SeedLinkedClassWithTwoTeachersAsync(db);
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.UpdateEmployeeAsync(setup.Replacement.Id, new UpdateEmployeeRequest(
            setup.Replacement.FullName, setup.Replacement.Email, null, null, 0, Status: "Resigned"),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var events = await ReadManagementEventsAsync(db);
        events.Where(item => item.EventType == "class.teacher.assigned")
            .Should().Contain(item =>
                item.Payload.GetProperty("teacherSourceId").GetString() == setup.Replacement.Id.ToString("N") &&
                item.Payload.GetProperty("teachingRole").GetString() == "co_teacher" &&
                item.Payload.GetProperty("status").GetString() == "revoked");
        events.Single(item => item.EventType == "class.upserted").Payload
            .GetProperty("leadTeacherSourceId").GetString().Should().Be(setup.Lead.Id.ToString("N"));
    }

    [Fact]
    public async Task ResignLastTeacher_FromLinkedClass_ShouldRevokeTeacherAndClearLead()
    {
        using var db = CreateInMemoryDb();
        var company = await db.Companies.SingleAsync();
        var course = new Course
        {
            CompanyId = company.Id, CourseSourceId = "last-teacher-sync", Title = "Khóa một giáo viên"
        };
        var cls = new CscaClass
        {
            CompanyId = company.Id, Course = course, Code = "CLS-LAST-TEACHER", Name = "Lớp một giáo viên"
        };
        var teacher = new Employee
        {
            CompanyId = company.Id, EmployeeCode = "LAST-TEACHER", FullName = "Giáo viên duy nhất",
            Email = "last.teacher@example.com", Status = "Active"
        };
        cls.Staff.Add(new CscaClassStaff { Class = cls, Employee = teacher, RoleInClass = "Teacher" });
        db.AddRange(course, cls, teacher, new LmsCourseLink
        {
            CompanyId = company.Id, Course = course, ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.UpdateEmployeeAsync(teacher.Id, new UpdateEmployeeRequest(
            teacher.FullName, teacher.Email, null, null, 0, Status: "Resigned"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var events = await ReadManagementEventsAsync(db);
        events.Where(item => item.EventType == "class.teacher.assigned")
            .Should().ContainSingle(item =>
                item.Payload.GetProperty("teacherSourceId").GetString() == teacher.Id.ToString("N") &&
                item.Payload.GetProperty("teachingRole").GetString() == "lead" &&
                item.Payload.GetProperty("status").GetString() == "revoked");
        events.Single(item => item.EventType == "class.upserted").Payload
            .GetProperty("leadTeacherSourceId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task ResignLinkedTeachingAssistant_ShouldRevokeAssistantAssignment()
    {
        using var db = CreateInMemoryDb();
        var company = await db.Companies.SingleAsync();
        var course = new Course { CompanyId = company.Id, CourseSourceId = "assistant-sync", Title = "Khóa trợ giảng" };
        var cls = new CscaClass
        {
            CompanyId = company.Id, Course = course, Code = "CLS-ASSISTANT", Name = "Lớp trợ giảng"
        };
        var lead = new Employee
        {
            CompanyId = company.Id, EmployeeCode = "LEAD-A", FullName = "Giáo viên chính",
            Email = "lead.a@example.com", Status = "Active"
        };
        var assistant = new Employee
        {
            CompanyId = company.Id, EmployeeCode = "ASSISTANT-A", FullName = "Trợ giảng",
            Email = "assistant.a@example.com", Status = "Active"
        };
        cls.Staff.Add(new CscaClassStaff { Class = cls, Employee = lead, RoleInClass = "Teacher" });
        cls.Staff.Add(new CscaClassStaff { Class = cls, Employee = assistant, RoleInClass = "TeachingAssistant" });
        db.AddRange(course, cls, lead, assistant, new LmsCourseLink
        {
            CompanyId = company.Id, Course = course, ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);

        var result = await service.UpdateEmployeeAsync(assistant.Id, new UpdateEmployeeRequest(
            assistant.FullName, assistant.Email, null, null, 0, Status: "Resigned"), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        var events = await ReadManagementEventsAsync(db);
        events.Where(item => item.EventType == "class.teacher.assigned")
            .Should().Contain(item =>
                item.Payload.GetProperty("teacherSourceId").GetString() == assistant.Id.ToString("N") &&
                item.Payload.GetProperty("teachingRole").GetString() == "assistant" &&
                item.Payload.GetProperty("status").GetString() == "revoked");
        events.Single(item => item.EventType == "class.upserted").Payload
            .GetProperty("leadTeacherSourceId").GetString().Should().Be(lead.Id.ToString("N"));
    }

    [Fact]
    public async Task UpdateEmployeeWithoutLinkedTeacherClass_ShouldNotQueueLmsEvents()
    {
        using var db = CreateInMemoryDb();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);
        var created = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "EMP-NO-LMS", "Nhân viên thường", "normal@example.com", null, null, 0), CancellationToken.None);

        var result = await service.UpdateEmployeeAsync(created.Value!.Id, new UpdateEmployeeRequest(
            "Nhân viên đổi tên", created.Value.Email, null, null, 0), CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        (await db.IntegrationOutboxes.CountAsync()).Should().Be(0);
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
    public async Task CreateOutputPaidCollaborator_WithBankDetails_ShouldKeepAccountInRestrictedLookup()
    {
        using var db = CreateInMemoryDb();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);
        var request = new CreateEmployeeRequest(
            "CTV-001", "Nguyễn Thị Sale", "sale@moli.local", null, "Cộng tác viên sale", 0,
            EmploymentType: EmploymentType.PART_TIME,
            PartTimeCalculationMethod: PartTimeCalculationMethod.OUTPUT,
            BankName: "Ngân hàng A",
            BankAccountNumber: "1234 5678 90",
            BankAccountHolder: "NGUYEN THI SALE");

        var created = await service.CreateEmployeeAsync(request, CancellationToken.None);

        created.Succeeded.Should().BeTrue();
        created.Value!.PartTimeCalculationMethodNameVi.Should().Be("Theo đầu việc");
        created.Value.PartTimeUnitRate.Should().BeNull();
        var payment = await service.GetEmployeePaymentDetailsAsync(created.Value.Id, CancellationToken.None);
        payment.Value!.BankAccountNumber.Should().Be("1234567890");
        payment.Value.BankAccountHolder.Should().Be("NGUYEN THI SALE");
    }

    [Fact]
    public async Task CreateEmployee_WithIncompleteBankDetails_ShouldFail()
    {
        using var db = CreateInMemoryDb();
        var service = new EmployeeService(db, new CurrentUserService(null!), NullLogger<EmployeeService>.Instance);
        var result = await service.CreateEmployeeAsync(new CreateEmployeeRequest(
            "CTV-002", "Thiếu ngân hàng", "bank@moli.local", null, null, 0,
            EmploymentType: EmploymentType.PART_TIME,
            PartTimeCalculationMethod: PartTimeCalculationMethod.OUTPUT,
            BankAccountNumber: "1234567890"), CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("ngân hàng"));
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

    private static async Task<(Employee Lead, Employee Replacement)> SeedLinkedClassWithTwoTeachersAsync(
        ApplicationDbContext db)
    {
        var company = await db.Companies.SingleAsync(company => company.Code == "MOLI");
        var course = new Course { CompanyId = company.Id, CourseSourceId = "employee-sync", Title = "Khóa đồng bộ" };
        var cls = new CscaClass
        {
            CompanyId = company.Id,
            Course = course,
            CourseId = course.Id,
            Code = "CLS-EMP-SYNC",
            Name = "Lớp đồng bộ giáo viên",
            Batch = "2026"
        };
        var lead = new Employee
        {
            CompanyId = company.Id,
            EmployeeCode = "TEACHER-LEAD",
            FullName = "Giáo viên chính",
            Email = "lead@example.com",
            Status = "Active"
        };
        var replacement = new Employee
        {
            CompanyId = company.Id,
            EmployeeCode = "TEACHER-REPLACEMENT",
            FullName = "Giáo viên thay thế",
            Email = "replacement@example.com",
            Status = "Active"
        };
        var leadAssignment = new CscaClassStaff
        {
            Class = cls,
            Employee = lead,
            RoleInClass = "Teacher",
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        var replacementAssignment = new CscaClassStaff
        {
            Class = cls,
            Employee = replacement,
            RoleInClass = "Teacher",
            CreatedAt = DateTime.UtcNow
        };
        cls.Staff.Add(leadAssignment);
        cls.Staff.Add(replacementAssignment);
        db.AddRange(course, cls, lead, replacement, leadAssignment, replacementAssignment, new LmsCourseLink
        {
            CompanyId = company.Id,
            Course = course,
            CourseId = course.Id,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var orderedTeachers = cls.Staff.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id).ToList();
        return (orderedTeachers[0].Employee, orderedTeachers[1].Employee);
    }

    private static async Task<List<(string EventType, JsonElement Payload)>> ReadManagementEventsAsync(
        ApplicationDbContext db)
    {
        var outboxes = await db.IntegrationOutboxes.OrderBy(item => item.CreatedAt).ToListAsync();
        return outboxes.Select(item =>
        {
            using var document = JsonDocument.Parse(item.PayloadJson);
            return (
                document.RootElement.GetProperty("eventType").GetString()!,
                document.RootElement.GetProperty("payload").Clone());
        }).ToList();
    }
}
