using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.IntegrationTests;

public class InterviewApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InterviewApiTests(CustomWebApplicationFactory factory)
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

    [Fact]
    public async Task GetInterviewCustomers_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/interview/customers");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<InterviewCustomerDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateInterviewCustomer_AndGetFinancialSummary_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Step 1: Create Interview Customer
        var createReq = new CreateInterviewCustomerRequest(
            "Trần Ứng Viên Test",
            "ungvien.test@gmail.com",
            "0988777666",
            "Gói Mock Interview FAANG Standard",
            2,
            3500000m,
            PaymentStatus.Paid);

        var createRes = await client.PostAsJsonAsync("/api/v1/interview/customers", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var createData = await createRes.Content.ReadFromJsonAsync<ApiResponse<InterviewCustomerDto>>();
        createData!.Success.Should().BeTrue();
        createData.Data!.FullName.Should().Be("Trần Ứng Viên Test");

        // Step 2: Get Financial Summary
        var finRes = await client.GetAsync("/api/v1/interview/financial-summary");
        finRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var finData = await finRes.Content.ReadFromJsonAsync<ApiResponse<InterviewFinancialSummaryDto>>();
        finData!.Success.Should().BeTrue();
        finData.Data!.TotalCustomers.Should().BeGreaterThanOrEqualTo(1);
        finData.Data.TotalRevenue.Should().BeGreaterThan(0);
    }
}
