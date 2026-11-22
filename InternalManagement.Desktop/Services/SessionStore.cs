using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

/// <summary>
/// Lưu phiên đăng nhập và danh sách tài khoản đăng nhập nhanh trên chính thiết bị Windows
/// bằng DPAPI CurrentUser. Không bao giờ lưu mật khẩu; chỉ lưu access/refresh token đã mã hóa.
/// </summary>
public sealed class SessionStore
{
    private const int MaxQuickAccounts = 3;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _storageDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MOLY",
        "InternalManagement");

    private string SessionPath => Path.Combine(_storageDir, "session.dat");
    private string QuickAccountsPath => Path.Combine(_storageDir, "quick-accounts.dat");

    public void Save(string username, ApiClient.LoginResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(response);

        WriteProtected(SessionPath, new SavedSession(username, response));
        SaveQuickAccount(username, response);
    }

    public bool TryLoad(out SavedSession? session)
    {
        session = ReadProtected<SavedSession>(SessionPath);
        if (session is null || string.IsNullOrWhiteSpace(session.Username) ||
            string.IsNullOrWhiteSpace(session.Response.RefreshToken))
        {
            if (session is not null) Clear();
            session = null;
            return false;
        }

        return true;
    }

    public void Clear()
    {
        DeleteFileQuietly(SessionPath);
    }

    public void SaveQuickAccount(string username, ApiClient.LoginResponse response)
    {
        if (string.IsNullOrWhiteSpace(username) || response is null || string.IsNullOrWhiteSpace(response.RefreshToken))
            return;

        try
        {
            var accounts = LoadQuickAccounts()
                .Where(a => !string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var fullName = string.IsNullOrWhiteSpace(response.User?.FullName) ? username : response.User.FullName.Trim();
            var roleLabel = response.User?.Roles is { Count: > 0 } roles
                ? string.Join(", ", roles.Take(2))
                : "Thành viên";
            var initials = BuildInitials(fullName);

            accounts.Insert(0, new QuickAccountEntry(
                username.Trim(),
                fullName,
                roleLabel,
                initials,
                DateTime.UtcNow,
                response));

            if (accounts.Count > MaxQuickAccounts)
                accounts = accounts.Take(MaxQuickAccounts).ToList();

            WriteProtected(QuickAccountsPath, accounts);
        }
        catch (IOException)
        {
            // Bỏ qua nếu không thể ghi bộ nhớ nhanh cục bộ.
        }
        catch (CryptographicException)
        {
            // Bỏ qua nếu DPAPI tạm thời không khả dụng.
        }
    }

    public IReadOnlyList<QuickAccountEntry> LoadQuickAccounts()
    {
        var accounts = ReadProtected<List<QuickAccountEntry>>(QuickAccountsPath);
        if (accounts is null || accounts.Count == 0)
            return Array.Empty<QuickAccountEntry>();

        return accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Username) && a.Response is not null)
            .OrderByDescending(a => a.LastUsedAt)
            .Take(MaxQuickAccounts)
            .ToList();
    }

    public void RemoveQuickAccount(string username)
    {
        if (string.IsNullOrWhiteSpace(username)) return;

        var remaining = LoadQuickAccounts()
            .Where(a => !string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (remaining.Count == 0)
        {
            DeleteFileQuietly(QuickAccountsPath);
            return;
        }

        try
        {
            WriteProtected(QuickAccountsPath, remaining);
        }
        catch (IOException) { }
        catch (CryptographicException) { }
    }

    private void WriteProtected<T>(string path, T value)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        var protectedPayload = ProtectedData.Protect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);
        Directory.CreateDirectory(_storageDir);
        File.WriteAllBytes(path, protectedPayload);
    }

    private T? ReadProtected<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var protectedPayload = File.ReadAllBytes(path);
            var payload = ProtectedData.Unprotect(protectedPayload, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<T>(payload, JsonOptions);
        }
        catch (CryptographicException)
        {
            DeleteFileQuietly(path);
            return null;
        }
        catch (JsonException)
        {
            DeleteFileQuietly(path);
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void DeleteFileQuietly(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { }
    }

    private static string BuildInitials(string fullName)
    {
        var letters = fullName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]))
            .ToArray();
        if (letters.Length == 0) return "M";
        if (letters.Length == 1) return letters[0].ToString();
        return $"{letters[0]}{letters[^1]}";
    }

    public sealed record SavedSession(string Username, ApiClient.LoginResponse Response);

    public sealed record QuickAccountEntry(
        string Username,
        string FullName,
        string RoleLabel,
        string Initials,
        DateTime LastUsedAt,
        ApiClient.LoginResponse Response);
}
