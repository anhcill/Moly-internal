using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.InternalData.DTOs;
using InternalManagement.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace InternalManagement.IntegrationTests;

public sealed class BusinessSegmentsApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BusinessSegmentsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InternalCustomers_ShouldKeepTechnologyAndFashionDataSeparate()
    {
        var technologyClient = await CreateScopedAdminClientAsync("EDTECH");
        var fashionClient = await CreateScopedAdminClientAsync("FASHION");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var sharedCode = $"KH-SEG-{suffix}";

        var technologyRequest = new CreateInternalCustomerRequest(
            sharedCode,
            $"Khách hàng công nghệ {suffix}",
            "Nguyễn Công Nghệ",
            $"tech-{suffix.ToLowerInvariant()}@moli.local",
            "0901000001",
            "Hà Nội",
            "Website",
            "ACTIVE",
            "Dữ liệu kiểm thử mảng công nghệ - giáo dục");
        var fashionRequest = new CreateInternalCustomerRequest(
            sharedCode,
            $"Khách hàng thời trang {suffix}",
            "Trần Thời Trang",
            $"fashion-{suffix.ToLowerInvariant()}@moli.local",
            "0901000002",
            "TP. Hồ Chí Minh",
            "TikTok Shop",
            "LEAD",
            "Dữ liệu kiểm thử mảng thời trang");

        var technologyResponse = await technologyClient.PostAsJsonAsync(
            "/api/v1/noi-bo/cong-nghe-giao-duc/khach-hang", technologyRequest);
        var fashionResponse = await fashionClient.PostAsJsonAsync(
            "/api/v1/noi-bo/thoi-trang/khach-hang", fashionRequest);

        technologyResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await technologyResponse.Content.ReadAsStringAsync());
        fashionResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await fashionResponse.Content.ReadAsStringAsync());

        var technology = (await technologyResponse.Content
            .ReadFromJsonAsync<ApiResponse<InternalCustomerDto>>())!.Data!;
        var fashion = (await fashionResponse.Content
            .ReadFromJsonAsync<ApiResponse<InternalCustomerDto>>())!.Data!;
        technology.BusinessSegment.Should().Be("TECHNOLOGY_EDUCATION");
        fashion.BusinessSegment.Should().Be("FASHION");
        technology.BusinessUnitId.Should().NotBe(fashion.BusinessUnitId);

        var technologyList = await GetDataAsync<PaginatedResult<InternalCustomerDto>>(
            technologyClient, $"/api/v1/noi-bo/cong-nghe-giao-duc/khach-hang?search={sharedCode}");
        var fashionList = await GetDataAsync<PaginatedResult<InternalCustomerDto>>(
            fashionClient, $"/api/v1/noi-bo/thoi-trang/khach-hang?search={sharedCode}");

        technologyList.Items.Should().ContainSingle(x => x.Id == technology.Id);
        technologyList.Items.Should().NotContain(x => x.Id == fashion.Id);
        fashionList.Items.Should().ContainSingle(x => x.Id == fashion.Id);
        fashionList.Items.Should().NotContain(x => x.Id == technology.Id);

        (await fashionClient.GetAsync($"/api/v1/noi-bo/thoi-trang/khach-hang/{technology.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await technologyClient.GetAsync($"/api/v1/noi-bo/cong-nghe-giao-duc/khach-hang/{fashion.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InternalResources_ShouldEnforceSegmentTypesAndPreventCrossAccess()
    {
        var technologyClient = await CreateScopedAdminClientAsync("EDTECH");
        var fashionClient = await CreateScopedAdminClientAsync("FASHION");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var sharedCode = $"TL-SEG-{suffix}";

        var technologyRequest = new CreateInternalResourceRequest(
            sharedCode,
            $"Đề kiểm tra công nghệ {suffix}",
            "EXAM",
            $"https://files.moli.local/education/{suffix}/exam.pdf",
            "exam.pdf",
            "application/pdf",
            1024,
            new string('a', 64),
            "1.0",
            "ACTIVE",
            ["Đề thi", "Nội bộ"],
            "{\"subject\":\"technology\"}");
        var fashionRequest = new CreateInternalResourceRequest(
            sharedCode,
            $"Mẫu thiết kế thời trang {suffix}",
            "DESIGN_SAMPLE",
            $"https://files.moli.local/fashion/{suffix}/design.png",
            "design.png",
            "image/png",
            2048,
            new string('b', 64),
            "2.0",
            "DRAFT",
            ["Mẫu thiết kế", "Nội bộ"],
            "{\"collection\":\"integration-test\"}");

        var technologyResponse = await technologyClient.PostAsJsonAsync(
            "/api/v1/noi-bo/cong-nghe-giao-duc/tai-lieu", technologyRequest);
        var fashionResponse = await fashionClient.PostAsJsonAsync(
            "/api/v1/noi-bo/thoi-trang/tai-lieu", fashionRequest);

        technologyResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await technologyResponse.Content.ReadAsStringAsync());
        fashionResponse.StatusCode.Should().Be(HttpStatusCode.Created,
            await fashionResponse.Content.ReadAsStringAsync());

        var technology = (await technologyResponse.Content
            .ReadFromJsonAsync<ApiResponse<InternalResourceDto>>())!.Data!;
        var fashion = (await fashionResponse.Content
            .ReadFromJsonAsync<ApiResponse<InternalResourceDto>>())!.Data!;
        technology.ResourceType.Should().Be("EXAM");
        technology.BusinessSegment.Should().Be("TECHNOLOGY_EDUCATION");
        fashion.ResourceType.Should().Be("DESIGN_SAMPLE");
        fashion.BusinessSegment.Should().Be("FASHION");

        var technologyList = await GetDataAsync<PaginatedResult<InternalResourceDto>>(
            technologyClient, $"/api/v1/noi-bo/cong-nghe-giao-duc/tai-lieu?search={sharedCode}");
        var fashionList = await GetDataAsync<PaginatedResult<InternalResourceDto>>(
            fashionClient, $"/api/v1/noi-bo/thoi-trang/tai-lieu?search={sharedCode}");
        technologyList.Items.Should().ContainSingle(x => x.Id == technology.Id);
        technologyList.Items.Should().NotContain(x => x.Id == fashion.Id);
        fashionList.Items.Should().ContainSingle(x => x.Id == fashion.Id);
        fashionList.Items.Should().NotContain(x => x.Id == technology.Id);

        (await fashionClient.GetAsync($"/api/v1/noi-bo/thoi-trang/tai-lieu/{technology.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await technologyClient.GetAsync($"/api/v1/noi-bo/cong-nghe-giao-duc/tai-lieu/{fashion.Id}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var invalidTypeResponse = await technologyClient.PostAsJsonAsync(
            "/api/v1/noi-bo/cong-nghe-giao-duc/tai-lieu",
            fashionRequest with { Code = $"INVALID-{suffix}" });
        invalidTypeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FinanceOverview_ShouldReturnTwoAreasAndAnExactCompanyTotal()
    {
        var client = await CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            "/api/v1/finance/overview?from=2000-01-01&to=2100-01-01");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var overview = (await response.Content
            .ReadFromJsonAsync<ApiResponse<CompanyFinancialOverviewDto>>())!.Data!;

        overview.Areas.Should().HaveCount(2);
        overview.Areas.Select(x => x.AreaCode).Should().BeEquivalentTo(
            ["TECHNOLOGY_EDUCATION", "FASHION"]);
        overview.CompanyTotal.AreaCode.Should().Be("COMPANY_TOTAL");
        overview.CompanyTotal.TotalIncome.Should().Be(overview.Areas.Sum(x => x.TotalIncome));
        overview.CompanyTotal.TotalExpense.Should().Be(overview.Areas.Sum(x => x.TotalExpense));
        overview.CompanyTotal.NetCashFlow.Should().Be(overview.Areas.Sum(x => x.NetCashFlow));
        overview.CompanyTotal.OperatingProfit.Should().Be(overview.Areas.Sum(x => x.OperatingProfit));
        overview.CompanyTotal.ConfirmedProfit.Should().Be(overview.Areas.Sum(x => x.ConfirmedProfit));
        overview.CompanyTotal.ProvisionalProfit.Should().Be(overview.Areas.Sum(x => x.ProvisionalProfit));
        overview.CompanyTotal.CashTransactionCount.Should().Be(overview.Areas.Sum(x => x.CashTransactionCount));
        overview.CompanyTotal.ProfitSnapshotCount.Should().Be(overview.Areas.Sum(x => x.ProfitSnapshotCount));
        overview.CalculationNote.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Employees_BusinessSegmentFilter_ShouldReturnOnlyTheRequestedArea()
    {
        var (client, login) = await CreateAuthenticatedClientWithLoginAsync();
        var technologyUnit = login.User.AccessibleBusinessUnits.Single(x => x.Code == "EDTECH");
        var fashionUnit = login.User.AccessibleBusinessUnits.Single(x => x.Code == "FASHION");
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var codePrefix = $"EMPSEG{suffix}";

        var technologyEmployee = new CreateEmployeeRequest(
            $"{codePrefix}T",
            $"Nhân sự công nghệ {suffix}",
            $"employee-tech-{suffix.ToLowerInvariant()}@moli.local",
            null,
            "Kiểm thử viên",
            15_000_000m,
            BusinessUnitId: technologyUnit.Id,
            CvUrlOrPath: $"https://files.moli.local/cv/{suffix}-tech.pdf");
        var fashionEmployee = new CreateEmployeeRequest(
            $"{codePrefix}F",
            $"Nhân sự thời trang {suffix}",
            $"employee-fashion-{suffix.ToLowerInvariant()}@moli.local",
            null,
            "Thiết kế viên",
            14_000_000m,
            BusinessUnitId: fashionUnit.Id,
            CvUrlOrPath: $"https://files.moli.local/cv/{suffix}-fashion.pdf");

        var technologyCreate = await client.PostAsJsonAsync("/api/v1/employees", technologyEmployee);
        var fashionCreate = await client.PostAsJsonAsync("/api/v1/employees", fashionEmployee);
        technologyCreate.StatusCode.Should().Be(HttpStatusCode.Created,
            await technologyCreate.Content.ReadAsStringAsync());
        fashionCreate.StatusCode.Should().Be(HttpStatusCode.Created,
            await fashionCreate.Content.ReadAsStringAsync());

        var technologyList = await GetDataAsync<PaginatedResult<EmployeeDto>>(
            client, $"/api/v1/employees?businessSegment=cong-nghe-giao-duc&search={codePrefix}");
        var fashionList = await GetDataAsync<PaginatedResult<EmployeeDto>>(
            client, $"/api/v1/employees?businessSegment=thoi-trang&search={codePrefix}");

        technologyList.Items.Should().ContainSingle(x => x.EmployeeCode == technologyEmployee.EmployeeCode);
        technologyList.Items.Should().OnlyContain(x => x.BusinessUnitId == technologyUnit.Id);
        technologyList.Items.Should().NotContain(x => x.EmployeeCode == fashionEmployee.EmployeeCode);
        fashionList.Items.Should().ContainSingle(x => x.EmployeeCode == fashionEmployee.EmployeeCode);
        fashionList.Items.Should().OnlyContain(x => x.BusinessUnitId == fashionUnit.Id);
        fashionList.Items.Should().NotContain(x => x.EmployeeCode == technologyEmployee.EmployeeCode);
    }

    [Fact]
    public async Task InternalDataEndpoints_WithoutToken_ShouldReturnUnauthorized()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/v1/noi-bo/cong-nghe-giao-duc/khach-hang"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/noi-bo/thoi-trang/tai-lieu"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var (client, _) = await CreateAuthenticatedClientWithLoginAsync();
        return client;
    }

    private async Task<(HttpClient Client, LoginResponse Login)> CreateAuthenticatedClientWithLoginAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest("admin", "Admin@123456"));
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var login = (await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>())!.Data!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, login);
    }

    private async Task<HttpClient> CreateScopedAdminClientAsync(string businessUnitCode)
    {
        var (client, login) = await CreateAuthenticatedClientWithLoginAsync();
        var businessUnit = login.User.AccessibleBusinessUnits.Single(x => x.Code == businessUnitCode);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] =
                    "IntegrationTestOnlySecretKey2026-DoNotUseInProduction-LongEnough",
                ["JwtSettings:Issuer"] = "MoliBackendApi",
                ["JwtSettings:Audience"] = "MoliClients",
                ["JwtSettings:AccessTokenExpirationMinutes"] = "15"
            })
            .Build();
        var tokenGenerator = new JwtTokenGenerator(configuration);
        var (token, _) = tokenGenerator.GenerateAccessToken(
            login.User.Id,
            login.User.Username,
            login.User.Email,
            login.User.CompanyId,
            businessUnit.Id,
            login.User.Roles,
            login.User.Permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<T> GetDataAsync<T>(HttpClient client, string uri)
    {
        var response = await client.GetAsync(uri);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<T>>();
        envelope.Should().NotBeNull();
        envelope!.Success.Should().BeTrue();
        envelope.Data.Should().NotBeNull();
        return envelope.Data!;
    }
}
