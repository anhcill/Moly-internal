using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Security;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence;

public partial class DatabaseSeeder : IDatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SeedCompanyAndBusinessUnitsAsync(cancellationToken);
            await SeedPermissionsAndRolesAsync(cancellationToken);
            await SeedUsersAsync(cancellationToken);
            await SeedCscaAndInterviewAsync(cancellationToken);
            await SeedEmployeesAndAttendanceAsync(cancellationToken);
            await SeedPayrollAsync(cancellationToken);
            await SeedEdTechDemoDataAsync(cancellationToken);
            await SeedFashionAndInventoryAsync(cancellationToken);
            await SeedManufacturingCostingAsync(cancellationToken);
            _logger.LogInformation("Database seeded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

}
