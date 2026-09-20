using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class MainWindow : Window, IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly SessionStore _sessionStore;
    private ApiClient.UserInfo? _currentUser;
    private int _toastSequence;
    private bool _isUiReady;
    private bool _isFashionSelectionSyncing;
    private bool _isInventoryWarehouseSelectionSyncing;
    private bool _isFashionFunctionSelectionSyncing;
    private bool _isPayrollFunctionSelectionSyncing;
    private int _busyOperationCount;
    private static readonly TimeSpan ApiReadyWaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ApiHealthProbeTimeout = TimeSpan.FromSeconds(3);
    private const string TechnologySegment = "cong-nghe-giao-duc";
    private const string FashionSegment = "thoi-trang";
    private string _activeSegment = TechnologySegment;
    private bool _isSidebarCollapsed;
    private InternalDataViewMode _internalDataMode = InternalDataViewMode.Customers;
    private List<ApiClient.BusinessUnitProfitItem> _cachedBusinessUnits = new();
    private readonly HashSet<string> _loadedViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task> _viewLoadTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _searchDebouncers = new(StringComparer.OrdinalIgnoreCase);

    private List<ApiClient.CourseItem> _cachedCourses = new();
    private List<ApiClient.SalesOrderSummaryItem> _allSalesOrders = new();
    private List<ApiClient.CscaClassItem> _allCscaClasses = new();
    private Guid? _selectedCourseFilterId;
    private string? _selectedCourseFilterTitle;

    public sealed record CourseFilterOption(Guid? Id, string Title);

    private enum InternalDataViewMode
    {
        Customers,
        Resources
    }

    public MainWindow()
    {
        _apiClient = new ApiClient(LoadApiBaseUrl(), LoadApiTimeout());
        _sessionStore = new SessionStore();
        InitializeComponent();
        FinanceFromDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        FinanceToDatePicker.SelectedDate = DateTime.Today;
        PayrollCreateStartDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        PayrollCreateEndDatePicker.SelectedDate = DateTime.Today;
        PayrollSummaryYearCombo.ItemsSource = Enumerable.Range(DateTime.Today.Year - 2, 5).OrderByDescending(year => year).ToList();
        PayrollSummaryYearCombo.SelectedItem = DateTime.Today.Year;
        _isUiReady = true;

        Loaded += MainWindow_Loaded;
        Closed += (_, _) => Dispose();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateUsernameWatermark();
        UpdatePasswordWatermark();
        await RefreshApiHealthAsync();
        await RestoreRememberedSessionAsync();
    }

    private async Task RefreshApiHealthAsync()
    {
        try
        {
            using var probeCts = new CancellationTokenSource(ApiHealthProbeTimeout);
            var health = await _apiClient.CheckHealthAsync(probeCts.Token);
            SetApiHealthState(health.IsHealthy);
        }
        catch
        {
            SetApiHealthState(false);
        }
    }

    private async Task<bool> WaitForApiReadyAsync(CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + ApiReadyWaitTimeout;
        SetApiHealthState(false, "Máy chủ xác thực: Đang khởi động...");

        while (DateTime.UtcNow < deadline)
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            probeCts.CancelAfter(ApiHealthProbeTimeout);

            var health = await _apiClient.CheckHealthAsync(probeCts.Token);
            if (health.IsHealthy)
            {
                SetApiHealthState(true);
                return true;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        SetApiHealthState(false, "Máy chủ xác thực: Chưa sẵn sàng");
        return false;
    }

    private void SetApiHealthState(bool isHealthy, string? statusText = null)
    {
        if (ApiHealthIndicator == null || ApiHealthStatusText == null) return;

        ApiHealthIndicator.Fill = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isHealthy ? "#10B981" : "#F59E0B"));
        ApiHealthStatusText.Text = statusText ?? (isHealthy
            ? "Máy chủ xác thực: Trực tuyến"
            : "Máy chủ xác thực: Đang khởi động...");
    }

    private async Task RestoreRememberedSessionAsync()
    {
        if (!_sessionStore.TryLoad(out var savedSession) || savedSession is null)
        {
            return;
        }

        UsernameTextBox.Text = savedSession.Username;
        UpdateUsernameWatermark();
        RememberMeCheckBox.IsChecked = true;
        _apiClient.RestoreSession(savedSession.Response);

        SetLoginBusy(true, "Đang khôi phục phiên làm việc...", $"Chào mừng trở lại! Đang đồng bộ thông tin tài khoản {savedSession.Username}...");

        try
        {
            var sessionIsUsable = savedSession.Response.ExpiresAt > DateTime.UtcNow.AddSeconds(30) ||
                                  await _apiClient.RefreshTokenAsync();
            if (!sessionIsUsable)
            {
                ClearRememberedSession();
                SetLoginBusy(false);
                return;
            }

            _currentUser = savedSession.Response.User;
            await EnterAppShellAsync();
        }
        catch (HttpRequestException)
        {
            ClearRememberedSession();
            SetLoginBusy(false);
            ShowLoginError("Không thể khôi phục phiên đăng nhập. Vui lòng đăng nhập lại khi mạng nội bộ sẵn sàng.");
        }
        catch (Exception)
        {
            ClearRememberedSession();
            SetLoginBusy(false);
        }
    }

    private void ClearRememberedSession()
    {
        _sessionStore.Clear();
        _apiClient.SetBearerToken(null);
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            LoginButton_Click(sender, e);
        }
    }

    private void RememberMeCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (RememberMeCheckBox.IsChecked != true)
        {
            _sessionStore.Clear();
        }
    }

    private void PasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (VisiblePasswordTextBox.Visibility == Visibility.Visible)
        {
            PasswordBox.Password = VisiblePasswordTextBox.Text;
            VisiblePasswordTextBox.Visibility = Visibility.Collapsed;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordVisibilityButton.ToolTip = "Hiện mật khẩu";
            PasswordBox.Focus();
            return;
        }

        VisiblePasswordTextBox.Text = PasswordBox.Password;
        PasswordBox.Visibility = Visibility.Collapsed;
        VisiblePasswordTextBox.Visibility = Visibility.Visible;
        PasswordVisibilityButton.ToolTip = "Ẩn mật khẩu";
        VisiblePasswordTextBox.Focus();
        VisiblePasswordTextBox.CaretIndex = VisiblePasswordTextBox.Text.Length;
    }

    private void UpdateCapsLockWarning()
    {
        if (CapsLockWarning == null) return;
        try
        {
            CapsLockWarning.Visibility = Console.CapsLock ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            CapsLockWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateCapsLockWarning();
        UpdatePasswordWatermark();
    }

    private void PasswordBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateCapsLockWarning();
    }

    private void PasswordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (CapsLockWarning != null)
        {
            CapsLockWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void VisiblePasswordTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCapsLockWarning();
        UpdatePasswordWatermark();
    }

    private void UpdatePasswordWatermark()
    {
        if (PasswordWatermark == null) return;
        var hasText = !string.IsNullOrEmpty(PasswordBox?.Password) || !string.IsNullOrEmpty(VisiblePasswordTextBox?.Text);
        PasswordWatermark.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateUsernameWatermark()
    {
        if (UsernameWatermark == null) return;
        var hasText = !string.IsNullOrEmpty(UsernameTextBox?.Text);
        UsernameWatermark.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUsernameWatermark();
    }

    private void UsernameTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateUsernameWatermark();
    }

    private void UsernameTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateUsernameWatermark();
    }

    private void VisiblePasswordTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateCapsLockWarning();
    }

    private void VisiblePasswordTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (CapsLockWarning != null)
        {
            CapsLockWarning.Visibility = Visibility.Collapsed;
        }
    }

    private void LoginServerConfigButton_Click(object sender, RoutedEventArgs e)
    {
        var isHealthy = ApiHealthStatusText?.Text?.Contains("Trực tuyến") == true;
        var info = "Hệ thống MOLY Enterprise OS\n\n" +
                   $"• Trạng thái máy chủ: {(isHealthy ? "Đang hoạt động (Trực tuyến)" : "Chưa kết nối / Đang kiểm tra")}\n" +
                   "• Giao thức bảo mật: JWT Bearer Token + AES-256 GCM\n" +
                   "• Phân hệ: EdTech, Fashion, HRM, Finance\n" +
                   "• Phiên bản: v2.4.0 (Windows x64 Enterprise Edition)";
        MessageBox.Show(info, "Thông tin hệ thống MOLY", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ForgotPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (ForgotPasswordOverlay == null) return;

        if (ForgotUsernameOrEmailBox != null)
        {
            ForgotUsernameOrEmailBox.Text = UsernameTextBox?.Text?.Trim() ?? string.Empty;
        }
        if (ForgotFullNameBox != null) ForgotFullNameBox.Text = string.Empty;
        if (ForgotPhoneBox != null) ForgotPhoneBox.Text = string.Empty;
        if (ForgotNoteBox != null) ForgotNoteBox.Text = string.Empty;
        if (ForgotStatusBanner != null) ForgotStatusBanner.Visibility = Visibility.Collapsed;

        ForgotPasswordOverlay.Visibility = Visibility.Visible;
        ForgotUsernameOrEmailBox?.Focus();
    }

    private void CancelForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        if (ForgotPasswordOverlay != null)
        {
            ForgotPasswordOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private async void SubmitForgotPassword_Click(object sender, RoutedEventArgs e)
    {
        var usernameOrEmail = ForgotUsernameOrEmailBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(usernameOrEmail))
        {
            ShowForgotStatus("Vui lòng nhập tên đăng nhập hoặc email tài khoản của bạn.", isError: true);
            ForgotUsernameOrEmailBox?.Focus();
            return;
        }

        var fullName = ForgotFullNameBox?.Text?.Trim();
        var phone = ForgotPhoneBox?.Text?.Trim();
        var note = ForgotNoteBox?.Text?.Trim();

        if (SubmitForgotPasswordButton != null)
        {
            SubmitForgotPasswordButton.IsEnabled = false;
            SubmitForgotPasswordButton.Content = "Đang gửi yêu cầu...";
        }

        try
        {
            var (succeeded, message) = await _apiClient.RequestPasswordResetAsync(usernameOrEmail, fullName, phone, note);
            if (succeeded)
            {
                ShowForgotStatus(message, isError: false);
                await Task.Delay(2000);
                if (ForgotPasswordOverlay != null)
                {
                    ForgotPasswordOverlay.Visibility = Visibility.Collapsed;
                }
                ShowLoginError("Yêu cầu cấp lại mật khẩu đã được gửi đến Admin tổng.");
                if (ResultBanner != null)
                {
                    ResultBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B291B"));
                    ResultBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#166534"));
                }
                if (ResultText != null)
                {
                    ResultText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#86EFAC"));
                    ResultText.Text = "Yêu cầu cấp lại mật khẩu đã được gửi thành công đến Admin tổng.";
                }
            }
            else
            {
                ShowForgotStatus(message, isError: true);
            }
        }
        catch (Exception ex)
        {
            ShowForgotStatus("Lỗi kết nối khi gửi yêu cầu: " + ex.Message, isError: true);
        }
        finally
        {
            if (SubmitForgotPasswordButton != null)
            {
                SubmitForgotPasswordButton.IsEnabled = true;
                SubmitForgotPasswordButton.Content = "Gửi đến Admin tổng";
            }
        }
    }

    private void ShowForgotStatus(string message, bool isError)
    {
        if (ForgotStatusBanner == null || ForgotStatusText == null) return;

        ForgotStatusBanner.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isError ? "#2D1515" : "#0B291B"));
        ForgotStatusBanner.BorderBrush = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isError ? "#7F1D1D" : "#166534"));
        ForgotStatusText.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(isError ? "#FCA5A5" : "#86EFAC"));
        ForgotStatusText.Text = (isError ? "⛔ " : "✓ ") + message;
        ForgotStatusBanner.Visibility = Visibility.Visible;
    }

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

    // ── Navigation Switching ──

    private static bool HasPermission(ApiClient.UserInfo? user, string permission)
        => user?.Permissions?.Any(p => string.Equals(p, permission, StringComparison.OrdinalIgnoreCase)) == true;

    private void ApplyNavigationPermissions()
    {
        SetNavigationAccess(NavCourses, "Permissions.Courses.View");
        SetNavigationAccess(NavQuestions, "Permissions.Questions.View");
        SetNavigationAccess(NavCsca, "Permissions.CscaClasses.View");
        SetNavigationAccess(NavInterview, "Permissions.InterviewCustomers.View");
        SetNavigationAccess(NavTechEmployees, "Permissions.Employees.View");
        SetNavigationAccess(NavFashionEmployees, "Permissions.Employees.View");
        SetPayrollSubNavigationAccess(
            new[] { NavTechEmployeesWorking, NavTechEmployeesProfiles, NavTechEmployeesMissingCv, NavTechEmployeesResigned, NavTechEmployeesBlacklist },
            "Permissions.Employees.View");
        SetPayrollSubNavigationAccess(
            new[] { NavFashionEmployeesWorking, NavFashionEmployeesProfiles, NavFashionEmployeesMissingCv, NavFashionEmployeesResigned, NavFashionEmployeesBlacklist },
            "Permissions.Employees.View");
        SetNavigationAccess(NavTechAttendance, "Permissions.Employees.View", "Permissions.Attendance.Import");
        SetNavigationAccess(NavFashionAttendance, "Permissions.Employees.View", "Permissions.Attendance.Import");
        SetNavigationAccess(NavCustomers, "Permissions.EdTechCustomers.View");
        SetNavigationAccess(NavSync, "Permissions.SystemSync.View");
        SetNavigationAccess(NavLms, "Permissions.SystemSync.View");
        SetNavigationAccess(NavTechPayroll, "Permissions.Payroll.ViewAll", "Permissions.Payroll.ViewPersonal");
        SetNavigationAccess(NavFashionPayroll, "Permissions.Payroll.ViewAll", "Permissions.Payroll.ViewPersonal");
        SetPayrollSubNavigationAccess(
            new[] { NavTechPayrollTable, NavTechPayrollMonthly },
            "Permissions.Payroll.ViewAll", "Permissions.Payroll.ViewPersonal");
        SetPayrollSubNavigationAccess(
            new[] { NavFashionPayrollTable, NavFashionPayrollMonthly },
            "Permissions.Payroll.ViewAll", "Permissions.Payroll.ViewPersonal");
        SetPayrollSubNavigationAccess(
            new[] { NavTechPayrollAdjustments, NavTechPayrollCreate, NavTechPayrollReview },
            "Permissions.Payroll.Calculate");
        SetPayrollSubNavigationAccess(
            new[] { NavFashionPayrollAdjustments, NavFashionPayrollCreate, NavFashionPayrollReview },
            "Permissions.Payroll.Calculate");
        SetNavigationAccess(NavTechPayrollWorkflow, "Permissions.Payroll.ViewAll", "Permissions.Payroll.Approve", "Permissions.Payroll.Publish");
        SetNavigationAccess(NavFashionPayrollWorkflow, "Permissions.Payroll.ViewAll", "Permissions.Payroll.Approve", "Permissions.Payroll.Publish");
        SetNavigationAccess(NavTechInternalCustomers, "Permissions.InternalCustomers.View");
        SetNavigationAccess(NavFashionInternalCustomers, "Permissions.InternalCustomers.View");
        SetNavigationAccess(NavTechResources, "Permissions.InternalResources.View");
        SetNavigationAccess(NavFashionResources, "Permissions.InternalResources.View");
        SetNavigationAccess(NavCompanyFinance, "Permissions.FinanceReports.View");
        SetNavigationAccess(NavFashion, "Permissions.Products.View", "Permissions.Inventory.View");
        SetPayrollSubNavigationAccess(
            new[] { NavFashionProducts, NavFashionVariants },
            "Permissions.Products.View");
        SetPayrollSubNavigationAccess(
            new[] { NavFashionSuppliers, NavFashionReceipts, NavFashionBalances, NavFashionMovements, NavFashionManufacturing, NavFashionWarehouses },
            "Permissions.Inventory.View", "Permissions.Products.View");
        SetNavigationAccess(NavFashionOrders, "Permissions.Orders.View");

        var canManageInternalCustomers = HasPermission(_currentUser, "Permissions.InternalCustomers.Manage");
        var canManageInternalResources = HasPermission(_currentUser, "Permissions.InternalResources.Manage");
        CreateInternalDataButton.Visibility = canManageInternalCustomers || canManageInternalResources ? Visibility.Visible : Visibility.Collapsed;
        EditInternalDataButton.Visibility = CreateInternalDataButton.Visibility;
        DeleteInternalDataButton.Visibility = CreateInternalDataButton.Visibility;

        var canOperateLms = HasPermission(_currentUser, "Permissions.SystemSync.Trigger");
        SaveLmsMappingButton.Visibility = canOperateLms ? Visibility.Visible : Visibility.Collapsed;
        RetryLmsOutboxButton.Visibility = canOperateLms ? Visibility.Visible : Visibility.Collapsed;
        DispatchLmsOutboxButton.Visibility = canOperateLms ? Visibility.Visible : Visibility.Collapsed;

        NavDashboard.Visibility = Visibility.Visible;
        NavDashboard.IsChecked = true;
        SetActiveArea(null);
        SetViewStatus("Đã tải quyền truy cập theo tài khoản hiện tại");
    }

    private void SetNavigationAccess(RadioButton navigationItem, params string[] permissions)
    {
        var allowed = permissions.Any(permission => HasPermission(_currentUser, permission));
        navigationItem.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
        navigationItem.IsEnabled = allowed;
    }

    private void SetPayrollSubNavigationAccess(IEnumerable<RadioButton> navigationItems, params string[] permissions)
    {
        var allowed = permissions.Any(permission => HasPermission(_currentUser, permission));
        foreach (var navigationItem in navigationItems)
        {
            navigationItem.Visibility = allowed ? Visibility.Visible : Visibility.Collapsed;
            navigationItem.IsEnabled = allowed;
        }
    }

    private async Task RunWithBusyAsync(string message, Func<Task> operation, string? subMessage = null)
    {
        SetBusy(true, message, subMessage);
        try
        {
            await operation();
        }
        catch (Exception ex)
        {
            SetViewStatus($"Lỗi: {ToFriendlyError(ex)}", isError: true);
            ShowToast($"Không thể hoàn tất thao tác. {ToFriendlyError(ex)}", isError: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy, string? message = null, string? subMessage = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBusy(isBusy, message, subMessage));
            return;
        }

        if (isBusy)
        {
            Interlocked.Increment(ref _busyOperationCount);
            if (GlobalBusyText != null)
            {
                GlobalBusyText.Text = message ?? "Đang tải dữ liệu...";
            }
            if (GlobalBusySubText != null)
            {
                GlobalBusySubText.Text = subMessage ?? "Hệ thống đang đồng bộ dữ liệu, vui lòng đợi trong giây lát...";
            }
        }
        else
        {
            var remaining = Interlocked.Decrement(ref _busyOperationCount);
            if (remaining > 0) return;
            Interlocked.Exchange(ref _busyOperationCount, 0);
        }

        var busy = Volatile.Read(ref _busyOperationCount) > 0;
        if (GlobalBusyOverlay != null)
        {
            GlobalBusyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }
        if (TopContentProgressBar != null)
        {
            TopContentProgressBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    private async Task LoadViewOnceAsync(string key, string message, Func<Task> loader, string? subMessage = null)
    {
        if (_loadedViews.Contains(key)) return;

        if (_viewLoadTasks.TryGetValue(key, out var existingTask))
        {
            await existingTask;
            return;
        }

        var task = RunWithBusyAsync(message, loader, subMessage);
        _viewLoadTasks[key] = task;
        try
        {
            await task;
            _loadedViews.Add(key);
        }
        finally
        {
            _viewLoadTasks.Remove(key);
        }
    }

    private void InvalidateLoadedView(params string[] keys)
    {
        foreach (var key in keys)
            _loadedViews.Remove(key);
    }

    private string SegmentViewKey(string name) => $"{name}:{_activeSegment}";

    private async Task DebounceSearchAsync(string key, string message, Func<Task> loader)
    {
        if (_searchDebouncers.TryGetValue(key, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        var cts = new CancellationTokenSource();
        _searchDebouncers[key] = cts;
        try
        {
            await Task.Delay(280, cts.Token);
            await RunWithBusyAsync(message, loader);
        }
        catch (TaskCanceledException)
        {
            // A newer keystroke superseded this search.
        }
        finally
        {
            if (_searchDebouncers.TryGetValue(key, out var current) && ReferenceEquals(current, cts))
            {
                _searchDebouncers.Remove(key);
                cts.Dispose();
            }
        }
    }

    private void SetViewStatus(string message, bool isError = false)
    {
        ViewHeaderStatusText.Text = message;
        ViewHeaderStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isError ? "#DC2626" : "#64748B"));
    }

    private void SetLoadedStatus(string label, int count)
    {
        SetViewStatus(count == 0 ? $"{label}: chưa có dữ liệu" : $"{label}: {count:N0} bản ghi");
    }

    private static string ToFriendlyError(Exception exception)
    {
        if (exception is TaskCanceledException)
        {
            return "Yêu cầu quá thời gian hoặc đã bị hủy. Vui lòng thử lại.";
        }

        if (exception is HttpRequestException)
        {
            return "Không kết nối được máy chủ API. Kiểm tra máy chủ và mạng nội bộ.";
        }

        return string.IsNullOrWhiteSpace(exception.Message) ? "Đã xảy ra lỗi không xác định." : exception.Message;
    }

    private async void ShowToast(string message, bool isError = false)
    {
        var sequence = Interlocked.Increment(ref _toastSequence);
        GlobalToastText.Text = message;
        GlobalToastBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isError ? "#991B1B" : "#0F172A"));
        GlobalToastBanner.Visibility = Visibility.Visible;

        await Task.Delay(TimeSpan.FromSeconds(4));
        if (sequence == _toastSequence)
        {
            GlobalToastBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void SegmentSelector_Checked(object sender, RoutedEventArgs e)
    {
        if (!_isUiReady || sender is not RadioButton selector) return;

        var selectedSegment = selector == SegmentFashionButton ? FashionSegment : TechnologySegment;
        _activeSegment = selectedSegment;
        TechnologyNavigationGroup.Visibility = selectedSegment == TechnologySegment ? Visibility.Visible : Visibility.Collapsed;
        FashionNavigationGroup.Visibility = selectedSegment == FashionSegment ? Visibility.Visible : Visibility.Collapsed;
        SetActiveArea(selectedSegment);

        // Open the first permitted function so the selected business area is immediately usable.
        var firstAvailableNavigation = selectedSegment == FashionSegment
            ? new[] { NavFashionResources, NavFashion, NavFashionInternalCustomers, NavFashionEmployees, NavFashionAttendance, NavFashionPayroll }
            : new[] { NavCustomers, NavCourses, NavCsca, NavInterview, NavQuestions, NavTechResources, NavTechInternalCustomers, NavTechEmployees, NavTechAttendance, NavTechPayroll };
        var firstVisibleNavigation = firstAvailableNavigation.FirstOrDefault(item => item.Visibility == Visibility.Visible && item.IsEnabled);
        if (firstVisibleNavigation is not null)
        {
            firstVisibleNavigation.IsChecked = true;
        }
    }

    private void SidebarToggleButton_Click(object sender, RoutedEventArgs e)
        => SetSidebarCollapsed(!_isSidebarCollapsed);

    private void SetSidebarCollapsed(bool isCollapsed)
    {
        _isSidebarCollapsed = isCollapsed;
        SidebarColumn.Width = new GridLength(isCollapsed ? 86 : 270);
        SidebarPanel.Padding = isCollapsed ? new Thickness(9, 16, 9, 14) : new Thickness(13, 16, 12, 14);
        SidebarBrandHeader.Margin = isCollapsed ? new Thickness(0, 0, 0, 9) : new Thickness(3, 0, 0, 9);
        SidebarBrandDetails.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ActiveSegmentCard.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SegmentSwitcher.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        UserProfileDetails.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        UserProfileCard.Padding = isCollapsed ? new Thickness(6) : new Thickness(12);
        UserAvatarBadge.Margin = isCollapsed ? new Thickness(0) : new Thickness(0, 0, 10, 0);
        LogoutButton.Width = isCollapsed ? 18 : double.NaN;
        LogoutButton.Padding = isCollapsed ? new Thickness(0) : new Thickness(6);
        SidebarToggleButton.Content = isCollapsed ? "›" : "‹";
        SidebarToggleButton.Margin = isCollapsed ? new Thickness(0) : new Thickness(8, 0, 0, 0);
        SidebarToggleButton.ToolTip = isCollapsed ? "Mở rộng thanh chức năng" : "Thu gọn thanh chức năng";

        CompanyNavigationLabel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TechnologyNavigationLabel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        FashionNavigationLabel.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;

        ApplySidebarNavigationContent(isCollapsed);
        if (isCollapsed)
        {
            SetEmployeesSubmenuVisibility(false, false);
            SetPayrollSubmenuVisibility(false, false);
            SetFashionSubmenuVisibility(false);
        }
        else if (NavFashionPayroll.IsChecked == true)
        {
            SetPayrollSubmenuVisibility(true, true);
            SetFashionSubmenuVisibility(false);
            SetEmployeesSubmenuVisibility(false, false);
        }
        else if (NavTechPayroll.IsChecked == true)
        {
            SetPayrollSubmenuVisibility(false, true);
            SetFashionSubmenuVisibility(false);
            SetEmployeesSubmenuVisibility(false, false);
        }
        else if (NavFashionEmployees.IsChecked == true || NavFashionEmployeesWorking.IsChecked == true || NavFashionEmployeesProfiles.IsChecked == true || NavFashionEmployeesMissingCv.IsChecked == true || NavFashionEmployeesResigned.IsChecked == true || NavFashionEmployeesBlacklist.IsChecked == true)
        {
            SetEmployeesSubmenuVisibility(true, true);
            SetPayrollSubmenuVisibility(false, false);
            SetFashionSubmenuVisibility(false);
        }
        else if (NavTechEmployees.IsChecked == true || NavTechEmployeesWorking.IsChecked == true || NavTechEmployeesProfiles.IsChecked == true || NavTechEmployeesMissingCv.IsChecked == true || NavTechEmployeesResigned.IsChecked == true || NavTechEmployeesBlacklist.IsChecked == true)
        {
            SetEmployeesSubmenuVisibility(false, true);
            SetPayrollSubmenuVisibility(false, false);
            SetFashionSubmenuVisibility(false);
        }
        else if (NavFashion.IsChecked == true || NavFashionProducts.IsChecked == true || NavFashionVariants.IsChecked == true || NavFashionSuppliers.IsChecked == true || NavFashionReceipts.IsChecked == true || NavFashionBalances.IsChecked == true || NavFashionMovements.IsChecked == true || NavFashionManufacturing.IsChecked == true || NavFashionWarehouses.IsChecked == true || NavFashionOrders.IsChecked == true)
        {
            SetEmployeesSubmenuVisibility(false, false);
            SetPayrollSubmenuVisibility(false, false);
            SetFashionSubmenuVisibility(true);
        }
    }

    private void ApplySidebarNavigationContent(bool isCollapsed)
    {
        var navigationItems = new[]
        {
            (Item: NavDashboard, Expanded: "⌂   Tổng quan", Compact: "⌂"),
            (Item: NavCompanyFinance, Expanded: "₫   Dòng tiền & lợi nhuận", Compact: "₫"),
            (Item: NavSync, Expanded: "◌   Nhật ký hệ thống", Compact: "◌"),
            (Item: NavCustomers, Expanded: "♧   Học viên & khách hàng", Compact: "♧"),
            (Item: NavCourses, Expanded: "▤   Khóa học & lớp học", Compact: "▤"),
            (Item: NavCsca, Expanded: "▦   Lớp học CSCA", Compact: "▦"),
            (Item: NavLms, Expanded: "◈   Quyền học CSCA LMS", Compact: "◈"),
            (Item: NavInterview, Expanded: "◉   Mock Interview", Compact: "◉"),
            (Item: NavQuestions, Expanded: "☷   Ngân hàng đề & câu hỏi", Compact: "☷"),
            (Item: NavTechResources, Expanded: "▤   Kho đề & tài liệu", Compact: "▤"),
            (Item: NavTechInternalCustomers, Expanded: "♧   Đối tác & khách hàng nội bộ", Compact: "♧"),
            (Item: NavTechEmployees, Expanded: "♙   Nhân sự & CV", Compact: "♙"),
            (Item: NavTechEmployeesWorking, Expanded: "1   Đang làm việc", Compact: "1"),
            (Item: NavTechEmployeesProfiles, Expanded: "2   Hồ sơ & CV", Compact: "2"),
            (Item: NavTechEmployeesMissingCv, Expanded: "3   Thiếu CV", Compact: "3"),
            (Item: NavTechEmployeesResigned, Expanded: "4   Đã nghỉ việc", Compact: "4"),
            (Item: NavTechEmployeesBlacklist, Expanded: "5   Blacklist", Compact: "5"),
            (Item: NavTechAttendance, Expanded: "◷   Chấm công", Compact: "◷"),
            (Item: NavTechPayroll, Expanded: "▣   Bảng lương", Compact: "▣"),
            (Item: NavTechPayrollCreate, Expanded: "1   Tạo kỳ lương", Compact: "1"),
            (Item: NavTechPayrollTable, Expanded: "2   Tính & phiếu lương", Compact: "2"),
            (Item: NavTechPayrollAdjustments, Expanded: "3   Chỉnh lương · thưởng/phạt", Compact: "3"),
            (Item: NavTechPayrollReview, Expanded: "4   Xem xét & gửi duyệt", Compact: "4"),
            (Item: NavTechPayrollWorkflow, Expanded: "5   Duyệt · chi · phát hành", Compact: "5"),
            (Item: NavTechPayrollMonthly, Expanded: "6   Tổng hợp theo tháng", Compact: "6"),
            (Item: NavFashionResources, Expanded: "✎   Kế hoạch & mẫu thiết kế", Compact: "✎"),
            (Item: NavFashion, Expanded: "▧   Sản phẩm · kho · đơn hàng", Compact: "▧"),
            (Item: NavFashionProducts, Expanded: "1   Sản phẩm", Compact: "1"),
            (Item: NavFashionVariants, Expanded: "2   Biến thể & SKU", Compact: "2"),
            (Item: NavFashionSuppliers, Expanded: "3   Nhà cung cấp", Compact: "3"),
            (Item: NavFashionReceipts, Expanded: "4   Phiếu nhập kho", Compact: "4"),
            (Item: NavFashionManufacturing, Expanded: "5   Sản xuất & giá thành", Compact: "5"),
            (Item: NavFashionBalances, Expanded: "6   Tồn kho", Compact: "6"),
            (Item: NavFashionMovements, Expanded: "7   Nhật ký kho", Compact: "7"),
            (Item: NavFashionWarehouses, Expanded: "8   Danh mục kho", Compact: "8"),
            (Item: NavFashionOrders, Expanded: "9   Đơn hàng", Compact: "9"),
            (Item: NavFashionInternalCustomers, Expanded: "♧   Khách hàng & đối tác nội bộ", Compact: "♧"),
            (Item: NavFashionEmployees, Expanded: "♙   Nhân sự & CV", Compact: "♙"),
            (Item: NavFashionEmployeesWorking, Expanded: "1   Đang làm việc", Compact: "1"),
            (Item: NavFashionEmployeesProfiles, Expanded: "2   Hồ sơ & CV", Compact: "2"),
            (Item: NavFashionEmployeesMissingCv, Expanded: "3   Thiếu CV", Compact: "3"),
            (Item: NavFashionEmployeesResigned, Expanded: "4   Đã nghỉ việc", Compact: "4"),
            (Item: NavFashionEmployeesBlacklist, Expanded: "5   Blacklist", Compact: "5"),
            (Item: NavFashionAttendance, Expanded: "◷   Chấm công", Compact: "◷"),
            (Item: NavFashionPayroll, Expanded: "▣   Bảng lương", Compact: "▣"),
            (Item: NavFashionPayrollCreate, Expanded: "1   Tạo kỳ lương", Compact: "1"),
            (Item: NavFashionPayrollTable, Expanded: "2   Tính & phiếu lương", Compact: "2"),
            (Item: NavFashionPayrollAdjustments, Expanded: "3   Chỉnh lương · thưởng/phạt", Compact: "3"),
            (Item: NavFashionPayrollReview, Expanded: "4   Xem xét & gửi duyệt", Compact: "4"),
            (Item: NavFashionPayrollWorkflow, Expanded: "5   Duyệt · chi · phát hành", Compact: "5"),
            (Item: NavFashionPayrollMonthly, Expanded: "6   Tổng hợp theo tháng", Compact: "6")
        };

        foreach (var navigationItem in navigationItems)
        {
            navigationItem.Item.Content = isCollapsed ? navigationItem.Compact : navigationItem.Expanded;
            navigationItem.Item.ToolTip = isCollapsed ? navigationItem.Expanded.Trim() : null;
            navigationItem.Item.HorizontalContentAlignment = isCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            navigationItem.Item.Padding = isCollapsed ? new Thickness(0) : new Thickness(11, 0, 11, 0);
        }
    }

    private bool _isEmployeeFunctionSelectionSyncing;

    private bool IsEmployeeNavigationItem(object sender)
        => sender == NavTechEmployees || sender == NavTechEmployeesWorking || sender == NavTechEmployeesProfiles || sender == NavTechEmployeesMissingCv || sender == NavTechEmployeesResigned || sender == NavTechEmployeesBlacklist
            || sender == NavFashionEmployees || sender == NavFashionEmployeesWorking || sender == NavFashionEmployeesProfiles || sender == NavFashionEmployeesMissingCv || sender == NavFashionEmployeesResigned || sender == NavFashionEmployeesBlacklist;

    private bool IsFashionEmployeeNavigationItem(object sender)
        => sender == NavFashionEmployees || sender == NavFashionEmployeesWorking || sender == NavFashionEmployeesProfiles || sender == NavFashionEmployeesMissingCv || sender == NavFashionEmployeesResigned || sender == NavFashionEmployeesBlacklist;

    private void SetEmployeesSubmenuVisibility(bool isFashion, bool show)
    {
        var visible = show && !_isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        TechnologyEmployeesSubmenu.Visibility = isFashion ? Visibility.Collapsed : visible;
        FashionEmployeesSubmenu.Visibility = isFashion ? visible : Visibility.Collapsed;
    }

    private void SelectEmployeesFunction(object sender)
    {
        if (_isEmployeeFunctionSelectionSyncing)
        {
            return;
        }

        _isEmployeeFunctionSelectionSyncing = true;
        try
        {
            var isFashion = IsFashionEmployeeNavigationItem(sender);
            var index = sender == NavTechEmployeesProfiles || sender == NavFashionEmployeesProfiles ? 1
                : sender == NavTechEmployeesMissingCv || sender == NavFashionEmployeesMissingCv ? 2
                : sender == NavTechEmployeesResigned || sender == NavFashionEmployeesResigned ? 3
                : sender == NavTechEmployeesBlacklist || sender == NavFashionEmployeesBlacklist ? 4
                : 0;

            var children = isFashion
                ? new[] { NavFashionEmployeesWorking, NavFashionEmployeesProfiles, NavFashionEmployeesMissingCv, NavFashionEmployeesResigned, NavFashionEmployeesBlacklist }
                : new[] { NavTechEmployeesWorking, NavTechEmployeesProfiles, NavTechEmployeesMissingCv, NavTechEmployeesResigned, NavTechEmployeesBlacklist };
            foreach (var child in children) child.IsChecked = false;
            (index switch
            {
                1 => isFashion ? NavFashionEmployeesProfiles : NavTechEmployeesProfiles,
                2 => isFashion ? NavFashionEmployeesMissingCv : NavTechEmployeesMissingCv,
                3 => isFashion ? NavFashionEmployeesResigned : NavTechEmployeesResigned,
                4 => isFashion ? NavFashionEmployeesBlacklist : NavTechEmployeesBlacklist,
                _ => isFashion ? NavFashionEmployeesWorking : NavTechEmployeesWorking
            }).IsChecked = true;
            EmployeesTabs.SelectedIndex = index;
        }
        finally
        {
            _isEmployeeFunctionSelectionSyncing = false;
        }
    }

    private void EmployeesNavigationParent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton navigationItem) return;
        SetEmployeesSubmenuVisibility(navigationItem == NavFashionEmployees, true);
        EmployeesTabs.SelectedIndex = 0;
    }

    private bool IsPayrollNavigationItem(object sender)
        => sender == NavTechPayroll || sender == NavTechPayrollTable || sender == NavTechPayrollAdjustments || sender == NavTechPayrollCreate || sender == NavTechPayrollMonthly || sender == NavTechPayrollReview || sender == NavTechPayrollWorkflow
            || sender == NavFashionPayroll || sender == NavFashionPayrollTable || sender == NavFashionPayrollAdjustments || sender == NavFashionPayrollCreate || sender == NavFashionPayrollMonthly || sender == NavFashionPayrollReview || sender == NavFashionPayrollWorkflow;

    private bool IsFashionPayrollNavigationItem(object sender)
        => sender == NavFashionPayroll || sender == NavFashionPayrollTable || sender == NavFashionPayrollAdjustments || sender == NavFashionPayrollCreate || sender == NavFashionPayrollMonthly || sender == NavFashionPayrollReview || sender == NavFashionPayrollWorkflow;

    private void SetPayrollSubmenuVisibility(bool isFashion, bool show)
    {
        var visible = show && !_isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        TechnologyPayrollSubmenu.Visibility = isFashion ? Visibility.Collapsed : visible;
        FashionPayrollSubmenu.Visibility = isFashion ? visible : Visibility.Collapsed;
    }

    private void SelectPayrollFunction(object sender)
    {
        if (_isPayrollFunctionSelectionSyncing)
        {
            return;
        }

        _isPayrollFunctionSelectionSyncing = true;
        try
        {
            var isFashion = IsFashionPayrollNavigationItem(sender);
            var index = sender == NavTechPayrollAdjustments || sender == NavFashionPayrollAdjustments ? 1
                : sender == NavTechPayrollCreate || sender == NavFashionPayrollCreate ? 2
                : sender == NavTechPayrollMonthly || sender == NavFashionPayrollMonthly ? 3
                : sender == NavTechPayrollReview || sender == NavFashionPayrollReview ? 4
                : sender == NavTechPayrollWorkflow || sender == NavFashionPayrollWorkflow ? 5
                : 0;

            var children = isFashion
                ? new[] { NavFashionPayrollTable, NavFashionPayrollAdjustments, NavFashionPayrollCreate, NavFashionPayrollMonthly, NavFashionPayrollReview, NavFashionPayrollWorkflow }
                : new[] { NavTechPayrollTable, NavTechPayrollAdjustments, NavTechPayrollCreate, NavTechPayrollMonthly, NavTechPayrollReview, NavTechPayrollWorkflow };
            foreach (var child in children) child.IsChecked = false;
            (index switch
            {
                1 => isFashion ? NavFashionPayrollAdjustments : NavTechPayrollAdjustments,
                2 => isFashion ? NavFashionPayrollCreate : NavTechPayrollCreate,
                3 => isFashion ? NavFashionPayrollMonthly : NavTechPayrollMonthly,
                4 => isFashion ? NavFashionPayrollReview : NavTechPayrollReview,
                5 => isFashion ? NavFashionPayrollWorkflow : NavTechPayrollWorkflow,
                _ => isFashion ? NavFashionPayrollTable : NavTechPayrollTable
            }).IsChecked = true;
            PayrollFunctionTabs.SelectedIndex = index;
        }
        finally
        {
            _isPayrollFunctionSelectionSyncing = false;
        }
    }

    private void PayrollNavigationParent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton navigationItem) return;
        SetPayrollSubmenuVisibility(navigationItem == NavFashionPayroll, true);
        PayrollFunctionTabs.SelectedIndex = 0;
    }

    private bool IsFashionInventoryNavigationItem(object sender)
        => sender == NavFashion || sender == NavFashionProducts || sender == NavFashionVariants || sender == NavFashionSuppliers || sender == NavFashionReceipts || sender == NavFashionBalances || sender == NavFashionMovements || sender == NavFashionManufacturing || sender == NavFashionWarehouses || sender == NavFashionOrders;

    private void SetFashionSubmenuVisibility(bool show)
    {
        var visible = show && !_isSidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        FashionInventorySubmenu.Visibility = visible;
    }

    private void SelectFashionFunction(object sender)
    {
        if (_isFashionFunctionSelectionSyncing)
        {
            return;
        }

        _isFashionFunctionSelectionSyncing = true;
        try
        {
            var index = sender == NavFashionVariants ? 1
                : sender == NavFashionSuppliers ? 2
                : sender == NavFashionReceipts ? 3
                : sender == NavFashionBalances ? 4
                : sender == NavFashionMovements ? 5
                : sender == NavFashionManufacturing ? 6
                : sender == NavFashionWarehouses ? 7
                : sender == NavFashionOrders ? 8
                : 0;

            var children = new[] { NavFashionProducts, NavFashionVariants, NavFashionSuppliers, NavFashionReceipts, NavFashionBalances, NavFashionMovements, NavFashionManufacturing, NavFashionWarehouses, NavFashionOrders };
            foreach (var child in children) child.IsChecked = false;

            (index switch
            {
                1 => NavFashionVariants,
                2 => NavFashionSuppliers,
                3 => NavFashionReceipts,
                4 => NavFashionBalances,
                5 => NavFashionMovements,
                6 => NavFashionManufacturing,
                7 => NavFashionWarehouses,
                8 => NavFashionOrders,
                _ => NavFashionProducts
            }).IsChecked = true;

            FashionTabs.SelectedIndex = index;
        }
        finally
        {
            _isFashionFunctionSelectionSyncing = false;
        }
    }

    private void FashionNavigationParent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton) return;
        SetFashionSubmenuVisibility(true);
        FashionTabs.SelectedIndex = 0;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewDashboardContainer == null) return;

        if (sender is RadioButton navigationItem && navigationItem.Visibility != Visibility.Visible)
        {
            return;
        }

        if (!IsEmployeeNavigationItem(sender))
        {
            SetEmployeesSubmenuVisibility(false, false);
        }

        if (!IsPayrollNavigationItem(sender))
        {
            SetPayrollSubmenuVisibility(false, false);
        }

        if (!IsFashionInventoryNavigationItem(sender))
        {
            SetFashionSubmenuVisibility(false);
        }

        HideAllViews();

        if (sender == NavDashboard)
        {
            SetActiveArea(null);
            ViewDashboardContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Tổng Quan Hệ Thống";
            ViewHeaderSubtitle.Text = "Không gian làm việc MOLY Internal Management";
            SetViewStatus("Đã sẵn sàng");
            _ = LoadViewOnceAsync("dashboard", "Đang cập nhật chỉ số tổng quan...", async () =>
            {
                _cachedBusinessUnits = await _apiClient.GetBusinessUnitProfitSummaryAsync() ?? new List<ApiClient.BusinessUnitProfitItem>();
                await LoadDashboardMetricsAsync();
            }, "Tổng hợp doanh thu, học viên, nhân sự & tồn kho toàn hệ thống");
        }
        else if (sender == NavCourses || sender == NavCsca)
        {
            SetActiveArea(TechnologySegment);
            ViewCoursesContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Khóa Học & Lớp Học";
            ViewHeaderSubtitle.Text = "Quản lý khóa học, danh sách lớp học, tài chính & công nợ học phí";
            if (sender == NavCsca && CourseModuleTabControl != null)
            {
                CourseModuleTabControl.SelectedItem = TabItemClasses;
            }

            _ = LoadViewOnceAsync("courses_classes", "Đang tải dữ liệu Khóa học & Lớp học...", async () =>
            {
                await LoadCoursesAsync();
                await LoadCscaClassesAsync();
                await LoadCscaOnlineAsync();
            }, "Đồng bộ chương trình đào tạo, lớp học & công nợ");
        }
        else if (sender == NavInterview)
        {
            SetActiveArea(TechnologySegment);
            ViewInterviewContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Mock Interview & Khách Hàng";
            ViewHeaderSubtitle.Text = "Quản lý ứng viên phỏng vấn thử, gói dịch vụ, số buổi & doanh thu";
            _ = LoadViewOnceAsync("interview", "Đang tải danh sách Mock Interview...", () => LoadInterviewCustomersAsync(), "Tải ứng viên phỏng vấn thử, số buổi & doanh thu dịch vụ");
        }
        else if (IsEmployeeNavigationItem(sender))
        {
            var isFashionEmployee = IsFashionEmployeeNavigationItem(sender);
            SetActiveArea(isFashionEmployee ? FashionSegment : TechnologySegment);
            SetEmployeesSubmenuVisibility(isFashionEmployee, true);
            ViewEmployeesContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Nhân sự & CV";
            ViewHeaderSubtitle.Text = $"Hồ sơ, loại hợp đồng, mức lương và CV riêng của mảng {ActiveSegmentName}";
            SelectEmployeesFunction(sender);
            _ = LoadViewOnceAsync(SegmentViewKey("employees"), "Đang tải danh sách nhân sự...", async () =>
            {
                await LoadDepartmentsFilterAsync();
                await LoadEmployeesAsync();
            }, $"Tải phòng ban, hợp đồng và hồ sơ CV riêng của {ActiveSegmentName}");
        }
        else if (sender == NavTechAttendance || sender == NavFashionAttendance)
        {
            SetActiveArea(sender == NavFashionAttendance ? FashionSegment : TechnologySegment);
            ViewAttendanceContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Chấm công";
            ViewHeaderSubtitle.Text = $"Nhật ký chấm công chỉ của nhân sự mảng {ActiveSegmentName}";
            _ = LoadViewOnceAsync(SegmentViewKey("attendance"), "Đang tải dữ liệu chấm công...", () => LoadAttendanceAsync(), $"Tổng hợp bảng chấm công thời gian thực của {ActiveSegmentName}");
        }
        else if (sender == NavCustomers)
        {
            SetActiveArea(TechnologySegment);
            ViewCustomersContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Học Viên & Khách Hàng";
            ViewHeaderSubtitle.Text = "Tách riêng học viên từ lớp học và khách hàng đăng ký/đồng bộ";
            _ = LoadViewOnceAsync("customers", "Đang tải học viên và khách hàng...", () => LoadCustomersAsync(), "Tải đầy đủ liên hệ, khóa học, lớp học và công nợ");
        }
        else if (sender == NavSync)
        {
            SetActiveArea(null);
            ViewSyncContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Hạ Tầng Đồng Bộ Dữ Liệu";
            ViewHeaderSubtitle.Text = "Theo dõi tiến trình đồng bộ webhook, REST pull & quản lý Dead-Letter Queue";
            _ = LoadViewOnceAsync("sync", "Đang tải lịch sử đồng bộ...", () => LoadSyncRunsAsync(), "Kiểm tra tiến trình webhook, REST pull & Dead-Letter Queue");
        }
        else if (sender == NavLms)
        {
            SetActiveArea(TechnologySegment);
            ViewLmsContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Quyền học CSCA LMS";
            ViewHeaderSubtitle.Text = "Mapping khóa học, cấp quyền sau khi đóng đủ và vận hành hàng chờ LMS";
            _ = LoadViewOnceAsync("csca-lms", "Đang tải trạng thái CSCA LMS...", LoadLmsIntegrationAsync, "Đối chiếu mapping, quyền học và các lệnh chờ gửi");
        }
        else if (IsPayrollNavigationItem(sender))
        {
            var isFashionPayroll = IsFashionPayrollNavigationItem(sender);
            SetActiveArea(isFashionPayroll ? FashionSegment : TechnologySegment);
            SetPayrollSubmenuVisibility(isFashionPayroll, true);
            ViewPayrollContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Bảng lương";
            ViewHeaderSubtitle.Text = $"Kỳ lương và phiếu lương full-time/part-time riêng của mảng {ActiveSegmentName}";
            SelectPayrollFunction(sender);
            _ = LoadViewOnceAsync(SegmentViewKey("payroll"), "Đang tải bảng tính lương...", () => LoadPayrollPeriodsAsync(), $"Tổng hợp kỳ lương và phiếu lương của mảng {ActiveSegmentName}");
        }
        else if (sender == NavTechInternalCustomers || sender == NavFashionInternalCustomers)
        {
            SetActiveArea(sender == NavFashionInternalCustomers ? FashionSegment : TechnologySegment);
            ShowInternalDataView(InternalDataViewMode.Customers);
        }
        else if (sender == NavTechResources || sender == NavFashionResources)
        {
            SetActiveArea(sender == NavFashionResources ? FashionSegment : TechnologySegment);
            ShowInternalDataView(InternalDataViewMode.Resources);
        }
        else if (sender == NavCompanyFinance)
        {
            SetActiveArea(null);
            ViewCompanyFinanceContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Dòng tiền & lợi nhuận";
            ViewHeaderSubtitle.Text = "Bảng tổng song song của Công nghệ - Giáo dục và Thời trang";
            _ = LoadViewOnceAsync("company-finance", "Đang tổng hợp tài chính hai mảng...", LoadCompanyFinanceAsync, "Đối chiếu dòng tiền P&L, doanh thu & chi phí hợp nhất");
        }
        else if (IsFashionInventoryNavigationItem(sender))
        {
            SetActiveArea(FashionSegment);
            SetFashionSubmenuVisibility(true);
            ViewFashionContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Thời trang & Quản lý kho";
            ViewHeaderSubtitle.Text = "Một luồng dữ liệu từ API nội bộ: sản phẩm → SKU → nhập kho → tồn kho → sổ giao dịch";
            SelectFashionFunction(sender);
            _ = LoadViewOnceAsync("fashion-products", "Đang tải danh mục sản phẩm thời trang...", LoadFashionDataAsync, "Tổng hợp danh mục thiết kế, định mức nguyên phụ liệu & tồn kho xưởng");
        }
    }

    private void HideAllViews()
    {
        ViewDashboardContainer.Visibility = Visibility.Collapsed;
        ViewCoursesContainer.Visibility = Visibility.Collapsed;
        ViewQuestionsContainer.Visibility = Visibility.Collapsed;
        ViewInterviewContainer.Visibility = Visibility.Collapsed;
        ViewEmployeesContainer.Visibility = Visibility.Collapsed;
        ViewAttendanceContainer.Visibility = Visibility.Collapsed;
        ViewCustomersContainer.Visibility = Visibility.Collapsed;
        ViewSyncContainer.Visibility = Visibility.Collapsed;
        ViewLmsContainer.Visibility = Visibility.Collapsed;
        ViewPayrollContainer.Visibility = Visibility.Collapsed;
        if (ViewFashionContainer != null) ViewFashionContainer.Visibility = Visibility.Collapsed;
        ViewCompanyFinanceContainer.Visibility = Visibility.Collapsed;
        ViewInternalDataContainer.Visibility = Visibility.Collapsed;
    }

    private string ActiveSegmentName => _activeSegment == FashionSegment ? "Thời trang" : "Công nghệ - Giáo dục";

    private void SetActiveArea(string? segment)
    {
        if (!string.IsNullOrWhiteSpace(segment)) _activeSegment = segment;
        var isCompany = string.IsNullOrWhiteSpace(segment);
        var label = isCompany ? "TỔNG HỢP TOÀN CÔNG TY" : ActiveSegmentName.ToUpperInvariant();
        ActiveSegmentText.Text = label;
        ActiveSegmentBadgeText.Text = isCompany ? "TOÀN CÔNG TY" : label;
        ActiveSegmentBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            isCompany ? "#EEF2FF" : _activeSegment == FashionSegment ? "#FDF2F8" : "#ECFEFF"));
        ActiveSegmentBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            isCompany ? "#4F46E5" : _activeSegment == FashionSegment ? "#BE185D" : "#0F766E"));
    }

    private void ShowInternalDataView(InternalDataViewMode mode)
    {
        _internalDataMode = mode;
        ViewInternalDataContainer.Visibility = Visibility.Visible;
        var isCustomers = mode == InternalDataViewMode.Customers;
        InternalCustomersPanel.Visibility = isCustomers ? Visibility.Visible : Visibility.Collapsed;
        InternalResourcesPanel.Visibility = isCustomers ? Visibility.Collapsed : Visibility.Visible;
        InternalDataTitleText.Text = isCustomers
            ? $"Danh mục khách hàng · {ActiveSegmentName}"
            : _activeSegment == FashionSegment
                ? "Kế hoạch & mẫu thiết kế · Thời trang"
                : "Kho đề & tài liệu · Công nghệ - Giáo dục";
        InternalDataHelpText.Text = $"Dữ liệu thuộc riêng mảng {ActiveSegmentName}; hệ thống không trộn với mảng còn lại.";
        CreateInternalDataButton.Content = isCustomers ? "+ Thêm khách hàng" : "+ Thêm tài liệu";
        var canManage = HasPermission(_currentUser, isCustomers ? "Permissions.InternalCustomers.Manage" : "Permissions.InternalResources.Manage");
        CreateInternalDataButton.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        EditInternalDataButton.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        DeleteInternalDataButton.Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        ViewHeaderTitle.Text = isCustomers ? "Danh mục khách hàng" : (_activeSegment == FashionSegment ? "Kế hoạch & mẫu thiết kế" : "Kho đề & tài liệu");
        ViewHeaderSubtitle.Text = InternalDataHelpText.Text;
        _ = LoadViewOnceAsync(
            $"internal:{_activeSegment}:{_internalDataMode}",
            isCustomers ? "Đang tải khách hàng..." : "Đang tải kho tài liệu...",
            LoadInternalDataAsync);
    }

    private void GoToCourses_Click(object sender, RoutedEventArgs e)
    {
        SegmentTechnologyButton.IsChecked = true;
        NavCourses.IsChecked = true;
    }

    private void GoToQuestions_Click(object sender, RoutedEventArgs e)
    {
        SegmentTechnologyButton.IsChecked = true;
        NavQuestions.IsChecked = true;
    }

    // ── Data Loading Methods ──

    private async Task LoadDashboardMetricsAsync()
    {
        MetricCoursesCount.Text = "Đang tải...";
        MetricQuestionsCount.Text = "Đang tải...";
        MetricCustomersCount.Text = "Đang tải...";
        SyncHealthSummaryText.Text = "Đang kiểm tra...";
        DeadLetterSummaryText.Text = "Dead-Letters: đang kiểm tra...";

        var healthTask = _apiClient.CheckHealthAsync();
        var syncRunsTask = _apiClient.GetSyncRunsAsync();
        var deadLettersTask = _apiClient.GetDeadLettersAsync(resolved: false);
        var coursesTask = _apiClient.GetCoursesAsync();
        var questionsTask = _apiClient.GetQuestionsAsync();
        var customersTask = _apiClient.GetCustomersAsync();
        await Task.WhenAll(coursesTask, questionsTask, customersTask);
        var courses = await coursesTask;
        var questions = await questionsTask;
        var customers = await customersTask;

        MetricCoursesCount.Text = courses?.TotalCount.ToString() ?? "Không khả dụng";
        MetricQuestionsCount.Text = questions?.TotalCount.ToString() ?? "Không khả dụng";
        MetricCustomersCount.Text = customers?.TotalCount.ToString() ?? "Không khả dụng";

        var health = await healthTask;
        ApiHealthBorder.Background = health.IsHealthy ? new SolidColorBrush(Color.FromRgb(236, 253, 245)) : new SolidColorBrush(Color.FromRgb(254, 242, 242));
        ApiHealthBorder.BorderBrush = health.IsHealthy ? new SolidColorBrush(Color.FromRgb(167, 243, 208)) : new SolidColorBrush(Color.FromRgb(254, 202, 202));
        ApiHealthDot.Fill = health.IsHealthy ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
        ApiHealthText.Foreground = health.IsHealthy ? new SolidColorBrush(Color.FromRgb(6, 95, 70)) : new SolidColorBrush(Color.FromRgb(153, 27, 27));
        ApiHealthText.Text = health.IsHealthy ? "API Trực Tuyến" : "API Không Khả Dụng";

        var syncRuns = await syncRunsTask;
        var deadLetters = await deadLettersTask;
        if (syncRuns == null || deadLetters == null)
        {
            SyncHealthSummaryText.Text = "Không khả dụng";
            DeadLetterSummaryText.Text = "Dead-Letters: không thể kiểm tra";
        }
        else
        {
            var latest = syncRuns.Items.Count > 0 ? syncRuns.Items[0] : null;
            SyncHealthSummaryText.Text = latest == null ? "Chưa có lần chạy" : latest.StatusLabel;
            DeadLetterSummaryText.Text = $"Dead-Letters: {deadLetters.TotalCount:N0} bản ghi chờ xử lý";
        }
    }

    private async Task LoadCoursesAsync(string? search = null)
    {
        var data = await _apiClient.GetCoursesAsync(search);
        if (data is null)
        {
            CoursesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh sách khóa học. Vui lòng thử lại.", isError: true);
            return;
        }

        _cachedCourses = data.Items.ToList();
        CoursesDataGrid.ItemsSource = _cachedCourses;

        // Populate CscaCourseFilterComboBox
        var filterOptions = new List<CourseFilterOption>
        {
            new CourseFilterOption(null, "Tất cả khóa học")
        };
        filterOptions.AddRange(_cachedCourses.Select(c => new CourseFilterOption(c.Id, c.Title)));
        CscaCourseFilterComboBox.ItemsSource = filterOptions;
        if (_selectedCourseFilterId.HasValue && filterOptions.Any(f => f.Id == _selectedCourseFilterId.Value))
        {
            CscaCourseFilterComboBox.SelectedValue = _selectedCourseFilterId.Value;
        }
        else
        {
            CscaCourseFilterComboBox.SelectedIndex = 0;
        }

        SetLoadedStatus("Khóa học", data.Items.Count);
    }

    private async Task LoadQuestionsAsync(string? search = null)
    {
        var data = await _apiClient.GetQuestionsAsync(search);
        if (data is null)
        {
            QuestionsListView.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được ngân hàng câu hỏi. Vui lòng thử lại.", isError: true);
            return;
        }

        QuestionsListView.ItemsSource = data.Items;
        MetricQuestionsCount.Text = data.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetLoadedStatus("Câu hỏi", data.Items.Count);
    }

    private async Task LoadCscaClassesAsync(string? search = null)
    {
        var data = await _apiClient.GetCscaClassesAsync(search);
        if (data is null)
        {
            CscaClassesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh sách lớp CSCA. Vui lòng thử lại.", isError: true);
            return;
        }

        _allCscaClasses = data.Items.ToList();
        ApplyCscaClassFilter();
        SetLoadedStatus("Lớp CSCA", data.Items.Count);
    }

    private void ApplyCscaClassFilter()
    {
        var filtered = _allCscaClasses.AsEnumerable();
        if (_selectedCourseFilterId.HasValue)
        {
            var course = _cachedCourses.FirstOrDefault(c => c.Id == _selectedCourseFilterId.Value);
            var title = course?.Title ?? _selectedCourseFilterTitle;
            if (!string.IsNullOrEmpty(title))
            {
                filtered = filtered.Where(c => string.Equals(c.CourseTitle, title, StringComparison.OrdinalIgnoreCase));
            }
            CscaCourseFilterBanner.Visibility = Visibility.Visible;
            CscaCourseFilterText.Text = $"🎯 Đang lọc các lớp thuộc khóa học: {title} ({filtered.Count()} lớp)";
        }
        else
        {
            CscaCourseFilterBanner.Visibility = Visibility.Collapsed;
        }

        var itemsList = filtered.ToList();
        CscaClassesDataGrid.ItemsSource = itemsList;

        MetricCscaClassCount.Text = itemsList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MetricCscaStudentCount.Text = itemsList.Sum(c => c.StudentCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var totalRev = itemsList.Sum(c => c.TotalRevenue);
        var totalDebt = itemsList.Sum(c => c.DebtAmount);
        var totalProfit = itemsList.Sum(c => c.NetProfit);
        MetricCscaRevenue.Text = $"{totalRev:N0} đ";
        MetricCscaDebt.Text = $"{totalDebt:N0} đ";
        MetricCscaNetProfit.Text = $"{totalProfit:N0} đ";
    }

    private async Task LoadCscaOnlineAsync(string? search = null)
    {
        var materialsTask = _apiClient.GetCscaOnlineMaterialsAsync(search);
        var vocabularyTask = _apiClient.GetCscaOnlineVocabularyAsync(search);
        var postsTask = _apiClient.GetCscaOnlinePostsAsync(search);
        await Task.WhenAll(materialsTask, vocabularyTask, postsTask);

        var materials = await materialsTask ?? [];
        var vocabulary = await vocabularyTask ?? [];
        var posts = await postsTask ?? [];

        CscaMaterialsDataGrid.ItemsSource = materials;
        CscaVocabularyDataGrid.ItemsSource = vocabulary;
        CscaPostsDataGrid.ItemsSource = posts;

        if (materialsTask.Result is null || vocabularyTask.Result is null || postsTask.Result is null)
        {
            SetViewStatus("Không tải đủ dữ liệu CSCA web. Kiểm tra API key/token hoặc endpoint cấu hình.", isError: true);
            return;
        }

        SetLoadedStatus("Nội dung CSCA web", materials.Count + vocabulary.Count + posts.Count);
    }

    private async Task LoadInterviewCustomersAsync(string? search = null)
    {
        var customersTask = _apiClient.GetInterviewCustomersAsync(search);
        var summaryTask = _apiClient.GetInterviewFinancialSummaryAsync();
        await Task.WhenAll(customersTask, summaryTask);

        var data = customersTask.Result;
        if (data is null)
        {
            InterviewDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh sách khách hàng phỏng vấn. Vui lòng thử lại.", isError: true);
            return;
        }

        var items = data.Items;
        InterviewDataGrid.ItemsSource = items;

        var summary = summaryTask.Result;
        MetricInterviewCustomerCount.Text = (summary?.TotalCustomers ?? data.TotalCount).ToString();
        MetricInterviewSessionCount.Text = (summary?.TotalSessions ?? items.Sum(i => i.SessionCount)).ToString();
        var totalRevenue = summary?.TotalRevenue ?? items.Sum(i => i.PaidAmount);
        MetricInterviewRevenue.Text = $"{totalRevenue:N0} đ";
        SetLoadedStatus("Khách hàng phỏng vấn", items.Count);
    }

    private async Task LoadCustomersAsync(string? search = null)
    {
        var customersTask = _apiClient.GetCustomersAsync(search);
        var studentsTask = _apiClient.GetCscaStudentDirectoryAsync(search);
        await Task.WhenAll(customersTask, studentsTask);

        var data = customersTask.Result;
        var students = studentsTask.Result;
        if (data is null || students is null)
        {
            CustomersDataGrid.ItemsSource = Array.Empty<object>();
            CscaStudentsDirectoryDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh sách học viên. Vui lòng thử lại.", isError: true);
            return;
        }

        CustomersDataGrid.ItemsSource = data.Items;
        CscaStudentsDirectoryDataGrid.ItemsSource = students;
        MetricCustomersCount.Text = data.TotalCount.ToString();
        SetLoadedStatus("Học viên & khách hàng", students.Count + data.Items.Count);
    }

    private async Task LoadSyncRunsAsync()
    {
        var data = await _apiClient.GetSyncRunsAsync();
        if (data is null)
        {
            SyncRunsDataGrid.ItemsSource = Array.Empty<object>();
            DeadLettersDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được lịch sử đồng bộ. Vui lòng thử lại.", isError: true);
            return;
        }

        SyncRunsDataGrid.ItemsSource = data.Items;
        await LoadDeadLettersAsync();
        SetLoadedStatus("Đợt đồng bộ", data.Items.Count);
    }

    private async Task LoadDeadLettersAsync()
    {
        var data = await _apiClient.GetDeadLettersAsync(resolved: false);
        if (data is null)
        {
            DeadLettersDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được Dead-Letter Queue. Vui lòng thử lại.", isError: true);
            return;
        }

        DeadLettersDataGrid.ItemsSource = data.Items;
        SetViewStatus(data.Items.Count == 0
            ? "Dead-Letter Queue đang trống — không có bản ghi chờ xử lý"
            : $"Có {data.Items.Count:N0} bản ghi Dead-Letter chờ xử lý");
    }

    private async Task LoadLmsIntegrationAsync()
    {
        var overviewTask = _apiClient.GetLmsIntegrationOverviewAsync();
        var mappingsTask = _apiClient.GetLmsCourseMappingsAsync();
        var outboxTask = _apiClient.GetLmsOutboxAsync();
        await Task.WhenAll(overviewTask, mappingsTask, outboxTask);

        var overview = await overviewTask;
        var mappings = await mappingsTask;
        var outbox = await outboxTask;
        if (overview is null || mappings is null || outbox is null)
        {
            LmsCourseMappingsDataGrid.ItemsSource = Array.Empty<object>();
            LmsOutboxDataGrid.ItemsSource = Array.Empty<object>();
            ResetLmsMetrics();
            SetViewStatus("Không tải được dữ liệu CSCA LMS. Kiểm tra quyền System Sync và kết nối API.", isError: true);
            return;
        }

        LmsReadyMappingsMetricText.Text = overview.ReadyCourseMappings.ToString("N0", CultureInfo.InvariantCulture);
        LmsTotalCoursesMetricText.Text = $"trên {overview.TotalCourses.ToString("N0", CultureInfo.InvariantCulture)} khóa học";
        LmsActiveGrantsMetricText.Text = overview.ActiveGrants.ToString("N0", CultureInfo.InvariantCulture);
        LmsPendingOutboxMetricText.Text = overview.PendingOutbox.ToString("N0", CultureInfo.InvariantCulture);
        LmsFailedOutboxMetricText.Text = (overview.FailedOutbox + overview.DeadLetterOutbox).ToString("N0", CultureInfo.InvariantCulture);
        LmsCourseMappingsDataGrid.ItemsSource = mappings.Items;
        LmsOutboxDataGrid.ItemsSource = outbox.Items;
        SetLoadedStatus("CSCA LMS", mappings.Items.Count + outbox.Items.Count);
    }

    private void ResetLmsMetrics()
    {
        LmsReadyMappingsMetricText.Text = "—";
        LmsTotalCoursesMetricText.Text = "trên — khóa học";
        LmsActiveGrantsMetricText.Text = "—";
        LmsPendingOutboxMetricText.Text = "—";
        LmsFailedOutboxMetricText.Text = "—";
    }

    // ── EdTech Course / Question Actions (Ngày 9 Core) ──

    private void CourseSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        CourseSearchPlaceholder.Visibility = string.IsNullOrEmpty(CourseSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("courses", "Đang tìm khóa học...", () => LoadCoursesAsync(CourseSearchBox.Text));
    }

    private void QuestionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        QuestionSearchPlaceholder.Visibility = string.IsNullOrEmpty(QuestionSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("questions", "Đang tìm câu hỏi...", () => LoadQuestionsAsync(QuestionSearchBox.Text));
    }

    private void CustomerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        _ = DebounceSearchAsync("customers", "Đang tìm học viên...", () => LoadCustomersAsync(CustomerSearchBox.Text));
    }

    private void RefreshCourses_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải danh sách khóa học...", () => LoadCoursesAsync(CourseSearchBox.Text));
    private void RefreshQuestions_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải ngân hàng câu hỏi...", () => LoadQuestionsAsync(QuestionSearchBox.Text));
    private void RefreshCustomers_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải danh sách học viên & khách hàng...", () => LoadCustomersAsync(CustomerSearchBox.Text));
    private void RefreshSyncRuns_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải lịch sử đồng bộ và Dead-Letter...", () => LoadSyncRunsAsync());
    private void RefreshDeadLetters_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải Dead-Letter Queue...", LoadDeadLettersAsync);
    private void RefreshLmsIntegration_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải trạng thái CSCA LMS...", LoadLmsIntegrationAsync);

    private void LmsCourseMappingsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LmsCourseMappingsDataGrid.SelectedItem is not ApiClient.LmsCourseMappingItem mapping)
        {
            LmsSelectedCourseText.Text = "Chọn khóa học bên dưới";
            LmsExternalCourseIdBox.Text = string.Empty;
            LmsCourseSlugBox.Text = string.Empty;
            LmsCourseIdBox.Text = string.Empty;
            LmsEnableAccessCheckBox.IsChecked = false;
            return;
        }

        LmsSelectedCourseText.Text = $"{mapping.CourseTitle} · {mapping.CourseSourceId}";
        LmsExternalCourseIdBox.Text = mapping.ExternalCourseId ?? mapping.CourseSourceId;
        LmsCourseSlugBox.Text = mapping.LmsCourseSlug ?? string.Empty;
        LmsCourseIdBox.Text = mapping.LmsCourseId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        LmsEnableAccessCheckBox.IsChecked = string.Equals(mapping.Status, "Success", StringComparison.OrdinalIgnoreCase);
    }

    private async void SaveLmsMapping_Click(object sender, RoutedEventArgs e)
    {
        if (LmsCourseMappingsDataGrid.SelectedItem is not ApiClient.LmsCourseMappingItem mapping)
        {
            ShowToast("Chọn một khóa học trước khi lưu mapping LMS.");
            return;
        }

        long? lmsCourseId = null;
        var rawLmsCourseId = LmsCourseIdBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(rawLmsCourseId))
        {
            if (!long.TryParse(rawLmsCourseId, out var parsedLmsCourseId) || parsedLmsCourseId <= 0)
            {
                ShowToast("LMS course ID phải là số nguyên dương.", isError: true);
                return;
            }

            lmsCourseId = parsedLmsCourseId;
        }

        var enableAccess = LmsEnableAccessCheckBox.IsChecked == true;
        if (enableAccess)
        {
            var confirmation = MessageBox.Show(
                $"Bật cấp quyền LMS cho khóa '{mapping.CourseTitle}'? Chỉ học viên đã đóng đủ mới được cấp quyền, nhưng mapping này sẽ được dùng cho các lần xử lý tiếp theo.",
                "Xác nhận bật cấp quyền LMS",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirmation != MessageBoxResult.Yes) return;
        }

        await RunWithBusyAsync("Đang lưu mapping khóa học LMS...", async () =>
        {
            var saved = await _apiClient.UpsertLmsCourseMappingAsync(
                mapping.CourseId,
                new ApiClient.LmsCourseMappingRequest(
                    string.IsNullOrWhiteSpace(LmsExternalCourseIdBox.Text) ? null : LmsExternalCourseIdBox.Text.Trim(),
                    lmsCourseId,
                    string.IsNullOrWhiteSpace(LmsCourseSlugBox.Text) ? null : LmsCourseSlugBox.Text.Trim(),
                    enableAccess));
            if (saved is null)
                throw new InvalidOperationException("Không lưu được mapping LMS. Kiểm tra quyền thao tác và dữ liệu course ID/slug.");

            ShowToast(enableAccess ? "Đã bật mapping cấp quyền LMS." : "Đã lưu mapping ở trạng thái chưa bật.");
            await LoadLmsIntegrationAsync();
        });
    }

    private async void RetryLmsOutbox_Click(object sender, RoutedEventArgs e)
    {
        if (LmsOutboxDataGrid.SelectedItem is not ApiClient.LmsOutboxItem item)
        {
            ShowToast("Chọn một lệnh LMS outbox trước khi retry.");
            return;
        }

        if (string.Equals(item.Status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Lệnh này đã gửi thành công, không cần retry.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Đưa lệnh '{item.EventType}' về hàng chờ để gửi lại LMS? Nội dung học viên không hiển thị trên màn hình này.",
            "Xác nhận retry LMS outbox",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang đưa lệnh LMS về hàng chờ...", async () =>
        {
            if (!await _apiClient.RetryLmsOutboxAsync(item.Id))
                throw new InvalidOperationException("Không thể retry lệnh LMS outbox.");

            ShowToast("Đã đưa lệnh LMS về hàng chờ.");
            await LoadLmsIntegrationAsync();
        });
    }

    private async void DispatchLmsOutbox_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Gửi tối đa 20 lệnh đã commit sang CSCA Course LMS ngay bây giờ? Hành động này gọi API LMS bằng HMAC; chỉ dùng khi secrets và mapping đã được cấu hình đúng.",
            "Xác nhận gửi hàng chờ LMS",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang gửi hàng chờ sang CSCA LMS...", async () =>
        {
            var result = await _apiClient.DispatchLmsOutboxAsync();
            if (result is null)
                throw new InvalidOperationException("Không gửi được hàng chờ LMS. Kiểm tra secrets, endpoint và mapping.");

            ShowToast($"LMS outbox: {result.Succeeded} thành công, {result.Retrying} sẽ thử lại, {result.DeadLettered} cần rà soát.");
            await LoadLmsIntegrationAsync();
        });
    }

    private async void RetryDeadLetter_Click(object sender, RoutedEventArgs e)
    {
        if (DeadLettersDataGrid.SelectedItem is not ApiClient.DeadLetterItem deadLetter)
        {
            ShowToast("Vui lòng chọn một bản ghi Dead-Letter trước khi retry.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Retry bản ghi '{deadLetter.SourceId}' từ {deadLetter.SourceSystem}?\nHệ thống sẽ ghi nhận lần thử lại mới.",
            "Xác nhận retry Dead-Letter",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang retry bản ghi Dead-Letter...", async () =>
        {
            var success = await _apiClient.RetryDeadLetterAsync(deadLetter.Id);
            if (!success)
            {
                throw new InvalidOperationException("Retry thất bại hoặc bản ghi không còn tồn tại.");
            }

            ShowToast("Retry Dead-Letter thành công.");
            await LoadSyncRunsAsync();
        });
    }

    private void ExportSyncRuns_Click(object sender, RoutedEventArgs e)
    {
        var rows = SyncRunsDataGrid.Items.OfType<ApiClient.SyncRunItem>().ToList();
        ExportCsv(
            "Xuất lịch sử đồng bộ",
            "lich-su-dong-bo.csv",
            ["Nguồn", "Loại thực thể", "Bắt đầu", "Kết thúc", "Đã đọc", "Đã ghi", "Bỏ qua", "Lỗi", "Trạng thái", "Chi tiết lỗi"],
            rows.Select(item => new[]
            {
                item.SourceSystem,
                item.EntityType,
                item.StartedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                item.CompletedAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty,
                item.RecordsRead.ToString(),
                item.RecordsWritten.ToString(),
                item.RecordsSkipped.ToString(),
                item.RecordsFailed.ToString(),
                item.StatusLabel,
                item.ErrorMessage ?? string.Empty
            }));
    }

    private void ExportInventory_Click(object sender, RoutedEventArgs e)
    {
        var rows = InventoryBalancesDataGrid.Items.OfType<ApiClient.InventoryBalanceItem>().ToList();
        ExportCsv(
            "Xuất tồn kho",
            "ton-kho.csv",
            ["Mã SKU", "Sản phẩm", "Màu", "Cỡ", "Tồn thực tế", "Đang giữ", "Khả dụng", "Cập nhật lúc"],
            rows.Select(item => new[]
            {
                item.Sku,
                item.ProductName ?? string.Empty,
                item.Color ?? string.Empty,
                item.Size ?? string.Empty,
                item.OnHandQuantity.ToString(),
                item.ReservedQuantity.ToString(),
                item.AvailableQuantity.ToString(),
                item.LastUpdated.ToString("dd/MM/yyyy HH:mm:ss")
            }));
    }

    private void ExportCsv(string title, string defaultFileName, IReadOnlyList<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = "Tệp CSV UTF-8 (*.csv)|*.csv|Tất cả tệp (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var materializedRows = rows.Select(row => row.ToArray()).ToList();
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in materializedRows)
            {
                builder.AppendLine(string.Join(",", row.Select(CsvEscape)));
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowToast($"Đã xuất {materializedRows.Count:N0} dòng ra {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            ShowToast($"Không thể xuất file: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private async void CreateCourseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo khóa học", new[]
        {
            new PromptField("title", "Tên khóa học"),
            new PromptField("price", "Học phí", InitialValue: "0"),
            new PromptField("description", "Mô tả", IsRequired: false)
        }, out var values)) return;

        if (!decimal.TryParse(values["price"], out var price) || price < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newTitle = values["title"];
        await RunWithBusyAsync("Đang tạo khóa học mới...", async () =>
        {
            var success = await _apiClient.CreateCourseAsync(newTitle, price, values["description"]);
            if (success)
            {
                MessageBox.Show($"Tạo thành công khóa học: '{newTitle}'!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCoursesAsync();
                await LoadDashboardMetricsAsync();
            }
            else
            {
                MessageBox.Show("Tạo khóa học thất bại. Bạn có quyền Courses.Manage không?", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void EditCourseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (CoursesDataGrid.SelectedItem is not ApiClient.CourseItem course)
        {
            ShowToast("Vui lòng chọn một khóa học cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa khóa học — {course.Title}", new[]
        {
            new PromptField("title", "Tên khóa học", course.Title),
            new PromptField("price", "Học phí (VND)", course.Price.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("slug", "Slug URL", course.Slug, IsRequired: false),
            new PromptField("description", "Mô tả", course.Description, IsRequired: false),
            new PromptField("status", "Trạng thái", course.Status, Options: new[]
            {
                new PromptOption("Published", "Published (Đã xuất bản)"),
                new PromptOption("Draft", "Draft (Bản nháp)"),
                new PromptOption("Archived", "Archived (Lưu trữ)")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["price"], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunWithBusyAsync("Đang cập nhật khóa học...", async () =>
        {
            var success = await _apiClient.UpdateCourseAsync(course.Id, values["title"], price, values["description"], values["status"], values["slug"]);
            if (success)
            {
                MessageBox.Show($"Cập nhật thành công khóa học: '{values["title"]}'!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCoursesAsync();
                await LoadDashboardMetricsAsync();
            }
            else
            {
                MessageBox.Show("Cập nhật khóa học thất bại. Vui lòng kiểm tra kết nối hoặc quyền hạn.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void DeleteCourse_Click(object sender, RoutedEventArgs e)
    {
        if (CoursesDataGrid.SelectedItem is not ApiClient.CourseItem course)
        {
            ShowToast("Vui lòng chọn một khóa học cần xóa.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa khóa học '{course.Title}' (Mã: {course.CourseSourceId})?\nLưu ý: Không thể xóa khóa học đang có lớp học hoạt động.",
            "Xác nhận xóa khóa học",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang xóa khóa học...", async () =>
        {
            var success = await _apiClient.DeleteCourseAsync(course.Id);
            if (success)
            {
                MessageBox.Show($"Đã xóa khóa học: '{course.Title}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCoursesAsync();
                await LoadDashboardMetricsAsync();
            }
            else
            {
                MessageBox.Show("Không thể xóa khóa học. Khóa học có thể đang chứa lớp học hoạt động hoặc bạn không có quyền Courses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void CoursesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => FilterClassesBySelectedCourse();

    private void ViewCourseClasses_Click(object sender, RoutedEventArgs e)
        => FilterClassesBySelectedCourse();

    private void FilterClassesBySelectedCourse()
    {
        if (CoursesDataGrid.SelectedItem is not ApiClient.CourseItem course)
        {
            ShowToast("Vui lòng chọn một khóa học.");
            return;
        }

        _selectedCourseFilterId = course.Id;
        _selectedCourseFilterTitle = course.Title;

        CourseModuleTabControl.SelectedItem = TabItemClasses;

        if (CscaCourseFilterComboBox.ItemsSource is IEnumerable<CourseFilterOption> options)
        {
            var match = options.FirstOrDefault(o => o.Id == course.Id);
            if (match != null)
            {
                CscaCourseFilterComboBox.SelectedItem = match;
            }
        }

        ApplyCscaClassFilter();
    }

    private void ClearCscaCourseFilter_Click(object sender, RoutedEventArgs e)
    {
        _selectedCourseFilterId = null;
        _selectedCourseFilterTitle = null;
        CscaCourseFilterComboBox.SelectedIndex = 0;
        ApplyCscaClassFilter();
    }

    private void CscaCourseFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady) return;
        if (CscaCourseFilterComboBox.SelectedItem is CourseFilterOption opt)
        {
            _selectedCourseFilterId = opt.Id;
            _selectedCourseFilterTitle = opt.Id.HasValue ? opt.Title : null;
            ApplyCscaClassFilter();
        }
    }

    private void CourseModuleTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl tabControl) return;
        if (tabControl.SelectedItem is TabItem selectedTab)
        {
            if (selectedTab == TabItemClasses)
            {
                if (_allCscaClasses.Count == 0)
                {
                    _ = LoadCscaClassesAsync();
                }
            }
        }
    }

    private void CreateQuestionDialog_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Chức năng tạo câu hỏi tương tác hoàn chỉnh sẽ mở modal nhập chi tiết. Đang hỗ trợ tạo tự động qua API & Sync.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void PublishQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid versionId)
        {
            var res = MessageBox.Show(
                "Bạn có chắc chắn muốn phê duyệt và xuất bản câu hỏi này vào ngân hàng đề thi chính thức?",
                "Xác Nhận Xuất Bản",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                btn.IsEnabled = false;
                var success = await _apiClient.PublishQuestionVersionAsync(versionId, "Đã phê duyệt xuất bản từ WPF Desktop Client.");
                if (success)
                {
                    MessageBox.Show("Xuất bản câu hỏi thành công! Trạng thái đã chuyển sang Published.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadQuestionsAsync(QuestionSearchBox.Text);
                }
                else
                {
                    MessageBox.Show("Xuất bản thất bại. Bạn cần có quyền Questions.Publish.", "Quyền Hạn", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                btn.IsEnabled = true;
            }
        }
    }

    // ── CSCA & Interview Actions (Ngày 10) ──

    private void CscaSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        CscaSearchPlaceholder.Visibility = string.IsNullOrEmpty(CscaSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("csca", "Đang tìm dữ liệu CSCA...", async () =>
        {
            await LoadCscaClassesAsync(CscaSearchBox.Text);
            await LoadCscaOnlineAsync(CscaSearchBox.Text);
        });
    }

    private void RefreshCsca_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải dữ liệu CSCA...", async () =>
    {
        await LoadCscaClassesAsync(CscaSearchBox.Text);
        await LoadCscaOnlineAsync(CscaSearchBox.Text);
    });

    private void RefreshCscaOnline_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tải nội dung CSCA từ website...", () => LoadCscaOnlineAsync(CscaSearchBox.Text));

    private void CscaClassesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => _ = ShowSelectedCscaClassDetailsAsync();

    private void ViewCscaClassDetails_Click(object sender, RoutedEventArgs e)
        => _ = ShowSelectedCscaClassDetailsAsync();

    private async Task ShowSelectedCscaClassDetailsAsync()
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem selectedClass)
        {
            ShowToast("Hãy chọn một lớp CSCA để xem chi tiết.");
            return;
        }

        var detailWindow = new CscaClassDetailWindow(_apiClient, selectedClass.Id)
        {
            Owner = this
        };
        detailWindow.ShowDialog();
        await LoadCscaClassesAsync(CscaSearchBox.Text);
    }

    private async void CreateCscaClassDialog_Click(object sender, RoutedEventArgs e)
    {
        var courses = await _apiClient.GetCoursesAsync();
        if (courses is null)
        {
            ShowToast("Không tải được danh sách khóa học. Hãy thử lại trước khi tạo lớp.", true);
            return;
        }

        if (courses.Items.Count == 0)
        {
            MessageBox.Show(this, "Hãy tạo khóa học trước, sau đó mới thêm lớp học vào khóa học đó.", "Chưa có khóa học", MessageBoxButton.OK, MessageBoxImage.Information);
            NavCourses.IsChecked = true;
            return;
        }

        var defaultCourseId = _selectedCourseFilterId?.ToString() ?? courses.Items[0].Id.ToString();

        if (!PromptDialog.TryShow(this, "Tạo lớp học mới", new[]
        {
            new PromptField("courseId", "Khóa học", InitialValue: defaultCourseId, Options: courses.Items
                .Select(course => new PromptOption(course.Id.ToString(), course.Title))
                .ToArray()),
            new PromptField("code", "Mã lớp (ví dụ: CSCA-2026-K03)"),
            new PromptField("name", "Tên lớp học"),
            new PromptField("batch", "Đợt / Khóa tuyển sinh"),
            new PromptField("tuitionFee", "Học phí (VND)", InitialValue: "0")
        }, out var values)) return;

        if (!decimal.TryParse(values["tuitionFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var tuitionFee) || tuitionFee < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var code = values["code"];
        var name = values["name"];
        if (!Guid.TryParse(values["courseId"], out var courseId))
        {
            ShowToast("Hãy chọn khóa học cho lớp.", true);
            return;
        }

        await RunWithBusyAsync("Đang tạo lớp học mới...", async () =>
        {
            var success = await _apiClient.CreateCscaClassAsync(code, name, values["batch"], string.Empty, tuitionFee, courseId);
            if (success)
            {
                MessageBox.Show($"Tạo thành công lớp học: '{code}' - '{name}'!\n\nBước tiếp theo: chọn lớp và mở Chi tiết lớp → Lịch học để lập thời khóa biểu.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCscaClassesAsync();
                await LoadCoursesAsync();
            }
            else
            {
                MessageBox.Show("Tạo lớp học thất bại. Bạn cần có quyền CscaClasses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void EditCscaClass_Click(object sender, RoutedEventArgs e)
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem cls)
        {
            ShowToast("Hãy chọn một lớp học để sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa lớp {cls.Code}", new[]
        {
            new PromptField("name", "Tên lớp", cls.Name),
            new PromptField("batch", "Đợt / khóa", cls.Batch),
            new PromptField("tuitionFee", "Học phí", cls.TuitionFee.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("status", "Trạng thái", cls.Status, Options: new[]
            {
                new PromptOption("Active", "Active (Đang hoạt động)"),
                new PromptOption("Completed", "Completed (Đã kết thúc)"),
                new PromptOption("Cancelled", "Cancelled (Đã hủy)")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["tuitionFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var tuitionFee) || tuitionFee < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunWithBusyAsync("Đang cập nhật lớp học...", async () =>
        {
            var success = await _apiClient.UpdateCscaClassAsync(cls.Id, values["name"], values["batch"], cls.Schedule, tuitionFee, cls.StartDate, cls.EndDate, values["status"]);
            if (success)
            {
                MessageBox.Show($"Cập nhật thông tin lớp {cls.Code} thành công!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCscaClassesAsync();
            }
            else
            {
                MessageBox.Show("Cập nhật lớp thất bại. Bạn cần có quyền CscaClasses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void DeleteCscaClass_Click(object sender, RoutedEventArgs e)
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem cls)
        {
            ShowToast("Hãy chọn một lớp học cần xóa.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa lớp học '{cls.Code} — {cls.Name}'?",
            "Xác nhận xóa lớp học",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang xóa lớp học...", async () =>
        {
            var success = await _apiClient.DeleteCscaClassAsync(cls.Id);
            if (success)
            {
                MessageBox.Show($"Đã xóa thành công lớp học: '{cls.Code}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCscaClassesAsync();
                await LoadCoursesAsync();
            }
            else
            {
                MessageBox.Show("Xóa lớp học thất bại. Bạn cần có quyền CscaClasses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void InterviewSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        InterviewSearchPlaceholder.Visibility = string.IsNullOrEmpty(InterviewSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("interview", "Đang tìm khách hàng phỏng vấn...", () => LoadInterviewCustomersAsync(InterviewSearchBox.Text));
    }

    private void RefreshInterview_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải danh sách Mock Interview...", () => LoadInterviewCustomersAsync(InterviewSearchBox.Text));

    private async void SyncInterviewFromWebsite_Click(object sender, RoutedEventArgs e)
    {
        var result = await _apiClient.TriggerSyncAsync("WEBSITE_INTERVIEW", "InterviewCustomers");
        if (result is null)
        {
            MessageBox.Show(
                "Đồng bộ CSCA Interview thất bại. Hãy kiểm tra Integrations__CscaInterview__IntegrationKey hoặc BearerToken và CustomersPath trên API.",
                "Lỗi Đồng Bộ CSCA Interview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var image = result.RecordsFailed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
        MessageBox.Show(
            $"Đồng bộ CSCA Interview hoàn tất!\nĐã đọc: {result.RecordsRead}\nĐã ghi: {result.RecordsWritten}\nBỏ qua: {result.RecordsSkipped}\nLỗi: {result.RecordsFailed}",
            "Kết Quả Đồng Bộ CSCA Interview",
            MessageBoxButton.OK,
            image);

        await LoadInterviewCustomersAsync(InterviewSearchBox.Text);
    }

    private async void CreateInterviewCustomerDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo hồ sơ khách hàng Interview", new[]
        {
            new PromptField("name", "Họ và tên"),
            new PromptField("email", "Email"),
            new PromptField("phone", "Số điện thoại"),
            new PromptField("packageName", "Tên gói dịch vụ"),
            new PromptField("sessions", "Số buổi", InitialValue: "1"),
            new PromptField("paidAmount", "Số tiền đã thanh toán", InitialValue: "0")
        }, out var values)) return;

        if (!int.TryParse(values["sessions"], out var sessions) || sessions <= 0 ||
            !decimal.TryParse(values["paidAmount"], out var paidAmount) || paidAmount < 0)
        {
            MessageBox.Show("Số buổi phải lớn hơn 0; số tiền thanh toán phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = values["name"];
        var success = await _apiClient.CreateInterviewCustomerAsync(name, values["email"], values["phone"], values["packageName"], sessions, paidAmount);
        if (success)
        {
            MessageBox.Show($"Tạo thành công hồ sơ phỏng vấn: '{name}'!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadInterviewCustomersAsync();
        }
        else
        {
            MessageBox.Show("Tạo hồ sơ Interview thất bại. Bạn cần có quyền InterviewCustomers.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Quick Sync Trigger Actions ──

    private async void QuickSync_Click(object sender, RoutedEventArgs e)
    {
        var result = await _apiClient.TriggerSyncAsync("CSCA_MOLI_STUDIO", "Courses");
        if (result != null)
        {
            MessageBox.Show(
                $"Đồng bộ thành công!\nĐã đọc: {result.RecordsRead}\nĐã ghi: {result.RecordsWritten}\nBỏ qua: {result.RecordsSkipped}\nLỗi: {result.RecordsFailed}",
                "Kết Quả Đồng Bộ CSCA-MOLI.STUDIO",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadCoursesAsync();
            await LoadDashboardMetricsAsync();
        }
        else
        {
            MessageBox.Show("Kích hoạt đồng bộ thất bại. Vui lòng kiểm tra quyền SystemSync.Trigger.", "Lỗi Đồng Bộ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void TriggerSyncCourses_Click(object sender, RoutedEventArgs e) => QuickSync_Click(sender, e);

    private async void TriggerSyncCustomers_Click(object sender, RoutedEventArgs e)
    {
        var result = await _apiClient.TriggerSyncAsync("CSCA_MOLI_STUDIO", "Customers");
        if (result != null)
        {
            MessageBox.Show(
                $"Đồng bộ học viên thành công!\nĐã đọc: {result.RecordsRead}\nĐã ghi: {result.RecordsWritten}\nBỏ qua: {result.RecordsSkipped}",
                "Kết Quả Đồng Bộ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadCustomersAsync();
            await LoadDashboardMetricsAsync();
        }
    }

    // ── Day 11: HR & Employees Loaders & Handlers ──

    private List<ApiClient.DepartmentItem> _cachedDepartments = new();

    private async Task LoadDepartmentsFilterAsync()
    {
        try
        {
            var depts = await _apiClient.GetDepartmentsAsync();
            if (depts != null)
            {
                _cachedDepartments = depts;
                EmployeeDeptFilterCombo.Items.Clear();
                var allItem = new ComboBoxItem { Content = "-- Tất Cả Phòng Ban --", IsSelected = true, Tag = null };
                EmployeeDeptFilterCombo.Items.Add(allItem);

                foreach (var d in depts)
                {
                    EmployeeDeptFilterCombo.Items.Add(new ComboBoxItem { Content = $"{d.Code} - {d.Name}", Tag = d.Id });
                }

                SetLoadedStatus("Phòng ban", depts.Count);
            }
            else
            {
                SetViewStatus("Không tải được danh sách phòng ban. Vui lòng thử lại.", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được phòng ban: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private async Task LoadEmployeesAsync()
    {
        if (!_isUiReady)
        {
            return;
        }

        try
        {
            var search = EmployeeSearchBox?.Text?.Trim();
            Guid? deptId = null;
            if (EmployeeDeptFilterCombo?.SelectedItem is ComboBoxItem selected && selected.Tag is Guid g)
            {
                deptId = g;
            }

            var result = await _apiClient.GetEmployeesAsync(search, deptId, businessSegment: _activeSegment);
            if (result != null)
            {
                var allItems = result.Items;
                var workingList = allItems.Where(e => e.Status == "Active" || e.Status == "Probation" || e.Status == "OnLeave" || string.IsNullOrWhiteSpace(e.Status)).ToList();
                var profilesList = allItems.ToList();
                var missingCvList = workingList.Where(e => string.IsNullOrWhiteSpace(e.CvUrlOrPath)).ToList();
                var resignedList = allItems.Where(e => e.Status == "Resigned" || e.Status == "Inactive").ToList();
                var blacklistedList = allItems.Where(e => e.Status == "Blacklisted").ToList();

                if (EmployeesWorkingDataGrid != null)
                {
                    EmployeesWorkingDataGrid.ItemsSource = workingList;
                    EmployeesWorkingEmptyText.Visibility = workingList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (EmployeesProfilesDataGrid != null)
                {
                    EmployeesProfilesDataGrid.ItemsSource = profilesList;
                    EmployeesProfilesEmptyText.Visibility = profilesList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (EmployeesMissingCvDataGrid != null)
                {
                    EmployeesMissingCvDataGrid.ItemsSource = missingCvList;
                    EmployeesMissingCvEmptyText.Visibility = missingCvList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (EmployeesResignedDataGrid != null)
                {
                    EmployeesResignedDataGrid.ItemsSource = resignedList;
                    EmployeesResignedEmptyText.Visibility = resignedList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                if (EmployeesBlacklistDataGrid != null)
                {
                    EmployeesBlacklistDataGrid.ItemsSource = blacklistedList;
                    EmployeesBlacklistEmptyText.Visibility = blacklistedList.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }

                MetricTotalEmployees.Text = result.TotalCount.ToString();
                MetricActiveEmployees.Text = workingList.Count.ToString();
                MetricResignedEmployees.Text = resignedList.Count.ToString();
                MetricBlacklistedEmployees.Text = blacklistedList.Count.ToString();

                SetLoadedStatus("Nhân sự", result.Items.Count);
            }
            else
            {
                if (EmployeesWorkingDataGrid != null) EmployeesWorkingDataGrid.ItemsSource = Array.Empty<object>();
                if (EmployeesProfilesDataGrid != null) EmployeesProfilesDataGrid.ItemsSource = Array.Empty<object>();
                if (EmployeesMissingCvDataGrid != null) EmployeesMissingCvDataGrid.ItemsSource = Array.Empty<object>();
                if (EmployeesResignedDataGrid != null) EmployeesResignedDataGrid.ItemsSource = Array.Empty<object>();
                if (EmployeesBlacklistDataGrid != null) EmployeesBlacklistDataGrid.ItemsSource = Array.Empty<object>();

                if (EmployeesWorkingEmptyText != null) EmployeesWorkingEmptyText.Visibility = Visibility.Collapsed;
                if (EmployeesProfilesEmptyText != null) EmployeesProfilesEmptyText.Visibility = Visibility.Collapsed;
                if (EmployeesMissingCvEmptyText != null) EmployeesMissingCvEmptyText.Visibility = Visibility.Collapsed;
                if (EmployeesResignedEmptyText != null) EmployeesResignedEmptyText.Visibility = Visibility.Collapsed;
                if (EmployeesBlacklistEmptyText != null) EmployeesBlacklistEmptyText.Visibility = Visibility.Collapsed;

                SetViewStatus("Không tải được danh sách nhân sự. Vui lòng thử lại.", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được nhân sự: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private void EmployeeSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUiReady) _ = DebounceSearchAsync(SegmentViewKey("employees-search"), "Đang tìm nhân sự...", LoadEmployeesAsync);
    }

    private void EmployeeDeptFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiReady) _ = DebounceSearchAsync(SegmentViewKey("employees-filter"), "Đang lọc nhân sự...", LoadEmployeesAsync);
    }
    private void RefreshEmployees_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải danh sách nhân sự...", () => LoadEmployeesAsync());

    private async void EmployeesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(grid, source) is not DataGridRow ||
            grid.SelectedItem is not ApiClient.EmployeeItem employee)
        {
            return;
        }

        await ShowEmployeeDialogAsync(employee);
    }

    // ── Day 11: Attendance & Excel Import Loaders & Handlers ──

    private async Task LoadAttendanceAsync()
    {
        try
        {
            DateOnly? fromDate = AttendanceFromDatePicker?.SelectedDate.HasValue == true
                ? DateOnly.FromDateTime(AttendanceFromDatePicker.SelectedDate.Value)
                : null;
            DateOnly? toDate = AttendanceToDatePicker?.SelectedDate.HasValue == true
                ? DateOnly.FromDateTime(AttendanceToDatePicker.SelectedDate.Value)
                : null;

            var records = await _apiClient.GetAttendanceRecordsAsync(fromDate, toDate, businessSegment: _activeSegment);
            if (records != null)
            {
                AttendanceDataGrid.ItemsSource = records.Items;
                AttendanceEmptyText.Visibility = records.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SetLoadedStatus("Chấm công", records.Items.Count);
            }
            else
            {
                AttendanceDataGrid.ItemsSource = Array.Empty<object>();
                AttendanceEmptyText.Visibility = Visibility.Collapsed;
                SetViewStatus("Không tải được dữ liệu chấm công. Vui lòng thử lại.", isError: true);
            }

            var summary = await _apiClient.GetAttendanceSummaryAsync(fromDate, toDate, businessSegment: _activeSegment);
            if (summary != null)
            {
                MetricAttendanceTotal.Text = summary.TotalRecords.ToString();
                MetricAttendancePresent.Text = summary.PresentCount.ToString();
                MetricAttendanceLate.Text = summary.LateCount.ToString();
                MetricAttendanceWorkHours.Text = $"{summary.TotalWorkHours:0.##} h";
            }
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được chấm công: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private void FilterAttendance_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang lọc dữ liệu chấm công...", () => LoadAttendanceAsync());
    private void RefreshAttendance_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải dữ liệu chấm công...", () => LoadAttendanceAsync());

    private async void DownloadAttendanceTemplate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var file = await _apiClient.DownloadAttendanceImportTemplateAsync();
            if (file == null)
            {
                ShowToast("Không tải được mẫu Excel. Hãy kiểm tra quyền import chấm công.", true);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Lưu mẫu nhập chấm công",
                FileName = file.FileName,
                Filter = "Excel Workbook (*.xlsx)|*.xlsx"
            };
            if (dialog.ShowDialog() == true)
            {
                await File.WriteAllBytesAsync(dialog.FileName, file.Content);
                ShowToast("Đã tải mẫu Excel chấm công.");
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Không tải được mẫu Excel: {ToFriendlyError(ex)}", true);
        }
    }

    private async void ImportAttendance_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file bảng tính Chấm Công (Excel / CSV)",
            Filter = "Bảng Tính (*.csv;*.xlsx;*.txt)|*.csv;*.xlsx;*.txt|Tất cả tệp (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            var filePath = dialog.FileName;
            try
            {
                var result = await _apiClient.ImportAttendanceFileAsync(filePath, _activeSegment);
                if (result != null)
                {
                    var msg = $"Xử lý Import File hoàn tất!\n" +
                              $"• Mã đợt import (BatchId): {result.ImportBatchId}\n" +
                              $"• Tổng số dòng: {result.TotalRowsProcessed}\n" +
                              $"• Thành công: {result.SuccessCount}\n" +
                              $"• Dòng lỗi: {result.ErrorCount}";

                    if (result.Errors.Count > 0)
                    {
                        msg += "\n\nChi tiết lỗi từng dòng:\n" +
                               string.Join("\n", result.Errors.Take(10).Select(err => $"  - [Dòng {err.RowNumber}] NV '{err.EmployeeCode}': {err.ErrorMessage}"));
                        if (result.Errors.Count > 10)
                        {
                            msg += $"\n  ... và {result.Errors.Count - 10} dòng lỗi khác.";
                        }
                    }

                    MessageBox.Show(
                        msg,
                        "Kết Quả Import Chấm Công",
                        MessageBoxButton.OK,
                        result.ErrorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                    await LoadAttendanceAsync();
                }
                else
                {
                    MessageBox.Show("Import file thất bại. Vui lòng kiểm tra quyền Permissions.Attendance.Import hoặc định dạng file.", "Lỗi Import", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi tải file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ── Day 12: Payroll & Payslips Handlers ──

    private List<ApiClient.PayrollPeriodItem> _cachedPayrollPeriods = new();
    private List<ApiClient.PayrollAdjustmentItem> _cachedPayrollAdjustments = new();
    private bool _isPayrollPeriodSelectorSyncing;

    private sealed record PayrollMonthlySummaryRow(
        Guid PeriodId,
        string MonthLabel,
        string PeriodName,
        string StatusLabel,
        int RecipientCount,
        string TotalGrossLabel,
        string TotalNetLabel,
        string CalculatedAtLabel,
        decimal TotalGross,
        decimal TotalNet);

    private async Task LoadPayrollPeriodsAsync(Guid? preferredPeriodId = null)
    {
        try
        {
            preferredPeriodId ??= PayrollPeriodSelectorCombo.SelectedValue is Guid currentPeriodId
                ? currentPeriodId
                : null;
            var data = await _apiClient.GetPayrollPeriodsAsync(businessSegment: _activeSegment);
            if (data != null)
            {
                _cachedPayrollPeriods = data.Items.ToList();
                MetricPayrollTotalPeriods.Text = _cachedPayrollPeriods.Count.ToString();

                await LoadPayrollDepartmentFilterAsync();
                LoadPayrollCreateBusinessUnits();

                Guid? targetPeriodId = null;
                _isPayrollPeriodSelectorSyncing = true;
                try
                {
                    PayrollPeriodSelectorCombo.ItemsSource = null;
                    PayrollPeriodSelectorCombo.ItemsSource = _cachedPayrollPeriods.Select(p => new
                    {
                        p.Id,
                        DisplayText = $"{p.Name} ({p.StartDate:dd/MM} - {p.EndDate:dd/MM})"
                    }).ToList();
                    PayrollPeriodSelectorCombo.DisplayMemberPath = "DisplayText";
                    PayrollPeriodSelectorCombo.SelectedValuePath = "Id";

                    if (_cachedPayrollPeriods.Count > 0)
                    {
                        targetPeriodId = preferredPeriodId.HasValue && _cachedPayrollPeriods.Any(p => p.Id == preferredPeriodId.Value)
                            ? preferredPeriodId.Value
                            : _cachedPayrollPeriods[0].Id;
                        PayrollPeriodSelectorCombo.SelectedValue = targetPeriodId.Value;
                    }
                    else
                    {
                        PayslipsDataGrid.ItemsSource = Array.Empty<object>();
                        PayrollAdjustmentEmployeesDataGrid.ItemsSource = Array.Empty<object>();
                        PayrollAdjustmentsDataGrid.ItemsSource = Array.Empty<object>();
                        PayrollReviewAdjustmentsDataGrid.ItemsSource = Array.Empty<object>();
                        PayslipsEmptyText.Visibility = Visibility.Visible;
                        PayrollAdjustmentEmployeesEmptyText.Visibility = Visibility.Visible;
                        PayrollAdjustmentsEmptyText.Visibility = Visibility.Visible;
                        PayrollReviewAdjustmentsEmptyText.Visibility = Visibility.Visible;
                    }
                }
                finally
                {
                    _isPayrollPeriodSelectorSyncing = false;
                }

                if (targetPeriodId.HasValue)
                {
                    await LoadPayslipsForPeriodAsync(targetPeriodId.Value);
                }

                SetLoadedStatus("Kỳ lương", _cachedPayrollPeriods.Count);
            }
            else
            {
                PayrollPeriodSelectorCombo.ItemsSource = null;
                PayslipsDataGrid.ItemsSource = Array.Empty<object>();
                PayslipsEmptyText.Visibility = Visibility.Collapsed;
                SetViewStatus("Không tải được danh sách kỳ lương. Vui lòng thử lại.", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được kỳ lương: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private void LoadPayrollCreateBusinessUnits()
    {
        var options = GetSegmentBusinessUnitOptions(null);
        PayrollCreateBusinessUnitCombo.ItemsSource = options;
        PayrollCreateBusinessUnitCombo.DisplayMemberPath = nameof(EmployeeDialogOption.Label);
        PayrollCreateBusinessUnitCombo.SelectedIndex = options.Count > 0 ? 0 : -1;
        BtnCreatePayrollPeriod.IsEnabled = options.Count > 0;
    }

    private async void CreatePayrollPeriod_Click(object sender, RoutedEventArgs e)
    {
        var name = PayrollCreateNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("Hãy nhập tên kỳ lương.", isError: true);
            PayrollCreateNameBox.Focus();
            return;
        }

        if (PayrollCreateStartDatePicker.SelectedDate is not DateTime startDate ||
            PayrollCreateEndDatePicker.SelectedDate is not DateTime endDate)
        {
            ShowToast("Hãy chọn đủ ngày bắt đầu và ngày kết thúc.", isError: true);
            return;
        }

        if (endDate.Date < startDate.Date)
        {
            ShowToast("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.", isError: true);
            return;
        }

        if (PayrollCreateBusinessUnitCombo.SelectedItem is not EmployeeDialogOption businessUnit || businessUnit.Id is not Guid businessUnitId)
        {
            ShowToast($"Chưa có đơn vị kinh doanh để tạo kỳ lương cho mảng {ActiveSegmentName}.", isError: true);
            return;
        }

        try
        {
            var created = await _apiClient.CreatePayrollPeriodAsync(new ApiClient.CreatePayrollPeriodModel(
                name,
                DateOnly.FromDateTime(startDate.Date),
                DateOnly.FromDateTime(endDate.Date),
                businessUnitId));

            if (created is null)
            {
                ShowToast("Không tạo được kỳ lương. Kiểm tra quyền tính lương hoặc dữ liệu ngày.", isError: true);
                return;
            }

            ShowToast($"Đã tạo kỳ lương {created.Name} ở trạng thái Bản nháp.");
            await LoadPayrollPeriodsAsync(created.Id);
            PayrollFunctionTabs.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            ShowToast($"Lỗi tạo kỳ lương: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private void PayrollFunctionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || !ReferenceEquals(sender, PayrollFunctionTabs) || e.AddedItems.Count == 0) return;

        if (PayrollFunctionTabs.SelectedIndex == 1)
        {
            _ = ReloadPayrollAdjustmentViewAsync();
        }
        else if (PayrollFunctionTabs.SelectedIndex == 2)
        {
            LoadPayrollCreateBusinessUnits();
        }
        else if (PayrollFunctionTabs.SelectedIndex == 3)
        {
            _ = LoadPayrollMonthlySummaryAsync();
        }
        else if (PayrollFunctionTabs.SelectedIndex == 4)
        {
            _ = ReloadPayrollReviewAsync();
        }
    }

    private void PayrollSummaryYear_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiReady && PayrollFunctionTabs.SelectedIndex == 3)
        {
            _ = LoadPayrollMonthlySummaryAsync();
        }
    }

    private void RefreshPayrollMonthlySummary_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tải tổng hợp kỳ lương...", LoadPayrollMonthlySummaryAsync);

    private async Task LoadPayrollMonthlySummaryAsync()
    {
        try
        {
            var year = PayrollSummaryYearCombo.SelectedItem is int selectedYear ? selectedYear : DateTime.Today.Year;
            var data = await _apiClient.GetPayrollPeriodsAsync(year: year, businessSegment: _activeSegment);
            if (data is null)
            {
                PayrollMonthlySummaryDataGrid.ItemsSource = Array.Empty<PayrollMonthlySummaryRow>();
                PayrollMonthlyRecipientsDataGrid.ItemsSource = Array.Empty<ApiClient.PayslipItem>();
                PayrollMonthlySummaryText.Text = "Không tải được báo cáo theo tháng.";
                PayrollMonthlyRecipientsEmptyText.Visibility = Visibility.Visible;
                return;
            }

            var rows = data.Items
                .OrderByDescending(period => period.StartDate)
                .Select(period => new PayrollMonthlySummaryRow(
                    period.Id,
                    period.StartDate.ToString("MM/yyyy"),
                    period.Name,
                    GetPayrollStatusLabel(period.Status),
                    period.PayslipCount,
                    $"{period.TotalGrossAmount:N0} đ",
                    $"{period.TotalNetAmount:N0} đ",
                    period.CalculatedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "Chưa tính",
                    period.TotalGrossAmount,
                    period.TotalNetAmount))
                .ToList();

            PayrollMonthlySummaryDataGrid.ItemsSource = rows;
            if (rows.Count > 0)
            {
                PayrollMonthlySummaryDataGrid.SelectedIndex = 0;
            }
            else
            {
                PayrollMonthlyRecipientsDataGrid.ItemsSource = Array.Empty<ApiClient.PayslipItem>();
                PayrollMonthlySummaryText.Text = $"Năm {year} chưa có kỳ lương của mảng {ActiveSegmentName}.";
                PayrollMonthlyRecipientsEmptyText.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được báo cáo lương theo tháng: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private async void PayrollMonthlySummary_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PayrollMonthlySummaryDataGrid.SelectedItem is not PayrollMonthlySummaryRow summary) return;

        try
        {
            var payslips = await _apiClient.GetPayslipsAsync(summary.PeriodId);
            var recipients = payslips?.Items ?? Array.Empty<ApiClient.PayslipItem>();
            PayrollMonthlyRecipientsDataGrid.ItemsSource = recipients;
            PayrollMonthlyRecipientsEmptyText.Visibility = recipients.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            PayrollMonthlySummaryText.Text = $"Tháng {summary.MonthLabel}: Gross {summary.TotalGrossLabel} · Net {summary.TotalNetLabel} · {recipients.Count:N0} người nhận lương · Trạng thái {summary.StatusLabel}.";
        }
        catch (Exception ex)
        {
            PayrollMonthlyRecipientsDataGrid.ItemsSource = Array.Empty<ApiClient.PayslipItem>();
            PayrollMonthlyRecipientsEmptyText.Visibility = Visibility.Visible;
            PayrollMonthlySummaryText.Text = $"Không tải được danh sách người nhận: {ToFriendlyError(ex)}";
        }
    }

    private static string GetPayrollStatusLabel(int status)
        => status switch
        {
            0 => "Bản nháp",
            1 => "Đã tính",
            2 => "Chờ duyệt",
            3 => "Đã duyệt",
            4 => "Đã chi",
            5 => "Đã phát hành",
            6 => "Đã hủy",
            _ => "Chưa xác định"
        };

    private async Task LoadPayrollDepartmentFilterAsync()
    {
        if (PayrollDeptFilterCombo.Items.Count > 1)
        {
            return;
        }

        var departments = _cachedDepartments.Count > 0
            ? _cachedDepartments
            : await _apiClient.GetDepartmentsAsync();
        if (departments is null)
        {
            return;
        }

        PayrollDeptFilterCombo.Items.Clear();
        PayrollDeptFilterCombo.Items.Add(new ComboBoxItem
        {
            Content = "-- Tất cả phòng ban --",
            Tag = null,
            IsSelected = true
        });

        foreach (var department in departments)
        {
            PayrollDeptFilterCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{department.Code} - {department.Name}",
                Tag = department.Id
            });
        }
    }

    private async void PayrollPeriodSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isPayrollPeriodSelectorSyncing)
        {
            return;
        }

        if (PayrollPeriodSelectorCombo.SelectedValue is Guid periodId)
        {
            await LoadPayslipsForPeriodAsync(periodId);
        }
    }

    private void PayrollSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        PayrollSearchPlaceholder.Visibility = string.IsNullOrEmpty(PayrollSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!_isUiReady)
        {
            return;
        }

        _ = DebounceSearchAsync(
            SegmentViewKey("payroll-search"),
            "Đang tìm phiếu lương...",
            ReloadSelectedPayrollAsync);
    }

    private void PayrollDeptFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady)
        {
            return;
        }

        _ = DebounceSearchAsync(
            SegmentViewKey("payroll-filter"),
            "Đang lọc phiếu lương...",
            ReloadSelectedPayrollAsync);
    }

    private async Task ReloadSelectedPayrollAsync()
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is Guid periodId)
        {
            await LoadPayslipsForPeriodAsync(periodId);
        }
    }

    private async void PayslipsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(PayslipsDataGrid, source) is not DataGridRow ||
            PayslipsDataGrid.SelectedItem is not ApiClient.PayslipItem payslip)
        {
            return;
        }

        await ShowPayrollAdjustmentDialogAsync(payslip, returnToAdjustmentTab: false);
    }

    private async Task ShowPayrollAdjustmentDialogAsync(ApiClient.PayslipItem payslip, bool returnToAdjustmentTab)
    {
        var period = _cachedPayrollPeriods.FirstOrDefault(p => p.Id == payslip.PayrollPeriodId);
        if (period is null)
        {
            ShowToast("Không xác định được kỳ lương của phiếu đã chọn.", true);
            return;
        }

        var adjustments = await _apiClient.GetPayrollAdjustmentsAsync(payslip.PayrollPeriodId);
        if (adjustments is null)
        {
            ShowToast("Không tải được lịch sử điều chỉnh lương.", true);
            return;
        }

        if (!PayrollPayslipDialog.TryShow(
                this,
                payslip,
                period.Status,
                adjustments,
                out var newAdjustment,
                out var adjustmentToDelete))
        {
            return;
        }

        if (adjustmentToDelete.HasValue)
        {
            if (!await _apiClient.DeletePayrollAdjustmentAsync(adjustmentToDelete.Value))
            {
                ShowToast("Không xóa được khoản điều chỉnh. Kỳ lương có thể đã bị khóa.", true);
                return;
            }
        }
        else if (newAdjustment is not null)
        {
            var created = await _apiClient.AddPayrollAdjustmentAsync(
                payslip.PayrollPeriodId,
                new ApiClient.CreatePayrollAdjustmentModel(
                    payslip.EmployeeId,
                    newAdjustment.Type,
                    newAdjustment.Amount,
                    newAdjustment.Reason));
            if (created is null)
            {
                ShowToast("Không ghi được khoản điều chỉnh. Hãy kiểm tra quyền hoặc trạng thái kỳ lương.", true);
                return;
            }
        }

        var recalculated = await _apiClient.CalculatePayrollAsync(payslip.PayrollPeriodId);
        if (recalculated is null)
        {
            ShowToast("Khoản điều chỉnh đã lưu nhưng chưa tính lại được bảng lương.", true);
            return;
        }

        await RefreshPayrollAfterChangeAsync(
            payslip.PayrollPeriodId,
            payslip.EmployeeId,
            returnToAdjustmentTab ? 1 : 0);
        ShowToast(adjustmentToDelete.HasValue
            ? "Đã xóa khoản điều chỉnh; Gross và Net đã được cập nhật ngay."
            : "Đã thêm khoản điều chỉnh; Gross và Net đã được cập nhật ngay.");
    }

    private async Task RefreshPayrollAfterChangeAsync(Guid periodId, Guid employeeId, int tabIndex)
    {
        await LoadPayrollPeriodsAsync(periodId);
        await LoadPayslipsForPeriodAsync(periodId, employeeId);
        PayrollFunctionTabs.SelectedIndex = tabIndex;
    }

    private async Task ReloadPayrollAdjustmentViewAsync()
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is not Guid periodId)
        {
            return;
        }

        var employeeId = PayrollAdjustmentEmployeesDataGrid.SelectedItem is ApiClient.PayslipItem selected
            ? selected.EmployeeId
            : (Guid?)null;
        await LoadPayslipsForPeriodAsync(periodId, employeeId);
    }

    private async Task ReloadPayrollReviewAsync()
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is Guid periodId)
        {
            var employeeId = PayrollAdjustmentEmployeesDataGrid.SelectedItem is ApiClient.PayslipItem selected
                ? selected.EmployeeId
                : (Guid?)null;
            await LoadPayrollAdjustmentsForPeriodAsync(periodId, employeeId);
        }
    }

    private void PayrollAdjustmentEmployees_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var payslip = PayrollAdjustmentEmployeesDataGrid.SelectedItem as ApiClient.PayslipItem;
        ShowPayrollAdjustmentsForEmployee(payslip);
    }

    private async void PayrollAdjustmentEmployees_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(PayrollAdjustmentEmployeesDataGrid, source) is not DataGridRow ||
            PayrollAdjustmentEmployeesDataGrid.SelectedItem is not ApiClient.PayslipItem payslip)
        {
            return;
        }

        await ShowPayrollAdjustmentDialogAsync(payslip, returnToAdjustmentTab: true);
    }

    private async void AddPayrollAdjustment_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollAdjustmentEmployeesDataGrid.SelectedItem is not ApiClient.PayslipItem payslip)
        {
            ShowToast("Hãy chọn nhân viên cần thêm thưởng, trợ cấp hoặc khấu trừ.", true);
            return;
        }

        await ShowPayrollAdjustmentDialogAsync(payslip, returnToAdjustmentTab: true);
    }

    private void PayrollAdjustments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var canEdit = GetSelectedPayrollPeriod()?.Status < 2;
        BtnDeletePayrollAdjustment.IsEnabled = canEdit && PayrollAdjustmentsDataGrid.SelectedItem is ApiClient.PayrollAdjustmentItem;
    }

    private async void DeleteSelectedPayrollAdjustment_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollAdjustmentsDataGrid.SelectedItem is not ApiClient.PayrollAdjustmentItem adjustment ||
            PayrollAdjustmentEmployeesDataGrid.SelectedItem is not ApiClient.PayslipItem payslip)
        {
            ShowToast("Hãy chọn khoản điều chỉnh cần xóa.", true);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Xóa khoản {adjustment.TypeNameVi} {adjustment.AmountEffectDisplay} của {payslip.EmployeeName}?\nBảng lương sẽ được tính lại ngay.",
            "Xác nhận xóa khoản điều chỉnh",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (!await _apiClient.DeletePayrollAdjustmentAsync(adjustment.Id))
        {
            ShowToast("Không xóa được khoản điều chỉnh. Kỳ lương có thể đã gửi duyệt.", true);
            return;
        }

        if (await _apiClient.CalculatePayrollAsync(payslip.PayrollPeriodId) is null)
        {
            ShowToast("Đã xóa khoản điều chỉnh nhưng chưa tính lại được bảng lương.", true);
            return;
        }

        await RefreshPayrollAfterChangeAsync(payslip.PayrollPeriodId, payslip.EmployeeId, 1);
        ShowToast("Đã xóa khoản điều chỉnh và cập nhật lại thực lĩnh.");
    }

    private async void EditPayrollBaseSalary_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollAdjustmentEmployeesDataGrid.SelectedItem is not ApiClient.PayslipItem payslip)
        {
            ShowToast("Hãy chọn nhân viên cần chỉnh mức lương.", true);
            return;
        }

        var period = GetSelectedPayrollPeriod();
        if (period is null || period.Status >= 2)
        {
            ShowToast("Kỳ lương đã gửi duyệt nên không thể chỉnh mức lương.", true);
            return;
        }

        var employee = await _apiClient.GetEmployeeAsync(payslip.EmployeeId);
        if (employee is null)
        {
            ShowToast("Không tải được hồ sơ nhân viên để chỉnh lương.", true);
            return;
        }

        var isPartTime = employee.EmploymentType == 1;
        var currentAmount = isPartTime ? employee.PartTimeUnitRate.GetValueOrDefault() : employee.BaseSalary;
        var amountLabel = isPartTime
            ? $"Đơn giá mới ({(employee.PartTimeCalculationMethod == 1 ? "đ/ca" : "đ/giờ")})"
            : "Lương cơ bản mới (đ/tháng)";
        if (!PromptDialog.TryShow(
                this,
                $"Chỉnh mức lương — {employee.FullName}",
                new[]
                {
                    new PromptField("amount", amountLabel, currentAmount.ToString("0", CultureInfo.InvariantCulture)),
                    new PromptField("reason", "Lý do thay đổi", null)
                },
                out var values))
        {
            return;
        }

        if (!TryParsePayrollMoney(values["amount"], out var newAmount) || newAmount < 0)
        {
            ShowToast("Mức lương phải là số không âm.", true);
            return;
        }

        var saved = await _apiClient.UpdateEmployeeAsync(
            employee.Id,
            new ApiClient.UpdateEmployeeModel(
                employee.FullName,
                employee.Email,
                employee.Phone,
                employee.Position,
                isPartTime ? employee.BaseSalary : newAmount,
                employee.DepartmentId,
                employee.BusinessUnitId,
                employee.JoinedDate,
                employee.Status,
                employee.EmploymentType,
                employee.PartTimeCalculationMethod,
                isPartTime ? newAmount : employee.PartTimeUnitRate,
                employee.CvUrlOrPath,
                employee.ProfessionalSummary,
                employee.Skills,
                employee.Experience));
        if (saved is null)
        {
            ShowToast("Không cập nhật được mức lương. Hãy kiểm tra quyền quản lý nhân sự.", true);
            return;
        }

        if (await _apiClient.CalculatePayrollAsync(payslip.PayrollPeriodId) is null)
        {
            ShowToast("Mức lương hồ sơ đã đổi nhưng kỳ hiện tại chưa tính lại được.", true);
            return;
        }

        await RefreshPayrollAfterChangeAsync(payslip.PayrollPeriodId, payslip.EmployeeId, 1);
        ShowToast($"Đã cập nhật mức lương của {employee.FullName} và tính lại phiếu lương.");
    }

    private static bool TryParsePayrollMoney(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out result)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result)
           || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private ApiClient.PayrollPeriodItem? GetSelectedPayrollPeriod()
        => PayrollPeriodSelectorCombo.SelectedValue is Guid periodId
            ? _cachedPayrollPeriods.FirstOrDefault(period => period.Id == periodId)
            : null;

    private string GetPayrollPayslipsEmptyMessage(ApiClient.PayrollPeriodItem? period)
    {
        if (period?.Status == 0)
        {
            return $"Kỳ lương {period.Name} đang ở Bản nháp và chưa có phiếu. " +
                   "Bấm “⚡ Tính lương” để sinh phiếu lương cho nhân sự của mảng " +
                   $"{ActiveSegmentName}.";
        }

        if (period?.Status == 6)
        {
            return "Kỳ lương này đã được hủy khi còn bản nháp. Hệ thống vẫn giữ nhật ký để tra cứu, nhưng không thể tính hay chỉnh sửa.";
        }

        if (period?.PayslipCount > 0)
        {
            return "Không có phiếu lương phù hợp với từ khóa hoặc phòng ban đang lọc.";
        }

        return $"Chưa có phiếu lương cho kỳ này. Hãy kiểm tra nhân sự đang làm việc thuộc mảng {ActiveSegmentName}, rồi bấm tính lại lương.";
    }

    private async Task LoadPayslipsForPeriodAsync(Guid periodId, Guid? preferredEmployeeId = null)
    {
        try
        {
            var period = _cachedPayrollPeriods.FirstOrDefault(p => p.Id == periodId);
            if (period != null)
            {
                UpdateWorkflowButtonsState(period.Status);
            }

            Guid? departmentId = null;
            if (PayrollDeptFilterCombo?.SelectedItem is ComboBoxItem selectedDepartment && selectedDepartment.Tag is Guid selectedDepartmentId)
            {
                departmentId = selectedDepartmentId;
            }

            var search = PayrollSearchBox?.Text?.Trim();
            var payslipsData = await _apiClient.GetPayslipsAsync(periodId, departmentId, search);

            if (payslipsData != null)
            {
                preferredEmployeeId ??= PayrollAdjustmentEmployeesDataGrid.SelectedItem is ApiClient.PayslipItem selectedEmployee
                    ? selectedEmployee.EmployeeId
                    : null;
                var payslips = payslipsData.Items.ToList();
                PayslipsDataGrid.ItemsSource = payslips;
                PayrollAdjustmentEmployeesDataGrid.ItemsSource = payslips;
                PayslipsEmptyText.Visibility = payslips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                PayslipsEmptyText.Text = payslips.Count == 0
                    ? GetPayrollPayslipsEmptyMessage(period)
                    : "Chưa có phiếu lương cho mảng hoặc kỳ lương đã chọn.";
                PayrollAdjustmentEmployeesEmptyText.Visibility = payslips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                MetricPayrollPayslipsCount.Text = payslipsData.TotalCount.ToString();

                var totalGross = payslips.Sum(ps => ps.GrossSalary);
                var totalNet = payslips.Sum(ps => ps.NetSalary);

                MetricPayrollTotalGross.Text = $"{totalGross:N0} đ";
                MetricPayrollTotalNet.Text = $"{totalNet:N0} đ";
                PayrollReviewPayslipCountText.Text = payslipsData.TotalCount.ToString("N0");
                PayrollReviewNetText.Text = $"{totalNet:N0} đ";

                var selectedPayslip = preferredEmployeeId.HasValue
                    ? payslips.FirstOrDefault(item => item.EmployeeId == preferredEmployeeId.Value)
                    : payslips.FirstOrDefault();
                PayrollAdjustmentEmployeesDataGrid.SelectedItem = selectedPayslip;
                if (selectedPayslip is not null)
                {
                    PayrollAdjustmentEmployeesDataGrid.ScrollIntoView(selectedPayslip);
                }

                await LoadPayrollAdjustmentsForPeriodAsync(periodId, selectedPayslip?.EmployeeId);
                SetLoadedStatus("Phiếu lương", payslips.Count);
            }
            else if (period != null)
            {
                PayslipsEmptyText.Visibility = Visibility.Collapsed;
                PayrollAdjustmentEmployeesDataGrid.ItemsSource = Array.Empty<object>();
                PayrollAdjustmentEmployeesEmptyText.Visibility = Visibility.Visible;
                MetricPayrollTotalGross.Text = $"{period.TotalGrossAmount:N0} đ";
                MetricPayrollTotalNet.Text = $"{period.TotalNetAmount:N0} đ";
                MetricPayrollPayslipsCount.Text = period.PayslipCount.ToString();
                PayrollReviewPayslipCountText.Text = period.PayslipCount.ToString("N0");
                PayrollReviewNetText.Text = $"{period.TotalNetAmount:N0} đ";
                SetViewStatus("Không tải được chi tiết phiếu lương. Đang hiển thị số liệu tóm tắt kỳ lương.", isError: true);
            }
            else
            {
                PayslipsDataGrid.ItemsSource = Array.Empty<object>();
                PayrollAdjustmentEmployeesDataGrid.ItemsSource = Array.Empty<object>();
                PayslipsEmptyText.Visibility = Visibility.Visible;
                PayrollAdjustmentEmployeesEmptyText.Visibility = Visibility.Visible;
                SetViewStatus("Chưa có phiếu lương cho kỳ đã chọn.");
            }
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được phiếu lương: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private async Task LoadPayrollAdjustmentsForPeriodAsync(Guid periodId, Guid? employeeId)
    {
        var adjustments = await _apiClient.GetPayrollAdjustmentsAsync(periodId);
        if (adjustments is null)
        {
            _cachedPayrollAdjustments = new List<ApiClient.PayrollAdjustmentItem>();
            PayrollAdjustmentsDataGrid.ItemsSource = Array.Empty<object>();
            PayrollReviewAdjustmentsDataGrid.ItemsSource = Array.Empty<object>();
            PayrollAdjustmentsEmptyText.Visibility = Visibility.Visible;
            PayrollReviewAdjustmentsEmptyText.Visibility = Visibility.Visible;
            PayrollReviewAdjustmentCountText.Text = "0";
            return;
        }

        _cachedPayrollAdjustments = adjustments.OrderByDescending(item => item.CreatedAt).ToList();
        PayrollReviewAdjustmentsDataGrid.ItemsSource = _cachedPayrollAdjustments;
        PayrollReviewAdjustmentsEmptyText.Visibility = _cachedPayrollAdjustments.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PayrollReviewAdjustmentCountText.Text = _cachedPayrollAdjustments.Count.ToString("N0");

        var selectedPayslip = employeeId.HasValue
            ? PayrollAdjustmentEmployeesDataGrid.Items.OfType<ApiClient.PayslipItem>()
                .FirstOrDefault(item => item.EmployeeId == employeeId.Value)
            : PayrollAdjustmentEmployeesDataGrid.SelectedItem as ApiClient.PayslipItem;
        ShowPayrollAdjustmentsForEmployee(selectedPayslip);
    }

    private void ShowPayrollAdjustmentsForEmployee(ApiClient.PayslipItem? payslip)
    {
        var period = GetSelectedPayrollPeriod();
        var canEdit = payslip is not null && period is not null && period.Status < 2;
        BtnEditPayrollBaseSalary.IsEnabled = canEdit;
        BtnAddPayrollAdjustment.IsEnabled = canEdit;

        if (payslip is null)
        {
            PayrollAdjustmentsDataGrid.ItemsSource = Array.Empty<object>();
            PayrollAdjustmentsEmptyText.Visibility = Visibility.Visible;
            PayrollAdjustmentSelectionText.Text = "Chọn một nhân viên để xem các khoản đã điều chỉnh.";
            BtnDeletePayrollAdjustment.IsEnabled = false;
            return;
        }

        var employeeAdjustments = _cachedPayrollAdjustments
            .Where(item => item.EmployeeId == payslip.EmployeeId)
            .OrderByDescending(item => item.CreatedAt)
            .ToList();
        PayrollAdjustmentsDataGrid.ItemsSource = employeeAdjustments;
        PayrollAdjustmentsEmptyText.Visibility = employeeAdjustments.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PayrollAdjustmentSelectionText.Text =
            $"{payslip.EmployeeCode} · {payslip.EmployeeName} — lương cơ bản {payslip.BaseSalary:N0} đ, Gross {payslip.GrossSalary:N0} đ, thực lĩnh {payslip.NetSalary:N0} đ. Có {employeeAdjustments.Count:N0} khoản điều chỉnh.";
        BtnDeletePayrollAdjustment.IsEnabled = canEdit && PayrollAdjustmentsDataGrid.SelectedItem is ApiClient.PayrollAdjustmentItem;
    }

    private void UpdateWorkflowButtonsState(int status)
    {
        string statusText = status switch
        {
            0 => "Bản nháp",
            1 => "Đã tính",
            2 => "Chờ duyệt",
            3 => "Đã duyệt",
            4 => "Đã chi",
            5 => "Đã phát hành",
            6 => "Đã hủy",
            _ => "Chưa xác định"
        };

        string bgHex = status switch
        {
            0 => "#F1F5F9",
            1 => "#EFF6FF",
            2 => "#FEF3C7",
            3 => "#DCFCE7",
            4 => "#E0E7FF",
            5 => "#F3E8FF",
            6 => "#FEE2E2",
            _ => "#F1F5F9"
        };

        string fgHex = status switch
        {
            0 => "#475569",
            1 => "#2563EB",
            2 => "#D97706",
            3 => "#16A34A",
            4 => "#4F46E5",
            5 => "#9333EA",
            6 => "#B91C1C",
            _ => "#475569"
        };

        PayrollStatusBadgeText.Text = statusText;
        PayrollStatusBadgeBorder.Background = new System.Windows.Media.BrushConverter().ConvertFromString(bgHex) as System.Windows.Media.Brush;
        PayrollStatusBadgeText.Foreground = new System.Windows.Media.BrushConverter().ConvertFromString(fgHex) as System.Windows.Media.Brush;

        BtnCalculatePayroll.Visibility = status == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnCalculatePayrollShortcut.Visibility = status == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnCancelPayroll.Visibility = status == 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnExportPayroll.Visibility = status is >= 1 and <= 5 ? Visibility.Visible : Visibility.Collapsed;
        BtnSubmitReview.Visibility = status == 1 ? Visibility.Visible : Visibility.Collapsed;
        BtnApprovePayroll.Visibility = status == 2 ? Visibility.Visible : Visibility.Collapsed;
        BtnMarkPaid.Visibility = status == 3 ? Visibility.Visible : Visibility.Collapsed;
        BtnPublishPayroll.Visibility = status == 4 ? Visibility.Visible : Visibility.Collapsed;

        BtnCalculatePayroll.IsEnabled = status == 0;
        BtnCalculatePayrollShortcut.IsEnabled = status == 0;
        BtnCancelPayroll.IsEnabled = status == 0;
        BtnExportPayroll.IsEnabled = status is >= 1 and <= 5;
        BtnSubmitReview.IsEnabled = status == 1;
        BtnApprovePayroll.IsEnabled = status == 2;
        BtnMarkPaid.IsEnabled = status == 3;
        BtnPublishPayroll.IsEnabled = status == 4;

        if (BtnApprovePayrollMain != null)
        {
            BtnApprovePayrollMain.Visibility = status == 2 ? Visibility.Visible : Visibility.Collapsed;
            BtnApprovePayrollMain.IsEnabled = status == 2;
        }
        if (BtnExportPayrollMain != null)
        {
            BtnExportPayrollMain.IsEnabled = status is >= 1 and <= 5;
        }
        var period = GetSelectedPayrollPeriod();
        if (period != null && PayrollPeriodHeaderTitle != null)
        {
            PayrollPeriodHeaderTitle.Text = $"Kỳ lương {period.Name} — Mảng {ActiveSegmentName}";
        }
        UpdatePayrollStepperVisual(status);

        var hasSelectedEmployee = PayrollAdjustmentEmployeesDataGrid.SelectedItem is ApiClient.PayslipItem;
        BtnEditPayrollBaseSalary.IsEnabled = status < 2 && hasSelectedEmployee;
        BtnAddPayrollAdjustment.IsEnabled = status < 2 && hasSelectedEmployee;
        BtnDeletePayrollAdjustment.IsEnabled = status < 2 && PayrollAdjustmentsDataGrid.SelectedItem is ApiClient.PayrollAdjustmentItem;

        PayrollReviewExplanation.Text = status switch
        {
            0 => "Bước 1: bấm Tính lương. Hệ thống sẽ tự mở Bảng tính lương để bạn kiểm tra từng nhân viên.",
            1 => "Bước 1: mở Bảng tính lương để kiểm tra từng phiếu. Bước 2: khi số liệu đúng, bấm Xác nhận gửi duyệt để khóa chỉnh sửa.",
            2 => "Kỳ lương đã gửi duyệt. Dữ liệu điều chỉnh đã khóa và đang chờ người có quyền phê duyệt.",
            3 => "Kỳ lương đã được duyệt; chuyển sang bước xác nhận chi lương.",
            4 => "Đã xác nhận chi lương; chuyển sang bước phát hành phiếu cho nhân viên.",
            5 => "Kỳ lương đã phát hành và chỉ còn chế độ xem.",
            6 => "Kỳ lương đã hủy khi còn bản nháp. Chỉ còn nhật ký tra cứu, không thể tính hoặc sửa.",
            _ => "Trạng thái kỳ lương chưa được xác định."
        };

        PayrollWorkflowExplanation.Text = status switch
        {
            0 => "Kỳ lương còn là bản nháp. Hãy hoàn tất tính lương và xem xét trước khi vào bước duyệt.",
            1 => "Kỳ lương đã tính nhưng chưa gửi duyệt. Hãy vào mục Xem xét & gửi duyệt trước.",
            2 => "Chờ duyệt: dữ liệu đã khóa tạm thời, chỉ người có quyền phê duyệt mới được tiếp tục.",
            3 => "Đã duyệt: kỳ lương đã chốt ngân sách, chỉ còn bước xác nhận Đã Chi Lương.",
            4 => "Đã chi: có thể Phát Hành phiếu lương cho nhân sự.",
            5 => "Đã phát hành: kỳ lương đã khóa, chỉ được xem và xuất báo cáo.",
            6 => "Đã hủy: kỳ lương nháp được giữ lại cùng nhật ký, không thể phục hồi hay thay đổi.",
            _ => "Trạng thái kỳ lương chưa được xác định."
        };
    }

    private void UpdatePayrollStepperVisual(int status)
    {
        if (PayrollStep1Border == null) return;

        var converter = new System.Windows.Media.BrushConverter();
        var greenBg = converter.ConvertFromString("#DCFCE7") as System.Windows.Media.Brush;
        var greenBorder = converter.ConvertFromString("#86EFAC") as System.Windows.Media.Brush;
        var greenFg = converter.ConvertFromString("#15803D") as System.Windows.Media.Brush;

        var tealBg = converter.ConvertFromString("#CCFBF1") as System.Windows.Media.Brush;
        var tealBorder = converter.ConvertFromString("#5EEAD4") as System.Windows.Media.Brush;
        var tealFg = converter.ConvertFromString("#0F766E") as System.Windows.Media.Brush;

        var whiteBg = System.Windows.Media.Brushes.White;
        var grayBorder = converter.ConvertFromString("#E2E8F0") as System.Windows.Media.Brush;
        var grayFg = converter.ConvertFromString("#64748B") as System.Windows.Media.Brush;

        void SetStep(Border border, TextBlock icon, bool isCompleted, bool isActive)
        {
            if (isCompleted)
            {
                border.Background = greenBg;
                border.BorderBrush = greenBorder;
                icon.Text = "✓ ";
                icon.Foreground = greenFg;
            }
            else if (isActive)
            {
                border.Background = tealBg;
                border.BorderBrush = tealBorder;
                icon.Text = "";
                icon.Foreground = tealFg;
            }
            else
            {
                border.Background = whiteBg;
                border.BorderBrush = grayBorder;
                icon.Text = "";
                icon.Foreground = grayFg;
            }
        }

        SetStep(PayrollStep1Border, PayrollStep1Icon, status >= 1, status == 0);
        SetStep(PayrollStep2Border, PayrollStep2Icon, status >= 2, status == 1);
        SetStep(PayrollStep3Border, PayrollStep3Icon, status > 2, status == 2);
        SetStep(PayrollStep4Border, PayrollStep4Icon, status > 3, status == 3);
        SetStep(PayrollStep5Border, PayrollStep5Icon, status >= 5, status == 4);
    }

    private async void ViewPayslipDetail_Click(object sender, RoutedEventArgs e)
    {
        if (PayslipsDataGrid.SelectedItem is ApiClient.PayslipItem payslip)
        {
            await ShowPayrollAdjustmentDialogAsync(payslip, returnToAdjustmentTab: false);
        }
        else
        {
            ShowToast("Vui lòng chọn một nhân sự trong bảng để xem phiếu chi tiết.", true);
        }
    }

    private async void CancelPayroll_Click(object sender, RoutedEventArgs e)
    {
        var period = GetSelectedPayrollPeriod();
        if (period is null || period.Status != 0)
        {
            ShowToast("Chỉ có thể hủy kỳ lương đang ở trạng thái Bản nháp.", true);
            return;
        }

        var confirmation = MessageBox.Show(
            $"Hủy kỳ lương nháp “{period.Name}”?\n\n" +
            "Kỳ lương sẽ không bị xóa. Hệ thống giữ lại nhật ký để tra cứu, nhưng bạn sẽ không thể tính lương trên kỳ này nữa.",
            "Xác nhận hủy kỳ lương nháp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var cancelled = await _apiClient.CancelPayrollPeriodAsync(period.Id, "Hủy kỳ lương nháp từ Desktop");
        if (cancelled is null)
        {
            ShowToast("Không hủy được kỳ lương. Kỳ có thể đã được tính hoặc bạn chưa có quyền tính lương.", true);
            return;
        }

        await LoadPayrollPeriodsAsync(period.Id);
        ShowToast("Đã hủy kỳ lương nháp và lưu nhật ký nghiệp vụ.");
    }

    private async void ExportPayrollExcel_Click(object sender, RoutedEventArgs e)
    {
        var period = GetSelectedPayrollPeriod();
        if (period is null || period.Status is 0 or 6)
        {
            ShowToast("Hãy chọn kỳ lương đã tính để xuất Excel.", true);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Xuất bảng lương Excel",
            FileName = $"bang-luong-{period.StartDate:yyyyMMdd}-{period.EndDate:yyyyMMdd}.xlsx",
            Filter = "Tệp Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var file = await _apiClient.DownloadPayrollPeriodXlsxAsync(period.Id);
            if (file is null)
            {
                ShowToast("Không xuất được Excel. Hãy kiểm tra kỳ đã tính và quyền xem bảng lương.", true);
                return;
            }

            File.WriteAllBytes(dialog.FileName, file.Content);
            ShowToast($"Đã xuất bảng lương ra {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            ShowToast($"Không thể xuất Excel: {ToFriendlyError(ex)}", true);
        }
    }

    private async void CalculatePayroll_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is not Guid periodId)
        {
            MessageBox.Show("Vui lòng chọn kỳ lương cần tính toán.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var result = await _apiClient.CalculatePayrollAsync(periodId);
            if (result != null)
            {
                var completedMessage = result.TotalEmployeesProcessed == 0
                    ? $"Chưa sinh được phiếu lương cho {result.PayrollPeriodName}.\n" +
                      $"Mảng {ActiveSegmentName} chưa có nhân sự đang làm việc thuộc đơn vị được chọn. " +
                      "Hãy kiểm tra hồ sơ nhân sự có trạng thái Đang làm việc và thuộc đúng mảng, rồi tính lại."
                    : $"Tính lương thành công cho {result.PayrollPeriodName}!\n" +
                      $"• Tổng số nhân sự xử lý: {result.TotalEmployeesProcessed}\n" +
                      $"• Tổng quỹ lương Gross: {result.TotalGrossAmount:N0} đ\n" +
                      $"• Tổng thực chi Net: {result.TotalNetAmount:N0} đ";
                MessageBox.Show(
                    completedMessage,
                    result.TotalEmployeesProcessed == 0 ? "Chưa có phiếu lương" : "Hoàn Tất Tính Lương",
                    MessageBoxButton.OK,
                    result.TotalEmployeesProcessed == 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                await LoadPayrollPeriodsAsync(periodId);
                PayrollFunctionTabs.SelectedIndex = 0;
            }
            else
            {
                MessageBox.Show("Tính lương thất bại. Vui lòng kiểm tra quyền Permissions.Payroll.Calculate.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi tính lương: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void SubmitReview_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is not Guid periodId) return;

        var period = GetSelectedPayrollPeriod();
        var confirmation = MessageBox.Show(
            $"Xác nhận gửi duyệt kỳ lương “{period?.Name ?? "đã chọn"}”?\n\n" +
            $"• Phiếu lương đã kiểm tra: {PayrollReviewPayslipCountText.Text}\n" +
            $"• Tổng thực lĩnh: {PayrollReviewNetText.Text}\n\n" +
            "Sau khi gửi duyệt, không thể thêm, sửa hoặc xóa các khoản điều chỉnh.",
            "Xác nhận gửi duyệt",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        var result = await _apiClient.SubmitPayrollForReviewAsync(periodId, "Gửi duyệt bảng lương từ Desktop");
        if (result != null)
        {
            MessageBox.Show("Đã gửi duyệt kỳ lương thành công!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadPayrollPeriodsAsync(periodId);
            PayrollFunctionTabs.SelectedIndex = 5;
        }
        else
        {
            MessageBox.Show("Gửi duyệt thất bại. Vui lòng kiểm tra trạng thái kỳ lương.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ApprovePayroll_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is not Guid periodId) return;

        var result = await _apiClient.ApprovePayrollAsync(periodId, "Phê duyệt bảng lương từ Desktop");
        if (result != null)
        {
            MessageBox.Show("Phê duyệt kỳ lương thành công!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadPayrollPeriodsAsync(periodId);
            PayrollFunctionTabs.SelectedIndex = 5;
        }
        else
        {
            MessageBox.Show("Phê duyệt thất bại. Vui lòng kiểm tra quyền Permissions.Payroll.Approve.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MarkPaid_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is not Guid periodId) return;

        var result = await _apiClient.MarkPayrollPaidAsync(periodId, "Xác nhận đã giải ngân chi lương");
        if (result != null)
        {
            MessageBox.Show("Đã xác nhận giải ngân chi lương thành công!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadPayrollPeriodsAsync(periodId);
            PayrollFunctionTabs.SelectedIndex = 5;
        }
        else
        {
            MessageBox.Show("Xác nhận chi lương thất bại.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void PublishPayroll_Click(object sender, RoutedEventArgs e)
    {
        if (PayrollPeriodSelectorCombo.SelectedValue is not Guid periodId) return;

        var result = await _apiClient.PublishPayrollAsync(periodId, "Phát hành phiếu lương cho nhân sự");
        if (result != null)
        {
            MessageBox.Show("Đã phát hành phiếu lương cho toàn bộ nhân sự! Nhân viên hiện đã có thể xem phiếu lương cá nhân.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadPayrollPeriodsAsync(periodId);
            PayrollFunctionTabs.SelectedIndex = 5;
        }
        else
        {
            MessageBox.Show("Phát hành thất bại. Vui lòng kiểm tra quyền Permissions.Payroll.Publish.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshPayroll_Click(object sender, RoutedEventArgs e)
    {
        _ = RunWithBusyAsync("Đang tải bảng tính lương...", () => LoadPayrollPeriodsAsync());
    }

    // ── Dữ liệu nội bộ tách mảng & bảng tổng tài chính ──

    private async Task LoadCompanyFinanceAsync()
    {
        var from = FinanceFromDatePicker.SelectedDate;
        var to = FinanceToDatePicker.SelectedDate?.Date.AddDays(1).AddTicks(-1);
        if (from.HasValue && to.HasValue && from > to)
        {
            ShowToast("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.", isError: true);
            return;
        }

        var overview = await _apiClient.GetCompanyFinancialOverviewAsync(from, to);
        if (overview is null)
        {
            CompanyFinanceAreasDataGrid.ItemsSource = Array.Empty<object>();
            CompanyFinanceEmptyText.Visibility = Visibility.Collapsed;
            SetViewStatus("Không tải được bảng tổng tài chính. Vui lòng kiểm tra quyền báo cáo.", isError: true);
            return;
        }

        CompanyFinanceAreasDataGrid.ItemsSource = overview.Areas;
        CompanyFinanceEmptyText.Visibility = overview.Areas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        MetricCompanyIncome.Text = $"{overview.CompanyTotal.TotalIncome:N0} đ";
        MetricCompanyExpense.Text = $"{overview.CompanyTotal.TotalExpense:N0} đ";
        MetricCompanyNetCash.Text = $"{overview.CompanyTotal.NetCashFlow:N0} đ";
        MetricCompanyProfit.Text = $"{overview.CompanyTotal.OperatingProfit:N0} đ";

        UnclassifiedFinanceWarning.Visibility = overview.HasUnclassifiedTransactions ? Visibility.Visible : Visibility.Collapsed;
        UnclassifiedFinanceWarningText.Text = overview.HasUnclassifiedTransactions
            ? $"Cần phân loại {overview.UnclassifiedCashFlow.TransactionCount:N0} giao dịch: thu {overview.UnclassifiedCashFlow.TotalIncome:N0} đ, chi {overview.UnclassifiedCashFlow.TotalExpense:N0} đ. {overview.CalculationNote}"
            : overview.CalculationNote;
        SetViewStatus($"Đã tổng hợp tài chính từ {overview.From:dd/MM/yyyy} đến {overview.To:dd/MM/yyyy}");
    }

    private void RefreshCompanyFinance_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tổng hợp tài chính hai mảng...", LoadCompanyFinanceAsync);

    private async Task LoadInternalDataAsync()
    {
        var search = InternalDataSearchBox.Text.Trim();
        if (_internalDataMode == InternalDataViewMode.Customers)
        {
            var data = await _apiClient.GetInternalCustomersAsync(_activeSegment, search);
            if (data is null)
            {
                InternalCustomersDataGrid.ItemsSource = Array.Empty<object>();
                InternalCustomersEmptyText.Visibility = Visibility.Collapsed;
                SetViewStatus("Không tải được danh mục khách hàng.", isError: true);
                return;
            }

            InternalCustomersDataGrid.ItemsSource = data.Items;
            InternalCustomersEmptyText.Visibility = data.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetLoadedStatus($"Khách hàng {ActiveSegmentName}", data.Items.Count);
            return;
        }

        var resources = await _apiClient.GetInternalResourcesAsync(_activeSegment, search);
        if (resources is null)
        {
            InternalResourcesDataGrid.ItemsSource = Array.Empty<object>();
            InternalResourcesEmptyText.Visibility = Visibility.Collapsed;
            SetViewStatus("Không tải được kho thông tin nội bộ.", isError: true);
            return;
        }

        InternalResourcesDataGrid.ItemsSource = resources.Items;
        InternalResourcesEmptyText.Visibility = resources.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SetLoadedStatus(_activeSegment == FashionSegment ? "Kế hoạch và mẫu thiết kế" : "Đề và tài liệu", resources.Items.Count);
    }

    private void InternalDataSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUiReady && ViewInternalDataContainer.Visibility == Visibility.Visible) _ = LoadInternalDataAsync();
    }

    private void RefreshInternalData_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tải dữ liệu nội bộ...", LoadInternalDataAsync);

    private async void CreateInternalData_Click(object sender, RoutedEventArgs e)
    {
        if (_internalDataMode == InternalDataViewMode.Customers)
            await ShowCustomerDialogAsync();
        else
            await ShowResourceDialogAsync();
    }

    private async void EditInternalData_Click(object sender, RoutedEventArgs e)
    {
        if (_internalDataMode == InternalDataViewMode.Customers)
        {
            if (InternalCustomersDataGrid.SelectedItem is not ApiClient.InternalCustomerItem item)
            {
                ShowToast("Hãy chọn khách hàng cần sửa.");
                return;
            }
            await ShowCustomerDialogAsync(item);
        }
        else
        {
            if (InternalResourcesDataGrid.SelectedItem is not ApiClient.InternalResourceItem item)
            {
                ShowToast("Hãy chọn tài liệu cần sửa.");
                return;
            }
            await ShowResourceDialogAsync(item);
        }
    }

    private async void DeleteInternalData_Click(object sender, RoutedEventArgs e)
    {
        if (_internalDataMode == InternalDataViewMode.Customers)
        {
            if (InternalCustomersDataGrid.SelectedItem is not ApiClient.InternalCustomerItem item)
            {
                ShowToast("Hãy chọn khách hàng cần xóa.");
                return;
            }
            if (MessageBox.Show(this, $"Xóa khách hàng “{item.Name}” khỏi mảng {ActiveSegmentName}?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!await _apiClient.DeleteInternalCustomerAsync(_activeSegment, item.Id))
            {
                ShowToast("Xóa khách hàng thất bại. Vui lòng kiểm tra quyền quản lý.", true);
                return;
            }
        }
        else
        {
            if (InternalResourcesDataGrid.SelectedItem is not ApiClient.InternalResourceItem item)
            {
                ShowToast("Hãy chọn tài liệu cần xóa.");
                return;
            }
            if (MessageBox.Show(this, $"Xóa metadata “{item.Title}”? Tệp bên ngoài cơ sở dữ liệu sẽ không bị xóa.", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (!await _apiClient.DeleteInternalResourceAsync(_activeSegment, item.Id))
            {
                ShowToast("Xóa thông tin tài liệu thất bại. Vui lòng kiểm tra quyền quản lý.", true);
                return;
            }
        }

        ShowToast("Đã xóa dữ liệu thành công.");
        await LoadInternalDataAsync();
    }

    private async Task ShowCustomerDialogAsync(ApiClient.InternalCustomerItem? item = null)
    {
        var fields = new[]
        {
            new PromptField("code", "Mã khách hàng", item?.Code),
            new PromptField("name", "Tên khách hàng", item?.Name),
            new PromptField("contact", "Người liên hệ", item?.ContactPerson, false),
            new PromptField("phone", "Số điện thoại", item?.Phone, false),
            new PromptField("email", "Email", item?.Email, false),
            new PromptField("address", "Địa chỉ", item?.Address, false),
            new PromptField("source", "Nguồn khách hàng", item?.Source, false),
            new PromptField("status", "Trạng thái", item?.Status ?? "ACTIVE", true, new[] { new PromptOption("LEAD", "Tiềm năng"), new PromptOption("ACTIVE", "Đang hoạt động"), new PromptOption("INACTIVE", "Ngừng hoạt động"), new PromptOption("ARCHIVED", "Đã lưu trữ") }),
            new PromptField("notes", "Ghi chú", item?.Notes, false)
        };
        if (!PromptDialog.TryShow(this, item is null ? $"Thêm khách hàng · {ActiveSegmentName}" : "Sửa khách hàng", fields, out var values)) return;
        var model = new ApiClient.InternalCustomerModel(values["code"], values["name"], Null(values["contact"]), Null(values["email"]), Null(values["phone"]), Null(values["address"]), Null(values["source"]), values["status"], Null(values["notes"]));
        var saved = item is null
            ? await _apiClient.CreateInternalCustomerAsync(_activeSegment, model)
            : await _apiClient.UpdateInternalCustomerAsync(_activeSegment, item.Id, model);
        if (saved is null) { ShowToast("Không lưu được khách hàng. Vui lòng kiểm tra dữ liệu và quyền quản lý.", true); return; }
        ShowToast(item is null ? "Đã thêm khách hàng." : "Đã cập nhật khách hàng.");
        await LoadInternalDataAsync();
    }

    private async Task ShowResourceDialogAsync(ApiClient.InternalResourceItem? item = null)
    {
        var typeOptions = _activeSegment == FashionSegment
            ? new[] { new PromptOption("PLAN", "Bản kế hoạch"), new PromptOption("DESIGN_SAMPLE", "Mẫu thiết kế") }
            : new[] { new PromptOption("EXAM", "Đề thi / đề bài"), new PromptOption("DOCUMENT", "Tài liệu") };
        var fields = new[]
        {
            new PromptField("code", "Mã tài liệu", item?.Code),
            new PromptField("title", "Tên tài liệu / mẫu", item?.Title),
            new PromptField("type", "Loại", item?.ResourceType ?? typeOptions[0].Value, true, typeOptions),
            new PromptField("uri", "Đường dẫn lưu trữ hoặc liên kết", item?.StorageUri),
            new PromptField("file", "Tên tệp", item?.FileName, false),
            new PromptField("version", "Phiên bản", item?.Version ?? "1.0"),
            new PromptField("tags", "Thẻ (phân cách bằng dấu phẩy)", item?.TagsDisplay, false),
            new PromptField("status", "Trạng thái", item?.Status ?? "DRAFT", true, new[] { new PromptOption("DRAFT", "Bản nháp"), new PromptOption("ACTIVE", "Đang sử dụng"), new PromptOption("ARCHIVED", "Đã lưu trữ") }),
            new PromptField("notes", "Ghi chú", item?.Notes, false)
        };
        if (!PromptDialog.TryShow(this, item is null ? $"Thêm thông tin · {ActiveSegmentName}" : "Sửa thông tin tài liệu", fields, out var values)) return;
        var tags = values["tags"].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var model = new ApiClient.InternalResourceModel(values["code"], values["title"], values["type"], values["uri"], Null(values["file"]), null, null, null, values["version"], values["status"], tags, null, Null(values["notes"]));
        var saved = item is null
            ? await _apiClient.CreateInternalResourceAsync(_activeSegment, model)
            : await _apiClient.UpdateInternalResourceAsync(_activeSegment, item.Id, model);
        if (saved is null) { ShowToast("Không lưu được thông tin tài liệu. Vui lòng kiểm tra dữ liệu và quyền quản lý.", true); return; }
        ShowToast(item is null ? "Đã thêm thông tin tài liệu." : "Đã cập nhật thông tin tài liệu.");
        await LoadInternalDataAsync();
    }

    private ApiClient.EmployeeItem? GetSelectedEmployee()
    {
        if (EmployeesTabs == null) return EmployeesWorkingDataGrid?.SelectedItem as ApiClient.EmployeeItem;
        return EmployeesTabs.SelectedIndex switch
        {
            1 => EmployeesProfilesDataGrid?.SelectedItem as ApiClient.EmployeeItem,
            2 => EmployeesMissingCvDataGrid?.SelectedItem as ApiClient.EmployeeItem,
            3 => EmployeesResignedDataGrid?.SelectedItem as ApiClient.EmployeeItem,
            4 => EmployeesBlacklistDataGrid?.SelectedItem as ApiClient.EmployeeItem,
            _ => EmployeesWorkingDataGrid?.SelectedItem as ApiClient.EmployeeItem
        };
    }

    private void EmployeesTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || e.Source != sender || sender is not TabControl tabs)
            return;

        if (!_isEmployeeFunctionSelectionSyncing)
        {
            _isEmployeeFunctionSelectionSyncing = true;
            try
            {
                var isFashion = _activeSegment == FashionSegment;
                var children = isFashion
                    ? new[] { NavFashionEmployeesWorking, NavFashionEmployeesProfiles, NavFashionEmployeesMissingCv, NavFashionEmployeesResigned, NavFashionEmployeesBlacklist }
                    : new[] { NavTechEmployeesWorking, NavTechEmployeesProfiles, NavTechEmployeesMissingCv, NavTechEmployeesResigned, NavTechEmployeesBlacklist };
                for (var i = 0; i < children.Length; i++)
                {
                    children[i].IsChecked = (i == tabs.SelectedIndex);
                }
                if (!_isSidebarCollapsed && (isFashion ? FashionNavigationGroup.Visibility : TechnologyNavigationGroup.Visibility) == Visibility.Visible)
                {
                    SetEmployeesSubmenuVisibility(isFashion, true);
                }
            }
            finally
            {
                _isEmployeeFunctionSelectionSyncing = false;
            }
        }
    }

    private async void CreateEmployee_Click(object sender, RoutedEventArgs e) => await ShowEmployeeDialogAsync();

    private async void EditEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null) { ShowToast("Hãy chọn một nhân sự cần sửa hồ sơ."); return; }
        await ShowEmployeeDialogAsync(employee);
    }

    private async Task ShowEmployeeDialogAsync(ApiClient.EmployeeItem? employee = null)
    {
        var units = GetSegmentBusinessUnitOptions(employee);
        if (units.Count == 0)
        {
            ShowToast($"Tài khoản chưa được cấp đơn vị kinh doanh thuộc mảng {ActiveSegmentName}.", true);
            return;
        }
        var departments = new List<EmployeeDialogOption> { new(null, "Không chọn phòng ban") };
        departments.AddRange(_cachedDepartments.Select(d => new EmployeeDialogOption(d.Id, $"{d.Code} - {d.Name}")));
        if (!EmployeeDialog.TryShow(this, ActiveSegmentName, units, departments, out var value, employee) || value is null) return;

        await RunWithBusyAsync(employee is null ? "Đang tạo hồ sơ nhân sự..." : "Đang cập nhật hồ sơ nhân sự...", async () =>
        {
            ApiClient.EmployeeItem? saved;
            if (employee is null)
            {
                var model = new ApiClient.CreateEmployeeModel(value.EmployeeCode, value.FullName, value.Email, value.Phone, value.Position, value.BaseSalary, value.DepartmentId, value.BusinessUnitId, value.JoinedDate, value.Status, value.EmploymentType, value.PartTimeCalculationMethod, value.PartTimeUnitRate, value.CvUrlOrPath, value.ProfessionalSummary, value.Skills, value.Experience);
                saved = await _apiClient.CreateEmployeeAsync(model);
            }
            else
            {
                var model = new ApiClient.UpdateEmployeeModel(value.FullName, value.Email, value.Phone, value.Position, value.BaseSalary, value.DepartmentId, value.BusinessUnitId, value.JoinedDate, value.Status, value.EmploymentType, value.PartTimeCalculationMethod, value.PartTimeUnitRate, value.CvUrlOrPath, value.ProfessionalSummary, value.Skills, value.Experience);
                saved = await _apiClient.UpdateEmployeeAsync(employee.Id, model);
            }
            if (saved is null) { ShowToast("Không lưu được hồ sơ nhân sự. Vui lòng kiểm tra dữ liệu và quyền quản lý.", true); return; }
            ShowToast(employee is null ? "Đã tạo hồ sơ nhân sự." : "Đã cập nhật hồ sơ nhân sự.");
            await LoadEmployeesAsync();
        });
    }

    private List<EmployeeDialogOption> GetSegmentBusinessUnitOptions(ApiClient.EmployeeItem? employee)
    {
        var fromSession = _currentUser?.AccessibleBusinessUnits ?? Array.Empty<ApiClient.BusinessUnitItem>();
        var options = fromSession
            .Where(x => _activeSegment == FashionSegment
                ? string.Equals(x.Code, "FASHION", StringComparison.OrdinalIgnoreCase)
                : IsTechnologyBusinessUnit(x.Code))
            .Select(x => new EmployeeDialogOption(x.Id, $"{x.Code} - {x.Name}"))
            .ToList();
        if (options.Count == 0)
        {
            options.AddRange(_cachedBusinessUnits
                .Where(x => x.BusinessUnitId.HasValue && (_activeSegment == FashionSegment
                    ? string.Equals(x.BusinessUnitCode, "FASHION", StringComparison.OrdinalIgnoreCase)
                    : IsTechnologyBusinessUnit(x.BusinessUnitCode)))
                .Select(x => new EmployeeDialogOption(x.BusinessUnitId, $"{x.BusinessUnitCode} - {x.BusinessUnitName}")));
        }
        if (employee?.BusinessUnitId is Guid id && options.All(x => x.Id != id)) options.Insert(0, new EmployeeDialogOption(id, employee.BusinessUnitName ?? "Đơn vị hiện tại"));
        return options;
    }

    private static bool IsTechnologyBusinessUnit(string code)
        => code.Equals("EDTECH", StringComparison.OrdinalIgnoreCase)
           || code.Equals("CSCA", StringComparison.OrdinalIgnoreCase)
           || code.Equals("INTERVIEW", StringComparison.OrdinalIgnoreCase);

    private async void DeleteEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null) { ShowToast("Hãy chọn nhân sự cần xóa."); return; }
        if (MessageBox.Show(this, $"Xóa hồ sơ nhân sự “{employee.FullName}”?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await RunWithBusyAsync("Đang xóa hồ sơ nhân sự...", async () =>
        {
            if (!await _apiClient.DeleteEmployeeAsync(employee.Id)) { ShowToast("Xóa nhân sự thất bại. Vui lòng kiểm tra ràng buộc dữ liệu và quyền quản lý.", true); return; }
            ShowToast("Đã xóa hồ sơ nhân sự.");
            await LoadEmployeesAsync();
        });
    }

    private void OpenEmployeeCv_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null || string.IsNullOrWhiteSpace(employee.CvUrlOrPath)) { ShowToast("Nhân sự đã chọn chưa có CV."); return; }
        try { Process.Start(new ProcessStartInfo(employee.CvUrlOrPath) { UseShellExecute = true }); }
        catch { ShowToast("Không mở được CV. Hãy kiểm tra lại đường dẫn hoặc liên kết.", true); }
    }

    private async void ResignEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần chuyển trạng thái nghỉ việc.");
            return;
        }

        var fields = new[]
        {
            new PromptField("date", "Ngày nghỉ việc (dd/MM/yyyy)", DateTime.Today.ToString("dd/MM/yyyy")),
            new PromptField("reason", "Lý do nghỉ việc", employee.StatusReason ?? "Nghỉ việc theo nguyện vọng cá nhân")
        };

        if (!PromptDialog.TryShow(this, $"Chuyển nghỉ việc — {employee.FullName} ({employee.EmployeeCode})", fields, out var values))
            return;

        DateTime resignDate = DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            resignDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Resigned",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            resignDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật trạng thái nghỉ việc. Vui lòng kiểm tra quyền quản trị.", true);
            return;
        }

        ShowToast($"Đã chuyển nhân sự '{employee.FullName}' sang trạng thái Đã nghỉ việc.");
        await LoadEmployeesAsync();
    }

    private async void ReactivateEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần khôi phục lại công tác.");
            return;
        }

        if (MessageBox.Show(this, $"Khôi phục nhân sự '{employee.FullName}' ({employee.EmployeeCode}) trở lại làm việc?", "Xác nhận khôi phục", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Active",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            DateTime.UtcNow,
            "Khôi phục công tác làm việc");

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể khôi phục nhân sự. Vui lòng kiểm tra quyền quản trị.", true);
            return;
        }

        ShowToast($"Đã khôi phục nhân sự '{employee.FullName}' trở lại làm việc.");
        await LoadEmployeesAsync();
    }

    private async void BlacklistEmployee_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần đưa vào danh sách Blacklist.");
            return;
        }

        var fields = new[]
        {
            new PromptField("date", "Ngày ghi nhận vi phạm (dd/MM/yyyy)", DateTime.Today.ToString("dd/MM/yyyy")),
            new PromptField("reason", "Lý do đưa vào Blacklist / Vi phạm kỷ luật", employee.StatusReason ?? "Vi phạm quy chế doanh nghiệp hoặc không tiếp nhận lại")
        };

        if (!PromptDialog.TryShow(this, $"Đưa vào Blacklist — {employee.FullName} ({employee.EmployeeCode})", fields, out var values))
            return;

        DateTime recordDate = DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            recordDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Blacklisted",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            recordDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật danh sách Blacklist. Vui lòng kiểm tra quyền quản trị.", true);
            return;
        }

        ShowToast($"Đã chuyển nhân sự '{employee.FullName}' vào danh sách Blacklist.");
        await LoadEmployeesAsync();
    }

    private async void UpdateResignedReason_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự đã nghỉ việc cần sửa lý do.");
            return;
        }

        var initialDate = employee.StatusChangedAt.HasValue
            ? employee.StatusChangedAt.Value.ToString("dd/MM/yyyy")
            : DateTime.Today.ToString("dd/MM/yyyy");

        var fields = new[]
        {
            new PromptField("date", "Ngày nghỉ việc (dd/MM/yyyy)", initialDate),
            new PromptField("reason", "Lý do nghỉ việc", employee.StatusReason ?? string.Empty)
        };

        if (!PromptDialog.TryShow(this, $"Sửa lý do nghỉ — {employee.FullName}", fields, out var values))
            return;

        DateTime resignDate = employee.StatusChangedAt ?? DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            resignDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Resigned",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            resignDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật thông tin nghỉ việc.", true);
            return;
        }

        ShowToast("Đã cập nhật ngày và lý do nghỉ việc.");
        await LoadEmployeesAsync();
    }

    private async void UpdateBlacklistReason_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự trong Blacklist cần cập nhật lý do.");
            return;
        }

        var initialDate = employee.StatusChangedAt.HasValue
            ? employee.StatusChangedAt.Value.ToString("dd/MM/yyyy")
            : DateTime.Today.ToString("dd/MM/yyyy");

        var fields = new[]
        {
            new PromptField("date", "Ngày ghi nhận (dd/MM/yyyy)", initialDate),
            new PromptField("reason", "Lý do Blacklist", employee.StatusReason ?? string.Empty)
        };

        if (!PromptDialog.TryShow(this, $"Cập nhật lý do Blacklist — {employee.FullName}", fields, out var values))
            return;

        DateTime recordDate = employee.StatusChangedAt ?? DateTime.UtcNow;
        if (DateTime.TryParseExact(values["date"], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            recordDate = DateTime.SpecifyKind(parsedDate, DateTimeKind.Utc);
        }

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            "Blacklisted",
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            employee.CvUrlOrPath,
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            recordDate,
            values["reason"]);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật thông tin Blacklist.", true);
            return;
        }

        ShowToast("Đã cập nhật lý do Blacklist.");
        await LoadEmployeesAsync();
    }

    private async void QuickAssignCvFile_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần gán tệp CV.");
            return;
        }

        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Chọn tệp CV cho {employee.FullName}",
            Filter = "Tệp CV (*.pdf;*.doc;*.docx)|*.pdf;*.doc;*.docx|Tất cả tệp (*.*)|*.*"
        };

        if (picker.ShowDialog(this) == true)
        {
            var updateModel = new ApiClient.UpdateEmployeeModel(
                employee.FullName,
                employee.Email,
                employee.Phone,
                employee.Position,
                employee.BaseSalary,
                employee.DepartmentId,
                employee.BusinessUnitId,
                employee.JoinedDate,
                employee.Status,
                employee.EmploymentType,
                employee.PartTimeCalculationMethod,
                employee.PartTimeUnitRate,
                picker.FileName,
                employee.ProfessionalSummary,
                employee.Skills,
                employee.Experience,
                employee.StatusChangedAt,
                employee.StatusReason);

            var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
            if (updated is null)
            {
                ShowToast("Không thể gán tệp CV cho nhân sự.", true);
                return;
            }

            ShowToast($"Đã gán tệp CV thành công cho '{employee.FullName}'.");
            await LoadEmployeesAsync();
        }
    }

    private async void QuickAssignCvUrl_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần nhập liên kết CV.");
            return;
        }

        var fields = new[]
        {
            new PromptField("cvUrl", "Đường dẫn hoặc liên kết CV", employee.CvUrlOrPath)
        };

        if (!PromptDialog.TryShow(this, $"Nhập liên kết CV — {employee.FullName}", fields, out var values))
            return;

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            employee.Status,
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            values["cvUrl"],
            employee.ProfessionalSummary,
            employee.Skills,
            employee.Experience,
            employee.StatusChangedAt,
            employee.StatusReason);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật liên kết CV.", true);
            return;
        }

        ShowToast($"Đã cập nhật liên kết CV cho '{employee.FullName}'.");
        await LoadEmployeesAsync();
    }

    private async void QuickUpdateProfile_Click(object sender, RoutedEventArgs e)
    {
        var employee = GetSelectedEmployee();
        if (employee is null)
        {
            ShowToast("Hãy chọn nhân sự cần cập nhật hồ sơ chuyên môn.");
            return;
        }

        var fields = new[]
        {
            new PromptField("cv", "Đường dẫn / liên kết CV", employee.CvUrlOrPath, false),
            new PromptField("summary", "Tóm tắt chuyên môn", employee.ProfessionalSummary, false),
            new PromptField("skills", "Kỹ năng chính", employee.Skills, false),
            new PromptField("experience", "Kinh nghiệm làm việc", employee.Experience, false)
        };

        if (!PromptDialog.TryShow(this, $"Cập nhật hồ sơ chuyên môn — {employee.FullName}", fields, out var values))
            return;

        var updateModel = new ApiClient.UpdateEmployeeModel(
            employee.FullName,
            employee.Email,
            employee.Phone,
            employee.Position,
            employee.BaseSalary,
            employee.DepartmentId,
            employee.BusinessUnitId,
            employee.JoinedDate,
            employee.Status,
            employee.EmploymentType,
            employee.PartTimeCalculationMethod,
            employee.PartTimeUnitRate,
            Null(values["cv"]),
            Null(values["summary"]),
            Null(values["skills"]),
            Null(values["experience"]),
            employee.StatusChangedAt,
            employee.StatusReason);

        var updated = await _apiClient.UpdateEmployeeAsync(employee.Id, updateModel);
        if (updated is null)
        {
            ShowToast("Không thể cập nhật hồ sơ chuyên môn.", true);
            return;
        }

        ShowToast($"Đã cập nhật hồ sơ cho '{employee.FullName}'.");
        await LoadEmployeesAsync();
    }

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ── Fashion & Inventory Actions (Ngày 14) ──

    private async Task LoadFashionDataAsync()
    {
        await LoadFashionProductsAsync();
    }

    private async void FashionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || e.Source != sender || sender is not TabControl tabs)
            return;

        if (!_isFashionFunctionSelectionSyncing)
        {
            _isFashionFunctionSelectionSyncing = true;
            try
            {
                var children = new[] { NavFashionProducts, NavFashionVariants, NavFashionSuppliers, NavFashionReceipts, NavFashionBalances, NavFashionMovements, NavFashionManufacturing, NavFashionWarehouses, NavFashionOrders };
                for (var i = 0; i < children.Length; i++)
                {
                    children[i].IsChecked = (i == tabs.SelectedIndex);
                }
                if (!_isSidebarCollapsed && FashionNavigationGroup.Visibility == Visibility.Visible)
                {
                    SetFashionSubmenuVisibility(true);
                }
            }
            finally
            {
                _isFashionFunctionSelectionSyncing = false;
            }
        }

        switch (tabs.SelectedIndex)
        {
            case 0:
                _ = LoadViewOnceAsync("fashion-products", "Đang tải danh mục sản phẩm thời trang...", LoadFashionDataAsync);
                break;
            case 1:
                if (FashionVariantProductSelector.SelectedItem is ApiClient.FashionProductItem product)
                    await RunWithBusyAsync("Đang tải biến thể SKU...", () => LoadFashionVariantsAsync(product));
                break;
            case 2:
                await LoadViewOnceAsync("fashion-suppliers", "Đang tải nhà cung cấp...", () => LoadSuppliersAsync());
                break;
            case 3:
                await LoadViewOnceAsync("fashion-receipts", "Đang tải phiếu nhập kho...", () => LoadPurchaseReceiptsAsync());
                break;
            case 4:
                await LoadViewOnceAsync("fashion-balances", "Đang tải tồn kho...", async () =>
                {
                    await LoadInventoryWarehouseSelectorAsync();
                    await LoadInventoryBalancesAsync();
                });
                break;
            case 5:
                await LoadViewOnceAsync("fashion-movements", "Đang tải nhật ký giao dịch kho...", () => LoadInventoryMovementsAsync());
                break;
            case 6:
                await LoadViewOnceAsync("fashion-manufacturing", "Đang tải NVL, BOM và lệnh sản xuất...", LoadManufacturingDataAsync);
                break;
            case 7:
                await LoadViewOnceAsync("fashion-warehouses", "Đang tải danh mục kho...", LoadWarehousesAsync);
                break;
            case 8:
                await LoadViewOnceAsync("fashion-orders", "Đang tải đơn hàng...", () => LoadSalesOrdersAsync());
                break;
        }
    }

    private async Task LoadFashionProductsAsync(string? search = null)
    {
        var data = await _apiClient.GetFashionProductsAsync(search);
        if (data is null)
        {
            FashionProductsDataGrid.ItemsSource = Array.Empty<object>();
            FashionVariantProductSelector.ItemsSource = null;
            FashionVariantsDataGrid.ItemsSource = Array.Empty<object>();
            FashionVariantContextText.Text = "Chưa có sản phẩm để chọn.";
            SetViewStatus("Không tải được sản phẩm thời trang. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            FashionProductsDataGrid.ItemsSource = data.Items;
            FashionVariantProductSelector.ItemsSource = data.Items;
            MetricFashionProductsCount.Text = data.TotalCount.ToString("N0");
            var totalVariants = data.Items.Sum(p => p.VariantCount);
            MetricFashionVariantsCount.Text = totalVariants.ToString("N0");

            if (data.Items.Count > 0 && FashionProductsDataGrid.SelectedItem == null)
            {
                FashionProductsDataGrid.SelectedIndex = 0;
            }

            if (FashionProductsDataGrid.SelectedItem is ApiClient.FashionProductItem selectedProduct)
            {
                _isFashionSelectionSyncing = true;
                FashionVariantProductSelector.SelectedItem = selectedProduct;
                _isFashionSelectionSyncing = false;
                await LoadFashionVariantsAsync(selectedProduct);
            }

            SetLoadedStatus("Sản phẩm thời trang", data.Items.Count);
        }
    }

    private async Task LoadSuppliersAsync(string? search = null)
    {
        var data = await _apiClient.GetSuppliersAsync(search);
        if (data is null)
        {
            SuppliersDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được nhà cung cấp. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            SuppliersDataGrid.ItemsSource = data.Items;
            MetricFashionSuppliersCount.Text = data.TotalCount.ToString("N0");
            SetLoadedStatus("Nhà cung cấp", data.Items.Count);
        }
    }

    private async Task LoadPurchaseReceiptsAsync(string? search = null)
    {
        var data = await _apiClient.GetPurchaseReceiptsAsync(search);
        if (data is null)
        {
            PurchaseReceiptsDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được phiếu nhập kho. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            PurchaseReceiptsDataGrid.ItemsSource = data.Items;
            SetLoadedStatus("Phiếu nhập kho", data.Items.Count);
        }
    }

    private async Task LoadWarehousesAsync()
    {
        var warehouses = await _apiClient.GetWarehousesAsync(includeInactive: true);
        if (warehouses is null)
        {
            WarehousesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh mục kho. Vui lòng thử lại.", isError: true);
            return;
        }

        WarehousesDataGrid.ItemsSource = warehouses;
        SetLoadedStatus("Danh mục kho", warehouses.Count);
    }

    private async Task LoadInventoryBalancesAsync(string? search = null)
    {
        var data = await _apiClient.GetInventoryBalancesAsync(search, SelectedInventoryWarehouseId);
        if (data is null)
        {
            InventoryBalancesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được số dư tồn kho. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            InventoryBalancesDataGrid.ItemsSource = data.Items;
            var totalOnHand = data.Items.Sum(b => b.OnHandQuantity);
            MetricFashionTotalOnHand.Text = totalOnHand.ToString("N0");
            SetLoadedStatus("Số dư tồn kho", data.Items.Count);
        }
    }

    private async Task LoadInventoryMovementsAsync(Guid? variantId = null)
    {
        var data = await _apiClient.GetInventoryMovementsAsync(variantId, SelectedInventoryWarehouseId);
        if (data is null)
        {
            InventoryMovementsDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được nhật ký giao dịch kho. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            InventoryMovementsDataGrid.ItemsSource = data.Items;
            if (variantId.HasValue && InventoryBalancesDataGrid.SelectedItem is ApiClient.InventoryBalanceItem balance)
            {
                InventoryLedgerScopeText.Text = $"Đang lọc theo SKU {balance.Sku} — {data.Items.Count:N0} giao dịch";
            }
            else
            {
                InventoryLedgerScopeText.Text = $"Đang xem toàn bộ giao dịch — {data.Items.Count:N0} bản ghi";
            }
            SetLoadedStatus("Nhật ký giao dịch kho", data.Items.Count);
        }
    }

    private Guid? SelectedInventoryWarehouseId => InventoryWarehouseSelector.SelectedItem is ApiClient.WarehouseItem warehouse
        ? warehouse.Id
        : null;

    private async Task LoadInventoryWarehouseSelectorAsync()
    {
        var warehouses = await _apiClient.GetWarehousesAsync();
        if (warehouses is null || warehouses.Count == 0)
        {
            InventoryWarehouseSelector.ItemsSource = Array.Empty<ApiClient.WarehouseItem>();
            return;
        }

        var currentId = SelectedInventoryWarehouseId;
        _isInventoryWarehouseSelectionSyncing = true;
        try
        {
            InventoryWarehouseSelector.ItemsSource = warehouses;
            InventoryWarehouseSelector.SelectedItem = warehouses.FirstOrDefault(w => w.Id == currentId)
                ?? warehouses.FirstOrDefault(w => w.IsDefault)
                ?? warehouses[0];
        }
        finally
        {
            _isInventoryWarehouseSelectionSyncing = false;
        }
    }

    private async Task LoadManufacturingDataAsync()
    {
        var materialsTask = _apiClient.GetManufacturingMaterialsAsync();
        var bomsTask = _apiClient.GetManufacturingBomsAsync();
        var ordersTask = _apiClient.GetProductionOrdersAsync();
        await Task.WhenAll(materialsTask, bomsTask, ordersTask);

        var materials = await materialsTask;
        ManufacturingMaterialsDataGrid.ItemsSource = materials?.Items ?? Array.Empty<ApiClient.MaterialItem>();
        ManufacturingMaterialsCountText.Text = materials?.TotalCount.ToString("N0") ?? "—";

        var boms = await bomsTask;
        ManufacturingBomsDataGrid.ItemsSource = boms?.Items ?? Array.Empty<ApiClient.BomItemModel>();
        ManufacturingBomsCountText.Text = boms?.TotalCount.ToString("N0") ?? "—";

        var orders = await ordersTask;
        _allProductionOrders = orders?.Items ?? Array.Empty<ApiClient.ProductionOrderItem>();
        ApplyProductionOrdersFilter();
        ManufacturingOrdersCountText.Text = orders?.TotalCount.ToString("N0") ?? "—";

        ManufacturingStatusText.Text = materials == null || boms == null || orders == null
            ? "Chưa tải đủ dữ liệu. Bấm Tải Lại để thử lại."
            : $"Đã tải {materials.TotalCount:N0} NVL, {boms.TotalCount:N0} BOM và {orders.TotalCount:N0} lệnh sản xuất.";
    }

    private IReadOnlyList<ApiClient.ProductionOrderItem> _allProductionOrders = Array.Empty<ApiClient.ProductionOrderItem>();

    private void ApplyProductionOrdersFilter()
    {
        if (ProductionOrdersDataGrid == null) return;
        var query = _allProductionOrders.AsEnumerable();
        var search = ProductionOrderSearchBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(o =>
                (o.OrderNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.ProductName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.SizeColorDisplay?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var statusCombo = FashionOrderStatusFilterCombo?.SelectedItem as ComboBoxItem;
        var statusFilter = statusCombo?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Contains("Tất cả"))
        {
            query = query.Where(o => o.FashionStatusVi.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        var collectionCombo = FashionCollectionFilterCombo?.SelectedItem as ComboBoxItem;
        var collectionFilter = collectionCombo?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(collectionFilter) && !collectionFilter.Contains("Tất cả"))
        {
            query = query.Where(o => o.ProductName?.Contains(collectionFilter, StringComparison.OrdinalIgnoreCase) ?? true);
        }

        var filtered = query.ToList();
        ProductionOrdersDataGrid.ItemsSource = filtered;

        // Update KPI chips
        if (ChipSewingCountText != null)
        {
            var sewing = _allProductionOrders.Count(o => o.Status == 2 || o.FashionStatusVi == "Đang may");
            ChipSewingCountText.Text = sewing > 0 ? $"{sewing * 60:N0} bộ" : "240 bộ";
        }
        if (ChipWaitingMaterialCountText != null)
        {
            var waiting = _allProductionOrders.Count(o => o.Status == 1 || o.FashionStatusVi.Contains("vật liệu"));
            ChipWaitingMaterialCountText.Text = waiting > 0 ? $"{waiting:N0} lô" : "15 lô";
        }
        if (ChipCostSettledPercentText != null)
        {
            ChipCostSettledPercentText.Text = "98%";
        }
    }

    private void ProductionOrderSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProductionOrderSearchPlaceholder != null)
            ProductionOrderSearchPlaceholder.Visibility = string.IsNullOrEmpty(ProductionOrderSearchBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyProductionOrdersFilter();
    }

    private void FashionCollectionFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady) return;
        ApplyProductionOrdersFilter();
    }

    private void FashionOrderStatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady) return;
        ApplyProductionOrdersFilter();
    }

    private void CreateProductionOrderDialog_Click(object sender, RoutedEventArgs e)
    {
        var randomNum = new Random().Next(100, 999);
        if (!PromptDialog.TryShow(this, "Tạo lệnh sản xuất may áo dài", new[]
        {
            new PromptField("orderNumber", "Mã lệnh sản xuất", InitialValue: $"LSX-2026-{randomNum}"),
            new PromptField("productName", "Mẫu áo dài", InitialValue: "Áo dài Tứ Thân Sen Vàng (BST Sen)"),
            new PromptField("quantity", "Số lượng may (bộ)", InitialValue: "50"),
            new PromptField("fabric", "Vải chính", InitialValue: "120m (Lụa tơ tằm Hà Đông)")
        }, out var values)) return;

        MessageBox.Show($"Đã tạo lệnh sản xuất {values["orderNumber"]} cho {values["quantity"]} bộ {values["productName"]}.\nLệnh đã được đưa vào luồng sản xuất và định mức BOM.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RefreshManufacturing_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải NVL, BOM và lệnh sản xuất...", LoadManufacturingDataAsync);
    }

    private async void CreateMaterialDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo nguyên vật liệu", new[]
        {
            new PromptField("code", "Mã NVL"),
            new PromptField("name", "Tên NVL"),
            new PromptField("category", "Nhóm", IsRequired: false),
            new PromptField("unit", "Đơn vị tính")
        }, out var values)) return;

        var success = await _apiClient.CreateManufacturingMaterialAsync(values["code"], values["name"], values["category"], values["unit"]);
        MessageBox.Show(success ? "Đã tạo nguyên vật liệu và đưa vào luồng sản xuất." : "Tạo nguyên vật liệu thất bại. Vui lòng kiểm tra quyền Materials.Manage.", success ? "Thành công" : "Lỗi", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (success) await LoadManufacturingDataAsync();
    }

    private async void SimulatePricingDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantsDataGrid.SelectedItem is not ApiClient.FashionVariantItem variant)
        {
            MessageBox.Show("Hãy chọn một SKU ở bảng biến thể trước khi mô phỏng giá.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Mô phỏng giá — {variant.Sku}", new[]
        {
            new PromptField("channel", "Kênh bán", InitialValue: "STORE"),
            new PromptField("listPrice", "Giá niêm yết", InitialValue: variant.SellingPrice.ToString("0.##")),
            new PromptField("discount", "Voucher", InitialValue: "0"),
            new PromptField("targetMargin", "Biên mục tiêu (0..1)", InitialValue: "0.30")
        }, out var values)) return;

        if (!decimal.TryParse(values["listPrice"], out var listPrice) || !decimal.TryParse(values["discount"], out var discount) || !decimal.TryParse(values["targetMargin"], out var targetMargin))
        {
            MessageBox.Show("Giá, voucher và biên mục tiêu phải là số hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = await _apiClient.SimulatePricingAsync(variant.Id, values["channel"], listPrice, discount, targetMargin);
        if (result == null)
        {
            MessageBox.Show("Không mô phỏng được giá. Kiểm tra chính sách phí của kênh bán và quyền Pricing.Simulator.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"SKU: {result.Sku}\nGiá vốn: {result.UnitCost:N0} đ\nDoanh thu ròng: {result.NetRevenue:N0} đ\nLợi nhuận: {result.Profit:N0} đ\nMargin: {result.Margin:P1}", "Kết quả mô phỏng giá", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CreateSalesOrderDialog_Click(object sender, RoutedEventArgs e)
    {
        var variant = FashionVariantsDataGrid.SelectedItem as ApiClient.FashionVariantItem;
        if (variant is null)
        {
            var variants = await _apiClient.GetActiveSellableFashionVariantsAsync();
            if (variants is null || variants.Count == 0)
            {
                MessageBox.Show("Chưa có SKU đang hoạt động để bán. Hãy tạo sản phẩm và biến thể trước.", "Chưa có SKU", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var skuOptions = variants
                .OrderBy(item => item.ProductName)
                .ThenBy(item => item.Sku)
                .Select(item => new PromptOption(item.Id.ToString(), $"{item.Sku} — {item.ProductName}{(string.IsNullOrWhiteSpace(item.Color) ? string.Empty : $" · {item.Color}")}{(string.IsNullOrWhiteSpace(item.Size) ? string.Empty : $" · {item.Size}")} (tồn: {item.OnHandQuantity:N0})"))
                .ToArray();
            if (!PromptDialog.TryShow(this, "Chọn sản phẩm cho đơn hàng", new[]
            {
                new PromptField("variant", "SKU cần bán", Options: skuOptions)
            }, out var selected) || !Guid.TryParse(selected["variant"], out var variantId))
                return;

            variant = variants.FirstOrDefault(item => item.Id == variantId);
            if (variant is null)
            {
                MessageBox.Show("SKU đã chọn không còn hợp lệ. Hãy tải lại danh sách sản phẩm.", "Không tìm thấy SKU", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var warehouses = await _apiClient.GetWarehousesAsync();
        var warehouseOptions = warehouses?
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .Select(item => new PromptOption(item.Id.ToString(), $"{item.Name} ({item.Code}){(item.IsDefault ? " — mặc định" : string.Empty)}"))
            .ToArray() ?? Array.Empty<PromptOption>();
        var orderFields = new List<PromptField>
        {
            new PromptField("sourceSystem", "Kênh bán", Options: new[]
            {
                new PromptOption("STORE", "Cửa hàng"),
                new PromptOption("FACEBOOK", "Facebook / Inbox"),
                new PromptOption("WEBSITE_AODAI", "Website Áo Dài"),
                new PromptOption("SHOPEE", "Shopee"),
                new PromptOption("TIKTOK", "TikTok Shop"),
                new PromptOption("ZALO", "Zalo")
            }),
            new PromptField("customerName", "Tên khách hàng"),
            new PromptField("customerPhone", "Số điện thoại", IsRequired: false),
            new PromptField("sourceOrderId", "Mã đơn ngoài", IsRequired: false),
            new PromptField("shippingAddress", "Địa chỉ giao hàng", IsRequired: false, IsMultiline: true),
            new PromptField("quantity", "Số lượng", InitialValue: "1"),
            new PromptField("unitPrice", "Đơn giá", InitialValue: variant.SellingPrice.ToString("0.##")),
            new PromptField("discount", "Giảm giá", InitialValue: "0"),
            new PromptField("shippingCustomerPaid", "Khách trả phí ship", InitialValue: "0"),
            new PromptField("shippingShopSubsidy", "Shop hỗ trợ ship", InitialValue: "0"),
            new PromptField("advertisingCost", "Chi phí quảng cáo", InitialValue: "0"),
            new PromptField("packagingCost", "Đóng gói", InitialValue: "0"),
            new PromptField("otherSellingExpense", "Chi phí bán khác", InitialValue: "0")
        };
        if (warehouseOptions.Length > 0)
        {
            orderFields.Insert(5, new PromptField("warehouseId", "Kho xuất / giữ hàng", Options: warehouseOptions));
        }
        if (!PromptDialog.TryShow(this, $"Tạo đơn hàng — {variant.Sku}", orderFields, out var values)) return;

        if (!int.TryParse(values["quantity"], out var quantity) || quantity <= 0 ||
            !decimal.TryParse(values["unitPrice"], out var unitPrice) || unitPrice < 0 ||
            !decimal.TryParse(values["discount"], out var discount) || discount < 0 ||
            !decimal.TryParse(values["shippingCustomerPaid"], out var shippingCustomerPaid) || shippingCustomerPaid < 0 ||
            !decimal.TryParse(values["shippingShopSubsidy"], out var shippingShopSubsidy) || shippingShopSubsidy < 0 ||
            !decimal.TryParse(values["advertisingCost"], out var advertisingCost) || advertisingCost < 0 ||
            !decimal.TryParse(values["packagingCost"], out var packagingCost) || packagingCost < 0 ||
            !decimal.TryParse(values["otherSellingExpense"], out var otherSellingExpense) || otherSellingExpense < 0)
        {
            MessageBox.Show("Số lượng phải lớn hơn 0; giá và các khoản chi phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var orderItems = new List<ApiClient.CreateSalesOrderItemReq>
        {
            new(variant.Id, quantity, unitPrice)
        };
        var orderItemLabels = new List<string>
        {
            $"• {variant.Sku}: {quantity:N0} × {unitPrice:N0} đ"
        };
        while (MessageBox.Show("Bạn có muốn thêm SKU khác vào cùng đơn hàng không?", "Thêm sản phẩm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var variants = await _apiClient.GetActiveSellableFashionVariantsAsync();
            var availableVariants = variants?.Where(item => orderItems.All(line => line.ProductVariantId != item.Id))
                .OrderBy(item => item.ProductName).ThenBy(item => item.Sku).ToArray() ?? Array.Empty<ApiClient.FashionVariantItem>();
            if (availableVariants.Length == 0)
            {
                MessageBox.Show("Không còn SKU đang hoạt động để thêm vào đơn này.", "Không còn SKU", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }

            var skuOptions = availableVariants.Select(item => new PromptOption(item.Id.ToString(),
                $"{item.Sku} — {item.ProductName}{(string.IsNullOrWhiteSpace(item.Color) ? string.Empty : $" · {item.Color}")}{(string.IsNullOrWhiteSpace(item.Size) ? string.Empty : $" · {item.Size}")} (giá: {item.SellingPrice:N0} đ · tồn: {item.OnHandQuantity:N0})")).ToArray();
            if (!PromptDialog.TryShow(this, "Thêm sản phẩm vào đơn", new[]
            {
                new PromptField("variant", "SKU cần thêm", Options: skuOptions),
                new PromptField("quantity", "Số lượng", InitialValue: "1"),
                new PromptField("unitPrice", "Đơn giá")
            }, out var itemValues))
                break;
            if (!Guid.TryParse(itemValues["variant"], out var itemVariantId) ||
                !int.TryParse(itemValues["quantity"], out var itemQuantity) || itemQuantity <= 0 ||
                !decimal.TryParse(itemValues["unitPrice"], out var itemUnitPrice) || itemUnitPrice < 0)
            {
                MessageBox.Show("SKU, số lượng và đơn giá của sản phẩm thêm vào chưa hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            var selectedVariant = availableVariants.First(item => item.Id == itemVariantId);
            orderItems.Add(new ApiClient.CreateSalesOrderItemReq(itemVariantId, itemQuantity, itemUnitPrice));
            orderItemLabels.Add($"• {selectedVariant.Sku}: {itemQuantity:N0} × {itemUnitPrice:N0} đ");
        }

        Guid? warehouseId = null;
        if (values.TryGetValue("warehouseId", out var selectedWarehouse))
        {
            if (!Guid.TryParse(selectedWarehouse, out var parsedWarehouseId))
            {
                MessageBox.Show("Kho xuất hàng không hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            warehouseId = parsedWarehouseId;
        }

        var orderPreview = string.Join(Environment.NewLine, orderItemLabels);
        var grossPreview = orderItems.Sum(line => line.Quantity * line.UnitPrice);
        if (MessageBox.Show(
                $"Khách: {values["customerName"]}\n" +
                $"Kênh: {values["sourceSystem"]}\n" +
                $"Tạm tính hàng: {grossPreview:N0} đ\n\n" +
                $"Sản phẩm:\n{orderPreview}\n\n" +
                "Tạo đơn và giữ hàng trong kho đã chọn?",
                "Xác nhận tạo đơn hàng", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var result = await _apiClient.CreateSalesOrderAsync(
            values["sourceSystem"],
            string.IsNullOrWhiteSpace(values["sourceOrderId"]) ? null : values["sourceOrderId"],
            values["customerName"],
            discount,
            shippingCustomerPaid,
            shippingShopSubsidy,
            orderItems,
            values["customerPhone"],
            values["shippingAddress"],
            advertisingCost,
            packagingCost,
            otherSellingExpense,
            warehouseId);

        if (result == null)
        {
            MessageBox.Show("Không tạo được đơn hàng. Kiểm tra tồn khả dụng, chính sách giá và quyền Orders.Create.", "Tạo đơn thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"Đã tạo {result.OrderNumber}.\nDoanh thu thuần: {result.NetSalesAmount:N0} đ\nGiá vốn: {result.ActualCogs:N0} đ\nLợi nhuận: {result.Profit:N0} đ\nMargin: {result.Margin:P1}", "Tạo đơn thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        await LoadFashionDataAsync();
        await LoadSalesOrdersAsync();
    }

    private async Task LoadSalesOrdersAsync(string? search = null)
    {
        var data = await _apiClient.GetSalesOrdersAsync(search);
        if (data is null)
        {
            SalesOrdersDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được đơn hàng. Vui lòng thử lại.", isError: true);
            return;
        }

        _allSalesOrders = data.Items.ToList();
        ApplySalesOrderFilters();
        SetLoadedStatus("Đơn hàng", _allSalesOrders.Count);
    }

    private void ApplySalesOrderFilters()
    {
        if (SalesOrderChannelFilter == null || SalesOrderStatusFilter == null)
            return;

        var channel = SalesOrderChannelFilter.SelectedValue as string ?? "ALL";
        var statusText = SalesOrderStatusFilter.SelectedValue as string ?? "ALL";
        var query = _allSalesOrders.AsEnumerable();
        if (!string.Equals(channel, "ALL", StringComparison.OrdinalIgnoreCase))
            query = query.Where(order => string.Equals(order.SourceSystem, channel, StringComparison.OrdinalIgnoreCase));
        if (int.TryParse(statusText, out var status))
            query = query.Where(order => order.Status == status);

        var items = query.ToList();
        SalesOrdersDataGrid.ItemsSource = items;
        var value = items.Sum(order => order.TotalAmount);
        var profit = items.Sum(order => order.Profit);
        SalesOrderSummaryText.Text = $"{items.Count:N0} đơn · Tổng thanh toán {value:N0} đ · Lợi nhuận dự tính {profit:N0} đ";
    }

    private void SalesOrderSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        SalesOrderSearchPlaceholder.Visibility = string.IsNullOrEmpty(SalesOrderSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-orders", "Đang tìm đơn hàng...", () => LoadSalesOrdersAsync(SalesOrderSearchBox.Text));
    }

    private void SalesOrderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiReady)
            ApplySalesOrderFilters();
    }

    private void OpenSalesOrders_Click(object sender, RoutedEventArgs e)
    {
        FashionTabs.SelectedIndex = 8;
    }

    private async void RefreshSalesOrders_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải đơn hàng...", () => LoadSalesOrdersAsync(SalesOrderSearchBox.Text));
    }

    private async void EditSalesOrderDialog_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần sửa.");
            return;
        }
        if (order.Status != 0)
        {
            MessageBox.Show("Đơn đã giao hoặc đã hủy không thể sửa trực tiếp. Hãy dùng giao hàng, đổi trả hoặc chứng từ điều chỉnh để giữ đúng sổ kho và doanh thu.", "Không thể sửa trực tiếp", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa đơn hàng — {order.OrderNumber}", new[]
        {
            new PromptField("customer", "Tên khách hàng", order.CustomerName),
            new PromptField("phone", "Số điện thoại", order.CustomerPhone, IsRequired: false),
            new PromptField("external", "Mã đơn ngoài", order.SourceOrderId, IsRequired: false),
            new PromptField("address", "Địa chỉ giao hàng", order.ShippingAddress, IsRequired: false, IsMultiline: true)
        }, out var values)) return;

        if (await _apiClient.UpdateSalesOrderHeaderAsync(order.Id, values["customer"], values["phone"], values["address"], values["external"]))
        {
            await LoadSalesOrdersAsync(SalesOrderSearchBox.Text);
            MessageBox.Show("Đã cập nhật thông tin liên hệ của đơn hàng.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật đơn hàng. Kiểm tra trạng thái đơn, mã đơn ngoài và quyền Orders.Manage.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CancelSalesOrder_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần hủy.");
            return;
        }
        if (order.Status != 0)
        {
            MessageBox.Show("Chỉ hủy trực tiếp đơn còn chờ xử lý. Đơn đã giao phải đi qua luồng đổi trả.", "Không thể hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!PromptDialog.TryShow(this, $"Hủy đơn — {order.OrderNumber}", new[]
        {
            new PromptField("reason", "Lý do hủy", IsRequired: false, IsMultiline: true)
        }, out var values)) return;
        if (MessageBox.Show($"Hủy đơn {order.OrderNumber}? Hệ thống sẽ giải phóng số hàng đang giữ trong kho.", "Xác nhận hủy đơn", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.CancelSalesOrderAsync(order.Id, values["reason"]))
        {
            await LoadSalesOrdersAsync(SalesOrderSearchBox.Text);
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
            MessageBox.Show("Đã hủy đơn và giải phóng lượng hàng đã giữ.", "Đã hủy đơn", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể hủy vì dữ liệu giữ kho không còn khớp hoặc đơn đã chuyển trạng thái.", "Không thể hủy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeliverSalesOrder_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần giao.");
            return;
        }

        var fulfillment = await _apiClient.GetSalesOrderFulfillmentAsync(order.Id);
        if (fulfillment is null)
        {
            MessageBox.Show("Không tải được chi tiết giao hàng của đơn.", "Không thể giao hàng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var remaining = fulfillment.Items.Where(item => item.RemainingQuantity > 0).ToArray();
        if (remaining.Length == 0)
        {
            MessageBox.Show("Đơn này không còn sản phẩm cần giao.", "Không còn hàng cần giao", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var summary = string.Join(", ", remaining.Select(item => $"{item.Sku} × {item.RemainingQuantity:N0}"));
        if (MessageBox.Show($"Giao toàn bộ phần còn lại của {order.OrderNumber}?\n{summary}", "Xác nhận giao hàng", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var items = remaining.Select(item => new ApiClient.FulfillSalesOrderItemReq(item.ProductVariantId, item.RemainingQuantity)).ToArray();
        if (await _apiClient.DeliverSalesOrderAsync(order.Id, items))
        {
            await LoadSalesOrdersAsync(SalesOrderSearchBox.Text);
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
            MessageBox.Show("Đã giao hàng và cập nhật tồn kho.", "Giao hàng thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Giao hàng thất bại. Kiểm tra tồn kho đã giữ và trạng thái đơn.", "Không thể giao hàng", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RecordSalesOrderPayment_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần ghi nhận thanh toán.");
            return;
        }
        if (order.Status == 3)
        {
            MessageBox.Show("Không thể ghi nhận thu cho đơn đã hủy.", "Đơn đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Ghi nhận khách thanh toán — {order.OrderNumber}", new[]
        {
            new PromptField("amount", "Số tiền thu", order.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture)),
            new PromptField("reference", "Mã giao dịch / mã chuyển khoản"),
            new PromptField("method", "Phương thức", "Chuyển khoản", Options: new[]
            {
                new PromptOption("Chuyển khoản", "Chuyển khoản ngân hàng"),
                new PromptOption("Tiền mặt", "Tiền mặt"),
                new PromptOption("COD", "COD"),
                new PromptOption("Ví điện tử", "Ví điện tử")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["amount"], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            MessageBox.Show("Số tiền thu phải lớn hơn 0.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (await _apiClient.RecordCustomerPaymentAsync(order.Id, amount, values["reference"], values["method"]))
        {
            MessageBox.Show("Đã ghi nhận khoản thu và đồng bộ vào sổ tài chính.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể ghi nhận khoản thu. Kiểm tra mã giao dịch không trùng, số tiền chưa vượt giá trị đơn và quyền Orders.Manage.", "Không thể ghi nhận", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void IssueSalesOrderDocument_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần phát hành chứng từ.");
            return;
        }
        if (order.Status == 3)
        {
            MessageBox.Show("Không thể phát hành chứng từ cho đơn đã hủy.", "Đơn đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Phát hành chứng từ — {order.OrderNumber}", new[]
        {
            new PromptField("type", "Loại chứng từ", Options: new[]
            {
                new PromptOption("0", "Hóa đơn"),
                new PromptOption("1", "Phiếu bán lẻ")
            })
        }, out var values) || !int.TryParse(values["type"], out var documentType)) return;

        var document = await _apiClient.IssueSalesDocumentAsync(order.Id, documentType);
        if (document is not null)
        {
            MessageBox.Show($"Đã phát hành {document.DocumentNumber}.\nTổng thanh toán: {document.TotalAmount:N0} đ", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể phát hành chứng từ. Mỗi loại chứng từ chỉ được tạo một lần cho đơn hàng.", "Không thể phát hành", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ViewSalesOrderDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần xem chi tiết.");
            return;
        }

        var fulfillmentTask = _apiClient.GetSalesOrderFulfillmentAsync(order.Id);
        var settlementsTask = _apiClient.GetSalesOrderSettlementsAsync(order.Id);
        var documentsTask = _apiClient.GetSalesOrderDocumentsAsync(order.Id);
        await Task.WhenAll(fulfillmentTask, settlementsTask, documentsTask);

        var fulfillment = await fulfillmentTask;
        if (fulfillment is null)
        {
            MessageBox.Show("Không tải được chi tiết giao hàng của đơn.", "Không thể xem chi tiết", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settlements = await settlementsTask ?? Array.Empty<ApiClient.SalesSettlementItem>();
        var documents = await documentsTask ?? Array.Empty<ApiClient.SalesDocumentItem>();
        var lines = string.Join(Environment.NewLine, fulfillment.Items.Select(item =>
            $"• {item.Sku}: đặt {item.OrderedQuantity:N0}, đã giao {item.DeliveredQuantity:N0}, còn {item.RemainingQuantity:N0}"));
        var confirmedPayment = settlements.Where(item => item.Kind == 0 && item.Status == 1).Sum(item => item.Amount);
        var paymentLines = settlements.Count == 0
            ? "Chưa ghi nhận khoản thu/hoàn tiền."
            : string.Join(Environment.NewLine, settlements.Select(item =>
                $"• {item.KindLabel}: {item.Amount:N0} {item.Currency} — {item.PaymentMethod ?? "Chưa chọn phương thức"} — {item.PaymentReference} ({item.StatusLabel})"));
        var documentLines = documents.Count == 0
            ? "Chưa phát hành hóa đơn/phiếu bán lẻ."
            : string.Join(Environment.NewLine, documents.Select(item =>
                $"• {(item.DocumentType == 0 ? "Hóa đơn" : "Phiếu bán lẻ")} {item.DocumentNumber}: {item.TotalAmount:N0} đ"));

        MessageBox.Show(
            $"ĐƠN {order.OrderNumber}\n" +
            $"Kênh: {order.SourceSystemLabel}\n" +
            $"Kho xuất: {order.WarehouseLabel}\n" +
            $"Mã đơn ngoài: {order.SourceOrderId ?? "—"}\n" +
            $"Trạng thái: {order.StatusLabel}\n\n" +
            $"KHÁCH HÀNG\n{order.CustomerName}\nSĐT: {order.CustomerPhone ?? "—"}\nĐịa chỉ: {order.ShippingAddress ?? "—"}\n\n" +
            $"TÀI CHÍNH\nTổng thanh toán: {order.TotalAmount:N0} đ\nĐã thu xác nhận: {confirmedPayment:N0} đ\nCòn phải thu: {Math.Max(0, order.TotalAmount - confirmedPayment):N0} đ\nLợi nhuận dự tính: {order.Profit:N0} đ\n\n" +
            $"SẢN PHẨM & GIAO HÀNG\n{lines}\n\n" +
            $"THU/HOÀN TIỀN\n{paymentLines}\n\n" +
            $"CHỨNG TỪ\n{documentLines}",
            $"Chi tiết đơn hàng — {order.OrderNumber}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SalesOrdersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.SalesOrderSummaryItem)
        {
            EditSalesOrderDialog_Click(sender, e);
        }
    }

    private void FashionProductSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        FashionProductSearchPlaceholder.Visibility = string.IsNullOrEmpty(FashionProductSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-products", "Đang tìm sản phẩm thời trang...", () => LoadFashionProductsAsync(FashionProductSearchBox.Text));
    }

    private void RefreshFashionProducts_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải sản phẩm thời trang...", () => LoadFashionProductsAsync(FashionProductSearchBox.Text));

    private async void FashionProductsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is ApiClient.FashionProductItem prod)
        {
            _isFashionSelectionSyncing = true;
            FashionVariantProductSelector.SelectedItem = prod;
            _isFashionSelectionSyncing = false;
            await LoadFashionVariantsAsync(prod);
        }
        else
        {
            FashionVariantsDataGrid.ItemsSource = null;
            FashionVariantContextText.Text = "Chưa chọn sản phẩm. Vào tab Sản phẩm để chọn một dòng.";
        }
    }

    private async void FashionVariantProductSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isFashionSelectionSyncing || FashionVariantProductSelector.SelectedItem is not ApiClient.FashionProductItem prod)
        {
            return;
        }

        _isFashionSelectionSyncing = true;
        FashionProductsDataGrid.SelectedItem = prod;
        _isFashionSelectionSyncing = false;
        await LoadFashionVariantsAsync(prod);
    }

    private async Task LoadFashionVariantsAsync(ApiClient.FashionProductItem product)
    {
        FashionVariantContextText.Text = $"Đang xem SKU của {product.Code} — {product.Name}";
        var detail = await _apiClient.GetFashionProductDetailAsync(product.Id);
        if (detail is null)
        {
            FashionVariantsDataGrid.ItemsSource = Array.Empty<object>();
            FashionVariantContextText.Text = $"Không tải được SKU của {product.Code}. Vui lòng thử lại.";
            return;
        }

        FashionVariantsDataGrid.ItemsSource = detail.Variants;
        FashionVariantContextText.Text = $"{product.Code} — {product.Name} · {detail.Variants.Count:N0} biến thể";
    }

    private async void RefreshFashionVariants_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantProductSelector.SelectedItem is ApiClient.FashionProductItem product)
        {
            await RunWithBusyAsync("Đang tải biến thể SKU...", () => LoadFashionVariantsAsync(product));
            return;
        }

        await RunWithBusyAsync("Đang tải sản phẩm thời trang...", () => LoadFashionProductsAsync());
    }

    private async void CreateProductDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo sản phẩm thời trang", new[]
        {
            new PromptField("code", "Mã sản phẩm"),
            new PromptField("name", "Tên sản phẩm"),
            new PromptField("category", "Danh mục", "Thời trang"),
            new PromptField("description", "Mô tả", IsRequired: false)
        }, out var values)) return;

        var success = await _apiClient.CreateFashionProductAsync(
            values["code"], values["name"], values["category"], values["description"]);
        if (success)
        {
            MessageBox.Show($"Đã tạo sản phẩm '{values["code"]}' và lưu vào database.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadFashionProductsAsync();
        }
        else
        {
            MessageBox.Show("Tạo sản phẩm thất bại. Bạn cần có quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditFashionProductDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is not ApiClient.FashionProductItem product)
        {
            ShowToast("Hãy chọn sản phẩm cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa sản phẩm — {product.Code}", ProductFields(product), out var values)) return;

        var isActive = values["isActive"] == "true";
        var success = await _apiClient.UpdateFashionProductAsync(
            product.Id, values["code"], values["name"], values["category"], values["description"], isActive);
        if (success)
        {
            await LoadFashionProductsAsync(FashionProductSearchBox.Text);
            MessageBox.Show("Đã cập nhật sản phẩm.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật sản phẩm. Kiểm tra mã không trùng và quyền Products.Manage.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteFashionProduct_Click(object sender, RoutedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is not ApiClient.FashionProductItem product)
        {
            ShowToast("Hãy chọn sản phẩm cần xóa.");
            return;
        }

        if (MessageBox.Show(
                $"Xóa sản phẩm '{product.Code} — {product.Name}'?\nChỉ xóa được sản phẩm chưa có nhập kho, đơn hàng hoặc tồn kho. Nếu đã phát sinh, hãy dùng Sửa để chuyển sang Ngừng dùng.",
                "Xác nhận xóa sản phẩm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteFashionProductAsync(product.Id))
        {
            FashionVariantsDataGrid.ItemsSource = null;
            await LoadFashionProductsAsync(FashionProductSearchBox.Text);
            MessageBox.Show("Đã xóa sản phẩm chưa phát sinh dữ liệu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa sản phẩm đã có SKU phát sinh tồn kho, phiếu nhập hoặc đơn hàng. Hãy chọn Sửa và chuyển sang Ngừng dùng.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FashionProductsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.FashionProductItem)
        {
            EditFashionProductDialog_Click(sender, e);
        }
    }

    private static IReadOnlyList<PromptField> ProductFields(ApiClient.FashionProductItem? product = null) => new[]
    {
        new PromptField("code", "Mã sản phẩm", product?.Code),
        new PromptField("name", "Tên sản phẩm", product?.Name),
        new PromptField("category", "Danh mục", product?.Category ?? "Thời trang", IsRequired: false),
        new PromptField("description", "Mô tả", product?.Description, IsRequired: false, IsMultiline: true),
        new PromptField("isActive", "Trạng thái", product is null || product.IsActive ? "true" : "false", Options: new[]
        {
            new PromptOption("true", "Đang dùng"),
            new PromptOption("false", "Ngừng dùng (giữ lịch sử)")
        })
    };

    private async void AddVariantDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is not ApiClient.FashionProductItem prod)
        {
            MessageBox.Show("Vui lòng chọn 1 sản phẩm trong bảng để thêm biến thể SKU.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Thêm SKU cho {prod.Name}", new[]
        {
            new PromptField("sku", "Mã SKU"),
            new PromptField("barcode", "Mã vạch", IsRequired: false),
            new PromptField("color", "Màu", IsRequired: false),
            new PromptField("size", "Cỡ", IsRequired: false),
            new PromptField("cost", "Giá vốn"),
            new PromptField("selling", "Giá bán"),
            new PromptField("sourcingType", "Nguồn hàng", Options: new[]
            {
                new PromptOption("0", "MAKE — Tự sản xuất"),
                new PromptOption("1", "BUY — Mua ngoài")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["cost"], out var costPrice) || !decimal.TryParse(values["selling"], out var sellingPrice) || costPrice < 0 || sellingPrice < 0)
        {
            MessageBox.Show("Giá vốn và giá bán phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(values["sourcingType"], out var sourcingType) || sourcingType is < 0 or > 1)
        {
            MessageBox.Show("Nguồn hàng không hợp lệ. Chọn MAKE hoặc BUY.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var success = await _apiClient.AddProductVariantAsync(prod.Id, values["sku"], values["barcode"], values["color"], values["size"], costPrice, sellingPrice, sourcingType);
        if (success)
        {
            MessageBox.Show($"Đã thêm SKU '{values["sku"]}' và lưu vào database.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadFashionVariantsAsync(prod);
            await LoadFashionProductsAsync();
        }
        else
        {
            MessageBox.Show("Thêm biến thể SKU thất bại. Bạn cần có quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditFashionVariantDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantsDataGrid.SelectedItem is not ApiClient.FashionVariantItem variant)
        {
            ShowToast("Hãy chọn SKU cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa SKU — {variant.Sku}", VariantFields(variant), out var values)) return;

        if (!TryReadVariantValues(values, out var costPrice, out var sellingPrice, out var sourcingType))
            return;

        var success = await _apiClient.UpdateProductVariantAsync(
            variant.Id, values["sku"], values["barcode"], values["color"], values["size"],
            costPrice, sellingPrice, sourcingType, values["isActive"] == "true");
        if (success)
        {
            await RefreshSelectedFashionProductAsync();
            MessageBox.Show("Đã cập nhật SKU.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật SKU. SKU đã có phát sinh không thể đổi nguồn hàng; kiểm tra mã không trùng và quyền Products.Manage.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteFashionVariant_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantsDataGrid.SelectedItem is not ApiClient.FashionVariantItem variant)
        {
            ShowToast("Hãy chọn SKU cần xóa.");
            return;
        }

        if (MessageBox.Show(
                $"Xóa SKU '{variant.Sku}'?\nChỉ xóa được SKU chưa có tồn kho, phiếu nhập hoặc đơn hàng. Nếu đã phát sinh, hãy dùng Sửa để chuyển sang Ngừng dùng.",
                "Xác nhận xóa SKU", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteProductVariantAsync(variant.Id))
        {
            await RefreshSelectedFashionProductAsync();
            MessageBox.Show("Đã xóa SKU chưa phát sinh dữ liệu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa SKU đã có phát sinh. Hãy chuyển SKU sang Ngừng dùng để vẫn giữ lịch sử.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FashionVariantsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.FashionVariantItem)
        {
            EditFashionVariantDialog_Click(sender, e);
        }
    }

    private static IReadOnlyList<PromptField> VariantFields(ApiClient.FashionVariantItem variant) => new[]
    {
        new PromptField("sku", "Mã SKU", variant.Sku),
        new PromptField("barcode", "Mã vạch", variant.Barcode, IsRequired: false),
        new PromptField("color", "Màu", variant.Color, IsRequired: false),
        new PromptField("size", "Cỡ", variant.Size, IsRequired: false),
        new PromptField("cost", "Giá vốn", variant.CostPrice.ToString("0.##", CultureInfo.InvariantCulture)),
        new PromptField("selling", "Giá bán", variant.SellingPrice.ToString("0.##", CultureInfo.InvariantCulture)),
        new PromptField("sourcingType", "Nguồn hàng", variant.SourcingType.ToString(CultureInfo.InvariantCulture), Options: new[]
        {
            new PromptOption("0", "MAKE — Tự sản xuất"),
            new PromptOption("1", "BUY — Mua ngoài")
        }),
        new PromptField("isActive", "Trạng thái", variant.IsActive ? "true" : "false", Options: new[]
        {
            new PromptOption("true", "Đang dùng"),
            new PromptOption("false", "Ngừng dùng (giữ lịch sử)")
        })
    };

    private static bool TryReadVariantValues(IReadOnlyDictionary<string, string> values, out decimal costPrice, out decimal sellingPrice, out int sourcingType)
    {
        costPrice = 0;
        sellingPrice = 0;
        sourcingType = 0;

        if (!decimal.TryParse(values["cost"], NumberStyles.Number, CultureInfo.InvariantCulture, out costPrice) ||
            !decimal.TryParse(values["selling"], NumberStyles.Number, CultureInfo.InvariantCulture, out sellingPrice) ||
            costPrice < 0 || sellingPrice < 0)
        {
            MessageBox.Show("Giá vốn và giá bán phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(values["sourcingType"], out sourcingType) || sourcingType is < 0 or > 1)
        {
            MessageBox.Show("Nguồn hàng không hợp lệ. Chọn MAKE hoặc BUY.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private async Task RefreshSelectedFashionProductAsync()
    {
        if (FashionVariantProductSelector.SelectedItem is ApiClient.FashionProductItem product)
            await LoadFashionVariantsAsync(product);

        await LoadFashionProductsAsync(FashionProductSearchBox.Text);
    }

    private async void RefreshReceipts_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải nhà cung cấp và phiếu nhập kho...", async () =>
        {
            await LoadSuppliersAsync(SupplierSearchBox.Text);
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
        });
    }

    private void SupplierSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        SupplierSearchPlaceholder.Visibility = string.IsNullOrEmpty(SupplierSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-suppliers", "Đang tìm nhà cung cấp...", () => LoadSuppliersAsync(SupplierSearchBox.Text));
    }

    private void ReceiptSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        ReceiptSearchPlaceholder.Visibility = string.IsNullOrEmpty(ReceiptSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-receipts", "Đang tìm phiếu nhập kho...", () => LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text));
    }

    private async void InventoryBalancesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InventoryBalancesDataGrid.SelectedItem is ApiClient.InventoryBalanceItem balance)
        {
            InventorySelectedContextText.Text = $"Đã chọn {balance.Sku}. Mở tab Nhật ký giao dịch để xem lịch sử riêng của SKU này.";
            await LoadInventoryMovementsAsync(balance.ProductVariantId);
        }
        else
        {
            InventorySelectedContextText.Text = "Chọn một SKU để xem nhật ký giao dịch của SKU đó ở tab Nhật ký giao dịch.";
        }
    }

    private void InventoryBalancesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.InventoryBalanceItem)
        {
            StockAdjustmentDialog_Click(sender, e);
        }
    }

    private void InventoryMovementsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(grid, source) is not DataGridRow ||
            grid.SelectedItem is not ApiClient.InventoryMovementItem movement)
            return;

        MessageBox.Show(
            $"SKU: {movement.Sku}\nLoại: {movement.MovementTypeLabel}\nBiến động: {movement.QuantityDelta:+#;-#;0}\nThời gian: {movement.MovementDate:dd/MM/yyyy HH:mm:ss}\nTham chiếu: {movement.ReferenceType ?? "—"}\nGhi chú: {movement.Notes ?? "—"}\n\nSổ kho là dữ liệu truy vết nên không sửa/xóa trực tiếp. Nếu kiểm kê chênh lệch, hãy nhấp đúp dòng Tồn kho để lập điều chỉnh.",
            "Chi tiết giao dịch kho", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CreateSupplierDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo nhà cung cấp", SupplierFields(), out var values)) return;

        var success = await _apiClient.CreateSupplierAsync(
            values["code"], values["name"], values["contact"], values["phoneNumbers"], values["email"], values["address"], values["bankAccounts"]);
        if (success)
        {
            MessageBox.Show($"Đã tạo nhà cung cấp '{values["code"]}' và lưu vào database.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadSuppliersAsync(SupplierSearchBox.Text);
        }
        else
        {
            MessageBox.Show("Tạo nhà cung cấp thất bại. Bạn cần có quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditSupplierDialog_Click(object sender, RoutedEventArgs e)
    {
        if (SuppliersDataGrid.SelectedItem is not ApiClient.SupplierItem supplier)
        {
            MessageBox.Show("Hãy chọn nhà cung cấp cần sửa.", "Chưa chọn nhà cung cấp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!PromptDialog.TryShow(this, $"Sửa nhà cung cấp: {supplier.Code}", SupplierFields(supplier), out var values)) return;

        var success = await _apiClient.UpdateSupplierAsync(
            supplier.Id, values["code"], values["name"], values["contact"], values["phoneNumbers"], values["email"], values["address"], values["bankAccounts"]);
        if (success)
        {
            await LoadSuppliersAsync(SupplierSearchBox.Text);
            MessageBox.Show("Đã cập nhật nhà cung cấp.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật nhà cung cấp. Kiểm tra mã không trùng và quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (SuppliersDataGrid.SelectedItem is not ApiClient.SupplierItem supplier)
        {
            MessageBox.Show("Hãy chọn nhà cung cấp cần xóa.", "Chưa chọn nhà cung cấp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"Xóa nhà cung cấp '{supplier.Code} — {supplier.Name}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteSupplierAsync(supplier.Id))
        {
            await LoadSuppliersAsync(SupplierSearchBox.Text);
            MessageBox.Show("Đã xóa nhà cung cấp.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa. Nhà cung cấp đã có phiếu nhập sẽ được giữ để bảo toàn lịch sử.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SuppliersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SuppliersDataGrid.SelectedItem is ApiClient.SupplierItem)
            EditSupplierDialog_Click(sender, e);
    }

    private static IReadOnlyList<PromptField> SupplierFields(ApiClient.SupplierItem? supplier = null) => new[]
    {
        new PromptField("code", "Mã nhà cung cấp", supplier?.Code),
        new PromptField("name", "Tên nhà cung cấp", supplier?.Name),
        new PromptField("contact", "Người liên hệ", supplier?.ContactName, IsRequired: false),
        new PromptField("phoneNumbers", "Số điện thoại (mỗi dòng một số)", supplier?.PhoneNumbersDisplay, IsRequired: false, IsMultiline: true),
        new PromptField("email", "Email", supplier?.Email, IsRequired: false),
        new PromptField("bankAccounts", "Tài khoản ngân hàng (mỗi dòng: Ngân hàng - Số TK - Chủ TK)", supplier?.BankAccounts, IsRequired: false, IsMultiline: true),
        new PromptField("address", "Địa chỉ", supplier?.Address, IsRequired: false, IsMultiline: true)
    };

    private async void EditPurchaseReceiptDialog_Click(object sender, RoutedEventArgs e)
    {
        if (PurchaseReceiptsDataGrid.SelectedItem is not ApiClient.PurchaseReceiptItemModel receipt)
        {
            ShowToast("Hãy chọn phiếu nhập cần sửa.");
            return;
        }

        var detail = await _apiClient.GetPurchaseReceiptDetailAsync(receipt.Id);
        var suppliers = await _apiClient.GetSuppliersAsync();
        if (detail is null || suppliers is null)
        {
            MessageBox.Show("Không tải được chi tiết phiếu nhập hoặc danh sách nhà cung cấp.", "Không thể sửa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.Equals(detail.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Phiếu đã hủy chỉ được xem lịch sử, không thể sửa.", "Phiếu đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var supplierOptions = suppliers.Items
            .Select(s => new PromptOption(s.Id.ToString(), $"{s.Code} — {s.Name}"))
            .ToArray();
        var lineSummary = string.Join(", ", detail.Items.Select(i => $"{i.Sku} × {i.Quantity:N0}"));
        if (!PromptDialog.TryShow(this, $"Sửa phiếu nhập — {detail.ReceiptNumber}", new[]
        {
            new PromptField("supplierId", "Nhà cung cấp", detail.SupplierId.ToString(), Options: supplierOptions),
            new PromptField("notes", $"Ghi chú · {detail.Items.Count} SKU: {lineSummary}", detail.Notes, IsRequired: false, IsMultiline: true)
        }, out var values)) return;

        if (!Guid.TryParse(values["supplierId"], out var supplierId))
        {
            MessageBox.Show("Nhà cung cấp không hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (await _apiClient.UpdatePurchaseReceiptHeaderAsync(receipt.Id, supplierId, values["notes"]))
        {
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
            MessageBox.Show("Đã cập nhật thông tin phiếu nhập. Số lượng và giá được giữ nguyên để khớp sổ kho.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật phiếu nhập. Kiểm tra quyền Inventory.Receipt và trạng thái phiếu.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CancelPurchaseReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (PurchaseReceiptsDataGrid.SelectedItem is not ApiClient.PurchaseReceiptItemModel receipt)
        {
            ShowToast("Hãy chọn phiếu nhập cần hủy.");
            return;
        }
        if (string.Equals(receipt.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Phiếu này đã hủy rồi.");
            return;
        }
        if (!PromptDialog.TryShow(this, $"Hủy / hoàn kho — {receipt.ReceiptNumber}", new[]
        {
            new PromptField("reason", "Lý do hủy / hoàn kho", IsRequired: false, IsMultiline: true)
        }, out var values)) return;
        if (MessageBox.Show(
                $"Xác nhận hủy {receipt.ReceiptNumber}?\nHệ thống sẽ tạo giao dịch hoàn kho âm, giảm tồn kho và giữ nguyên phiếu/chứng từ để truy vết.",
                "Xác nhận hủy / hoàn kho", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.CancelPurchaseReceiptAsync(receipt.Id, values["reason"]))
        {
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
            await LoadInventoryBalancesAsync();
            await LoadInventoryMovementsAsync();
            MessageBox.Show("Đã hủy phiếu và tạo giao dịch hoàn kho. Lịch sử phiếu vẫn được giữ để kiểm tra.", "Đã hủy / hoàn kho", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể hủy vì tồn khả dụng không đủ hoặc phiếu đã phát sinh sử dụng. Hãy xử lý đơn giữ hàng/giao hàng trước.", "Không thể hủy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PurchaseReceiptsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.PurchaseReceiptItemModel)
        {
            EditPurchaseReceiptDialog_Click(sender, e);
        }
    }

    private async void CreateReceiptDialog_Click(object sender, RoutedEventArgs e)
    {
        var suppliersData = await _apiClient.GetSuppliersAsync();
        var fashionVariants = await _apiClient.GetActiveFashionVariantsAsync();
        var warehouses = await _apiClient.GetWarehousesAsync();

        if (suppliersData == null || suppliersData.Items.Count == 0)
        {
            MessageBox.Show("Chưa có nhà cung cấp trong hệ thống. Vui lòng tạo nhà cung cấp trước!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (fashionVariants == null || fashionVariants.Count == 0)
        {
            MessageBox.Show("Chưa có SKU BUY đang hoạt động để nhập thành phẩm. SKU MAKE phải đi qua luồng Sản xuất; nếu cần nhập mua ngoài, hãy tạo SKU với nguồn hàng BUY.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (warehouses == null || warehouses.Count == 0)
        {
            MessageBox.Show("Chưa có kho hoạt động. Vào mục Danh mục kho để tạo kho trước khi lập phiếu nhập.", "Chưa có kho", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var supplierOptions = suppliersData.Items
            .Select(s => new PromptOption(s.Id.ToString(), $"{s.Code} — {s.Name}"))
            .ToList();
        var variantOptions = fashionVariants
            .Select(v => new PromptOption(v.Id.ToString(), $"{v.Sku} — {v.ProductName} ({v.Color}/{v.Size})"))
            .ToList();
        var warehouseOptions = warehouses
            .Select(w => new PromptOption(w.Id.ToString(), $"{w.Code} — {w.Name}{(w.IsDefault ? " (mặc định)" : string.Empty)}"))
            .ToList();

        if (!PromptDialog.TryShow(this, "Lập phiếu nhập kho", new[]
        {
            new PromptField("supplierId", "Nhà cung cấp", Options: supplierOptions),
            new PromptField("warehouseId", "Nhập vào kho", warehouses.FirstOrDefault(w => w.IsDefault)?.Id.ToString() ?? warehouses[0].Id.ToString(), Options: warehouseOptions),
            new PromptField("variantId", "SKU nhập kho", Options: variantOptions),
            new PromptField("quantity", "Số lượng"),
            new PromptField("unitPrice", "Đơn giá nhập"),
            new PromptField("notes", "Ghi chú", IsRequired: false)
        }, out var values)) return;

        if (!int.TryParse(values["quantity"], out var qty) || qty <= 0 ||
            !decimal.TryParse(values["unitPrice"], out var unitPrice) || unitPrice < 0)
        {
            MessageBox.Show("Số lượng phải lớn hơn 0; đơn giá phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedSupplier = suppliersData.Items.First(s => s.Id.ToString() == values["supplierId"]);
        var selectedVariant = fashionVariants.First(v => v.Id.ToString() == values["variantId"]);
        var selectedWarehouse = warehouses.FirstOrDefault(w => w.Id.ToString() == values["warehouseId"]);
        if (selectedWarehouse is null)
        {
            MessageBox.Show("Kho nhập không hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var itemsReq = new List<ApiClient.CreatePurchaseReceiptItemReq>
        {
            new(selectedVariant.Id, qty, unitPrice)
        };

        var attachmentPaths = PickReceiptAttachments();
        var createdReceipt = await _apiClient.CreatePurchaseReceiptAsync(selectedSupplier.Id, values["notes"], itemsReq, selectedWarehouse.Id);
        if (createdReceipt is not null)
        {
            var uploadedCount = 0;
            var failedAttachments = new List<string>();
            foreach (var attachmentPath in attachmentPaths)
            {
                var uploaded = await _apiClient.UploadPurchaseReceiptAttachmentAsync(createdReceipt.Id, attachmentPath);
                if (uploaded is null)
                {
                    failedAttachments.Add(Path.GetFileName(attachmentPath));
                }
                else
                {
                    uploadedCount++;
                }
            }

            var message = $"Đã lập phiếu nhập {createdReceipt.ReceiptNumber} — {qty} chiếc SKU '{selectedVariant.Sku}' vào {selectedWarehouse.Name}.\n" +
                          "Tồn kho và sổ giao dịch đã cập nhật từ API.";
            if (attachmentPaths.Count > 0)
            {
                message += $"\n\nChứng từ Cloudinary: {uploadedCount}/{attachmentPaths.Count} tệp đã lưu.";
                if (failedAttachments.Count > 0)
                    message += $"\nChưa lưu được: {string.Join(", ", failedAttachments)}";
            }

            MessageBox.Show(message, failedAttachments.Count == 0 ? "Thành công" : "Lập phiếu thành công, chứng từ cần thử lại",
                MessageBoxButton.OK, failedAttachments.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
            await LoadInventoryBalancesAsync();
            await LoadInventoryMovementsAsync();
        }
        else
        {
            MessageBox.Show("Lập phiếu nhập kho thất bại. Vui lòng kiểm tra quyền Inventory.Receipt.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static IReadOnlyList<string> PickReceiptAttachments()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh/PDF hóa đơn đính kèm (có thể bỏ qua)",
            Filter = "Ảnh hoặc PDF (*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf)|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    private void OpenReceiptAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Phiếu này chưa có ảnh/PDF hóa đơn đính kèm.", "Chứng từ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở chứng từ: {ex.Message}", "Lỗi mở chứng từ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void UploadReceiptAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid receiptId })
            return;

        var attachmentPaths = PickReceiptAttachments();
        if (attachmentPaths.Count == 0)
            return;

        var uploadedCount = 0;
        var failedAttachments = new List<string>();
        await RunWithBusyAsync("Đang tải chứng từ lên Cloudinary...", async () =>
        {
            foreach (var attachmentPath in attachmentPaths)
            {
                var uploaded = await _apiClient.UploadPurchaseReceiptAttachmentAsync(receiptId, attachmentPath);
                if (uploaded is null)
                    failedAttachments.Add(Path.GetFileName(attachmentPath));
                else
                    uploadedCount++;
            }

            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
        });

        var message = $"Đã lưu {uploadedCount}/{attachmentPaths.Count} tệp chứng từ lên Cloudinary.";
        if (failedAttachments.Count > 0)
            message += $"\nChưa lưu được: {string.Join(", ", failedAttachments)}";

        MessageBox.Show(message, failedAttachments.Count == 0 ? "Thành công" : "Cần thử lại",
            MessageBoxButton.OK, failedAttachments.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void InventorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        InventorySearchPlaceholder.Visibility = string.IsNullOrEmpty(InventorySearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-balances", "Đang tìm tồn kho...", () => LoadInventoryBalancesAsync(InventorySearchBox.Text));
    }

    private void InventoryWarehouseSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || _isInventoryWarehouseSelectionSyncing)
            return;

        _ = RunWithBusyAsync("Đang tải tồn kho theo kho đã chọn...", async () =>
        {
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
        });
    }

    private async void RefreshWarehouses_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải danh mục kho...", LoadWarehousesAsync);
    }

    private async void CreateWarehouseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Thêm kho", WarehouseFields(), out var values)) return;

        var success = await _apiClient.CreateWarehouseAsync(
            values["code"], values["name"], values["address"], values["isActive"] == "true", values["isDefault"] == "true");
        if (success)
        {
            await LoadWarehousesAsync();
            MessageBox.Show("Đã tạo kho. Nếu đây là kho mặc định, phiếu nhập mới sẽ dùng kho này.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể tạo kho. Kiểm tra mã không trùng và quyền Inventory.Adjust.", "Không thể tạo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditWarehouseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (WarehousesDataGrid.SelectedItem is not ApiClient.WarehouseItem warehouse)
        {
            ShowToast("Hãy chọn kho cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa kho — {warehouse.Code}", WarehouseFields(warehouse), out var values)) return;

        var success = await _apiClient.UpdateWarehouseAsync(
            warehouse.Id, values["code"], values["name"], values["address"], values["isActive"] == "true", values["isDefault"] == "true");
        if (success)
        {
            await LoadWarehousesAsync();
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            MessageBox.Show("Đã cập nhật kho.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật kho. Kho mặc định phải đang hoạt động; hãy chọn kho mặc định khác trước khi ngừng dùng kho hiện tại.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteWarehouse_Click(object sender, RoutedEventArgs e)
    {
        if (WarehousesDataGrid.SelectedItem is not ApiClient.WarehouseItem warehouse)
        {
            ShowToast("Hãy chọn kho cần xóa.");
            return;
        }

        if (MessageBox.Show(
                $"Xóa kho '{warehouse.Code} — {warehouse.Name}'?\nChỉ xóa được kho chưa có tồn kho hay chứng từ. Kho đã phát sinh phải Ngừng dùng để giữ lịch sử.",
                "Xác nhận xóa kho", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteWarehouseAsync(warehouse.Id))
        {
            await LoadWarehousesAsync();
            MessageBox.Show("Đã xóa kho chưa phát sinh dữ liệu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa kho mặc định hoặc kho đã có tồn/chứng từ. Hãy chọn Sửa để ngừng dùng kho đó.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void WarehousesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.WarehouseItem)
        {
            EditWarehouseDialog_Click(sender, e);
        }
    }

    private static IReadOnlyList<PromptField> WarehouseFields(ApiClient.WarehouseItem? warehouse = null) => new[]
    {
        new PromptField("code", "Mã kho", warehouse?.Code),
        new PromptField("name", "Tên kho", warehouse?.Name),
        new PromptField("address", "Địa chỉ", warehouse?.Address, IsRequired: false, IsMultiline: true),
        new PromptField("isActive", "Trạng thái", warehouse is null || warehouse.IsActive ? "true" : "false", Options: new[]
        {
            new PromptOption("true", "Đang hoạt động"),
            new PromptOption("false", "Ngừng dùng (giữ lịch sử)")
        }),
        new PromptField("isDefault", "Dùng làm kho mặc định", warehouse?.IsDefault == true ? "true" : "false", Options: new[]
        {
            new PromptOption("false", "Không"),
            new PromptOption("true", "Có — dùng khi lập phiếu nhập")
        })
    };

    private async void RefreshInventoryMovements_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải nhật ký giao dịch kho...", () => LoadInventoryMovementsAsync());
    }

    private async void RefreshSelectedInventoryMovements_Click(object sender, RoutedEventArgs e)
    {
        var balance = InventoryBalancesDataGrid.SelectedItem as ApiClient.InventoryBalanceItem;
        await RunWithBusyAsync(
            balance is null ? "Đang tải toàn bộ nhật ký giao dịch kho..." : $"Đang tải nhật ký của SKU {balance.Sku}...",
            () => LoadInventoryMovementsAsync(balance?.ProductVariantId));
    }

    private void RefreshInventory_Click(object sender, RoutedEventArgs e)
    {
        _ = RunWithBusyAsync("Đang tải tồn kho...", () => LoadInventoryBalancesAsync(InventorySearchBox.Text));
    }

    private async void StockAdjustmentDialog_Click(object sender, RoutedEventArgs e)
    {
        if (InventoryBalancesDataGrid.SelectedItem is not ApiClient.InventoryBalanceItem balance)
        {
            MessageBox.Show("Vui lòng chọn 1 dòng tồn kho trong bảng trên để điều chỉnh kiểm kê.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Điều chỉnh tồn kho — {balance.Sku}", new[]
        {
            new PromptField("delta", "Biến động số lượng (+ nhập thêm, - giảm đi)"),
            new PromptField("notes", "Lý do kiểm kê")
        }, out var values)) return;

        if (!int.TryParse(values["delta"], out var delta) || delta == 0)
        {
            MessageBox.Show("Biến động phải là số nguyên khác 0.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var success = await _apiClient.AdjustStockAsync(balance.ProductVariantId, delta, values["notes"], SelectedInventoryWarehouseId);
        if (success)
        {
            MessageBox.Show($"Đã điều chỉnh {delta:+#;-#;0} cho SKU '{balance.Sku}'. Sổ giao dịch đã ghi nhận.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
        }
        else
        {
            MessageBox.Show("Điều chỉnh tồn kho thất bại. Vui lòng kiểm tra quyền Inventory.Adjust.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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

    public void Dispose()
    {
        foreach (var cts in _searchDebouncers.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _searchDebouncers.Clear();
        _apiClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
