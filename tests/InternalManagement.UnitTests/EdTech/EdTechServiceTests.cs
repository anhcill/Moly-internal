using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.EdTech.DTOs;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.EdTech;

public class EdTechServiceTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("EdTechServiceTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateCourse_WithValidData_ShouldCreateCourseAndModules()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EdTechService(db, currentUser, NullLogger<EdTechService>.Instance);

        var request = new CreateCourseRequest(
            "Khóa học C# Chuyên sâu",
            "csharp-chuyen-sau",
            "Mô tả khóa học",
            "https://cdn.moli.vn/csharp-adv.jpg",
            799000m,
            "Published",
            ["Mở đầu", "Generics & Collections", "Async/Await & Concurrency"]);

        // Act
        var result = await service.CreateCourseAsync(request, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("Khóa học C# Chuyên sâu");
        result.Value.Price.Should().Be(799000m);
        result.Value.Modules.Count.Should().Be(3);
        result.Value.Modules[0].Title.Should().Be("Mở đầu");
    }

    [Fact]
    public async Task CreateQuestion_WithChoices_ShouldCreateQuestionAndVersionOne()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EdTechService(db, currentUser, NullLogger<EdTechService>.Instance);

        var request = new CreateQuestionRequest(
            "C# Basics Bank",
            "Lập trình",
            "Cấu trúc dữ liệu",
            "Easy",
            "<p>Khai báo mảng nào đúng trong C#?</p>",
            "<p>int[] arr = new int[5];</p>",
            [
                new CreateQuestionChoiceRequest("A", "int[] arr = new int[5];", true, 0),
                new CreateQuestionChoiceRequest("B", "int arr[] = new int[5];", false, 1)
            ],
            ["csharp", "arrays"]);

        // Act
        var result = await service.CreateQuestionAsync(request, CancellationToken.None);

        // Assert
        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.CurrentVersionNumber.Should().Be(1);
        result.Value.Status.Should().Be("Draft");
        result.Value.Choices.Count.Should().Be(2);
        result.Value.Choices.Should().Contain(c => c.IsCorrect && c.Label == "A");
    }

    [Fact]
    public async Task CreateQuestionVersion_ShouldIncrementVersionNumber()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EdTechService(db, currentUser, NullLogger<EdTechService>.Instance);

        var createReq = new CreateQuestionRequest(
            "Bank 1", "Math", "Algebra", "Medium",
            "<p>1 + 1 = ?</p>", null,
            [
                new CreateQuestionChoiceRequest("A", "2", true, 0),
                new CreateQuestionChoiceRequest("B", "3", false, 1)
            ]);

        var question = (await service.CreateQuestionAsync(createReq, CancellationToken.None)).Value!;

        // Act: Create Version 2 with refined choices
        var versionReq = new CreateQuestionVersionRequest(
            "<p>1 + 1 = ? (Chọn đáp án chính xác nhất)</p>",
            "<p>Giải thích: 1 + 1 luôn bằng 2</p>",
            [
                new CreateQuestionChoiceRequest("A", "2", true, 0),
                new CreateQuestionChoiceRequest("B", "3", false, 1),
                new CreateQuestionChoiceRequest("C", "4", false, 2)
            ]);

        var updated = await service.CreateQuestionVersionAsync(question.Id, versionReq, CancellationToken.None);

        // Assert
        updated.Succeeded.Should().BeTrue();
        updated.Value!.CurrentVersionNumber.Should().Be(2);
        updated.Value.Choices.Count.Should().Be(3);
        updated.Value.Versions.Count.Should().Be(2);
    }

    [Fact]
    public async Task PublishQuestionVersion_ShouldUpdateStatusAndCreatePublicationRecord()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var currentUser = new CurrentUserService(null!);
        var service = new EdTechService(db, currentUser, NullLogger<EdTechService>.Instance);

        var createReq = new CreateQuestionRequest(
            "Bank 2", "IT", "Networking", "Hard",
            "<p>Giao thức nào thuộc tầng Transport?</p>", null,
            [
                new CreateQuestionChoiceRequest("A", "TCP", true, 0),
                new CreateQuestionChoiceRequest("B", "IP", false, 1)
            ]);

        var question = (await service.CreateQuestionAsync(createReq, CancellationToken.None)).Value!;
        var versionId = question.CurrentVersionId!.Value;

        // Act: Publish version 1
        var publishReq = new PublishQuestionVersionRequest("Đã duyệt bởi Trưởng bộ môn");
        var pubResult = await service.PublishQuestionVersionAsync(versionId, publishReq, CancellationToken.None);

        // Assert
        pubResult.Succeeded.Should().BeTrue();
        pubResult.Value.Should().NotBeNull();
        pubResult.Value!.VersionNumber.Should().Be(1);
        pubResult.Value.Notes.Should().Be("Đã duyệt bởi Trưởng bộ môn");

        // Verify Question status is now Published
        var reloaded = (await service.GetQuestionByIdAsync(question.Id, CancellationToken.None)).Value!;
        reloaded.Status.Should().Be("Published");

        // Verify publication in DB
        var pubInDb = await db.ContentPublications.FirstOrDefaultAsync(p => p.QuestionVersionId == versionId);
        pubInDb.Should().NotBeNull();
        pubInDb!.PublishedBy.Should().NotBeNullOrWhiteSpace();
    }
}
