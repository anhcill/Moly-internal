using System.Reflection;
using FluentAssertions;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace InternalManagement.UnitTests.Persistence;

public sealed class MigrationMetadataTests
{
    [Fact]
    public void EveryMigration_ShouldBeDiscoverableByEntityFramework()
    {
        var migrations = typeof(ApplicationDbContext).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(Migration).IsAssignableFrom(type))
            .ToList();

        migrations.Should().NotBeEmpty();
        var undiscoverable = migrations.Where(type =>
            type.GetCustomAttribute<MigrationAttribute>() is null ||
            type.GetCustomAttribute<DbContextAttribute>()?.ContextType != typeof(ApplicationDbContext))
            .Select(type => type.Name);
        undiscoverable.Should().BeEmpty();
        migrations.Select(type => type.GetCustomAttribute<MigrationAttribute>()!.Id)
            .Should().OnlyHaveUniqueItems();
    }
}
