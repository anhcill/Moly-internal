using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Enums;

namespace InternalManagement.IntegrationTests;

public class EmployeesApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public EmployeesApiTests(CustomWebApplicationFactory factory)
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
    public async Task GetEmployees_AsAdmin_ShouldReturnOkWithSeededEmployees()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/employees");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<EmployeeDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetDepartments_AsAdmin_ShouldReturnDepartments()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/departments");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<DepartmentDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Should().NotBeEmpty();
        result.Data!.Should().Contain(d => d.Code == "TECH" || d.Code == "HR");
    }

    [Fact]
    public async Task CreateEmployee_AsAdmin_ShouldReturnCreated()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var code = "EMP-INTG-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var req = new CreateEmployeeRequest(code, "Nhân Viên Mới", $"{code.ToLowerInvariant()}@moli.local", "0909000111", "Tester", 13000000m);

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/employees", req);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.EmployeeCode.Should().Be(code);
    }

    [Fact]
    public async Task GetEmployees_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/employees");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PaymentDetails_ShouldBeStoredButNotExposedInEmployeeListOrDetail()
    {
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var request = new CreateEmployeeRequest(
            $"CTV-{suffix}", "Cộng tác viên nội dung", $"ctv-{suffix.ToLowerInvariant()}@moli.local",
            null, "Biên soạn đề", 0m,
            EmploymentType: EmploymentType.PART_TIME,
            PartTimeCalculationMethod: PartTimeCalculationMethod.OUTPUT,
            BankName: "Vietcombank", BankAccountNumber: "1234567890",
            BankAccountHolder: "CONG TAC VIEN NOI DUNG");

        var created = await client.PostAsJsonAsync("/api/v1/employees", request);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var employee = (await created.Content.ReadFromJsonAsync<ApiResponse<EmployeeDto>>())!.Data!;

        var list = await client.GetAsync($"/api/v1/employees?search=CTV-{suffix}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        (await list.Content.ReadAsStringAsync()).Should().NotContain("bankAccountNumber");
        var detail = await client.GetAsync($"/api/v1/employees/{employee.Id}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadAsStringAsync()).Should().NotContain("bankAccountNumber");

        var paymentDetails = await client.GetAsync($"/api/v1/employees/{employee.Id}/payment-details");
        paymentDetails.StatusCode.Should().Be(HttpStatusCode.OK);
        var bank = (await paymentDetails.Content.ReadFromJsonAsync<ApiResponse<EmployeePaymentDetailsDto>>())!.Data!;
        bank.BankAccountNumber.Should().Be("1234567890");

        var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/employees/{employee.Id}/payment-details"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
