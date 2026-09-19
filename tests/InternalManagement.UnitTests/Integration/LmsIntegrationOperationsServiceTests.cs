using FluentAssertions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace InternalManagement.UnitTests.Integration;

public sealed class LmsIntegrationOperationsServiceTests
{
    [Fact]
    public async Task UpsertCourseMapping_WhenEnabledWithConfirmedLmsTarget_ShouldMakeMappingReady()
    {
        await using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var course = AddCourse(db, companyId, "csca-foundation");
        await db.SaveChangesAsync();
        var service = CreateService(db, companyId);

        var result = await service.UpsertCourseMappingAsync(
            course.Id,
            new UpsertLmsCourseMappingRequest(null, 101L, "csca-foundation", true),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.Status.Should().Be(IntegrationStatus.Success.ToString());
        result.Value.ExternalCourseId.Should().Be("csca-foundation");
        var overview = await service.GetOverviewAsync(CancellationToken.None);
        overview.Value!.ReadyCourseMappings.Should().Be(1);
    }

    [Fact]
    public async Task UpsertCourseMapping_WhenEnabledWithoutTargetConfirmation_ShouldRejectRequest()
    {
        await using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var course = AddCourse(db, companyId, "csca-foundation");
        await db.SaveChangesAsync();
        var service = CreateService(db, companyId);

        var result = await service.UpsertCourseMappingAsync(
            course.Id,
            new UpsertLmsCourseMappingRequest(null, null, null, true),
            CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Should().Contain("LMS course ID");
        (await db.LmsCourseLinks.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RetryOutbox_WhenDeadLetter_ShouldReturnItToPendingWithoutExposingPayload()
    {
        await using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var outbox = new IntegrationOutbox
        {
            CompanyId = companyId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = "evt-001",
            EventType = "StudentProvisionRequested",
            AggregateType = "LmsAccountLink",
            AggregateId = Guid.NewGuid().ToString("N"),
            IdempotencyKey = "private-key",
            PayloadJson = "{\"email\":\"student@example.com\"}",
            Status = IntegrationStatus.DeadLetter,
            AttemptCount = 3,
            LastError = "Safe error"
        };
        db.IntegrationOutboxes.Add(outbox);
        await db.SaveChangesAsync();
        var service = CreateService(db, companyId);

        var retry = await service.RetryOutboxAsync(outbox.Id, CancellationToken.None);
        var list = await service.GetOutboxAsync("Pending", 1, 20, CancellationToken.None);

        retry.Succeeded.Should().BeTrue();
        retry.Value!.Status.Should().Be(IntegrationStatus.Pending.ToString());
        retry.Value.AttemptCount.Should().Be(0);
        list.Value!.Items.Should().ContainSingle();
        list.Value.Items.Single().GetType().GetProperty("PayloadJson").Should().BeNull();
        list.Value.Items.Single().GetType().GetProperty("IdempotencyKey").Should().BeNull();
    }

    private static Course AddCourse(ApplicationDbContext db, Guid companyId, string sourceId)
    {
        var course = new Course
        {
            CompanyId = companyId,
            CourseSourceId = sourceId,
            Title = "CSCA Foundation",
            Slug = sourceId
        };
        db.Courses.Add(course);
        return course;
    }

    private static LmsIntegrationOperationsService CreateService(ApplicationDbContext db, Guid companyId)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(value => value.CompanyId).Returns(companyId);
        currentUser.SetupGet(value => value.Username).Returns("operator@example.com");
        return new LmsIntegrationOperationsService(db, currentUser.Object);
    }

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LmsIntegrationOperations_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
