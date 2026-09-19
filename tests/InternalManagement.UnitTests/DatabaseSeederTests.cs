using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Security;

namespace InternalManagement.UnitTests;

public class DatabaseSeederTests
{
    [Fact]
    public async Task SeedAsync_ShouldSeedAllInitialDataSuccessfully()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("SeederTestDb_" + Guid.NewGuid())
            .Options;

        using var context = new ApplicationDbContext(options);
        var passwordHasher = new PasswordHasher();
        var seeder = new DatabaseSeeder(context, passwordHasher, NullLogger<DatabaseSeeder>.Instance);

        // Act
        await seeder.SeedAsync();

        // Assert
        var company = await context.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI");
        company.Should().NotBeNull();

        var adminUser = await context.Users
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Username == "admin");

        adminUser.Should().NotBeNull();
        passwordHasher.VerifyPassword("Admin@123456", adminUser!.PasswordHash).Should().BeTrue();
        adminUser.UserRoles.Should().NotBeEmpty();
        adminUser.UserRoles.First().Role.Code.Should().Be("SuperAdmin");
    }
}
