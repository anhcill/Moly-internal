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
    // ── Login & Quick Login Workflow ──

    private void RefreshQuickLoginAccounts()
    {
        if (QuickLoginSection == null || QuickLoginItemsControl == null) return;

        var accounts = _sessionStore.LoadQuickAccounts();
        QuickLoginItemsControl.ItemsSource = accounts;
        QuickLoginSection.Visibility = accounts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetLoginBusy(bool isBusy, string? title = null, string? subText = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetLoginBusy(isBusy, title, subText));
            return;
        }

        if (LoginBusyOverlay != null)
        {
            LoginBusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        }
        if (LoginBusyTitle != null)
        {
            LoginBusyTitle.Text = title ?? "Đang xác thực hệ thống...";
        }
        if (LoginBusySubText != null)
        {
            LoginBusySubText.Text = subText ?? "Đang kết nối hệ thống dữ liệu MOLY...";
        }
        if (LoginButton != null)
        {
            LoginButton.IsEnabled = !isBusy;
            LoginButton.Content = isBusy ? "Đang xác thực..." : "Đăng nhập";
        }
        if (UsernameTextBox != null) UsernameTextBox.IsEnabled = !isBusy;
        if (PasswordBox != null) PasswordBox.IsEnabled = !isBusy;
        if (VisiblePasswordTextBox != null) VisiblePasswordTextBox.IsEnabled = !isBusy;
        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        var username = UsernameTextBox.Text.Trim();
        var password = VisiblePasswordTextBox.Visibility == Visibility.Visible
            ? VisiblePasswordTextBox.Text
            : PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            ShowLoginError("Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu được cấp.");
            return;
        }

        SetLoginBusy(true, "Đang đăng nhập...", "Xác thực tài khoản MOLY...");
        ResultBanner.Visibility = Visibility.Collapsed;

        try
        {
            ApiClient.LoginResult result;
            var apiRecentlyHealthy = _lastApiHealthyAtUtc.HasValue
                && DateTime.UtcNow - _lastApiHealthyAtUtc.Value <= TimeSpan.FromSeconds(45);

            if (apiRecentlyHealthy)
            {
                try
                {
                    result = await _apiClient.LoginAsync(username, password);
                }
                catch (HttpRequestException)
                {
                    _lastApiHealthyAtUtc = null;
                    if (!await WaitForApiReadyAsync())
                    {
                        SetLoginBusy(false);
                        ShowLoginError("Máy chủ đang khởi động hoặc chưa kết nối được cơ sở dữ liệu. Vui lòng chờ vài giây rồi thử lại.");
                        return;
                    }
                    result = await _apiClient.LoginAsync(username, password);
                }
            }
            else
            {
                if (!await WaitForApiReadyAsync())
                {
                    SetLoginBusy(false);
                    ShowLoginError("Máy chủ đang khởi động hoặc chưa kết nối được cơ sở liệu. Vui lòng chờ vài giây rồi thử lại.");
                    return;
                }
                result = await _apiClient.LoginAsync(username, password);
            }

            if (!result.Succeeded || result.User is null)
            {
                SetLoginBusy(false);
                ShowLoginError(ToFriendlyLoginError(result.Error));
                return;
            }

            _currentUser = result.User;
            _sessionUsername = username;
            _rememberSession = RememberMeCheckBox.IsChecked == true;
            if (_rememberSession && result.Session is not null)
            {
                _sessionStore.Save(username, result.Session);
            }
            else
            {
                _sessionStore.Clear();
            }

            ApplySelectedLoginBusinessUnit();
            await EnterAppShellAsync();
        }
        catch (HttpRequestException)
        {
            SetLoginBusy(false);
            ShowLoginError("Không thể kết nối máy chủ dữ liệu nội bộ. Vui lòng kiểm tra kết nối mạng và thử lại.");
        }
        catch (Exception)
        {
            SetLoginBusy(false);
            ShowLoginError("Đăng nhập chưa hoàn tất. Vui lòng thử lại hoặc liên hệ Ban Công nghệ & Quản trị IT.");
        }
        finally
        {
            SetLoginBusy(false);
        }
    }

    private async void QuickLoginAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string username } || string.IsNullOrWhiteSpace(username))
            return;

        var entry = _sessionStore.LoadQuickAccounts()
            .FirstOrDefault(a => string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            RefreshQuickLoginAccounts();
            return;
        }

        UsernameTextBox.Text = entry.Username;
        UpdateUsernameWatermark();
        RememberMeCheckBox.IsChecked = true;
        ResultBanner.Visibility = Visibility.Collapsed;

        SetLoginBusy(true, $"Đang vào nhanh — {entry.FullName}...");
        try
        {
            _sessionUsername = entry.Username;
            _rememberSession = true;
            _apiClient.RestoreSession(entry.Response);

            var sessionUsable = entry.Response.ExpiresAt > DateTime.UtcNow.AddSeconds(30)
                || await _apiClient.RefreshTokenAsync();
            if (!sessionUsable)
            {
                _apiClient.SetBearerToken(null);
                SetLoginBusy(false);
                ShowLoginError($"Phiên của {entry.FullName} đã hết hạn. Vui lòng nhập mật khẩu để đăng nhập lại.");
                PasswordBox.Focus();
                return;
            }

            var activeSession = _apiClient.CurrentSession ?? entry.Response;
            _currentUser = activeSession.User ?? entry.Response.User;
            _sessionStore.Save(entry.Username, activeSession);
            ApplySelectedLoginBusinessUnit();
            await EnterAppShellAsync();
        }
        catch (HttpRequestException)
        {
            _apiClient.SetBearerToken(null);
            SetLoginBusy(false);
            ShowLoginError("Không thể kết nối máy chủ để đăng nhập nhanh. Vui lòng kiểm tra mạng nội bộ.");
        }
        catch (Exception)
        {
            _apiClient.SetBearerToken(null);
            SetLoginBusy(false);
            ShowLoginError($"Không thể đăng nhập nhanh bằng tài khoản {entry.Username}. Vui lòng nhập mật khẩu.");
            PasswordBox.Focus();
        }
        finally
        {
            SetLoginBusy(false);
        }
    }

    private void RemoveQuickAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string username } || string.IsNullOrWhiteSpace(username))
            return;

        _sessionStore.RemoveQuickAccount(username);
        if (string.Equals(_sessionUsername, username, StringComparison.OrdinalIgnoreCase))
        {
            _sessionStore.Clear();
        }
        RefreshQuickLoginAccounts();
    }

    private void ApplySelectedLoginBusinessUnit()
    {
        if (LoginBusinessUnitCombo?.SelectedIndex == 2)
        {
            _activeSegment = TechnologySegment;
        }
        else if (LoginBusinessUnitCombo?.SelectedIndex == 3)
        {
            _activeSegment = FashionSegment;
        }
    }

    private static string ToFriendlyLoginError(string? errorCode)
        => errorCode switch
        {
            "INVALID_CREDENTIALS" => "Tên đăng nhập hoặc mật khẩu chưa chính xác.",
            "LOGIN_TIMEOUT" => "Kết nối quá thời gian. Vui lòng thử lại.",
            "LOGIN_UNAVAILABLE" => "Hệ thống đăng nhập chưa sẵn sàng. Ứng dụng sẽ kiểm tra lại máy chủ, vui lòng thử lại sau ít giây.",
            _ => "Đăng nhập chưa thành công. Vui lòng kiểm tra thông tin hoặc liên hệ quản trị viên."
        };

    private bool HandleExpiredAuthentication()
    {
        if (!_apiClient.RequiresAuthentication) return false;

        _apiClient.TakeLastApiError();
        HandleAuthenticationExpired();
        return true;
    }

    private void ShowLoginError(string message)
    {
        if (ResultBanner != null)
        {
            ResultBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2D1515"));
            ResultBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7F1D1D"));
            ResultBanner.Visibility = Visibility.Visible;
        }
        if (ResultText != null)
        {
            ResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
            ResultText.Text = message;
        }
    }

    private Task EnterAppShellAsync()
    {
        SetLoginBusy(false);
        LoginContainer.Visibility = Visibility.Collapsed;
        AppShellContainer.Visibility = Visibility.Visible;

        if (_currentUser != null)
        {
            UserFullNameText.Text = _currentUser.FullName;
            UserRoleBadge.Text = string.Join(", ", _currentUser.Roles);
            var initials = string.Join("", _currentUser.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w[0])).ToUpperInvariant();
            UserAvatarText.Text = initials.Length > 2 ? initials[..2] : initials;
        }

        // Enter shell immediately; Nav_Checked loads Dashboard + BusinessUnits concurrently.
        ApplyNavigationPermissions();
        return Task.CompletedTask;
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRememberedSession();
        ResetSessionState();
        RefreshQuickLoginAccounts();

        AppShellContainer.Visibility = Visibility.Collapsed;
        LoginContainer.Visibility = Visibility.Visible;
        PasswordBox.Password = string.Empty;
        VisiblePasswordTextBox.Text = string.Empty;
        VisiblePasswordTextBox.Visibility = Visibility.Collapsed;
        PasswordBox.Visibility = Visibility.Visible;
        PasswordVisibilityButton.ToolTip = "Hiện mật khẩu";
        ResultBanner.Visibility = Visibility.Collapsed;
    }

}
