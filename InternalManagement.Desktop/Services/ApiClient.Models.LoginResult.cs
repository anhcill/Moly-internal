using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed class LoginResult
    {
        private LoginResult(bool succeeded, string? error, LoginResponse? response)
        {
            Succeeded = succeeded;
            Error = error;
            Response = response;
        }

        private LoginResponse? Response { get; }
        public bool Succeeded { get; }
        public string? Error { get; }
        public LoginResponse? Session => Response;
        public UserInfo? User => Response?.User;
        public string? AccessToken => Response?.AccessToken;
        public string? RefreshToken => Response?.RefreshToken;

        public static LoginResult Success(LoginResponse response) => new(true, null, response);
        public static LoginResult Failed(string error) => new(false, error, null);
    }
}
