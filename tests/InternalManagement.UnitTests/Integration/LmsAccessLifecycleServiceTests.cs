using FluentAssertions;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InternalManagement.UnitTests.Integration;

public sealed class LmsAccessLifecycleServiceTests
{
    [Fact]
    public async Task PaidInFull_WithCourseMapping_ShouldQueueProvisionAndActiveAccess()
    {
        await using var db = CreateInMemoryDb();
        var setup = await AddMappedCourseAsync(db);
        var service = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);

        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup, PaymentStatus.Paid, paidAmount: 5_000_000m),
            CancellationToken.None);
        await db.SaveChangesAsync();

        var account = await db.LmsAccountLinks.SingleAsync();
        var grant = await db.LmsAccessGrants.SingleAsync();
        account.Status.Should().Be(LmsAccountStatus.Active);
        grant.Status.Should().Be(LmsAccessGrantStatus.Active);
        (await db.IntegrationOutboxes.Select(item => item.EventType).ToListAsync())
            .Should().BeEquivalentTo([
                LmsOutboxEventTypes.StudentProvisionRequested,
                LmsOutboxEventTypes.StudentAccessRequested
            ]);
    }

    [Fact]
    public async Task PartialPayment_ShouldProvisionPendingWithoutAccessCommand()
    {
        await using var db = CreateInMemoryDb();
        var setup = await AddMappedCourseAsync(db);
        var service = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);

        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup, PaymentStatus.Partial, paidAmount: 2_000_000m),
            CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.PendingPayment);
        (await db.LmsAccessGrants.SingleAsync()).Status.Should().Be(LmsAccessGrantStatus.PendingPayment);
        (await db.IntegrationOutboxes.Select(item => item.EventType).ToListAsync())
            .Should().ContainSingle()
            .Which.Should().Be(LmsOutboxEventTypes.StudentProvisionRequested);
    }

    [Fact]
    public async Task RefundedPayment_AfterActiveGrant_ShouldQueueRevocation()
    {
        await using var db = CreateInMemoryDb();
        var setup = await AddMappedCourseAsync(db);
        var service = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);

        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup, PaymentStatus.Paid, paidAmount: 5_000_000m),
            CancellationToken.None);
        await db.SaveChangesAsync();

        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup, PaymentStatus.Refunded, paidAmount: 5_000_000m, sourceUpdatedAt: DateTime.UtcNow.AddMinutes(1)),
            CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.Revoked);
        var grant = await db.LmsAccessGrants.SingleAsync();
        grant.Status.Should().Be(LmsAccessGrantStatus.Revoked);
        grant.RevokedAt.Should().NotBeNull();
        (await db.IntegrationOutboxes.CountAsync(item => item.EventType == LmsOutboxEventTypes.StudentAccessRequested))
            .Should().Be(2);
    }

    [Fact]
    public async Task MissingCourseMapping_ShouldCreateManualReviewWithoutGrant()
    {
        await using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var course = new Course { CompanyId = companyId, CourseSourceId = "course-not-mapped", Title = "Course" };
        db.Courses.Add(course);
        await db.SaveChangesAsync();
        var service = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);
        var setup = new EnrollmentSetup(companyId, course.Id, Guid.NewGuid());

        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup, PaymentStatus.Paid, paidAmount: 5_000_000m),
            CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.PendingPayment);
        (await db.LmsAccessGrants.CountAsync()).Should().Be(0);
        (await db.IntegrationDeadLetters.SingleAsync()).ErrorCode.Should().Be("LMS_COURSE_MAPPING_MISSING");
    }

    private static async Task<EnrollmentSetup> AddMappedCourseAsync(ApplicationDbContext db)
    {
        var companyId = Guid.NewGuid();
        var course = new Course
        {
            CompanyId = companyId,
            CourseSourceId = "csca-foundation",
            Title = "CSCA Foundation"
        };
        db.Courses.Add(course);
        db.LmsCourseLinks.Add(new LmsCourseLink
        {
            CompanyId = companyId,
            Course = course,
            CourseId = course.Id,
            ExternalCourseId = course.CourseSourceId,
            Status = IntegrationStatus.Success
        });
        await db.SaveChangesAsync();
        return new EnrollmentSetup(companyId, course.Id, Guid.NewGuid());
    }

    private static LmsAccessEvaluationRequest CreateRequest(
        EnrollmentSetup setup,
        PaymentStatus status,
        decimal paidAmount,
        DateTime? sourceUpdatedAt = null) => new(
        setup.CompanyId,
        null,
        setup.StudentId,
        null,
        Guid.NewGuid(),
        setup.CourseId,
        "csca-foundation",
        "Nguyen Van A",
        "student@example.com",
        "0900000000",
        paidAmount,
        5_000_000m,
        status,
        sourceUpdatedAt ?? DateTime.UtcNow,
        "payment-001");

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LmsAccessLifecycle_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed record EnrollmentSetup(Guid CompanyId, Guid CourseId, Guid StudentId);
}
