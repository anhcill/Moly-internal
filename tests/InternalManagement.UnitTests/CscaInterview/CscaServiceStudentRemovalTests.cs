using System.Text.Json;
using FluentAssertions;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InternalManagement.UnitTests.CscaInterview;

public partial class CscaServiceTests
{
    [Fact]
    public async Task RemoveStudent_WithPendingUnapprovedLmsAudit_ShouldRevokeAuditAndDeleteStudent()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "pending-course", Title = "Course chưa duyệt" };
        var cls = new CscaClass
        {
            Course = course,
            Code = "CSCA-PENDING-REMOVE",
            Name = "Lớp có audit chờ duyệt",
            TuitionFee = 2_000_000m
        };
        var student = new CscaClassStudent
        {
            Class = cls,
            StudentName = "Học viên cần xóa",
            Email = "pending.remove@example.com",
            PaymentStatus = PaymentStatus.Cancelled
        };
        var courseLink = new LmsCourseLink
        {
            Course = course,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Pending
        };
        var accountLink = new LmsAccountLink
        {
            CscaClassStudent = student,
            ExternalStudentId = student.Id.ToString("N"),
            LmsEmail = student.Email,
            Status = LmsAccountStatus.PendingPayment
        };
        var grant = new LmsAccessGrant
        {
            LmsAccountLink = accountLink,
            LmsCourseLink = courseLink,
            CscaClassStudent = student,
            ExternalGrantId = $"grant:{student.Id:N}:{course.CourseSourceId}",
            Status = LmsAccessGrantStatus.PendingPayment
        };
        db.AddRange(course, cls, student, courseLink, accountLink, grant);
        await db.SaveChangesAsync();
        var lifecycle = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            lmsAccessLifecycleService: lifecycle, integrationDb: db);

        var result = await service.RemoveStudentAsync(cls.Id, student.Id, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        (await db.CscaClassStudents.CountAsync()).Should().Be(0);
        var preservedGrant = await db.LmsAccessGrants.SingleAsync();
        preservedGrant.Status.Should().Be(LmsAccessGrantStatus.Revoked);
        preservedGrant.CscaClassStudentId.Should().BeNull();
        (await db.LmsAccountLinks.SingleAsync()).CscaClassStudentId.Should().BeNull();
    }

    [Fact]
    public async Task RemoveStudent_WithActiveLmsAccess_ShouldQueueRevocationAndDeleteStudent()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "active-course", Title = "Course đã duyệt" };
        var cls = new CscaClass
        {
            Course = course,
            Code = "CSCA-ACTIVE-REMOVE",
            Name = "Lớp có quyền LMS",
            TuitionFee = 2_000_000m
        };
        var student = new CscaClassStudent
        {
            Class = cls,
            StudentName = "Học viên đang có quyền",
            Email = "active.remove@example.com",
            PaidAmount = 2_000_000m,
            PaymentStatus = PaymentStatus.Paid
        };
        var courseLink = new LmsCourseLink
        {
            Course = course,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        };
        var accountLink = new LmsAccountLink
        {
            CscaClassStudent = student,
            ExternalStudentId = student.Id.ToString("N"),
            LmsEmail = student.Email,
            Status = LmsAccountStatus.Active
        };
        var grant = new LmsAccessGrant
        {
            LmsAccountLink = accountLink,
            LmsCourseLink = courseLink,
            CscaClassStudent = student,
            ExternalGrantId = $"grant:{student.Id:N}:{course.CourseSourceId}",
            Status = LmsAccessGrantStatus.Active
        };
        db.AddRange(course, cls, student, courseLink, accountLink, grant);
        await db.SaveChangesAsync();
        var lifecycle = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            lmsAccessLifecycleService: lifecycle, integrationDb: db);

        var result = await service.RemoveStudentAsync(cls.Id, student.Id, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        (await db.CscaClassStudents.CountAsync()).Should().Be(0);
        var preservedGrant = await db.LmsAccessGrants.SingleAsync();
        preservedGrant.Status.Should().Be(LmsAccessGrantStatus.Revoked);
        preservedGrant.CscaClassStudentId.Should().BeNull();
        var membership = await db.IntegrationOutboxes
            .Where(item => item.EventType == LmsOutboxEventTypes.ManagementEventRequested &&
                           item.PayloadJson.Contains("class.membership.changed"))
            .SingleAsync();
        GetManagementPayload(membership).GetProperty("status").GetString().Should().Be("revoked");
        (await db.IntegrationOutboxes.CountAsync(item =>
            item.EventType == LmsOutboxEventTypes.StudentAccessRequested)).Should().Be(1);
    }

    [Fact]
    public async Task RemoveStudent_WithActiveGrantAndPendingCourseMapping_ShouldStillRevokeAndDelete()
    {
        using var db = CreateInMemoryDb();
        var course = new Course { CourseSourceId = "pending-active-course", Title = "Course đổi trạng thái duyệt" };
        var cls = new CscaClass
        {
            Course = course,
            Code = "CSCA-PENDING-ACTIVE-REMOVE",
            Name = "Lớp từng có quyền LMS",
            TuitionFee = 2_000_000m
        };
        var student = new CscaClassStudent
        {
            Class = cls,
            StudentName = "Học viên có quyền cũ",
            Email = null,
            PaidAmount = 2_000_000m,
            PaymentStatus = PaymentStatus.Paid
        };
        var courseLink = new LmsCourseLink
        {
            Course = course,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Pending
        };
        var accountLink = new LmsAccountLink
        {
            CscaClassStudent = student,
            ExternalStudentId = student.Id.ToString("N"),
            LmsEmail = "pending.active.remove@example.com",
            Status = LmsAccountStatus.Active
        };
        var grant = new LmsAccessGrant
        {
            LmsAccountLink = accountLink,
            LmsCourseLink = courseLink,
            CscaClassStudent = student,
            ExternalGrantId = $"grant:{student.Id:N}:{course.CourseSourceId}",
            Status = LmsAccessGrantStatus.Active
        };
        db.AddRange(course, cls, student, courseLink, accountLink, grant);
        await db.SaveChangesAsync();
        var lifecycle = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            lmsAccessLifecycleService: lifecycle, integrationDb: db);

        var result = await service.RemoveStudentAsync(cls.Id, student.Id, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        (await db.CscaClassStudents.CountAsync()).Should().Be(0);
        (await db.LmsAccessGrants.SingleAsync()).Status.Should().Be(LmsAccessGrantStatus.Revoked);
        var accessCommand = await db.IntegrationOutboxes.SingleAsync(item =>
            item.EventType == LmsOutboxEventTypes.StudentAccessRequested);
        JsonDocument.Parse(accessCommand.PayloadJson).RootElement.GetProperty("access")
            .GetProperty("accessStatus").GetString().Should().Be("Revoked");
        var membership = await db.IntegrationOutboxes.SingleAsync(item =>
            item.EventType == LmsOutboxEventTypes.ManagementEventRequested &&
            item.PayloadJson.Contains("class.membership.changed"));
        GetManagementPayload(membership).GetProperty("status").GetString().Should().Be("revoked");
    }

    [Fact]
    public async Task RemoveStudent_ShouldKeepRemainingStudentRevenueInProfitAllocation()
    {
        using var db = CreateInMemoryDb();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance,
            integrationDb: db);
        var cls = await service.CreateClassAsync(new CreateCscaClassRequest(
            "CSCA-REMOVE-REVENUE", "Lớp giữ doanh thu", "Batch 1", string.Empty, 5_000_000m),
            CancellationToken.None);
        var removed = await service.EnrollStudentAsync(cls.Value!.Id,
            new EnrollStudentRequest("Học viên xóa", "remove.revenue@example.com", null,
                2_000_000m, PaymentStatus.Partial), CancellationToken.None);
        await service.EnrollStudentAsync(cls.Value.Id,
            new EnrollStudentRequest("Học viên còn lại", "remain.revenue@example.com", null,
                3_000_000m, PaymentStatus.Partial), CancellationToken.None);

        var result = await service.RemoveStudentAsync(cls.Value.Id, removed.Value!.Id, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        (await db.ProfitAllocations.SingleAsync(allocation =>
            allocation.ReferenceType == "CscaClass" && allocation.ReferenceId == cls.Value.Id))
            .IncomeAmount.Should().Be(3_000_000m);
    }
}
