using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Infrastructure.Persistence;

namespace InternalManagement.IntegrationTests;

public sealed class SecurityHardeningTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SecurityHardeningTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task InvalidBearerToken_ShouldBeRejected()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "not-a-valid-jwt");

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserWithoutFinancePermission_ShouldNotReadFinanceReports()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("hr_staff", "Hr@123456"));
        var loginData = await login.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        loginData!.Data.Should().NotBeNull();

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", loginData.Data!.AccessToken);
        var response = await client.GetAsync("/api/v1/finance/cash-flow");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RefreshTokenReplay_ShouldInvalidateTheSession()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("director", "Director@123456"));
        var loginData = await login.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var refreshToken = loginData!.Data!.RefreshToken;

        var firstRefresh = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh-token", new RefreshTokenRequest(refreshToken));
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);

        var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh-token", new RefreshTokenRequest(refreshToken));
        replay.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var newToken = await firstRefresh.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var sessionAfterReplay = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh-token",
            new RefreshTokenRequest(newToken!.Data!.RefreshToken));
        sessionAfterReplay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RefreshToken_ShouldBeStoredAsOneWayHash()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("warehouse_mgr", "Warehouse@123456"));
        var loginData = await login.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        var rawRefreshToken = loginData!.Data!.RefreshToken;

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stored = await db.UserRefreshTokens.SingleAsync(x => x.UserId == loginData.Data.User.Id && x.RevokedAt == null);

        stored.TokenHash.Should().HaveLength(64);
        stored.TokenHash.Should().NotBe(rawRefreshToken);
        stored.ReplacedByTokenHash.Should().BeNull();
    }
}
