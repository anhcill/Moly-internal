using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Integration.Mappers;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using System.Text.Json;

namespace InternalManagement.UnitTests.Integration;

public class SyncEngineTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("SyncEngineTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    private SyncEngine CreateEngine(ApplicationDbContext db, IExternalConnector connector, IEntityMapper[]? mappers = null)
    {
        var factory = new ConnectorFactory([connector]);
        var mapperFactory = new EntityMapperFactory(mappers ?? []);
        var deadLetterService = new DeadLetterService(db, new CurrentUserService(null!), NullLogger<DeadLetterService>.Instance);
        return new SyncEngine(db, factory, mapperFactory, deadLetterService, NullLogger<SyncEngine>.Instance);
    }

    [Fact]
    public async Task ExecutePull_ShouldCreateRunAndRecordResults()
    {
        using var db = CreateInMemoryDb();
        var engine = CreateEngine(db, new NullConnector());

        var result = await engine.ExecutePullAsync("NULL_CONNECTOR", "Test", false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.SourceSystem.Should().Be("NULL_CONNECTOR");

        var run = await db.IntegrationRuns.FirstOrDefaultAsync();
        run.Should().NotBeNull();
        run!.Status.Should().Be(IntegrationStatus.Success);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutePull_WithMultiplePages_ShouldAdvanceCursor()
    {
        using var db = CreateInMemoryDb();
        var engine = CreateEngine(db, new MultiPageTestConnector(totalPages: 3, itemsPerPage: 5));

        var result = await engine.ExecutePullAsync("MULTI_PAGE_TEST", "Items", false, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.RecordsRead.Should().Be(15);

        var run = await db.IntegrationRuns.FirstOrDefaultAsync();
        run!.Status.Should().Be(IntegrationStatus.Success);
        run.RecordsRead.Should().Be(15);
        run.Cursor.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecutePull_WhenConnectorThrows_ShouldMarkRunAsFailed()
    {
        using var db = CreateInMemoryDb();
        var engine = CreateEngine(db, new FailingTestConnector());

        var result = await engine.ExecutePullAsync("FAILING_CONNECTOR", "Test", false, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Simulated connector failure");

        var run = await db.IntegrationRuns.FirstOrDefaultAsync();
        run!.Status.Should().Be(IntegrationStatus.Failed);
        run.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutePull_ForceFullSync_ShouldResetCursor()
    {
        using var db = CreateInMemoryDb();
        var connector = new CursorTrackingConnector();
        var engine = CreateEngine(db, connector);

        await engine.ExecutePullAsync("CURSOR_TRACKING", "Items", false, CancellationToken.None);
        var result = await engine.ExecutePullAsync("CURSOR_TRACKING", "Items", true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        connector.LastReceivedCursor.Should().NotBeNull();
        connector.LastReceivedCursor!.UpdatedSince.Should().BeNull();
        connector.LastReceivedCursor.Offset.Should().Be(0);
    }

    [Fact]
    public async Task GetRuns_ShouldReturnPaginatedResults()
    {
        using var db = CreateInMemoryDb();
        var engine = CreateEngine(db, new NullConnector());

        await engine.ExecutePullAsync("NULL_CONNECTOR", "Test", false, CancellationToken.None);
        await engine.ExecutePullAsync("NULL_CONNECTOR", "Test", false, CancellationToken.None);
        await engine.ExecutePullAsync("NULL_CONNECTOR", "Test", false, CancellationToken.None);

        var result = await engine.GetRunsAsync(null, null, null, 1, 2, CancellationToken.None);

        result.TotalCount.Should().Be(3);
        result.Items.Count.Should().Be(2);
        result.HasNextPage.Should().BeTrue();
    }

    // ── Test connectors ──

    private sealed class MultiPageTestConnector : IExternalConnector
    {
        private readonly int _totalPages;
        private readonly int _itemsPerPage;
        private int _currentPage;

        public MultiPageTestConnector(int totalPages, int itemsPerPage)
        {
            _totalPages = totalPages;
            _itemsPerPage = itemsPerPage;
        }

        public string SourceSystem => "MULTI_PAGE_TEST";
        public IReadOnlyList<string> SupportedEntityTypes { get; } = ["Items"];

        public Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct)
        {
            _currentPage++;
            var items = Enumerable.Range(1, _itemsPerPage)
                .Select(i => JsonSerializer.SerializeToElement(new { id = i, page = _currentPage }))
                .ToList();

            return Task.FromResult(new SyncPage
            {
                Items = items,
                HasMore = _currentPage < _totalPages,
                NextCursor = new SyncCursor { Offset = _currentPage * _itemsPerPage }
            });
        }

        public Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct) =>
            Task.FromResult(new ConnectorHealth { SourceSystem = SourceSystem, IsHealthy = true });
    }

    private sealed class FailingTestConnector : IExternalConnector
    {
        public string SourceSystem => "FAILING_CONNECTOR";
        public IReadOnlyList<string> SupportedEntityTypes { get; } = ["Test"];

        public Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated connector failure");

        public Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct) =>
            Task.FromResult(new ConnectorHealth { SourceSystem = SourceSystem, IsHealthy = false, Message = "Failing" });
    }

    private sealed class CursorTrackingConnector : IExternalConnector
    {
        public string SourceSystem => "CURSOR_TRACKING";
        public IReadOnlyList<string> SupportedEntityTypes { get; } = ["Items"];
        public SyncCursor? LastReceivedCursor { get; private set; }

        public Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct)
        {
            LastReceivedCursor = cursor;
            return Task.FromResult(new SyncPage
            {
                Items = [JsonSerializer.SerializeToElement(new { id = 1 })],
                HasMore = false,
                NextCursor = new SyncCursor { UpdatedSince = DateTime.UtcNow, Offset = 1 }
            });
        }

        public Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct) =>
            Task.FromResult(new ConnectorHealth { SourceSystem = SourceSystem, IsHealthy = true });
    }
}
