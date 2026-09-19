using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Auth.DTOs;
using InternalManagement.Application.Features.Auth.Services;
using InternalManagement.Domain.Entities.Identity;

namespace InternalManagement.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly Application.Common.Interfaces.IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;

    public AuthService(
        Application.Common.Interfaces.IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ICurrentUserService currentUserService,
        IConfiguration configuration)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _currentUserService = currentUserService;
        _configuration = configuration;
    }

    public async Task<Result<LoginResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password) ||
            request.Username.Length > 200 || request.Password.Length > 256)
        {
            return Result<LoginResponse>.Failure("Thông tin đăng nhập không hợp lệ.");
        }

        var normalizedUsername = request.Username.Trim().ToLowerInvariant();

        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.DefaultBusinessUnit)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername || u.Email.ToLower() == normalizedUsername, ct);

        var now = DateTime.UtcNow;
        if (user is not null && user.LockoutEndAt.HasValue && user.LockoutEndAt.Value <= now)
        {
            user.FailedLoginCount = 0;
            user.LastFailedLoginAt = null;
            user.LockoutEndAt = null;
        }

        if (user is null || !user.IsActive ||
            (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > now) ||
            !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            if (user is not null && user.IsActive &&
                (!user.LockoutEndAt.HasValue || user.LockoutEndAt.Value <= now))
            {
                RegisterFailedLogin(user, now);
                await _context.SaveChangesAsync(ct);
            }

            return Result<LoginResponse>.Failure("Không thể đăng nhập với thông tin đã cung cấp.");
        }

        user.FailedLoginCount = 0;
        user.LastFailedLoginAt = null;
        user.LockoutEndAt = null;

        var roles = user.UserRoles.Select(ur => ur.Role.Code).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var (accessToken, accessExpiresAt) = _jwtTokenGenerator.GenerateAccessToken(
            user.Id,
            user.Username,
            user.Email,
            user.CompanyId,
            user.DefaultBusinessUnitId,
            roles,
            permissions);

        var (refreshToken, refreshExpiresAt) = _jwtTokenGenerator.GenerateRefreshToken();

        var userRefreshToken = new UserRefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(refreshToken),
            ExpiresAt = refreshExpiresAt,
            CreatedAt = now,
            CreatedByIp = ipAddress
        };

        _context.UserRefreshTokens.Add(userRefreshToken);
        user.LastLoginAt = now;

        await _context.SaveChangesAsync(ct);

        var accessibleBus = await _context.BusinessUnits
            .Where(b => b.CompanyId == user.CompanyId && b.IsActive)
            .Select(b => new BusinessUnitDto(b.Id, b.Code, b.Name))
            .ToListAsync(ct);

        var userDto = new UserDto(
            user.Id,
            user.CompanyId,
            user.Company.Name,
            user.DefaultBusinessUnitId,
            user.DefaultBusinessUnit?.Code,
            user.Username,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roles,
            permissions,
            accessibleBus);

        return Result<LoginResponse>.Success(new LoginResponse(
            accessToken,
            refreshToken,
            accessExpiresAt,
            userDto));
    }

    public async Task<Result<LoginResponse>> RefreshTokenAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result<LoginResponse>.Failure("Refresh token không được để trống.");
        }

        var tokenHash = HashRefreshToken(request.RefreshToken);
        var existingToken = await _context.UserRefreshTokens
            .Include(t => t.User)
                .ThenInclude(u => u.Company)
            .Include(t => t.User)
                .ThenInclude(u => u.DefaultBusinessUnit)
            .Include(t => t.User)
                .ThenInclude(u => u.UserRoles)
                    .ThenInclude(ur => ur.Role)
                        .ThenInclude(r => r.RolePermissions)
                            .ThenInclude(rp => rp.Permission)
            .AsSplitQuery()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

        if (existingToken == null)
        {
            return Result<LoginResponse>.Failure("Refresh token không hợp lệ.");
        }

        // Check for replay attack: if already revoked, revoke all tokens for this user
        if (existingToken.IsRevoked)
        {
            var compromisedTokens = await _context.UserRefreshTokens
                .Where(t => t.UserId == existingToken.UserId && t.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var token in compromisedTokens)
            {
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedByIp = ipAddress;
                token.ReplacedByTokenHash = "COMPROMISED_REUSED_TOKEN";
            }

            await _context.SaveChangesAsync(ct);
            return Result<LoginResponse>.Failure("Phát hiện refresh token đã bị thu hồi. Toàn bộ phiên đăng nhập đã bị vô hiệu hóa.");
        }

        if (existingToken.IsExpired)
        {
            return Result<LoginResponse>.Failure("Refresh token đã hết hạn. Vui lòng đăng nhập lại.");
        }

        var user = existingToken.User;
        if (!user.IsActive)
        {
            return Result<LoginResponse>.Failure("Tài khoản người dùng đã bị khóa.");
        }

        var (newRefreshToken, newRefreshExpiresAt) = _jwtTokenGenerator.GenerateRefreshToken();

        // Revoke current token and link to new token
        existingToken.RevokedAt = DateTime.UtcNow;
        existingToken.RevokedByIp = ipAddress;
        existingToken.ReplacedByTokenHash = HashRefreshToken(newRefreshToken);

        var nextRefreshToken = new UserRefreshToken
        {
            UserId = user.Id,
            TokenHash = HashRefreshToken(newRefreshToken),
            ExpiresAt = newRefreshExpiresAt,
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        _context.UserRefreshTokens.Add(nextRefreshToken);

        var roles = user.UserRoles.Select(ur => ur.Role.Code).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var (newAccessToken, accessExpiresAt) = _jwtTokenGenerator.GenerateAccessToken(
            user.Id,
            user.Username,
            user.Email,
            user.CompanyId,
            user.DefaultBusinessUnitId,
            roles,
            permissions);

        await _context.SaveChangesAsync(ct);

        var accessibleBus = await _context.BusinessUnits
            .Where(b => b.CompanyId == user.CompanyId && b.IsActive)
            .Select(b => new BusinessUnitDto(b.Id, b.Code, b.Name))
            .ToListAsync(ct);

        var userDto = new UserDto(
            user.Id,
            user.CompanyId,
            user.Company.Name,
            user.DefaultBusinessUnitId,
            user.DefaultBusinessUnit?.Code,
            user.Username,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roles,
            permissions,
            accessibleBus);

        return Result<LoginResponse>.Success(new LoginResponse(
            newAccessToken,
            newRefreshToken,
            accessExpiresAt,
            userDto));
    }

    public async Task<Result> LogoutAsync(string? ipAddress, CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            return Result.Failure("Không tìm thấy phiên người dùng hợp lệ.");
        }

        var activeTokens = await _context.UserRefreshTokens
            .Where(t => t.UserId == currentUserId.Value && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = ipAddress;
        }

        await _context.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<UserDto>> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            return Result<UserDto>.Failure("Người dùng chưa được xác thực.");
        }

        var user = await _context.Users
            .Include(u => u.Company)
            .Include(u => u.DefaultBusinessUnit)
            .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                    .ThenInclude(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
            .AsSplitQuery()
            .FirstOrDefaultAsync(u => u.Id == currentUserId.Value, ct);

        if (user == null)
        {
            return Result<UserDto>.Failure("Không tìm thấy thông tin người dùng.");
        }

        var roles = user.UserRoles.Select(ur => ur.Role.Code).Distinct().ToList();
        var permissions = user.UserRoles
            .SelectMany(ur => ur.Role.RolePermissions)
            .Select(rp => rp.Permission.Code)
            .Distinct()
            .ToList();

        var accessibleBus = await _context.BusinessUnits
            .Where(b => b.CompanyId == user.CompanyId && b.IsActive)
            .Select(b => new BusinessUnitDto(b.Id, b.Code, b.Name))
            .ToListAsync(ct);

        var userDto = new UserDto(
            user.Id,
            user.CompanyId,
            user.Company.Name,
            user.DefaultBusinessUnitId,
            user.DefaultBusinessUnit?.Code,
            user.Username,
            user.Email,
            user.FullName,
            user.PhoneNumber,
            roles,
            permissions,
            accessibleBus);

        return Result<UserDto>.Success(userDto);
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordRequest request, CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        if (!currentUserId.HasValue)
        {
            return Result.Failure("Người dùng chưa được xác thực.");
        }

        var passwordPolicyError = ValidateNewPassword(request.NewPassword);
        if (passwordPolicyError is not null)
        {
            return Result.Failure(passwordPolicyError);
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value, ct);
        if (user == null)
        {
            return Result.Failure("Không tìm thấy người dùng.");
        }

        if (!_passwordHasher.VerifyPassword(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Failure("Mật khẩu hiện tại không chính xác.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        var activeTokens = await _context.UserRefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in activeTokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.RevokedByIp = "PASSWORD_CHANGED";
            token.ReplacedByTokenHash = "PASSWORD_CHANGED";
        }
        await _context.SaveChangesAsync(ct);

        return Result.Success();
    }

    private void RegisterFailedLogin(User user, DateTime now)
    {
        var maximumAttempts = Math.Clamp(
            _configuration.GetValue<int?>("Security:Authentication:MaxFailedLoginAttempts") ?? 5,
            3,
            20);
        var lockoutMinutes = Math.Clamp(
            _configuration.GetValue<int?>("Security:Authentication:LockoutMinutes") ?? 15,
            1,
            1440);

        user.FailedLoginCount++;
        user.LastFailedLoginAt = now;
        if (user.FailedLoginCount >= maximumAttempts)
        {
            user.LockoutEndAt = now.AddMinutes(lockoutMinutes);
        }
    }

    private static string? ValidateNewPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
            return "Mật khẩu mới phải có ít nhất 12 ký tự.";
        if (password.Length > 256)
            return "Mật khẩu mới không được vượt quá 256 ký tự.";
        if (password != password.Trim())
            return "Mật khẩu mới không được có khoảng trắng ở đầu hoặc cuối.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(ch => !char.IsLetterOrDigit(ch)))
            return "Mật khẩu mới cần có chữ hoa, chữ thường, số và ký tự đặc biệt.";

        return null;
    }

    private static string HashRefreshToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    public async Task<Result> RequestPasswordResetAsync(PasswordResetRequestDto request, string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail))
        {
            return Result.Failure("Vui lòng nhập tên đăng nhập hoặc email cần cấp lại mật khẩu.");
        }

        var query = request.UsernameOrEmail.Trim().ToLowerInvariant();
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == query || u.Email.ToLower() == query, ct);

        var now = DateTime.UtcNow;
        var defaultCompanyId = user?.CompanyId ?? await _context.Companies.Select(c => c.Id).FirstOrDefaultAsync(ct);

        var details = JsonSerializer.Serialize(new
        {
            UsernameOrEmail = request.UsernameOrEmail.Trim(),
            MatchedUserId = user?.Id,
            MatchedUsername = user?.Username,
            MatchedEmail = user?.Email,
            FullName = request.FullName?.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            Note = request.Note?.Trim(),
            RequestedAtUtc = now,
            Status = "PendingAdminReview"
        });

        var auditLog = new AuditLog
        {
            Id = Guid.NewGuid(),
            CompanyId = defaultCompanyId,
            BusinessUnitId = user?.DefaultBusinessUnitId,
            UserId = user?.Id,
            Action = "PasswordResetRequested",
            EntityName = "User",
            EntityId = user?.Id.ToString() ?? "Unknown",
            NewValues = details,
            IpAddress = ipAddress,
            CreatedAt = now
        };

        _context.AuditLogs.Add(auditLog);
        await _context.SaveChangesAsync(ct);

        return Result.Success();
    }
}
