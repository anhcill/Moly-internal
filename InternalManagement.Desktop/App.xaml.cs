using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace InternalManagement.Desktop;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        // This must be the first startup call so Velopack can handle install/update hooks
        // before WPF creates a window.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Do not block the first window while the release feed is contacted.
        _ = CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync()
    {
#if DEBUG
        // Local F5 runs are not installed Velopack packages and should not contact GitHub.
        await Task.CompletedTask;
        return;
#else
        try
        {
            var settings = LoadUpdateSettings();
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.RepositoryUrl))
            {
                return;
            }

            var updateManager = new UpdateManager(
                new GithubSource(settings.RepositoryUrl, accessToken: null, settings.IncludePrerelease));

            // The existing Inno Setup installation is intentionally left alone. The first
            // Velopack Setup.exe install opts into self-updates; until then this is a no-op.
            if (!updateManager.IsInstalled)
            {
                return;
            }

            var update = await updateManager.CheckForUpdatesAsync();
            if (update is null)
            {
                return;
            }

            var currentVersion = updateManager.CurrentVersion?.ToString() ?? "hiện tại";
            var newVersion = update.TargetFullRelease.Version.ToString();
            var choice = MessageBox.Show(
                $"MOLY có phiên bản mới v{newVersion}.\n\n" +
                $"Phiên bản đang dùng: v{currentVersion}\n" +
                "Bạn có muốn tải và cập nhật ngay không?",
                "Có phiên bản MOLY mới",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (choice != MessageBoxResult.Yes)
            {
                return;
            }

            await updateManager.DownloadUpdatesAsync(update);
            updateManager.ApplyUpdatesAndRestart(update);
        }
        catch (Exception ex)
        {
            // An update feed outage must never prevent the user from opening the app.
            Debug.WriteLine($"Velopack update check failed: {ex}");
        }
#endif
    }

    private static UpdateSettings LoadUpdateSettings()
    {
#if DEBUG
        const string settingsFileName = "appsettings.json";
#else
        const string settingsFileName = "appsettings.production.json";
#endif

        var path = Path.Combine(AppContext.BaseDirectory, settingsFileName);
        if (!File.Exists(path))
        {
            return new UpdateSettings(false, null, false);
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (!document.RootElement.TryGetProperty("Velopack", out var section))
            {
                return new UpdateSettings(false, null, false);
            }

            var enabled = section.TryGetProperty("Enabled", out var enabledValue) &&
                          enabledValue.ValueKind == JsonValueKind.True;
            var repositoryUrl = section.TryGetProperty("GitHubRepository", out var repositoryValue)
                ? repositoryValue.GetString()
                : null;
            var includePrerelease = section.TryGetProperty("IncludePrerelease", out var prereleaseValue) &&
                                    prereleaseValue.ValueKind == JsonValueKind.True;

            return new UpdateSettings(enabled, repositoryUrl, includePrerelease);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"Velopack settings are invalid: {ex.Message}");
            return new UpdateSettings(false, null, false);
        }
    }

    private sealed record UpdateSettings(bool Enabled, string? RepositoryUrl, bool IncludePrerelease);
}
