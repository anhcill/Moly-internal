using System.Net;
using System.Text.Json;
using FluentAssertions;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Integration.Connectors;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace InternalManagement.UnitTests.Integration;

public sealed class LmsOutboxDispatcherTests
{
    [Fact]
    public async Task DispatchStudentAndMembershipEvents_UsesAccountAggregateInOrder()
    {
        await using var db = CreateInMemoryDb();
        var account = new LmsAccountLink
        {
            CompanyId = Guid.NewGuid(),
            ExternalStudentId = "party-001",
            LmsEmail = "student@example.com",
            Status = LmsAccountStatus.Active
        };
        db.LmsAccountLinks.Add(account);
        var eventTypes = new[] { "student.provisioned", "class.membership.changed" };
        for (var index = 0; index < eventTypes.Length; index++)
        {
            var command = new LmsManagementEvent($"evt-{index}", eventTypes[index],
                DateTime.UtcNow, "internal-management", new { StudentSourceId = "party-001" });
            db.IntegrationOutboxes.Add(new IntegrationOutbox
            {
                CompanyId = account.CompanyId,
                EventId = command.EventId,
                EventType = LmsOutboxEventTypes.ManagementEventRequested,
                AggregateType = nameof(LmsAccountLink),
                AggregateId = account.Id.ToString("N"),
                IdempotencyKey = $"event:{index}",
                CorrelationId = "student-membership-001",
                PayloadJson = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CreatedAt = DateTime.UtcNow.AddSeconds(index),
                NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
            });
        }
        await db.SaveChangesAsync();
        var delivered = new List<string>();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.SendManagementEventAsync(
                It.IsAny<LmsManagementEvent>(), It.IsAny<LmsOutboundRequestContext>(), It.IsAny<CancellationToken>()))
            .Callback<LmsManagementEvent, LmsOutboundRequestContext, CancellationToken>((command, _, _) =>
                delivered.Add(command.EventType))
            .Returns(Task.CompletedTask);
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Should().Be(new LmsOutboxDispatchResult(2, 2, 0, 0));
        delivered.Should().ContainInOrder(eventTypes);
        (await db.IntegrationOutboxes.CountAsync(item => item.Status == IntegrationStatus.Success)).Should().Be(2);
    }

    [Fact]
    public async Task DispatchProvision_WhenLmsAccepts_ShouldMarkOutboxAndAccountSynchronized()
    {
        await using var db = CreateInMemoryDb();
        var account = AddProvisionOutbox(db);
        await db.SaveChangesAsync();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.ProvisionStudentAsync(
                It.IsAny<LmsProvisionCommand>(),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LmsProvisionResult(false, 45L, "Provisioned", "corr-lms"));
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Should().Be(new LmsOutboxDispatchResult(1, 1, 0, 0));
        (await db.IntegrationOutboxes.SingleAsync()).Status.Should().Be(IntegrationStatus.Success);
        (await db.LmsAccountLinks.SingleAsync()).LmsUserId.Should().Be(45L);
        (await db.LmsSyncStatuses.SingleAsync()).Status.Should().Be(IntegrationStatus.Success);
    }

    [Fact]
    public async Task DispatchProvision_WhenLmsReturnsConflict_ShouldDeadLetterForManualReview()
    {
        await using var db = CreateInMemoryDb();
        AddProvisionOutbox(db);
        await db.SaveChangesAsync();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.ProvisionStudentAsync(
                It.IsAny<LmsProvisionCommand>(),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LmsIntegrationHttpException("CSCA Course LMS returned HTTP 409.", HttpStatusCode.Conflict));
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Should().Be(new LmsOutboxDispatchResult(1, 0, 0, 1));
        (await db.IntegrationOutboxes.SingleAsync()).Status.Should().Be(IntegrationStatus.DeadLetter);
        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.ProvisioningFailed);
        (await db.IntegrationDeadLetters.SingleAsync()).SourceSystem.Should().Be(LmsIntegrationSourceSystems.CscaCourseLms);
    }

    [Fact]
    public async Task DispatchProvision_AfterManualRetrySucceeds_ShouldResolvePriorDeadLetter()
    {
        await using var db = CreateInMemoryDb();
        AddProvisionOutbox(db);
        await db.SaveChangesAsync();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.ProvisionStudentAsync(
                It.IsAny<LmsProvisionCommand>(),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new LmsIntegrationHttpException("CSCA Course LMS returned HTTP 409.", HttpStatusCode.Conflict));
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        await dispatcher.DispatchPendingAsync(10, CancellationToken.None);
        var retryItem = await db.IntegrationOutboxes.SingleAsync();
        retryItem.Status = IntegrationStatus.Pending;
        retryItem.AttemptCount = 0;
        retryItem.NextAttemptAt = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        client.Setup(value => value.ProvisionStudentAsync(
                It.IsAny<LmsProvisionCommand>(),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LmsProvisionResult(false, 45L, "Provisioned", "corr-lms"));

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Should().Be(new LmsOutboxDispatchResult(1, 1, 0, 0));
        var deadLetter = await db.IntegrationDeadLetters.SingleAsync();
        deadLetter.Resolved.Should().BeTrue();
        deadLetter.ResolvedBy.Should().Be("lms-outbox-dispatcher");
        deadLetter.ResolvedAt.Should().NotBeNull();
        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.Active);
    }

    [Fact]
    public async Task DispatchManagement_WhenOldBatchIsBlocked_ShouldSkipItAndSendNewBatch()
    {
        await using var db = CreateInMemoryDb();
        var link = new LmsCourseLink
        {
            CompanyId = Guid.NewGuid(), CourseId = Guid.NewGuid(), ExternalCourseId = "course-001",
            Status = IntegrationStatus.Processing
        };
        db.LmsCourseLinks.Add(link);
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        AddManagementOutbox(db, link, "class.session.upserted", "old-batch", startedAt,
            IntegrationStatus.DeadLetter, new { lessonDate = "0001-01-01", startTime = "00:00:00", endTime = "00:00:00" });
        AddManagementOutbox(db, link, "class.upserted", "old-batch", startedAt.AddMilliseconds(1));
        AddManagementOutbox(db, link, "course.upserted", "new-batch", startedAt.AddMinutes(1));
        await db.SaveChangesAsync();
        var delivered = new List<string>();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.SendManagementEventAsync(
                It.IsAny<LmsManagementEvent>(), It.IsAny<LmsOutboundRequestContext>(), It.IsAny<CancellationToken>()))
            .Callback<LmsManagementEvent, LmsOutboundRequestContext, CancellationToken>((command, _, _) =>
                delivered.Add(command.EventType))
            .Returns(Task.CompletedTask);
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Succeeded.Should().Be(1);
        delivered.Should().Equal("course.upserted");
        var oldBatch = await db.IntegrationOutboxes.Where(item => item.CorrelationId == "old-batch").ToListAsync();
        oldBatch.Single(item => item.LastError?.Contains("lệnh trước") == true).Status
            .Should().Be(IntegrationStatus.Skipped);
        (await db.IntegrationOutboxes.SingleAsync(item => item.CorrelationId == "new-batch")).Status
            .Should().Be(IntegrationStatus.Success);
    }

    [Fact]
    public async Task DispatchManagement_WhenSessionPayloadIsInvalid_ShouldSkipItAndContinueBatch()
    {
        await using var db = CreateInMemoryDb();
        var link = new LmsCourseLink
        {
            CompanyId = Guid.NewGuid(), CourseId = Guid.NewGuid(), ExternalCourseId = "course-001",
            Status = IntegrationStatus.Success
        };
        db.LmsCourseLinks.Add(link);
        var startedAt = DateTime.UtcNow.AddMinutes(-2);
        AddManagementOutbox(db, link, "class.session.upserted", "batch-001", startedAt,
            payload: new { lessonDate = "0001-01-01", startTime = "00:00:00", endTime = "00:00:00" });
        AddManagementOutbox(db, link, "class.upserted", "batch-001", startedAt.AddMilliseconds(1));
        await db.SaveChangesAsync();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.SendManagementEventAsync(
                It.IsAny<LmsManagementEvent>(), It.IsAny<LmsOutboundRequestContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Processed.Should().Be(2);
        result.Succeeded.Should().Be(1);
        var items = await db.IntegrationOutboxes.OrderBy(item => item.CreatedAt).ToListAsync();
        items[0].Status.Should().Be(IntegrationStatus.Skipped);
        items[0].LastError.Should().Contain("chưa có ngày");
        items[1].Status.Should().Be(IntegrationStatus.Success);
    }

    [Fact]
    public async Task DispatchRoutineCalendarEvent_WhenLinkIsAlreadySuccessful_ShouldKeepLinkSuccessful()
    {
        await using var db = CreateInMemoryDb();
        var link = new LmsCourseLink
        {
            CompanyId = Guid.NewGuid(),
            CourseId = Guid.NewGuid(),
            ExternalCourseId = "course-001",
            Status = IntegrationStatus.Success,
            LastSyncedAt = DateTime.UtcNow.AddHours(-1)
        };
        db.LmsCourseLinks.Add(link);
        AddManagementOutbox(db, link, "class.schedule.upserted", "calendar-update", DateTime.UtcNow,
            payload: new { classSourceId = Guid.NewGuid().ToString("N"), dayOfWeek = 2 });
        await db.SaveChangesAsync();
        var client = new Mock<ICscaCourseLmsClient>();
        client.Setup(value => value.SendManagementEventAsync(
                It.IsAny<LmsManagementEvent>(), It.IsAny<LmsOutboundRequestContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var dispatcher = new LmsOutboxDispatcher(db, client.Object, NullLogger<LmsOutboxDispatcher>.Instance);

        var result = await dispatcher.DispatchPendingAsync(10, CancellationToken.None);

        result.Succeeded.Should().Be(1);
        (await db.LmsCourseLinks.SingleAsync()).Status.Should().Be(IntegrationStatus.Success);
    }

    private static LmsAccountLink AddProvisionOutbox(ApplicationDbContext db)
    {
        var account = new LmsAccountLink
        {
            CompanyId = Guid.NewGuid(),
            CscaClassStudentId = Guid.NewGuid(),
            ExternalStudentId = "student-001",
            LmsEmail = "student@example.com",
            Status = LmsAccountStatus.Active
        };
        var payload = new LmsProvisionCommand(
            account.ExternalStudentId,
            null,
            "Nguyen Van A",
            account.LmsEmail,
            null,
            "Active",
            "Paid",
            ["csca-foundation"],
            "class-001",
            DateTime.UtcNow);
        db.LmsAccountLinks.Add(account);
        db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = account.CompanyId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = "evt-001",
            EventType = LmsOutboxEventTypes.StudentProvisionRequested,
            AggregateType = nameof(LmsAccountLink),
            AggregateId = account.Id.ToString("N"),
            IdempotencyKey = "provision:student-001",
            CorrelationId = "corr-001",
            PayloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = IntegrationStatus.Pending,
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
        });
        return account;
    }

    private static void AddManagementOutbox(
        ApplicationDbContext db,
        LmsCourseLink link,
        string managementEventType,
        string correlationId,
        DateTime createdAt,
        IntegrationStatus status = IntegrationStatus.Pending,
        object? payload = null)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var command = new LmsManagementEvent(eventId, managementEventType, createdAt,
            "internal-management", payload ?? new { sourceUpdatedAt = createdAt });
        db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = link.CompanyId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = eventId,
            EventType = LmsOutboxEventTypes.ManagementEventRequested,
            AggregateType = nameof(LmsCourseLink),
            AggregateId = link.Id.ToString("N"),
            IdempotencyKey = $"management:{eventId}",
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            Status = status,
            NextAttemptAt = status == IntegrationStatus.DeadLetter ? null : createdAt,
            CreatedAt = createdAt
        });
    }

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LmsOutboxDispatcher_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
