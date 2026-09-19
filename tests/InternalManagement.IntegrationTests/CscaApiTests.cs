using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.IntegrationTests;

public class CscaApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public CscaApiTests(CustomWebApplicationFactory factory)
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

    private async Task<string> GetCoordinatorTokenAsync(HttpClient client)
    {
        var loginReq = new LoginRequest("coordinator", "Coordinator@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return loginData!.Data!.AccessToken;
    }

    [Fact]
    public async Task GetClasses_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/csca/classes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<CscaClassDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateClass_EnrollStudent_GetFinancialSummary_Workflow_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetCoordinatorTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Step 1: Create Class
        var randomCode = $"CSCA-TEST-{Guid.NewGuid().ToString()[..6].ToUpper()}";
        var createReq = new CreateCscaClassRequest(
            randomCode,
            "Lớp Test Tự Động CSCA",
            "Đợt Test 2026",
            "T3-T5 (19h30 - 21h30)",
            5000000m,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(3));

        var createRes = await client.PostAsJsonAsync("/api/v1/csca/classes", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createData = await createRes.Content.ReadFromJsonAsync<ApiResponse<CscaClassDto>>();
        createData!.Success.Should().BeTrue();
        var classId = createData.Data!.Id;

        // Step 2: Enroll Student
        var enrollReq = new EnrollStudentRequest(
            "Nguyễn Học Viên Test",
            "hocvien.test@gmail.com",
            "0909123456",
            5000000m,
            PaymentStatus.Paid,
            "Đã nộp tiền đầy đủ");

        var enrollRes = await client.PostAsJsonAsync($"/api/v1/csca/classes/{classId}/students", enrollReq);
        enrollRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: Get Financial Summary
        var finRes = await client.GetAsync($"/api/v1/csca/classes/{classId}/financial-summary");
        finRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var finData = await finRes.Content.ReadFromJsonAsync<ApiResponse<ClassFinancialSummaryDto>>();
        finData!.Success.Should().BeTrue();
        finData.Data!.TotalStudents.Should().Be(1);
        finData.Data.ActualRevenue.Should().Be(5000000m);
    }
}
