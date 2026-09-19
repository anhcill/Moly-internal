using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

/// <summary>
/// Lưu phiên đăng nhập trên chính thiết bị Windows bằng DPAPI CurrentUser.
/// Không bao giờ lưu mật khẩu; chỉ lưu access/refresh token đã được mã hóa.
/// </summary>
public sealed class SessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _sessionPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MOLY",
        "InternalManagement",
        "session.dat");

    public void Save(string username, ApiClient.LoginResponse response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentNullException.ThrowIfNull(response);

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new SavedSession(username, response),
            JsonOptions);
        var protectedPayload = ProtectedData.Protect(
            payload,
            optionalEntropy: null,
            DataProtectionScope.CurrentUser);

        var directory = Path.GetDirectoryName(_sessionPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllBytes(_sessionPath, protectedPayload);
    }

    public bool TryLoad(out SavedSession? session)
    {
        session = null;

        try
        {
            if (!File.Exists(_sessionPath))
            {
                return false;
            }

            var protectedPayload = File.ReadAllBytes(_sessionPath);
            var payload = ProtectedData.Unprotect(
                protectedPayload,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);
            session = JsonSerializer.Deserialize<SavedSession>(payload, JsonOptions);

            if (session is null || string.IsNullOrWhiteSpace(session.Username) ||
                string.IsNullOrWhiteSpace(session.Response.RefreshToken))
            {
                Clear();
                session = null;
                return false;
            }

            return true;
        }
        catch (CryptographicException)
        {
            Clear();
            return false;
        }
        catch (JsonException)
        {
            Clear();
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_sessionPath))
            {
                File.Delete(_sessionPath);
            }
        }
        catch (IOException)
        {
            // Không làm hỏng thao tác đăng xuất nếu file đã bị xóa hoặc đang bị khóa.
        }
    }

    public sealed record SavedSession(string Username, ApiClient.LoginResponse Response);
}
