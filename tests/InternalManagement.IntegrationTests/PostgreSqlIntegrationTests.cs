using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;

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
