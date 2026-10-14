using FluentAssertions;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace InternalManagement.UnitTests.CscaInterview;

public class CscaJitsiMeetingTests
{
    [Fact]
    public async Task CreateOnlineSessions_WithoutMeetingUrl_ShouldGenerateUniqueStableJitsiUrls()
    {
        await using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var classId = await CreateClassAsync(service, "CSCA-JITSI-ONLINE");

        var first = await service.CreateLessonSessionAsync(classId,
            new CreateCscaLessonSessionRequest(new DateOnly(2026, 9, 7), TimeSpan.FromHours(18), TimeSpan.FromHours(20)),
            CancellationToken.None);
        var second = await service.CreateLessonSessionAsync(classId,
            new CreateCscaLessonSessionRequest(new DateOnly(2026, 9, 8), TimeSpan.FromHours(18), TimeSpan.FromHours(20)),
            CancellationToken.None);

        first.Succeeded.Should().BeTrue();
        second.Succeeded.Should().BeTrue();
        first.Value!.MeetingUrl.Should().MatchRegex(
            $"^https://meet\\.jit\\.si/csca-{first.Value.Id:N}-[0-9a-f]{{36}}$");
        second.Value!.MeetingUrl.Should().MatchRegex(
            $"^https://meet\\.jit\\.si/csca-{second.Value.Id:N}-[0-9a-f]{{36}}$");
        second.Value.MeetingUrl.Should().NotBe(first.Value.MeetingUrl);

        var updated = await service.UpdateLessonSessionAsync(classId, first.Value.Id,
            new UpdateCscaLessonSessionRequest(
                new DateOnly(2026, 9, 7), TimeSpan.FromHours(19), TimeSpan.FromHours(21),
                ChangeReason: "Điều chỉnh giờ học"),
            CancellationToken.None);

        updated.Succeeded.Should().BeTrue();
        updated.Value!.MeetingUrl.Should().Be(first.Value.MeetingUrl);
    }

    [Fact]
    public async Task CreatePhysicalSession_WithoutMeetingUrl_ShouldLeaveMeetingUrlEmpty()
    {
        await using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var classId = await CreateClassAsync(service, "CSCA-JITSI-PHYSICAL");
        var classroom = await service.CreateClassroomAsync(
            new CreateCscaClassroomRequest("P-JITSI", "Phòng học trực tiếp", 10), CancellationToken.None);

        var result = await service.CreateLessonSessionAsync(classId,
            new CreateCscaLessonSessionRequest(
                new DateOnly(2026, 9, 7), TimeSpan.FromHours(18), TimeSpan.FromHours(20), classroom.Value!.Id),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value!.MeetingUrl.Should().BeNull();
    }

    [Fact]
    public async Task GenerateRecurringSessions_ShouldUsePerSessionJitsiUrlsAndPreserveExplicitOrPhysicalSettings()
    {
        await using var db = CreateInMemoryDb();
        var service = CreateService(db);
        var classId = await CreateClassAsync(service, "CSCA-JITSI-RECURRING");
        const string explicitUrl = "https://zoom.us/j/123456789";

        (await service.AddScheduleAsync(classId,
            new CreateCscaScheduleRequest(1, TimeSpan.FromHours(9), TimeSpan.FromHours(10)), CancellationToken.None))
            .Succeeded.Should().BeTrue();
        (await service.AddScheduleAsync(classId,
            new CreateCscaScheduleRequest(2, TimeSpan.FromHours(9), TimeSpan.FromHours(10), Room: "Phòng A"), CancellationToken.None))
            .Succeeded.Should().BeTrue();
        (await service.AddScheduleAsync(classId,
            new CreateCscaScheduleRequest(3, TimeSpan.FromHours(9), TimeSpan.FromHours(10), MeetingUrl: explicitUrl), CancellationToken.None))
            .Succeeded.Should().BeTrue();

        var generated = await service.GenerateLessonSessionsAsync(classId,
            new GenerateCscaLessonSessionsRequest(new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 16)),
            CancellationToken.None);
        var sessions = await service.GetLessonSessionsAsync(classId, null, null, CancellationToken.None);

        generated.Value.Should().Be(6);
        var onlineSessions = sessions.Where(item => item.LessonDate.DayOfWeek == DayOfWeek.Monday).ToList();
        onlineSessions.Should().HaveCount(2);
        onlineSessions.Select(item => item.MeetingUrl).Should().OnlyContain(url =>
            url != null && url.StartsWith("https://meet.jit.si/csca-", StringComparison.Ordinal));
        onlineSessions.Select(item => item.MeetingUrl).Should().OnlyHaveUniqueItems();

        sessions.Where(item => item.LessonDate.DayOfWeek == DayOfWeek.Tuesday)
            .Should().OnlyContain(item => item.MeetingUrl == null);
        sessions.Where(item => item.LessonDate.DayOfWeek == DayOfWeek.Wednesday)
            .Should().OnlyContain(item => item.MeetingUrl == explicitUrl);
    }

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("CscaJitsiMeetingTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CscaService CreateService(ApplicationDbContext db) =>
        new(db, new CurrentUserService(null!), NullLogger<CscaService>.Instance);

    private static async Task<Guid> CreateClassAsync(CscaService service, string code)
    {
        var result = await service.CreateClassAsync(
            new CreateCscaClassRequest(code, code, "Batch 1", string.Empty, 1_000_000m),
            CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        return result.Value!.Id;
    }
}
