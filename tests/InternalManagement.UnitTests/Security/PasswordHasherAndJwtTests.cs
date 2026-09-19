using System.IdentityModel.Tokens.Jwt;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using InternalManagement.Infrastructure.Security;

namespace InternalManagement.UnitTests.Security;

public class PasswordHasherAndJwtTests
{
    private readonly PasswordHasher _passwordHasher = new();

    [Fact]
    public void HashPassword_ShouldReturnValidBcryptHash()
    {
        // Arrange
        var password = "SuperSecretPassword123!";

        // Act
        var hash = _passwordHasher.HashPassword(password);

        // Assert
        hash.Should().NotBeNullOrWhiteSpace();
        hash.Should().StartWith("$2");
        _passwordHasher.VerifyPassword(password, hash).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_WithIncorrectPassword_ShouldReturnFalse()
    {
        // Arrange
        var password = "CorrectPassword123!";
        var hash = _passwordHasher.HashPassword(password);

        // Act & Assert
        _passwordHasher.VerifyPassword("WrongPassword123!", hash).Should().BeFalse();
        _passwordHasher.VerifyPassword("", hash).Should().BeFalse();
        _passwordHasher.VerifyPassword(password, "").Should().BeFalse();
    }

    [Fact]
    public void JwtTokenGenerator_ShouldGenerateValidTokenWithClaims()
    {
        // Arrange
        var configValues = new Dictionary<string, string?>
        {
            ["JwtSettings:Secret"] = "VeryLongSecretKeyForTestingJwtTokensInAntigravity2026!",
            ["JwtSettings:Issuer"] = "TestIssuer",
            ["JwtSettings:Audience"] = "TestAudience",
            ["JwtSettings:AccessTokenExpirationMinutes"] = "30",
            ["JwtSettings:RefreshTokenExpirationDays"] = "14"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();
        var tokenGenerator = new JwtTokenGenerator(configuration);

        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();
        var roles = new[] { "SuperAdmin", "Director" };
        var permissions = new[] { "Permissions.Courses.View", "Permissions.Courses.Manage" };

        // Act
        var (token, expiresAt) = tokenGenerator.GenerateAccessToken(
            userId,
            "testuser",
            "test@moli.local",
            companyId,
            buId,
            roles,
            permissions);

        // Assert
        token.Should().NotBeNullOrWhiteSpace();
        expiresAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(25));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Issuer.Should().Be("TestIssuer");
        jwt.Audiences.Should().Contain("TestAudience");
        jwt.Claims.Should().Contain(c => c.Type == "company_id" && c.Value == companyId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "bu_id" && c.Value == buId.ToString());
        jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value).Should().BeEquivalentTo(permissions);
    }
}
