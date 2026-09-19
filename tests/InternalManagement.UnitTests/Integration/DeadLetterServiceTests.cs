using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.Integration;

public class DeadLetterServiceTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("DeadLetterTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Add_ShouldCreateDeadLetterRecord()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new DeadLetterService(db, currentUser, NullLogger<DeadLetterService>.Instance);

        // Act
        await service.AddAsync(
            "WEBSITE_EDTECH", "Courses", "course_123",
            "{\"id\":123}", "SCHEMA_ERROR", "Missing required field: title",
            CancellationToken.None);

        // Assert
        var deadLetter = await db.IntegrationDeadLetters.FirstOrDefaultAsync();
        deadLetter.Should().NotBeNull();
        deadLetter!.SourceSystem.Should().Be("WEBSITE_EDTECH");
        deadLetter.EntityType.Should().Be("Courses");
        deadLetter.SourceId.Should().Be("course_123");
        deadLetter.ErrorCode.Should().Be("SCHEMA_ERROR");
        deadLetter.ErrorMessage.Should().Contain("title");
        deadLetter.RetryCount.Should().Be(0);
        deadLetter.Resolved.Should().BeFalse();
    }

    [Fact]
    public async Task Retry_ShouldIncrementRetryCountAndResolve()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new DeadLetterService(db, currentUser, NullLogger<DeadLetterService>.Instance);

        await service.AddAsync(
            "WEBSITE_EDTECH", "Questions", "q_456",
            "{\"id\":456}", "VALIDATION", "Invalid format",
            CancellationToken.None);

        var deadLetter = await db.IntegrationDeadLetters.FirstAsync();

        // Act
        var success = await service.RetryAsync(deadLetter.Id, CancellationToken.None);

        // Assert
        success.Should().BeTrue();

        var updated = await db.IntegrationDeadLetters.FirstAsync();
        updated.RetryCount.Should().Be(1);
        updated.Resolved.Should().BeTrue();
        updated.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Retry_NonExistentId_ShouldReturnFalse()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new DeadLetterService(db, currentUser, NullLogger<DeadLetterService>.Instance);

        // Act
        var success = await service.RetryAsync(Guid.NewGuid(), CancellationToken.None);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public async Task GetList_ShouldFilterAndPaginate()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new DeadLetterService(db, currentUser, NullLogger<DeadLetterService>.Instance);

        await service.AddAsync("SRC_A", "TypeA", "id1", "{}", "E1", "Err1", CancellationToken.None);
        await service.AddAsync("SRC_A", "TypeA", "id2", "{}", "E2", "Err2", CancellationToken.None);
        await service.AddAsync("SRC_B", "TypeB", "id3", "{}", "E3", "Err3", CancellationToken.None);

        // Act: filter by sourceSystem
        var resultA = await service.GetListAsync("SRC_A", null, 1, 10, CancellationToken.None);

        // Assert
        resultA.TotalCount.Should().Be(2);
        resultA.Items.Should().AllSatisfy(d => d.SourceSystem.Should().Be("SRC_A"));

        // Act: paginate
        var resultPage = await service.GetListAsync(null, null, 1, 2, CancellationToken.None);
        resultPage.Items.Count.Should().Be(2);
        resultPage.TotalCount.Should().Be(3);
        resultPage.HasNextPage.Should().BeTrue();
    }
}
