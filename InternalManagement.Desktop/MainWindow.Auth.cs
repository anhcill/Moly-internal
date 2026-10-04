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
    // ── Login Workflow ──

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
            LoginButton.Content = isBusy ? "ĐANG XÁC THỰC BẢO MẬT..." : "ĐĂNG NHẬP VÀO HỆ THỐNG →";
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

        SetLoginBusy(true, "Đang xác thực bảo mật...", "Kiểm tra thông tin tài khoản và phân quyền truy cập MOLY...");
        ResultBanner.Visibility = Visibility.Collapsed;

        try
        {
            if (!await WaitForApiReadyAsync())
            {
                SetLoginBusy(false);
                ShowLoginError("Máy chủ đang khởi động hoặc chưa kết nối được cơ sở dữ liệu. Vui lòng chờ vài giây rồi thử lại.");
                return;
            }

            var result = await _apiClient.LoginAsync(username, password);

            if (!result.Succeeded || result.User is null)
            {
                SetLoginBusy(false);
                ShowLoginError(ToFriendlyLoginError(result.Error));
                return;
            }

            _currentUser = result.User;
            if (RememberMeCheckBox.IsChecked == true && result.Session is not null)
            {
                _sessionStore.Save(username, result.Session);
            }
            else
            {
                _sessionStore.Clear();
            }

            if (LoginBusinessUnitCombo?.SelectedIndex == 1)
            {
                _activeSegment = TechnologySegment;
            }
            else if (LoginBusinessUnitCombo?.SelectedIndex == 2)
            {
                _activeSegment = FashionSegment;
            }

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

    private static string ToFriendlyLoginError(string? errorCode)
        => errorCode switch
        {
            "INVALID_CREDENTIALS" => "Tên đăng nhập hoặc mật khẩu chưa chính xác.",
            "LOGIN_TIMEOUT" => "Kết nối quá thời gian. Vui lòng thử lại.",
            "LOGIN_UNAVAILABLE" => "Hệ thống đăng nhập chưa sẵn sàng. Ứng dụng sẽ kiểm tra lại máy chủ, vui lòng thử lại sau ít giây.",
            _ => "Đăng nhập chưa thành công. Vui lòng kiểm tra thông tin hoặc liên hệ quản trị viên."
        };

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

    private async Task EnterAppShellAsync()
    {
        SetLoginBusy(false);
        LoginContainer.Visibility = Visibility.Collapsed;
        AppShellContainer.Visibility = Visibility.Visible;

        if (_currentUser != null)
        {
            UserFullNameText.Text = _currentUser.FullName;
            UserRoleBadge.Text = string.Join(", ", _currentUser.Roles);
            var initials = string.Join("", _currentUser.FullName.Split(' ').Select(w => w.FirstOrDefault())).ToUpperInvariant();
            UserAvatarText.Text = initials.Length > 2 ? initials[..2] : initials;
        }

        ApplyNavigationPermissions();

        await RunWithBusyAsync("Đang khởi tạo không gian làm việc...", async () =>
        {
            _cachedBusinessUnits = await _apiClient.GetBusinessUnitProfitSummaryAsync() ?? new List<ApiClient.BusinessUnitProfitItem>();
            await LoadDashboardMetricsAsync();
        }, "Đồng bộ quyền hạn, mảng kinh doanh & số liệu tổng quan");
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        ClearRememberedSession();
        _currentUser = null;

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
