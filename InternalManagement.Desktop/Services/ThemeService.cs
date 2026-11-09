using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace InternalManagement.Desktop.Services;

public enum AppTheme
{
    Light,
    Dark
}

/// <summary>
/// Quản lý giao diện sáng / tối (Light/Dark mode) cho toàn bộ ứng dụng MOLY.
/// Hỗ trợ chuyển đổi mượt mà không cần khởi động lại ứng dụng, lưu cấu hình
/// cục bộ và đồng bộ màu thanh tiêu đề Windows 10/11.
/// </summary>
public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Instance => _instance.Value;

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private readonly string _settingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MOLY",
        "InternalManagement",
        "theme-settings.json");

    private AppTheme _currentTheme = AppTheme.Light;
    public AppTheme CurrentTheme => _currentTheme;
    public bool IsDark => _currentTheme == AppTheme.Dark;

    public event EventHandler<AppTheme>? ThemeChanged;

    private sealed record ThemeConfig(string Theme);

    private ThemeService()
    {
    }

    public void Initialize()
    {
        var savedTheme = LoadSavedTheme();
        ApplyTheme(savedTheme, persist: false);
    }

    public AppTheme ToggleTheme()
    {
        var nextTheme = _currentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        SetTheme(nextTheme);
        return nextTheme;
    }

    public void SetTheme(AppTheme theme)
    {
        ApplyTheme(theme, persist: true);
    }

    private void ApplyTheme(AppTheme theme, bool persist)
    {
        _currentTheme = theme;

        if (persist)
        {
            SaveTheme(theme);
        }

        var app = Application.Current;
        if (app != null)
        {
            if (app.Dispatcher.CheckAccess())
            {
                ApplyThemeCore(app, theme);
            }
            else
            {
                app.Dispatcher.Invoke(() => ApplyThemeCore(app, theme));
            }
        }

        ThemeChanged?.Invoke(this, theme);
    }

    private void ApplyThemeCore(Application app, AppTheme theme)
    {
        var isDark = theme == AppTheme.Dark;
        var themeDictUri = new Uri(
            isDark ? "pack://application:,,,/Styles/Themes/Theme.Dark.xaml"
                   : "pack://application:,,,/Styles/Themes/Theme.Light.xaml",
            UriKind.Absolute);

        try
        {
            var newThemeDict = new ResourceDictionary { Source = themeDictUri };

            // Cập nhật các merged dictionaries
            var merged = app.Resources.MergedDictionaries;
            for (var i = merged.Count - 1; i >= 0; i--)
            {
                var d = merged[i];
                var src = d.Source?.OriginalString ?? string.Empty;
                if (src.Contains("Theme.Light.xaml", StringComparison.OrdinalIgnoreCase) ||
                    src.Contains("Theme.Dark.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    merged.RemoveAt(i);
                }
            }

            merged.Add(newThemeDict);

            // Cập nhật trực tiếp các key trong Application.Current.Resources để kích hoạt DynamicResource
            foreach (var key in newThemeDict.Keys)
            {
                app.Resources[key] = newThemeDict[key];
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load theme dictionary: {ex}");
        }

        // Cập nhật thanh tiêu đề các cửa sổ đang mở
        foreach (Window window in app.Windows)
        {
            SetWindowDarkMode(window, isDark);
        }
    }

    public static void SetWindowDarkMode(Window window, bool isDark)
    {
        if (window == null) return;

        try
        {
            var helper = new WindowInteropHelper(window);
            var handle = helper.Handle;
            if (handle != IntPtr.Zero)
            {
                int useDark = isDark ? 1 : 0;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int));
            }
            else
            {
                window.SourceInitialized += (s, e) =>
                {
                    try
                    {
                        var h = new WindowInteropHelper(window).Handle;
                        if (h != IntPtr.Zero)
                        {
                            int val = isDark ? 1 : 0;
                            DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int));
                        }
                    }
                    catch { }
                };
            }
        }
        catch
        {
            // Bỏ qua nếu chạy trên phiên bản Windows cũ không hỗ trợ DWM attribute này
        }
    }

    private AppTheme LoadSavedTheme()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var json = File.ReadAllText(_settingsFilePath);
                var config = JsonSerializer.Deserialize<ThemeConfig>(json);
                if (config != null && Enum.TryParse<AppTheme>(config.Theme, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
            }
        }
        catch
        {
            // Dùng mặc định nếu lỗi đọc file
        }

        return AppTheme.Light;
    }

    private void SaveTheme(AppTheme theme)
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new ThemeConfig(theme.ToString()));
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Không làm gián đoạn ứng dụng nếu không ghi được cấu hình
        }
    }
}
