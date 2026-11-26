using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class SessionStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"moly-session-test-{Guid.NewGuid():N}");

    [Fact]
    public void LegacySessionFile_AutoMigratesIntoQuickAccountsAndSurvivesLogoutClear()
    {
        Directory.CreateDirectory(_tempDir);
        var legacySession = new SessionStore.SavedSession(
            "admin",
            BuildLoginResponse("admin", "Admin Tổng MOLY", "SuperAdmin", "refresh-token-1"));
        var payload = JsonSerializer.SerializeToUtf8Bytes(legacySession, JsonOptions);
        var protectedBytes = ProtectedData.Protect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(Path.Combine(_tempDir, "session.dat"), protectedBytes);

        var store = new SessionStore(_tempDir);

        // Simulate app update startup restoring legacy session.dat
        Assert.True(store.TryLoad(out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal("admin", loaded!.Username);

        // Simulate user clicking Logout (clears session.dat)
        store.Clear();
        Assert.False(store.TryLoad(out _));

        // Quick Login card must still be available on Login screen
        var quickAccounts = store.LoadQuickAccounts();
        Assert.Single(quickAccounts);
        Assert.Equal("admin", quickAccounts[0].Username);
        Assert.Equal("Admin Tổng MOLY", quickAccounts[0].FullName);
        Assert.Equal("refresh-token-1", quickAccounts[0].Response.RefreshToken);
    }

    [Fact]
    public void SaveAndRemoveQuickAccounts_KeepsMostRecentThreeAndRemovesCleanly()
    {
        var store = new SessionStore(_tempDir);
        store.Save("user1", BuildLoginResponse("user1", "Nguyễn An", "Admin", "rt-1"));
        store.Save("user2", BuildLoginResponse("user2", "Trần Bình", "Accountant", "rt-2"));
        store.Save("user3", BuildLoginResponse("user3", "Lê Chi", "HR", "rt-3"));
        store.Save("user4", BuildLoginResponse("user4", "Phạm Dũng", "Teacher", "rt-4"));

        var accounts = store.LoadQuickAccounts();
        Assert.Equal(3, accounts.Count);
        Assert.Equal("user4", accounts[0].Username);
        Assert.DoesNotContain(accounts, a => a.Username == "user1");

        store.RemoveQuickAccount("user3");
        var remaining = store.LoadQuickAccounts();
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, a => a.Username == "user3");
    }

    private static ApiClient.LoginResponse BuildLoginResponse(
        string username, string fullName, string role, string refreshToken) =>
        new(
            AccessToken: $"access-{username}",
            RefreshToken: refreshToken,
            ExpiresAt: DateTime.UtcNow.AddHours(1),
            User: new ApiClient.UserInfo(
                Guid.NewGuid(),
                username,
                $"{username}@moly.vn",
                fullName,
                [role],
                []));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException) { }
    }
}
