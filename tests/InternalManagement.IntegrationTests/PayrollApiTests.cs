using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.IntegrationTests;

public class PayrollApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PayrollApiTests(CustomWebApplicationFactory factory)
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

    private static async Task SeedAttendanceForPayrollAsync(HttpClient client, DateOnly date)
    {
        var employeesResponse = await client.GetAsync("/api/v1/employees?pageSize=100");
        employeesResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var employees = await employeesResponse.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<EmployeeDto>>>();

        foreach (var employee in employees!.Data!.Items.Where(e =>
                     e.Status == "Active" && e.EmploymentType == EmploymentType.FULL_TIME))
        {
            var attendance = new CreateAttendanceRecordRequest(employee.Id, date, new TimeOnly(8, 0), new TimeOnly(17, 0), 8, "Present");
            var response = await client.PostAsJsonAsync("/api/v1/attendance", attendance);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task GetPayrollPeriods_AsAdmin_ShouldReturnSeededPeriods()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/payroll/periods");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<PayrollPeriodDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreatePeriod_Calculate_Workflow_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Create Period
        var uniqueName = "Kỳ Lương Test " + Guid.NewGuid().ToString("N")[..6];
        var createReq = new CreatePayrollPeriodRequest(uniqueName, new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));
        var createRes = await client.PostAsJsonAsync("/api/v1/payroll/periods", createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var createData = await createRes.Content.ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>();
        var periodId = createData!.Data!.Id;

        await SeedAttendanceForPayrollAsync(client, new DateOnly(2026, 7, 1));

        // 2. Calculate Payroll
        var calcRes = await client.PostAsync($"/api/v1/payroll/periods/{periodId}/calculate", null);
        calcRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var calcData = await calcRes.Content.ReadFromJsonAsync<ApiResponse<PayrollCalculationResultDto>>();
        calcData.Should().NotBeNull();
        calcData!.Success.Should().BeTrue();
        calcData.Data!.TotalEmployeesProcessed.Should().BeGreaterThan(0);
        calcData.Data!.TotalNetAmount.Should().BeGreaterThan(0);

        // 3. Get Payslips for this period
        var payslipsRes = await client.GetAsync($"/api/v1/payroll/periods/{periodId}/payslips");
        payslipsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var payslipsData = await payslipsRes.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<PayslipDto>>>();
        payslipsData!.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetActivePolicy_AsAdmin_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/payroll/policies/active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PayrollPolicyVersionDto>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.VersionNumber.Should().BeGreaterThanOrEqualTo(1);
    }
}
