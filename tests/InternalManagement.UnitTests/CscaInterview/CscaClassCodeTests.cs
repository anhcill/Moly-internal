using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.CscaInterview;

public class CscaClassCodeTests
{
    [Fact]
    public async Task DeletedClassCode_ShouldRemainReservedAndNextCodeShouldAdvance()
    {
        using var db = CreateInMemoryDb();
        var service = new CscaService(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);
        var reservedCode = $"CSCA-{DateTime.UtcNow.Year}-01";

        var created = await service.CreateClassAsync(
            new CreateCscaClassRequest(reservedCode, "Deleted class", "Batch 1", "", 1000000m),
            CancellationToken.None);
        created.Succeeded.Should().BeTrue();
        (await service.DeleteClassAsync(created.Value!.Id, CancellationToken.None)).Succeeded.Should().BeTrue();

        var nextCode = await service.GetNextClassCodeAsync(CancellationToken.None);
        var duplicate = await service.CreateClassAsync(
            new CreateCscaClassRequest(reservedCode, "Replacement class", "Batch 1", "", 1000000m),
            CancellationToken.None);

        nextCode.Value.Should().Be($"CSCA-{DateTime.UtcNow.Year}-02");
        duplicate.Succeeded.Should().BeFalse();
        duplicate.Errors.Should().ContainSingle(error => error.Contains(reservedCode));
    }

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("CscaClassCodeTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }
}
