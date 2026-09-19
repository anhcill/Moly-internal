namespace InternalManagement.Application.Features.Auth.DTOs;

public record LoginRequest(string Username, string Password);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User);

public record RefreshTokenRequest(string RefreshToken);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record PasswordResetRequestDto(
    string UsernameOrEmail,
    string? FullName,
    string? PhoneNumber,
    string? Note);

public record UserDto(
    Guid Id,
    Guid CompanyId,
    string CompanyName,
    Guid? DefaultBusinessUnitId,
    string? DefaultBusinessUnitCode,
    string Username,
    string Email,
    string FullName,
    string? PhoneNumber,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<BusinessUnitDto> AccessibleBusinessUnits);

public record BusinessUnitDto(Guid Id, string Code, string Name);
