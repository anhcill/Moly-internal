using System.Windows;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class ThemeServiceTests
{
    [Fact]
    public void ToggleTheme_SwitchesThemeMode()
    {
        var service = ThemeService.Instance;
        service.SetTheme(AppTheme.Light);
        Assert.Equal(AppTheme.Light, service.CurrentTheme);
        Assert.False(service.IsDark);

        var toggled = service.ToggleTheme();
        Assert.Equal(AppTheme.Dark, toggled);
        Assert.Equal(AppTheme.Dark, service.CurrentTheme);
        Assert.True(service.IsDark);

        var toggledBack = service.ToggleTheme();
        Assert.Equal(AppTheme.Light, toggledBack);
        Assert.Equal(AppTheme.Light, service.CurrentTheme);
        Assert.False(service.IsDark);
    }

    [Fact]
    public void SetTheme_FiresThemeChangedEvent()
    {
        var service = ThemeService.Instance;
        service.SetTheme(AppTheme.Light);

        AppTheme? notifiedTheme = null;
        void OnThemeChanged(object? sender, AppTheme theme)
        {
            notifiedTheme = theme;
        }

        service.ThemeChanged += OnThemeChanged;
        try
        {
            service.SetTheme(AppTheme.Dark);
            Assert.Equal(AppTheme.Dark, notifiedTheme);

            service.SetTheme(AppTheme.Light);
            Assert.Equal(AppTheme.Light, notifiedTheme);
        }
        finally
        {
            service.ThemeChanged -= OnThemeChanged;
        }
    }
}
