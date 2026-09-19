using Microsoft.EntityFrameworkCore;
using InternalManagement.Infrastructure.Persistence;
using DotNet.Testcontainers.Containers;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace InternalManagement.IntegrationTests;

/// <summary>
/// PostgreSQL thật cho các bài test Ngày 18.
/// Mặc định không bật để các bài test nhanh vẫn chạy được trên máy không có Docker;
/// chạy RUN_POSTGRES_TESTS=true dotnet test để bật fixture này.
/// </summary>
public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private const string ContainerBackupPath = "/var/lib/postgresql/backups";
    private readonly string _hostBackupPath = Path.Combine(
        Path.GetTempPath(),
        "moli-internal-management-backups",
        Guid.NewGuid().ToString("N"));

    private readonly PostgreSqlContainer _container;

    public PostgreSqlContainerFixture()
    {
        DatabaseName = $"moli_test_{Guid.NewGuid():N}";
        _container = new PostgreSqlBuilder("postgres:17-alpine")
            .WithDatabase(DatabaseName)
            .WithUsername("postgres")
            .WithPassword("postgres_password")
            .WithBindMount(_hostBackupPath, ContainerBackupPath)
            .Build();
    }

    public string DatabaseName { get; }
    public string ConnectionString => _container.GetConnectionString();
    public string HostBackupPath => _hostBackupPath;
    public string ContainerBackupPathValue => ContainerBackupPath;
    public bool Enabled { get; private set; }
    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (!IsEnabledByEnvironment())
        {
            SkipReason = "PostgreSQL/Testcontainers đang tắt. Đặt RUN_POSTGRES_TESTS=true để chạy.";
            return;
        }

        try
        {
            Directory.CreateDirectory(_hostBackupPath);
            await _container.StartAsync();
            Enabled = true;
        }
        catch (Exception ex)
        {
            SkipReason = $"Không khởi động được PostgreSQL Testcontainer: {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();

        if (Directory.Exists(_hostBackupPath))
            Directory.Delete(_hostBackupPath, recursive: true);
    }

    public ApplicationDbContext CreateDbContext()
    {
        EnsureEnabled();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(ConnectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new ApplicationDbContext(options);
    }

    public void EnsureEnabled()
    {
        if (!Enabled)
            throw SkipException.ForSkip(SkipReason ?? "PostgreSQL Testcontainer không khả dụng.");
    }

    public Task<ExecResult> ExecAsync(IList<string> command, CancellationToken ct = default)
    {
        EnsureEnabled();
        return _container.ExecAsync(command, ct);
    }

    private static bool IsEnabledByEnvironment() =>
        string.Equals(
            Environment.GetEnvironmentVariable("RUN_POSTGRES_TESTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
}

[CollectionDefinition("PostgreSQL integration", DisableParallelization = true)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlContainerFixture>
{
}
