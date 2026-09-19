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

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LmsOutboxDispatcher_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }
}
