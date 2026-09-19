using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.CscaInterview;

public class CscaServiceTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("CscaServiceTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateClass_WithValidData_ShouldSucceed()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new CscaService(db, currentUser, NullLogger<CscaService>.Instance);

        var request = new CreateCscaClassRequest(
            "CSCA-2026-T01",
            "Lớp CSCA Test Đợt 1",
            "Đợt 1 - 2026",
            "T2 - T4 (18h30 - 20h30)",
            5000000m,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc));

        // Act
        var result = await service.CreateClassAsync(request, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Code.Should().Be("CSCA-2026-T01");
        result.Value.Name.Should().Be("Lớp CSCA Test Đợt 1");
        result.Value.TuitionFee.Should().Be(5000000m);
    }

    [Fact]
    public async Task EnrollStudent_AndCalculateProfit_ShouldUpdateRevenueAndProfit()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new CscaService(db, currentUser, NullLogger<CscaService>.Instance);

        // Step 1: Create class
        var classRes = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-K10", "Lớp CSCA K10", "Batch 1", "T2-T4", 4000000m),
            CancellationToken.None);
        var classId = classRes.Value!.Id;

        // Step 2: Enroll 2 students (1 paid full 4M, 1 paid 2M)
        await service.EnrollStudentAsync(classId, new EnrollStudentRequest(
            "Học Viên A", "a@test.com", "0900111222", 4000000m, PaymentStatus.Paid), CancellationToken.None);

        await service.EnrollStudentAsync(classId, new EnrollStudentRequest(
            "Học Viên B", "b@test.com", "0900333444", 2000000m, PaymentStatus.Partial), CancellationToken.None);

        // Step 3: Add an employee and assign staff (teacher compensation = 2.5M)
        var employee = new Employee
        {
            EmployeeCode = "EMP-TEST01",
            FullName = "Thầy Giáo Test",
            Email = "teacher@test.com",
            Position = "Giảng Viên"
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        await service.AssignStaffAsync(classId, new AssignStaffRequest(
            employee.Id, "Teacher", 2500000m, "Phụ trách chuyên môn"), CancellationToken.None);

        // Act: Get Financial Summary
        var finRes = await service.GetClassFinancialSummaryAsync(classId, CancellationToken.None);

        // Assert
        finRes.Succeeded.Should().BeTrue();
        finRes.Value.Should().NotBeNull();
        finRes.Value!.TotalStudents.Should().Be(2);
        finRes.Value.PaidStudents.Should().Be(1);
        finRes.Value.ExpectedRevenue.Should().Be(8000000m); // 2 * 4M
        finRes.Value.ActualRevenue.Should().Be(6000000m);   // 4M + 2M
        finRes.Value.TotalStaffExpense.Should().Be(2500000m);
        finRes.Value.NetProfit.Should().Be(3500000m);       // 6M - 2.5M
    }

    [Fact]
    public async Task EnrollStudent_WithSharedDataServices_ShouldCreatePartyEnrollmentDocumentAndIncomePosting()
    {
        using var db = CreateInMemoryDb();
        db.Companies.Add(new Company { Code = "MOLI", Name = "MOLI Test" });
        await db.SaveChangesAsync();
        var currentUser = new CurrentUserService(null!);
        var documents = new BusinessDocumentRegistry(db);
        var service = new CscaService(
            db,
            currentUser,
            NullLogger<CscaService>.Instance,
            new PartyResolver(db),
            documents,
            new FinancePostingService(db, documents, currentUser));

        var classResult = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-LINK", "Lớp liên kết dữ liệu", "Batch 1", "T2-T4", 4_000_000m),
            CancellationToken.None);

        var enrolled = await service.EnrollStudentAsync(
            classResult.Value!.Id,
            new EnrollStudentRequest("Học viên liên kết", "lienket@example.com", "0901 234 567", 4_000_000m, PaymentStatus.Paid),
            CancellationToken.None);

        enrolled.Succeeded.Should().BeTrue();
        var student = await db.CscaClassStudents.SingleAsync();
        student.PartyId.Should().NotBeNull();
        student.BusinessDocumentId.Should().NotBeNull();
        (await db.Parties.CountAsync()).Should().Be(1);
        (await db.BusinessDocuments.CountAsync(x => x.DocumentType == BusinessDocumentType.CscaEnrollment)).Should().Be(1);
        (await db.FinanceTransactions.CountAsync(x => x.ReferenceType == nameof(CscaClassStudent) && x.TransactionType == TransactionType.Income)).Should().Be(1);
        (await db.BusinessDocumentLinks.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task DeleteClass_ShouldSoftDelete()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new CscaService(db, currentUser, NullLogger<CscaService>.Instance);

        var classRes = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-DEL", "Lớp Xóa", "Batch 1", "T2-T4", 3000000m),
            CancellationToken.None);
        var classId = classRes.Value!.Id;

        // Act
        var delRes = await service.DeleteClassAsync(classId, CancellationToken.None);

        // Assert
        delRes.Succeeded.Should().BeTrue();

        var getRes = await service.GetClassByIdAsync(classId, CancellationToken.None);
        getRes.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Schedule_ShouldValidateAndRoundTrip()
    {
        using var db = CreateInMemoryDb();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var classRes = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-SCHEDULE", "Lớp có lịch", "Batch 1", "", 1000000m), CancellationToken.None);

        var invalid = await service.AddScheduleAsync(classRes.Value!.Id,
            new CreateCscaScheduleRequest(8, TimeSpan.FromHours(18), TimeSpan.FromHours(20)), CancellationToken.None);
        invalid.Succeeded.Should().BeFalse();

        var created = await service.AddScheduleAsync(classRes.Value.Id,
            new CreateCscaScheduleRequest(2, TimeSpan.FromHours(18), TimeSpan.FromHours(20), "Phòng A"), CancellationToken.None);
        created.Succeeded.Should().BeTrue();
        var schedules = await service.GetSchedulesAsync(classRes.Value.Id, CancellationToken.None);
        schedules.Should().ContainSingle(s => s.DayOfWeek == 2 && s.Room == "Phòng A");

        var duplicate = await service.AddScheduleAsync(classRes.Value.Id,
            new CreateCscaScheduleRequest(2, TimeSpan.FromHours(18), TimeSpan.FromHours(20)), CancellationToken.None);
        duplicate.Succeeded.Should().BeFalse();

        var overlap = await service.AddScheduleAsync(classRes.Value.Id,
            new CreateCscaScheduleRequest(2, TimeSpan.FromHours(19), TimeSpan.FromHours(21)), CancellationToken.None);
        overlap.Succeeded.Should().BeFalse();

        var adjacent = await service.AddScheduleAsync(classRes.Value.Id,
            new CreateCscaScheduleRequest(2, TimeSpan.FromHours(20), TimeSpan.FromHours(21), "Phòng B"), CancellationToken.None);
        adjacent.Succeeded.Should().BeTrue();

        var invalidMeetingUrl = await service.AddScheduleAsync(classRes.Value.Id,
            new CreateCscaScheduleRequest(4, TimeSpan.FromHours(18), TimeSpan.FromHours(20), MeetingUrl: "meet.invalid"), CancellationToken.None);
        invalidMeetingUrl.Succeeded.Should().BeFalse();

        var detail = await service.GetClassByIdAsync(classRes.Value.Id, CancellationToken.None);
        detail.Value!.Schedule.Should().Contain("T3 (18:00 - 20:00)");

        (await service.RemoveScheduleAsync(classRes.Value.Id, created.Value!.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        (await service.RemoveScheduleAsync(classRes.Value.Id, adjacent.Value!.Id, CancellationToken.None)).Succeeded.Should().BeTrue();
        var detailAfterRemovingAllSlots = await service.GetClassByIdAsync(classRes.Value.Id, CancellationToken.None);
        detailAfterRemovingAllSlots.Value!.Schedule.Should().BeEmpty();
    }

    [Fact]
    public async Task ClassroomSessionsAndAttendance_ShouldPreventConflictsAndPersistAttendance()
    {
        using var db = CreateInMemoryDb();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var firstClass = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-ROOM-A", "Lớp dùng phòng A", "Batch 1", string.Empty, 1_000_000m), CancellationToken.None);
        var secondClass = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-ROOM-B", "Lớp dùng phòng B", "Batch 1", string.Empty, 1_000_000m), CancellationToken.None);
        var classroom = await service.CreateClassroomAsync(
            new CreateCscaClassroomRequest("P-101", "Phòng 101", 30), CancellationToken.None);

        firstClass.Succeeded.Should().BeTrue();
        secondClass.Succeeded.Should().BeTrue();
        classroom.Succeeded.Should().BeTrue();

        var firstSchedule = await service.AddScheduleAsync(firstClass.Value!.Id,
            new CreateCscaScheduleRequest(1, TimeSpan.FromHours(18), TimeSpan.FromHours(20), ClassroomId: classroom.Value!.Id), CancellationToken.None);
        var conflictingSchedule = await service.AddScheduleAsync(secondClass.Value!.Id,
            new CreateCscaScheduleRequest(1, TimeSpan.FromHours(19), TimeSpan.FromHours(21), ClassroomId: classroom.Value!.Id), CancellationToken.None);

        firstSchedule.Succeeded.Should().BeTrue();
        conflictingSchedule.Succeeded.Should().BeFalse();

        var lessonDate = new DateOnly(2026, 9, 7);
        var firstSession = await service.CreateLessonSessionAsync(firstClass.Value.Id,
            new CreateCscaLessonSessionRequest(lessonDate, TimeSpan.FromHours(18), TimeSpan.FromHours(20), classroom.Value.Id), CancellationToken.None);
        var conflictingSession = await service.CreateLessonSessionAsync(secondClass.Value!.Id,
            new CreateCscaLessonSessionRequest(lessonDate, TimeSpan.FromHours(19), TimeSpan.FromHours(21), classroom.Value.Id), CancellationToken.None);

        firstSession.Succeeded.Should().BeTrue();
        conflictingSession.Succeeded.Should().BeFalse();

        var student = await service.EnrollStudentAsync(firstClass.Value.Id,
            new EnrollStudentRequest("Học viên điểm danh", "attendance@test.com", "0900000000", 1_000_000m, PaymentStatus.Paid), CancellationToken.None);
        student.Succeeded.Should().BeTrue();

        var savedAttendance = await service.UpsertLessonAttendanceAsync(firstClass.Value.Id, firstSession.Value!.Id,
            new UpsertCscaLessonAttendanceRequest(student.Value!.Id, "Present", DateTime.UtcNow), CancellationToken.None);
        var attendance = await service.GetLessonAttendanceAsync(firstClass.Value.Id, firstSession.Value.Id, CancellationToken.None);

        savedAttendance.Succeeded.Should().BeTrue();
        attendance.Succeeded.Should().BeTrue();
        attendance.Value.Should().ContainSingle(item => item.StudentId == student.Value.Id && item.Status == "Present");
    }

    [Fact]
    public async Task UpdateStaff_ShouldChangeExistingAssignmentWithoutCreatingAnotherRow()
    {
        using var db = CreateInMemoryDb();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var classResult = await service.CreateClassAsync(
            new CreateCscaClassRequest("CSCA-STAFF", "Lớp kiểm tra phân công", "Batch 1", string.Empty, 1_000_000m), CancellationToken.None);
        var employee = new Employee { EmployeeCode = "EMP-CSCA-01", FullName = "Giáo viên CSCA", Email = "teacher@csca.test" };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();

        var assigned = await service.AssignStaffAsync(classResult.Value!.Id,
            new AssignStaffRequest(employee.Id, "Teacher", 1_000_000m, "Phụ trách lớp"), CancellationToken.None);

        var updated = await service.UpdateStaffAsync(classResult.Value.Id, assigned.Value!.Id,
            new UpdateCscaStaffRequest("Mentor", 1_200_000m, "Hỗ trợ chuyên môn"), CancellationToken.None);

        updated.Succeeded.Should().BeTrue();
        updated.Value!.RoleInClass.Should().Be("Mentor");
        updated.Value.CompensationRate.Should().Be(1_200_000m);
        (await db.CscaClassStaffs.CountAsync()).Should().Be(1);
    }
}
