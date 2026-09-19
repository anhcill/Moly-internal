using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;

namespace InternalManagement.Application.Features.Auth.Services;

public interface IAuthService
{
    Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default);
    Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken ct = default);
    Task<Result> LogoutAsync(string? ipAddress, CancellationToken ct = default);
    Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken ct = default);
    Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default);
    Task<Result> RequestPasswordResetAsync(PasswordResetRequestDto request, string? ipAddress, CancellationToken ct = default);
}
