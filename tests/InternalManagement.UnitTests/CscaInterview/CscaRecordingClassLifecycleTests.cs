using System.Text.Json;
using FluentAssertions;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InternalManagement.UnitTests.CscaInterview;

public class CscaRecordingClassLifecycleTests
{
    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("CscaRecordingLifecycleTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateRecordingClass_AllowsPaidLiveSourceFromAnotherCourse()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "record-course", Title = "Khóa Record" };
        var otherCourse = new Course { CourseSourceId = "other-course", Title = "Khóa khác" };
        db.AddRange(course, otherCourse);
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var live = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-01", "Lớp Live", "", "", 2_000_000m, CourseId: course.Id), default);

        var missingSource = await service.CreateClassAsync(new CreateCscaClassRequest(
            "REC-MISSING", "Record thiếu nguồn", "", "", 2_000_000m,
            CourseId: course.Id, ClassType: "RECORDING"), default);
        var freeRecording = await service.CreateClassAsync(new CreateCscaClassRequest(
            "REC-FREE", "Record miễn phí", "", "", 0,
            CourseId: course.Id, IsTrial: true, ClassType: "RECORDING", SourceClassId: live.Value!.Id), default);
        var crossCourse = await service.CreateClassAsync(new CreateCscaClassRequest(
            "REC-CROSS", "Record kế thừa chéo khóa", "", "", 2_000_000m,
            CourseId: otherCourse.Id, ClassType: "RECORDING", SourceClassId: live.Value.Id), default);

        missingSource.Succeeded.Should().BeFalse();
        freeRecording.Succeeded.Should().BeFalse();
        crossCourse.Succeeded.Should().BeTrue();
        crossCourse.Value!.CourseId.Should().Be(otherCourse.Id);
        crossCourse.Value.SourceClassId.Should().Be(live.Value.Id);
    }

    [Fact]
    public async Task RecordingClass_KeepsOwnFinance_QueuesAliasOnly_AndRejectsLiveOperations()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "record-linked", Title = "Khóa Record Linked" };
        db.AddRange(course, new LmsCourseLink
        {
            Course = course,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            integrationDb: db);
        var live = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-REC", "Lớp Live nguồn", "", "", 3_000_000m, CourseId: course.Id), default);
        db.IntegrationOutboxes.RemoveRange(db.IntegrationOutboxes);
        await db.SaveChangesAsync();

        var recording = await service.CreateClassAsync(new CreateCscaClassRequest(
            "RECORD-01", "Lớp Record", "", "", 1_500_000m,
            CourseId: course.Id, ClassType: "RECORDING", SourceClassId: live.Value!.Id), default);

        recording.Succeeded.Should().BeTrue();
        recording.Value!.TuitionFee.Should().Be(1_500_000m);
        recording.Value.ClassType.Should().Be("RECORDING");
        var queued = await db.IntegrationOutboxes.SingleAsync();
        GetManagementEventType(queued).Should().Be("class.upserted");
        var payload = GetManagementPayload(queued);
        payload.GetProperty("classType").GetString().Should().Be("RECORDING");
        payload.GetProperty("recordingContentMode").GetString().Should().Be("MIRROR");
        payload.GetProperty("sourceClassSourceId").GetString().Should().Be(live.Value.Id.ToString("N"));
        payload.GetProperty("priceVnd").GetDecimal().Should().Be(1_500_000m);

        var enrolled = await service.EnrollStudentAsync(recording.Value.Id,
            new EnrollStudentRequest("Học viên Record", "record.student@example.com", "0900000001",
                500_000m, PaymentStatus.Partial), default);
        var finance = await service.GetClassFinancialSummaryAsync(recording.Value.Id, default);
        finance.Value!.ExpectedRevenue.Should().Be(1_500_000m);
        finance.Value.ActualRevenue.Should().Be(500_000m);
        enrolled.Succeeded.Should().BeTrue();

        var employee = new Employee
        {
            EmployeeCode = "REC-TEACHER",
            FullName = "Giảng viên",
            Email = "record.teacher@example.com"
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        (await service.AssignStaffAsync(recording.Value.Id,
            new AssignStaffRequest(employee.Id, "Teacher", 100_000m), default)).Succeeded.Should().BeFalse();
        (await service.AddScheduleAsync(recording.Value.Id,
            new CreateCscaScheduleRequest(1, new TimeSpan(19, 0, 0), new TimeSpan(21, 0, 0)), default)).Succeeded.Should().BeFalse();
        (await service.CreateLessonSessionAsync(recording.Value.Id,
            new CreateCscaLessonSessionRequest(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
                new TimeSpan(19, 0, 0), new TimeSpan(21, 0, 0)), default)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task IndependentRecordingClass_DoesNotRequireLiveSource_AndKeepsPaidFinance()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "independent-record", Title = "Khóa video độc lập" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);

        var result = await service.CreateClassAsync(new CreateCscaClassRequest(
            "VIDEO-01", "Khóa video độc lập", "", "", 1_800_000m,
            CourseId: course.Id, ClassType: "RECORDING", RecordingContentMode: "INDEPENDENT"), default);

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors));
        result.Value!.ClassType.Should().Be("RECORDING");
        result.Value.RecordingContentMode.Should().Be("INDEPENDENT");
        result.Value.SourceClassId.Should().BeNull();
        (await service.EnrollStudentAsync(result.Value.Id,
            new EnrollStudentRequest("Học viên video", "video@example.com", "0900000022",
                1_800_000m, PaymentStatus.Paid), default)).Succeeded.Should().BeTrue();
        var finance = await service.GetClassFinancialSummaryAsync(result.Value.Id, default);
        finance.Value!.ExpectedRevenue.Should().Be(1_800_000m);
        finance.Value.ActualRevenue.Should().Be(1_800_000m);
    }

    [Fact]
    public async Task UpdateClass_ShouldRejectLearningModeChangeWhenStudentsExist()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "record-mode-lock", Title = "Khóa khóa mode" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var live = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-MODE", "Lớp Live có học viên", "", "", 2_000_000m, CourseId: course.Id), default);
        var source = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-SOURCE", "Lớp Live nguồn", "", "", 2_000_000m, CourseId: course.Id), default);
        await service.EnrollStudentAsync(live.Value!.Id,
            new EnrollStudentRequest("Học viên đã xếp lớp", "mode.lock@example.com", "0900000011",
                2_000_000m, PaymentStatus.Paid), default);

        var result = await service.UpdateClassAsync(live.Value.Id, new UpdateCscaClassRequest(
            live.Value.Name, live.Value.Batch, live.Value.Schedule, live.Value.TuitionFee,
            live.Value.StartDate, live.Value.EndDate, live.Value.Status, live.Value.CourseId,
            ClassType: "RECORDING", SourceClassId: source.Value!.Id), default);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle(error => error.Contains("đã có học viên"));
        (await db.CscaClasses.SingleAsync(item => item.Id == live.Value.Id)).ClassType.Should().Be("LIVE");
    }

    [Fact]
    public async Task LiveSource_ShouldRejectMutationsThatBreakRecordingAliases()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "record-source-guard", Title = "Khóa nguồn Record" };
        var otherCourse = new Course { CourseSourceId = "record-source-other", Title = "Khóa khác" };
        db.AddRange(course, otherCourse);
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var source = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-GUARDED", "Lớp Live nguồn", "", "", 3_000_000m, CourseId: course.Id), default);
        var alternateSource = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-ALTERNATE", "Lớp Live khác", "", "", 3_000_000m, CourseId: course.Id), default);
        var recording = await service.CreateClassAsync(new CreateCscaClassRequest(
            "RECORD-GUARDED", "Lớp Record", "", "", 1_500_000m,
            CourseId: course.Id, ClassType: "RECORDING", SourceClassId: source.Value!.Id), default);
        recording.Succeeded.Should().BeTrue();

        var cancel = await service.UpdateClassAsync(source.Value.Id, new UpdateCscaClassRequest(
            source.Value.Name, source.Value.Batch, source.Value.Schedule, source.Value.TuitionFee,
            source.Value.StartDate, source.Value.EndDate, "Cancelled", source.Value.CourseId,
            ClassType: "LIVE"), default);
        var moveCourse = await service.UpdateClassAsync(source.Value.Id, new UpdateCscaClassRequest(
            source.Value.Name, source.Value.Batch, source.Value.Schedule, source.Value.TuitionFee,
            source.Value.StartDate, source.Value.EndDate, source.Value.Status, otherCourse.Id,
            ClassType: "LIVE"), default);
        var changeType = await service.UpdateClassAsync(source.Value.Id, new UpdateCscaClassRequest(
            source.Value.Name, source.Value.Batch, source.Value.Schedule, source.Value.TuitionFee,
            source.Value.StartDate, source.Value.EndDate, source.Value.Status, source.Value.CourseId,
            ClassType: "RECORDING", SourceClassId: alternateSource.Value!.Id), default);
        var delete = await service.DeleteClassAsync(source.Value.Id, default);

        cancel.Succeeded.Should().BeFalse();
        moveCourse.Succeeded.Should().BeFalse();
        changeType.Succeeded.Should().BeFalse();
        delete.Succeeded.Should().BeFalse();
        var persistedSource = await db.CscaClasses.SingleAsync(item => item.Id == source.Value.Id);
        persistedSource.Status.Should().Be("Active");
        persistedSource.CourseId.Should().Be(course.Id);
        persistedSource.ClassType.Should().Be("LIVE");
        persistedSource.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task LiveSource_ShouldAllowMutationAfterRecordingAliasIsDeleted()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "record-source-release", Title = "Khóa giải phóng nguồn" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var source = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-RELEASED", "Lớp Live nguồn", "", "", 3_000_000m, CourseId: course.Id), default);
        var recording = await service.CreateClassAsync(new CreateCscaClassRequest(
            "RECORD-RELEASED", "Lớp Record sẽ xóa", "", "", 1_500_000m,
            CourseId: course.Id, ClassType: "RECORDING", SourceClassId: source.Value!.Id), default);

        (await service.DeleteClassAsync(recording.Value!.Id, default)).Succeeded.Should().BeTrue();
        var cancel = await service.UpdateClassAsync(source.Value.Id, new UpdateCscaClassRequest(
            source.Value.Name, source.Value.Batch, source.Value.Schedule, source.Value.TuitionFee,
            source.Value.StartDate, source.Value.EndDate, "Cancelled", source.Value.CourseId,
            ClassType: "LIVE"), default);

        cancel.Succeeded.Should().BeTrue();
        (await db.CscaClasses.IgnoreQueryFilters().SingleAsync(item => item.Id == source.Value.Id))
            .Status.Should().Be("Cancelled");
    }

    [Fact]
    public async Task DeleteRecordingClass_WhenLinked_ShouldCancelStatusAndQueueCancelledEventForWeb()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "live-course", Title = "Khóa Live" };
        db.AddRange(course, new LmsCourseLink
        {
            Course = course,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            integrationDb: db);
        var live = await service.CreateClassAsync(new CreateCscaClassRequest(
            "LIVE-DEL", "Lớp Live nguồn", "", "", 3_000_000m, CourseId: course.Id), default);
        var recording = await service.CreateClassAsync(new CreateCscaClassRequest(
            "REC-DEL", "Lớp Record bị xóa", "Đợt 1", "", 888_000m,
            CourseId: course.Id, ClassType: "RECORDING", SourceClassId: live.Value!.Id), default);
        db.IntegrationOutboxes.RemoveRange(db.IntegrationOutboxes);
        await db.SaveChangesAsync();

        var deleted = await service.DeleteClassAsync(recording.Value!.Id, default);

        deleted.Succeeded.Should().BeTrue(string.Join("; ", deleted.Errors));
        var persisted = await db.CscaClasses.IgnoreQueryFilters().SingleAsync(item => item.Id == recording.Value.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.Status.Should().Be("Cancelled");

        var queued = await db.IntegrationOutboxes.SingleAsync();
        GetManagementEventType(queued).Should().Be("class.upserted");
        var payload = GetManagementPayload(queued);
        payload.GetProperty("classSourceId").GetString().Should().Be(recording.Value.Id.ToString("N"));
        payload.GetProperty("status").GetString().Should().Be("cancelled");
        payload.GetProperty("classType").GetString().Should().Be("RECORDING");
        payload.GetProperty("recordingContentMode").GetString().Should().Be("MIRROR");
        payload.GetProperty("priceVnd").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task IndependentRecordingClass_ShouldAllowTeacherAssignmentAndSyncCourseAuthorWithoutClassTeacherAssigned()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "independent-video", Title = "Khóa video toán", Price = 888_000m, Status = "Published" };
        var teacher = new Employee
        {
            EmployeeCode = "GV-MATH",
            FullName = "Giáo viên Toán Video",
            Email = "math.video@example.com"
        };
        db.AddRange(course, teacher, new LmsCourseLink
        {
            Course = course,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            integrationDb: db);
        var independent = await service.CreateClassAsync(new CreateCscaClassRequest(
            "VID-MATH-01", "Khóa record toán csca", "Đợt 1", "", 888_000m,
            CourseId: course.Id, ClassType: "RECORDING", RecordingContentMode: "INDEPENDENT"), default);
        db.IntegrationOutboxes.RemoveRange(db.IntegrationOutboxes);
        await db.SaveChangesAsync();

        var assigned = await service.AssignStaffAsync(independent.Value!.Id,
            new AssignStaffRequest(teacher.Id, "Teacher", 0m), default);

        assigned.Succeeded.Should().BeTrue(string.Join("; ", assigned.Errors));
        var events = (await db.IntegrationOutboxes.OrderBy(item => item.CreatedAt).ToListAsync())
            .Select(item => new { Type = GetManagementEventType(item), Payload = GetManagementPayload(item) })
            .ToList();
        events.Select(item => item.Type).Should().Equal("teacher.upserted", "class.upserted", "course.upserted");
        events.Should().NotContain(item => item.Type == "class.teacher.assigned");
        events[1].Payload.GetProperty("leadTeacherSourceId").GetString().Should().Be(teacher.Id.ToString("N"));
        events[1].Payload.GetProperty("recordingContentMode").GetString().Should().Be("INDEPENDENT");
        events[1].Payload.GetProperty("sourceClassSourceId").ValueKind.Should().Be(JsonValueKind.Null);
        events[2].Payload.GetProperty("teacherSourceId").GetString().Should().Be(teacher.Id.ToString("N"));
        events[2].Payload.GetProperty("priceVnd").GetDecimal().Should().Be(888_000m);

        var updated = await service.UpdateStaffAsync(independent.Value.Id, assigned.Value!.Id,
            new UpdateCscaStaffRequest("Mentor", 0m, null), default);
        updated.Succeeded.Should().BeTrue();
        var removed = await service.RemoveStaffAsync(independent.Value.Id, assigned.Value.Id, default);
        removed.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task IndependentRecording_CurriculumReference_ShouldBeReferenceOnlyValidatedAndIdempotent()
    {
        using var db = CreateInMemoryDb();
        var sourceCourse = new Course { CourseSourceId = "math-01", Title = "Toán 01" };
        var recordingCourse = new Course { CourseSourceId = "math-record", Title = "Khóa record toán csca" };
        db.AddRange(sourceCourse, recordingCourse, new LmsCourseLink
        {
            Course = recordingCourse,
            ExternalCourseId = recordingCourse.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            integrationDb: db);
        var source = await service.CreateClassAsync(new CreateCscaClassRequest(
            "MATH-01", "Toán CSCA - Lớp 01", "", "", 3_000_000m, CourseId: sourceCourse.Id), default);
        var unrelatedSource = await service.CreateClassAsync(new CreateCscaClassRequest(
            "MATH-02", "Toán CSCA - Lớp 02", "", "", 3_000_000m, CourseId: sourceCourse.Id), default);
        var recording = await service.CreateClassAsync(new CreateCscaClassRequest(
            "MATH-REC", "Khóa record toán csca", "", "", 888_000m,
            CourseId: recordingCourse.Id, ClassType: "RECORDING", RecordingContentMode: "INDEPENDENT"), default);
        var makeup = new CscaLessonSession
        {
            ClassId = source.Value!.Id,
            LessonDate = new DateOnly(2026, 10, 26),
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(22, 0, 0),
            SessionKind = "Makeup",
            Notes = "Imported from CSCA Course LMS: Buổi học bù 26/10"
        };
        var foreignSession = new CscaLessonSession
        {
            ClassId = unrelatedSource.Value!.Id,
            LessonDate = new DateOnly(2026, 10, 27),
            StartTime = new TimeSpan(20, 0, 0),
            EndTime = new TimeSpan(22, 0, 0)
        };
        db.AddRange(makeup, foreignSession);
        db.IntegrationOutboxes.RemoveRange(db.IntegrationOutboxes);
        await db.SaveChangesAsync();

        var invalidDestination = await service.UpdateRecordingCurriculumReferenceAsync(source.Value.Id,
            new UpdateRecordingCurriculumReferenceRequest(source.Value.Id, [makeup.Id]), default);
        var invalidExclusion = await service.UpdateRecordingCurriculumReferenceAsync(recording.Value!.Id,
            new UpdateRecordingCurriculumReferenceRequest(source.Value.Id, [foreignSession.Id]), default);
        var linked = await service.UpdateRecordingCurriculumReferenceAsync(recording.Value.Id,
            new UpdateRecordingCurriculumReferenceRequest(source.Value.Id, [makeup.Id]), default);

        invalidDestination.Succeeded.Should().BeFalse();
        invalidExclusion.Succeeded.Should().BeFalse();
        linked.Succeeded.Should().BeTrue(string.Join("; ", linked.Errors));
        linked.Value!.IncludeVideos.Should().BeFalse();
        linked.Value.ExcludedSessionSourceIds.Should().Equal(makeup.Id);
        var queued = await db.IntegrationOutboxes.SingleAsync();
        GetManagementEventType(queued).Should().Be("class.curriculum.linked");
        var payload = GetManagementPayload(queued);
        payload.GetProperty("classSourceId").GetString().Should().Be(recording.Value.Id.ToString("N"));
        payload.GetProperty("contentSourceClassSourceId").GetString().Should().Be(source.Value.Id.ToString("N"));
        payload.GetProperty("includeVideos").GetBoolean().Should().BeFalse();
        payload.GetProperty("excludedSessionSourceIds")[0].GetString().Should().Be(makeup.Id.ToString("N"));

        var linkedAgain = await service.UpdateRecordingCurriculumReferenceAsync(recording.Value.Id,
            new UpdateRecordingCurriculumReferenceRequest(source.Value.Id, [makeup.Id, makeup.Id]), default);

        linkedAgain.Succeeded.Should().BeTrue();
        (await db.IntegrationOutboxes.CountAsync()).Should().Be(1,
            "an unchanged reference must not enqueue duplicate work");
        (await db.CscaLessonSessions.CountAsync(session => session.ClassId == recording.Value.Id)).Should().Be(0,
            "curriculum reuse is a reference and must not copy sessions or videos");
        var persisted = await db.CscaClasses.SingleAsync(cls => cls.Id == recording.Value.Id);
        persisted.SourceClassId.Should().BeNull("the independent recording must never become MIRROR");
        persisted.CurriculumSourceClassId.Should().Be(source.Value.Id);
    }

    private static string? GetManagementEventType(IntegrationOutbox item) =>
        JsonDocument.Parse(item.PayloadJson).RootElement.GetProperty("eventType").GetString();

    private static JsonElement GetManagementPayload(IntegrationOutbox item) =>
        JsonDocument.Parse(item.PayloadJson).RootElement.GetProperty("payload").Clone();
}
