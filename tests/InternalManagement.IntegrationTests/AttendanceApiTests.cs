using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.HrPayroll.DTOs;

namespace InternalManagement.IntegrationTests;

public class AttendanceApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AttendanceApiTests(CustomWebApplicationFactory factory)
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
    public async Task GetAttendance_AsAdmin_ShouldReturnSeededRecords()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/attendance");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<PaginatedResult<AttendanceRecordDto>>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetAttendanceSummary_AsAdmin_ShouldReturnSummary()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/api/v1/attendance/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceSummaryDto>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.TotalRecords.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ImportAttendanceCsv_AsAdmin_ShouldProcessFileAndReturnDetailedResult()
    {
        // Arrange
        var client = _factory.CreateClient();
        var token = await GetAdminTokenAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var csv = @"Mã NV,Ngày,Giờ Vào,Giờ Ra,Giờ Công,Trạng Thái
EMP-TCH01,2026-08-18,08:15,17:30,8.0,Present
EMP-TA01,2026-08-18,08:50,17:30,7.67,Late
EMP-INVALID,2026-08-18,08:00,17:00,8.0,Present";

        using var form = new MultipartFormDataContent();
        var bytes = Encoding.UTF8.GetBytes(csv);
        var byteContent = new ByteArrayContent(bytes);
        byteContent.Headers.ContentType = MediaTypeHeaderValue.Parse("text/csv");
        form.Add(byteContent, "file", "daily_attendance.csv");

        // Act
        var response = await client.PostAsync("/api/v1/attendance/import", form);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<AttendanceImportResultDto>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data!.SuccessCount.Should().Be(2);
        result.Data!.ErrorCount.Should().Be(1);
        result.Data!.Errors.Should().ContainSingle(e => e.EmployeeCode == "EMP-INVALID");
    }
}
