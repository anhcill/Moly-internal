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
                LmsOutboxEventTypes.ManagementEventRequested,
                LmsOutboxEventTypes.ManagementEventRequested,
                LmsOutboxEventTypes.StudentAccessRequested
            ]);
        var membershipEvents = await db.IntegrationOutboxes
            .Where(item => item.EventType == LmsOutboxEventTypes.ManagementEventRequested)
            .OrderBy(item => item.CreatedAt)
            .ToListAsync();
        membershipEvents[0].PayloadJson.Should().Contain("student.provisioned");
        membershipEvents[1].PayloadJson.Should().Contain("class.membership.changed");
        membershipEvents.Select(item => item.CorrelationId).Distinct().Should().ContainSingle();
        membershipEvents[1].PayloadJson.Should().Contain(setup.StudentId.ToString("N"));
    }

    [Fact]
    public async Task PaidFreeClass_WithParty_ShouldMigrateLegacyStudentIdentityAndQueuePartyId()
    {
        await using var db = CreateInMemoryDb();
        var setup = await AddMappedCourseAsync(db);
        var partyId = Guid.NewGuid();
        var legacyId = setup.StudentId.ToString("N");
        db.LmsAccountLinks.Add(new LmsAccountLink
        {
            CompanyId = setup.CompanyId,
            PartyId = partyId,
            CscaClassStudentId = setup.StudentId,
            ExternalStudentId = legacyId,
            LmsEmail = "student@example.com",
            Status = LmsAccountStatus.PendingPayment
        });
        await db.SaveChangesAsync();
        var service = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);

        var request = CreateRequest(setup, PaymentStatus.Paid, paidAmount: 0m) with
        {
            PartyId = partyId,
            TuitionFee = 0m
        };
        await service.ReconcileStudentAccessAsync(request, CancellationToken.None);
        await db.SaveChangesAsync();

        var account = await db.LmsAccountLinks.SingleAsync();
        account.ExternalStudentId.Should().Be(partyId.ToString("N"));
        account.Status.Should().Be(LmsAccountStatus.Active);
        (await db.IntegrationOutboxes.Where(item => item.EventType == LmsOutboxEventTypes.StudentProvisionRequested)
            .Select(item => item.PayloadJson).SingleAsync()).Should().Contain(partyId.ToString("N"));
        (await db.LmsAccessGrants.SingleAsync()).Status.Should().Be(LmsAccessGrantStatus.Active);
    }

    [Fact]
    public async Task UnpaidSecondClass_ShouldNotRevokePartyWithAnotherPaidClass()
    {
        await using var db = CreateInMemoryDb();
        var setup = await AddMappedCourseAsync(db);
        var partyId = Guid.NewGuid();
        var service = new LmsAccessLifecycleService(db, NullLogger<LmsAccessLifecycleService>.Instance);

        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup, PaymentStatus.Paid, paidAmount: 5_000_000m) with { PartyId = partyId },
            CancellationToken.None);
        await db.SaveChangesAsync();
        await service.ReconcileStudentAccessAsync(
            CreateRequest(setup with { StudentId = Guid.NewGuid() }, PaymentStatus.Cancelled, paidAmount: 0m,
                sourceUpdatedAt: DateTime.UtcNow.AddMinutes(1)) with { PartyId = partyId },
            CancellationToken.None);
        await db.SaveChangesAsync();

        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.Active);
        (await db.LmsAccessGrants.CountAsync(grant => grant.Status == LmsAccessGrantStatus.Active)).Should().Be(1);
        (await db.IntegrationOutboxes.Where(item => item.EventType == LmsOutboxEventTypes.StudentProvisionRequested)
            .OrderByDescending(item => item.CreatedAt).Select(item => item.PayloadJson).FirstAsync())
            .Should().Contain("Active");
        (await db.IntegrationOutboxes.Where(item => item.EventType == LmsOutboxEventTypes.StudentAccessRequested)
            .OrderByDescending(item => item.CreatedAt).Select(item => item.PayloadJson).FirstAsync())
            .Should().Contain("Active");
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
        (await db.IntegrationOutboxes.CountAsync(item => item.EventType == LmsOutboxEventTypes.StudentProvisionRequested))
            .Should().Be(1);
        (await db.IntegrationOutboxes.CountAsync(item => item.EventType == LmsOutboxEventTypes.StudentAccessRequested))
            .Should().Be(0);
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
