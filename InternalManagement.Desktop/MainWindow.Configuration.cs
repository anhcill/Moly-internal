using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private static string LoadApiBaseUrl()
    {
#if DEBUG
        const string fallback = "http://localhost:59724/";
        const string settingsFileName = "appsettings.json";
#else
        const string fallback = "https://internal-management-api-production-6f08.up.railway.app/";
        const string settingsFileName = "appsettings.production.json";
#endif
        var path = Path.Combine(AppContext.BaseDirectory, settingsFileName);
        if (!File.Exists(path))
        {
            return fallback;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement
                .GetProperty("ApiSettings")
                .GetProperty("BaseUrl")
                .GetString() ?? fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private static TimeSpan LoadApiTimeout()
    {
        const int fallbackSeconds = 30;
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return TimeSpan.FromSeconds(fallbackSeconds);

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            var seconds = document.RootElement
                .GetProperty("ApiSettings")
                .GetProperty("TimeoutSeconds")
                .GetInt32();
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 120));
        }
        catch (Exception)
        {
            return TimeSpan.FromSeconds(fallbackSeconds);
        }
    }

}
