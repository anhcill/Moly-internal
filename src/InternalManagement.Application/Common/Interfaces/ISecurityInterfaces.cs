namespace InternalManagement.Application.Common.Interfaces;

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public interface IJwtTokenGenerator
{
    (string Token, DateTime ExpiresAt) GenerateAccessToken(
        Guid userId,
        string username,
        string email,
        Guid companyId,
        Guid? businessUnitId,
        IReadOnlyList<string> roles,
        IReadOnlyList<string> permissions);

    (string Token, DateTime ExpiresAt) GenerateRefreshToken();
}

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
}
