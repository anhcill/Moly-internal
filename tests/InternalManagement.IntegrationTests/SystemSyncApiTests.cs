using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.Integration.DTOs;

namespace InternalManagement.IntegrationTests;

public class SystemSyncApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SystemSyncApiTests(CustomWebApplicationFactory factory)
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

    private async Task<string> GetEmployeeTokenAsync(HttpClient client)
    {
        var loginReq = new LoginRequest("employee", "Employee@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginData!.Data!.AccessToken;
    }

    [Fact]
    public async Task GetRuns_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/system/sync/runs");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("success");
    }

    [Fact]
    public async Task TriggerSync_AsAdmin_ShouldCreateRun()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new TriggerSyncRequest("NULL_CONNECTOR", "Test");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/system/sync/trigger", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SyncTriggerResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.SourceSystem.Should().Be("NULL_CONNECTOR");
        result.Data.Status.Should().Be("Success");
    }

    [Fact]
    public async Task TriggerSync_EdtechCourses_ShouldSyncAndRecordResults()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new TriggerSyncRequest("WEBSITE_EDTECH", "Courses");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/system/sync/trigger", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SyncTriggerResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.SourceSystem.Should().Be("WEBSITE_EDTECH");
        result.Data.EntityType.Should().Be("Courses");
        result.Data.Status.Should().Be("Success");
        result.Data.RecordsRead.Should().Be(3);
        (result.Data.RecordsWritten + result.Data.RecordsSkipped).Should().Be(3);
    }

    [Fact]
    public async Task TriggerSync_EdtechCustomers_ShouldSyncAndRecordResults()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new TriggerSyncRequest("WEBSITE_EDTECH", "Customers");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/system/sync/trigger", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<SyncTriggerResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.SourceSystem.Should().Be("WEBSITE_EDTECH");
        result.Data.EntityType.Should().Be("Customers");
        result.Data.Status.Should().Be("Success");
        result.Data.RecordsRead.Should().Be(4);
    }

    [Fact]
    public async Task TriggerSync_WithoutPermission_ShouldReturn403()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetEmployeeTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new TriggerSyncRequest("NULL_CONNECTOR", "Test");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/system/sync/trigger", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDeadLetters_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/system/sync/dead-letters");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WebhookIngest_ShouldReturn202()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new WebhookIngestRequest("evt_test_001", "course.created", "{\"courseId\":999}");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/system/webhooks/WEBSITE_EDTECH", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<WebhookIngestResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task WebhookIngest_DuplicateEventId_ShouldReturn200()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new WebhookIngestRequest("evt_dup_001", "payment.updated", "{\"paymentId\":100}");

        // First ingest
        await client.PostAsJsonAsync("/api/v1/system/webhooks/WEBSITE_EDTECH", request);

        // Act: duplicate
        var response = await client.PostAsJsonAsync("/api/v1/system/webhooks/WEBSITE_EDTECH", request);

        // Assert: idempotent → 200 OK
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<WebhookIngestResponse>>();
        result!.Data!.Accepted.Should().BeTrue();
        result.Data.Message.Should().Contain("idempotent");
    }
}
