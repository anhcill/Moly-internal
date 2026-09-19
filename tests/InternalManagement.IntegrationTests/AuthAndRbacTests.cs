using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;

namespace InternalManagement.IntegrationTests;

public class AuthAndRbacTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public AuthAndRbacTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidAdminCredentials_ShouldReturnOkAndTokens()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new LoginRequest("admin", "Admin@123456");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        result.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
        result.Data.User.Username.Should().Be("admin");
        result.Data.User.Roles.Should().Contain("SuperAdmin");
        response.Headers.CacheControl?.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_ShouldReturnUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new LoginRequest("admin", "WrongPassword123!");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.Message.Should().Be("Không thể đăng nhập với thông tin đã cung cấp.");
    }

    [Fact]
    public async Task Login_AfterFiveInvalidAttempts_ShouldTemporarilyLockTheAccount()
    {
        var client = _factory.CreateClient();
        var invalid = new LoginRequest("question_author", "WrongPassword123!");

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/v1/auth/login", invalid);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var validAttempt = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest("question_author", "Author@123456"));

        validAttempt.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_WithValidToken_ShouldRotateAndReturnNewTokens()
    {
        // Arrange: first login
        var client = _factory.CreateClient();
        var loginReq = new LoginRequest("director", "Director@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var originalRefreshToken = loginData!.Data!.RefreshToken;

        // Act: request new token using refresh token
        var refreshReq = new RefreshTokenRequest(originalRefreshToken);
        var refreshRes = await client.PostAsJsonAsync("/api/v1/auth/refresh-token", refreshReq);

        // Assert
        refreshRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshData = await refreshRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        refreshData.Should().NotBeNull();
        refreshData!.Success.Should().BeTrue();
        refreshData.Data!.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshData.Data.RefreshToken.Should().NotBeNullOrWhiteSpace();
        refreshData.Data.RefreshToken.Should().NotBe(originalRefreshToken);

        // Act 2: reusing the old refresh token should be rejected (replay protection)
        var reuseRes = await client.PostAsJsonAsync("/api/v1/auth/refresh-token", refreshReq);
        reuseRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetCurrentUser_WithValidToken_ShouldReturnUserProfile()
    {
        // Arrange: login as hr_staff
        var client = _factory.CreateClient();
        var loginReq = new LoginRequest("hr_staff", "Hr@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var token = loginData!.Data!.AccessToken;

        // Act: call /api/v1/auth/me with Bearer token
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var meResponse = await client.GetAsync("/api/v1/auth/me");

        // Assert
        meResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var meData = await meResponse.Content.ReadFromJsonAsync<ApiResponse<UserDto>>();
        meData.Should().NotBeNull();
        meData!.Success.Should().BeTrue();
        meData.Data!.Username.Should().Be("hr_staff");
        meData.Data.Roles.Should().Contain("HrStaff");
        meData.Data.Permissions.Should().Contain("Permissions.Employees.View");
    }

    [Fact]
    public async Task GetCurrentUser_WithoutAuthorization_ShouldReturnUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestWithMismatchedCompanyHeader_ShouldBeForbidden()
    {
        var client = _factory.CreateClient();
        var loginRes = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("hr_staff", "Hr@123456"));
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var token = loginData!.Data!.AccessToken;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Company-ID", Guid.NewGuid().ToString());

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ChangePassword_WithValidCredentials_ShouldSucceed()
    {
        // Arrange: login as content_editor
        var client = _factory.CreateClient();
        var loginReq = new LoginRequest("content_editor", "Editor@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var token = loginData!.Data!.AccessToken;
        var refreshToken = loginData.Data.RefreshToken;

        // Act: change password
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var changeReq = new ChangePasswordRequest("Editor@123456", "NewEditorPassword@2026");
        var changeRes = await client.PostAsJsonAsync("/api/v1/auth/change-password", changeReq);

        // Assert
        changeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var oldRefreshRes = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh-token", new RefreshTokenRequest(refreshToken));
        oldRefreshRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify login with new password
        var reLoginReq = new LoginRequest("content_editor", "NewEditorPassword@2026");
        var reLoginRes = await client.PostAsJsonAsync("/api/v1/auth/login", reLoginReq);
        reLoginRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_WithValidToken_ShouldRevokeTokens()
    {
        // Arrange: login as warehouse_mgr
        var client = _factory.CreateClient();
        var loginReq = new LoginRequest("warehouse_mgr", "Warehouse@123456");
        var loginRes = await client.PostAsJsonAsync("/api/v1/auth/login", loginReq);
        var loginData = await loginRes.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var token = loginData!.Data!.AccessToken;
        var refreshToken = loginData.Data.RefreshToken;

        // Act: logout
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var logoutRes = await client.PostAsync("/api/v1/auth/logout", null);

        // Assert
        logoutRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Refresh token should now be revoked
        var refreshReq = new RefreshTokenRequest(refreshToken);
        var refreshRes = await client.PostAsJsonAsync("/api/v1/auth/refresh-token", refreshReq);
        refreshRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_WithValidUser_ShouldReturnOk()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new PasswordResetRequestDto(
            UsernameOrEmail: "admin",
            FullName: "Quản trị viên Tổng",
            PhoneNumber: "0901234567",
            Note: "Quên mật khẩu máy làm việc");

        // Act
        var response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ApiResponse<string>>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }
}
