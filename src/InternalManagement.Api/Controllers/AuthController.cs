using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.Auth.Services;

namespace InternalManagement.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController : BaseApiController
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        PreventTokenCaching();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.LoginAsync(request, ipAddress, ct);

        if (!result.Succeeded)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Không thể đăng nhập với thông tin đã cung cấp."));
        }

        return Ok(ApiResponse<LoginResponse>.Ok(result.Value!, "Đăng nhập thành công."));
    }

    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        PreventTokenCaching();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RefreshTokenAsync(request, ipAddress, ct);

        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<LoginResponse>.Fail(result.Errors.FirstOrDefault() ?? "Không thể cấp lại token."));
        }

        return Ok(ApiResponse<LoginResponse>.Ok(result.Value!, "Cấp lại access token thành công."));
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        PreventTokenCaching();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.LogoutAsync(ipAddress, ct);

        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<string>.Fail(result.Errors.FirstOrDefault() ?? "Đăng xuất thất bại."));
        }

        return Ok(ApiResponse<string>.Ok("Đăng xuất thành công.", "Đăng xuất thành công."));
    }

    private void PreventTokenCaching()
    {
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UserDto>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken ct)
    {
        var result = await _authService.GetCurrentUserAsync(ct);

        if (!result.Succeeded)
        {
            return Unauthorized(ApiResponse<UserDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy thông tin người dùng."));
        }

        return Ok(ApiResponse<UserDto>.Ok(result.Value!, "Lấy thông tin người dùng thành công."));
    }

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await _authService.ChangePasswordAsync(request, ct);

        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<string>.Fail(result.Errors.FirstOrDefault() ?? "Đổi mật khẩu thất bại."));
        }

        return Ok(ApiResponse<string>.Ok("Đổi mật khẩu thành công.", "Đổi mật khẩu thành công."));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ForgotPassword([FromBody] PasswordResetRequestDto request, CancellationToken ct)
    {
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var result = await _authService.RequestPasswordResetAsync(request, ipAddress, ct);

        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<string>.Fail(result.Errors.FirstOrDefault() ?? "Yêu cầu cấp lại mật khẩu không hợp lệ."));
        }

        return Ok(ApiResponse<string>.Ok(
            "Yêu cầu cấp lại mật khẩu đã được gửi đến Quản trị viên Tổng công ty.",
            "Yêu cầu cấp lại mật khẩu đã được gửi thành công đến Quản trị viên Tổng công ty. Vui lòng liên hệ Admin để nhận mật khẩu mới."));
    }
}
