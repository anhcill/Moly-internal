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
    public async Task WorkEntries_ShouldExposeDeliverableHistoryAndSupportVoid()
    {
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var employeesResponse = await client.GetAsync("/api/v1/employees?pageSize=100");
        var employees = (await employeesResponse.Content
            .ReadFromJsonAsync<ApiResponse<PaginatedResult<EmployeeDto>>>())!.Data!;
        var employee = employees.Items.First(e => e.Status == "Active");
        var year = 2100 + Random.Shared.Next(0, 100);
        var periodResponse = await client.PostAsJsonAsync("/api/v1/payroll/periods",
            new CreatePayrollPeriodRequest($"Kiểm thử đầu việc {Guid.NewGuid():N}",
                new DateOnly(year, 1, 1), new DateOnly(year, 1, 31)));
        periodResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await periodResponse.Content.ReadAsStringAsync());
        var period = (await periodResponse.Content
            .ReadFromJsonAsync<ApiResponse<PayrollPeriodDto>>())!.Data!;

        var reference = $"DE-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var request = new CreatePayrollWorkEntryRequest(employee.Id, PayrollWorkTypes.QuestionPosted,
            reference, "Đề kiểm thử đã đăng", new DateOnly(year, 1, 15), 2m, 100_000m);
        var add = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{period.Id}/work-entries", request);
        add.StatusCode.Should().Be(HttpStatusCode.OK, await add.Content.ReadAsStringAsync());
        var entry = (await add.Content
            .ReadFromJsonAsync<ApiResponse<PayrollWorkEntryDto>>())!.Data!;
        entry.Amount.Should().Be(200_000m);

        var referralReference = $"HV-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
        var referral = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{period.Id}/work-entries",
            new CreatePayrollWorkEntryRequest(employee.Id, PayrollWorkTypes.StudentReferral,
                referralReference, "Học viên được giới thiệu", new DateOnly(year, 1, 16), 1m, 250_000m));
        referral.StatusCode.Should().Be(HttpStatusCode.OK, await referral.Content.ReadAsStringAsync());
        (await referral.Content.ReadFromJsonAsync<ApiResponse<PayrollWorkEntryDto>>())!.Data!.Amount
            .Should().Be(250_000m);

        var duplicate = await client.PostAsJsonAsync($"/api/v1/payroll/periods/{period.Id}/work-entries", request);
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var list = await client.GetFromJsonAsync<ApiResponse<List<PayrollWorkEntryDto>>>(
            $"/api/v1/payroll/periods/{period.Id}/work-entries");
        list!.Data.Should().ContainSingle(e => e.ReferenceCode == reference && !e.IsVoided);

        var voided = await client.PostAsync($"/api/v1/payroll/work-entries/{entry.Id}/void", null);
        voided.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterVoid = await client.GetFromJsonAsync<ApiResponse<List<PayrollWorkEntryDto>>>(
            $"/api/v1/payroll/periods/{period.Id}/work-entries");
        afterVoid!.Data.Should().ContainSingle(e => e.Id == entry.Id && e.IsVoided);
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
