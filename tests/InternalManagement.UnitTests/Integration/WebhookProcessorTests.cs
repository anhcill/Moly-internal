using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Persistence;

namespace InternalManagement.UnitTests.Integration;

public class WebhookProcessorTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("WebhookProcessorTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Ingest_NewEvent_ShouldSaveToInbox()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        // Act
        var (accepted, message) = await processor.IngestAsync(
            "WEBSITE_EDTECH", "evt_001", "course.updated",
            "{\"courseId\":123}", null, CancellationToken.None);

        // Assert
        accepted.Should().BeTrue();
        message.Should().Contain("tiếp nhận thành công");

        var inbox = await db.IntegrationInboxes.FirstOrDefaultAsync();
        inbox.Should().NotBeNull();
        inbox!.SourceSystem.Should().Be("WEBSITE_EDTECH");
        inbox.EventId.Should().Be("evt_001");
        inbox.EventType.Should().Be("course.updated");
        inbox.Status.Should().Be(IntegrationStatus.Pending);
    }

    [Fact]
    public async Task Ingest_DuplicateEvent_ShouldReturnAcceptedWithoutDuplicate()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        // First ingest
        await processor.IngestAsync(
            "WEBSITE_EDTECH", "evt_002", "payment.created",
            "{\"paymentId\":456}", null, CancellationToken.None);

        // Act: duplicate ingest
        var (accepted, message) = await processor.IngestAsync(
            "WEBSITE_EDTECH", "evt_002", "payment.created",
            "{\"paymentId\":456}", null, CancellationToken.None);

        // Assert
        accepted.Should().BeTrue();
        message.Should().Contain("idempotent");

        // Should only have 1 record
        var count = await db.IntegrationInboxes.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPending_ShouldMarkItemsAsSuccess()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        await processor.IngestAsync("SRC", "evt_a", "type.a", "{}", null, CancellationToken.None);
        await processor.IngestAsync("SRC", "evt_b", "type.b", "{}", null, CancellationToken.None);

        // Act
        var processed = await processor.ProcessPendingAsync(10, CancellationToken.None);

        // Assert
        processed.Should().Be(2);

        var items = await db.IntegrationInboxes.ToListAsync();
        items.Should().AllSatisfy(i =>
        {
            i.Status.Should().Be(IntegrationStatus.Success);
            i.ProcessedAt.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task ProcessPending_WithNoPending_ShouldReturnZero()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        // Act
        var processed = await processor.ProcessPendingAsync(10, CancellationToken.None);

        // Assert
        processed.Should().Be(0);
    }
}
