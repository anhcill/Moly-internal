using FluentAssertions;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class PostgreSqlIntegrationTests
{
    private readonly PostgreSqlContainerFixture _fixture;

    public PostgreSqlIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task TwoInstancesReservingLastSku_ShouldPersistOnlyOneOrder()
    {
        if (!_fixture.Enabled)
            return;

        Guid companyId;
        Guid businessUnitId;
        Guid warehouseId;
        Guid variantId;
        await using (var setup = _fixture.CreateDbContext())
        {
            await setup.Database.MigrateAsync();
            var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
            var company = new Company { Code = $"STK-{suffix}", Name = "Stock concurrency test" };
            var unit = new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "Fashion" };
            var warehouse = new Warehouse
            {
                CompanyId = company.Id, BusinessUnitId = unit.Id,
                Code = "MAIN", Name = "Main", IsDefault = true
            };
            var product = new Product
            {
                CompanyId = company.Id, BusinessUnitId = unit.Id,
                Code = $"P-{suffix}", Name = "Concurrency SKU"
            };
            var variant = new ProductVariant
            {
                ProductId = product.Id, Sku = $"SKU-{suffix}",
                CostPrice = 100m, SellingPrice = 200m, CostStatus = CostStatus.Actual
            };
            var balance = new InventoryBalance
            {
                CompanyId = company.Id, BusinessUnitId = unit.Id,
                WarehouseId = warehouse.Id, ProductVariantId = variant.Id,
                OnHandQuantity = 1, ReservedQuantity = 0
            };
            var policy = new ChannelFeePolicy
            {
                CompanyId = company.Id, BusinessUnitId = unit.Id,
                Code = $"STORE-{suffix}", Channel = "STORE", VersionNumber = 1,
                EffectiveFrom = DateTime.UtcNow.AddDays(-1)
            };
            setup.AddRange(company, unit, warehouse, product, variant, balance, policy);
            await setup.SaveChangesAsync();
            companyId = company.Id;
            businessUnitId = unit.Id;
            warehouseId = warehouse.Id;
            variantId = variant.Id;
        }

        var barrier = new InventorySaveBarrier();
        await using var firstDb = CreateDbContextWithInterceptor(barrier);
        await using var secondDb = CreateDbContextWithInterceptor(barrier);
        var user = new TestCurrentUser(companyId, businessUnitId);
        var logger = new CaptureOrderLogger();
        var first = new OrderCostingService(firstDb, user, logger);
        var second = new OrderCostingService(secondDb, user, logger);
        var item = new CreateSalesOrderItemRequest(variantId, 1, 200m);
        var firstRequest = new CreateSalesOrderRequest("STORE", "CONCURRENT-1", "Customer 1", null, null,
            0m, 0m, 0m, [item], WarehouseId: warehouseId);
        var secondRequest = firstRequest with { SourceOrderId = "CONCURRENT-2", CustomerName = "Customer 2" };

        var results = await Task.WhenAll(
            first.CreateOrderAndSnapshotAsync(firstRequest, CancellationToken.None),
            second.CreateOrderAndSnapshotAsync(secondRequest, CancellationToken.None));

        results.Count(result => result.Succeeded).Should().Be(1,
            "response errors were: {0}; database errors were: {1}",
            string.Join(" | ", results.SelectMany(result => result.Errors)),
            string.Join(" | ", logger.Errors.Select(error => error.GetBaseException().Message)));
        results.Count(result => !result.Succeeded).Should().Be(1);
        results.Single(result => !result.Succeeded).Errors.Should().ContainSingle()
            .Which.Should().Contain("Tồn kho vừa thay đổi");

        await using var verify = _fixture.CreateDbContext();
        var persisted = await verify.InventoryBalances.SingleAsync(b =>
            b.CompanyId == companyId && b.WarehouseId == warehouseId && b.ProductVariantId == variantId);
        persisted.OnHandQuantity.Should().Be(1);
        persisted.ReservedQuantity.Should().Be(1);
        (await verify.SalesOrders.CountAsync(o => o.CompanyId == companyId)).Should().Be(1);
        (await verify.OrderCostSnapshots.CountAsync(o => o.CompanyId == companyId)).Should().Be(1);
    }

    private ApplicationDbContext CreateDbContextWithInterceptor(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(interceptor)
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class InventorySaveBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            eventData.Context?.ChangeTracker.DetectChanges();
            if (eventData.Context?.ChangeTracker.Entries<InventoryBalance>()
                .Any(entry => entry.State == EntityState.Modified) == true)
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothReady.TrySetResult();
                await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    private sealed class CaptureOrderLogger : ILogger<OrderCostingService>
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
                Errors.Enqueue(exception);
        }
    }

    private sealed record TestCurrentUser(Guid Company, Guid Unit) : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? Username => "stock-test";
        public Guid? CompanyId => Company;
        public Guid? BusinessUnitId => Unit;
        public IReadOnlyList<string> Permissions => [];
        public bool HasPermission(string permission) => false;
    }

    [Fact]
    public async Task MigrationsAndFinanceLedger_ShouldRoundTripAgainstPostgreSql()
    {
        if (!_fixture.Enabled)
            return;

        await using var db = _fixture.CreateDbContext();
        await db.Database.MigrateAsync();

        var company = new Company { Code = $"PG-{Guid.NewGuid():N}"[..11], Name = "MOLY PostgreSQL Test" };
        var businessUnit = new BusinessUnit
        {
            CompanyId = company.Id,
            Code = "FINANCE",
            Name = "Tài chính PostgreSQL"
        };
        var category = new FinanceCategory
        {
            CompanyId = company.Id,
            BusinessUnitId = businessUnit.Id,
            Code = $"TEST-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            Name = "Thu kiểm thử",
            Type = TransactionType.Income
        };
        var transaction = new FinanceTransaction
        {
            CompanyId = company.Id,
            BusinessUnitId = businessUnit.Id,
            Category = category,
            TransactionType = TransactionType.Income,
            Amount = 123_456m,
            TransactionDate = DateTime.UtcNow,
            ReferenceType = "IntegrationTest",
            ReferenceId = Guid.NewGuid(),
            Description = "Round-trip PostgreSQL"
        };

        db.AddRange(company, businessUnit, category, transaction);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await db.FinanceTransactions
            .Include(x => x.Category)
            .SingleAsync(x => x.Id == transaction.Id);

        loaded.Amount.Should().Be(123_456m);
        loaded.Category!.Code.Should().Be(category.Code);
        loaded.TransactionType.Should().Be(TransactionType.Income);
    }

    [Fact]
    public async Task BackupAndRestore_ShouldRecoverDeletedMarkerRow()
    {
        if (!_fixture.Enabled)
            return;

        await using (var db = _fixture.CreateDbContext())
        {
            await db.Database.MigrateAsync();
            db.Companies.Add(new Company
            {
                Code = $"BKP-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
                Name = "Marker backup restore"
            });
            await db.SaveChangesAsync();
        }

        var backupFile = $"moli-backup-{Guid.NewGuid():N}.dump";
        var backupResult = await _fixture.ExecAsync(new[]
        {
            "pg_dump",
            "--username=postgres",
            $"--dbname={_fixture.DatabaseName}",
            "--format=custom",
            "--no-owner",
            "--no-privileges",
            $"--file={_fixture.ContainerBackupPathValue}/{backupFile}"
        });

        backupResult.ExitCode.Should().Be(0, backupResult.Stderr);
        var backupPath = Path.Combine(_fixture.HostBackupPath, backupFile);
        File.Exists(backupPath).Should().BeTrue();
        new FileInfo(backupPath).Length.Should().BeGreaterThan(0);

        await using (var db = _fixture.CreateDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM companies WHERE name = 'Marker backup restore'");
            (await db.Companies.CountAsync(x => x.Name == "Marker backup restore")).Should().Be(0);
        }

        var restoreResult = await _fixture.ExecAsync(new[]
        {
            "pg_restore",
            "--username=postgres",
            $"--dbname={_fixture.DatabaseName}",
            "--clean",
            "--if-exists",
            "--no-owner",
            "--no-privileges",
            "--exit-on-error",
            $"{_fixture.ContainerBackupPathValue}/{backupFile}"
        });

        restoreResult.ExitCode.Should().Be(0, restoreResult.Stderr);
        await using var restoredDb = _fixture.CreateDbContext();
        (await restoredDb.Companies.CountAsync(x => x.Name == "Marker backup restore")).Should().Be(1);
    }
}
