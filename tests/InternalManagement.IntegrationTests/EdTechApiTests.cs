using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.EdTech.DTOs;

namespace InternalManagement.IntegrationTests;

public class EdTechApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EdTechApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<string> GetAdminTokenAsync(HttpClient client)
    {
        var loginReq = new LoginRequest("admin", "Admin@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginData!.Data!.AccessToken;
    }

    private async Task<string> GetEditorTokenAsync(HttpClient client)
    {
        var loginReq = new LoginRequest("content_editor", "Editor@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginData!.Data!.AccessToken;
    }

    [Fact]
    public async Task GetCourses_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/edtech/courses");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<CourseDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAndPublishQuestion_Workflow_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetEditorTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Step 1: Create Question
        var createReq = new CreateQuestionRequest(
            "Ngân Hàng C# Test",
            "Công Nghệ Thông Tin",
            "C# Nâng Cao",
            "Medium",
            "<p>Khai báo record nào bất biến trong C#?</p>",
            "<p>public record Person(string Name);</p>",
            [
                new CreateQuestionChoiceRequest("A", "public record Person(string Name);", true, 0),
                new CreateQuestionChoiceRequest("B", "public class Person { public string Name; }", false, 1)
            ],
            ["csharp", "records"]);

        var createRes = await client.PostAsJsonAsync("/api/v1/edtech/questions", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createData = await createRes.Content.ReadFromJsonAsync<ApiResponse<QuestionDto>>();
        createData!.Success.Should().BeTrue();
        var questionId = createData.Data!.Id;
        var versionId = createData.Data.CurrentVersionId!.Value;

        // Step 2: Publish Version
        var pubReq = new PublishQuestionVersionRequest("Phê duyệt câu hỏi kiểm tra cuối khóa");
        var pubRes = await client.PostAsJsonAsync($"/api/v1/edtech/questions/versions/{versionId}/publish", pubReq);
        pubRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var pubData = await pubRes.Content.ReadFromJsonAsync<ApiResponse<ContentPublicationDto>>();
        pubData!.Success.Should().BeTrue();
        pubData.Data!.VersionNumber.Should().Be(1);

        // Step 3: Verify Question is now Published
        var getRes = await client.GetAsync($"/api/v1/edtech/questions/{questionId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var getData = await getRes.Content.ReadFromJsonAsync<ApiResponse<QuestionDto>>();
        getData!.Data!.Status.Should().Be("Published");
    }

    [Fact]
    public async Task GetEdTechCustomers_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/edtech/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
