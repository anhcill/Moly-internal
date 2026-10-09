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
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop;

public partial class MainWindow : Window, IDisposable
{
    private readonly ApiClient _apiClient;
    private readonly DashboardViewModel _dashboardViewModel;
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
    private List<ApiClient.CscaClassItem> _allCscaFinanceClasses = new();
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
        _dashboardViewModel = new DashboardViewModel(_apiClient);
        _sessionStore = new SessionStore();
        InitializeComponent();
        ApplySidebarNavigationContent(false);
        ViewEmployeesContainer.SetViewModel(new EmployeesViewModel(_apiClient));
        LoginContainer.ActionRequested += DispatchFeatureViewAction;
        ViewDashboardContainer.DataContext = _dashboardViewModel;
        ViewDashboardContainer.OpenCoursesRequested += GoToCourses_Click;
        ViewDashboardContainer.OpenQuestionsRequested += GoToQuestions_Click;
        ViewQuestionsContainer.SetViewModel(new QuestionBankViewModel(_apiClient));
        ViewQuestionsContainer.SearchChanged += (_, _) =>
        {
            if (_isUiReady)
                _ = DebounceSearchAsync("questions", "Đang tìm câu hỏi...", () => LoadQuestionsAsync(ViewQuestionsContainer.SearchText));
        };
        ViewQuestionsContainer.RefreshRequested += (_, _) =>
            _ = RunWithBusyAsync("Đang tải ngân hàng câu hỏi...", () => LoadQuestionsAsync(ViewQuestionsContainer.SearchText));
        ViewQuestionsContainer.CreateRequested += CreateQuestionDialog_Click;
        ViewQuestionsContainer.PublishRequested += PublishQuestion_Click;
        ViewInterviewContainer.SetViewModel(new InterviewCustomersViewModel(_apiClient));
        ViewInterviewContainer.SearchChanged += (_, _) =>
        {
            if (_isUiReady)
                _ = DebounceSearchAsync("interview", "Đang tìm khách hàng phỏng vấn...", () => LoadInterviewCustomersAsync(ViewInterviewContainer.SearchText));
        };
        ViewInterviewContainer.RefreshRequested += (_, _) =>
            _ = RunWithBusyAsync("Đang tải danh sách Mock Interview...", () => LoadInterviewCustomersAsync(ViewInterviewContainer.SearchText));
        ViewInterviewContainer.SyncRequested += SyncInterviewFromWebsite_Click;
        ViewInterviewContainer.CreateRequested += CreateInterviewCustomerDialog_Click;
        ViewCustomersContainer.SetViewModel(new CustomersViewModel(_apiClient));
        ViewCustomersContainer.SearchChanged += (_, _) =>
        {
            if (_isUiReady)
                _ = DebounceSearchAsync("customers", "Đang tìm học viên...", () => LoadCustomersAsync(ViewCustomersContainer.SearchText));
        };
        ViewCustomersContainer.RefreshRequested += (_, _) =>
            _ = RunWithBusyAsync("Đang tải danh sách học viên & khách hàng...", () => LoadCustomersAsync(ViewCustomersContainer.SearchText));
        ViewSyncContainer.SyncCoursesRequested += TriggerSyncCourses_Click;
        ViewSyncContainer.SyncCustomersRequested += TriggerSyncCustomers_Click;
        ViewSyncContainer.RefreshRunsRequested += RefreshSyncRuns_Click;
        ViewSyncContainer.ExportRunsRequested += ExportSyncRuns_Click;
        ViewSyncContainer.RefreshDeadLettersRequested += RefreshDeadLetters_Click;
        ViewSyncContainer.RetryDeadLetterRequested += RetryDeadLetter_Click;
        ViewInternalDataContainer.SearchChanged += InternalDataSearch_TextChanged;
        ViewInternalDataContainer.RefreshRequested += RefreshInternalData_Click;
        ViewInternalDataContainer.CreateRequested += CreateInternalData_Click;
        ViewInternalDataContainer.EditRequested += EditInternalData_Click;
        ViewInternalDataContainer.DeleteRequested += DeleteInternalData_Click;
        ViewAttendanceContainer.FilterRequested += FilterAttendance_Click;
        ViewAttendanceContainer.RefreshRequested += RefreshAttendance_Click;
        ViewAttendanceContainer.DownloadTemplateRequested += DownloadAttendanceTemplate_Click;
        ViewAttendanceContainer.ImportRequested += ImportAttendance_Click;
        ViewAttendanceContainer.RecordRequested += RecordAttendance_Click;
        ViewCompanyFinanceContainer.RefreshRequested += RefreshCompanyFinance_Click;
        ViewCoursesContainer.ActionRequested += DispatchFeatureViewAction;
        ViewEmployeesContainer.BlacklistRequested += BlacklistEmployee_Click;
        ViewEmployeesContainer.CreateRequested += CreateEmployee_Click;
        ViewEmployeesContainer.DeleteRequested += DeleteEmployee_Click;
        ViewEmployeesContainer.EditRequested += EditEmployee_Click;
        ViewEmployeesContainer.OpenCvRequested += OpenEmployeeCv_Click;
        ViewEmployeesContainer.AssignCvFileRequested += QuickAssignCvFile_Click;
        ViewEmployeesContainer.AssignCvUrlRequested += QuickAssignCvUrl_Click;
        ViewEmployeesContainer.UpdateProfileRequested += QuickUpdateProfile_Click;
        ViewEmployeesContainer.ReactivateRequested += ReactivateEmployee_Click;
        ViewEmployeesContainer.RefreshRequested += RefreshEmployees_Click;
        ViewEmployeesContainer.ResignRequested += ResignEmployee_Click;
        ViewEmployeesContainer.UpdateBlacklistReasonRequested += UpdateBlacklistReason_Click;
        ViewEmployeesContainer.UpdateResignedReasonRequested += UpdateResignedReason_Click;
        ViewEmployeesContainer.OpenEmployeeRequested += EmployeesDataGrid_MouseDoubleClick;
        ViewEmployeesContainer.DepartmentFilterChanged += EmployeeDeptFilter_SelectionChanged;
        ViewEmployeesContainer.TabChanged += EmployeesTabs_SelectionChanged;
        ViewEmployeesContainer.SearchChanged += EmployeeSearch_TextChanged;
        ViewLmsContainer.ActionRequested += DispatchFeatureViewAction;
        ViewPayrollContainer.AddPayrollAdjustmentRequested += AddPayrollAdjustment_Click;
        ViewPayrollContainer.ApprovePayrollRequested += ApprovePayroll_Click;
        ViewPayrollContainer.CalculatePayrollRequested += CalculatePayroll_Click;
        ViewPayrollContainer.CancelPayrollRequested += CancelPayroll_Click;
        ViewPayrollContainer.CreatePayrollPeriodRequested += CreatePayrollPeriod_Click;
        ViewPayrollContainer.DeleteSelectedPayrollAdjustmentRequested += DeleteSelectedPayrollAdjustment_Click;
        ViewPayrollContainer.EditPayrollBaseSalaryRequested += EditPayrollBaseSalary_Click;
        ViewPayrollContainer.EditPayrollTeachingHoursRequested += EditPayrollTeachingHours_Click;
        ViewPayrollContainer.ExportPayrollExcelRequested += ExportPayrollExcel_Click;
        ViewPayrollContainer.MarkPaidRequested += MarkPaid_Click;
        ViewPayrollContainer.OpenPayrollWorkEntriesRequested += OpenPayrollWorkEntries_Click;
        ViewPayrollContainer.PublishPayrollRequested += PublishPayroll_Click;
        ViewPayrollContainer.RefreshPayrollRequested += RefreshPayroll_Click;
        ViewPayrollContainer.RefreshPayrollMonthlySummaryRequested += RefreshPayrollMonthlySummary_Click;
        ViewPayrollContainer.SubmitReviewRequested += SubmitReview_Click;
        ViewPayrollContainer.ViewPayslipDetailRequested += ViewPayslipDetail_Click;
        ViewPayrollContainer.PayrollAdjustmentEmployeesDoubleClickRequested += PayrollAdjustmentEmployees_MouseDoubleClick;
        ViewPayrollContainer.PayslipsDataGridDoubleClickRequested += PayslipsDataGrid_MouseDoubleClick;
        ViewPayrollContainer.PayrollAdjustmentEmployeesSelectionChanged += PayrollAdjustmentEmployees_SelectionChanged;
        ViewPayrollContainer.PayrollAdjustmentsSelectionChanged += PayrollAdjustments_SelectionChanged;
        ViewPayrollContainer.PayrollDeptFilterSelectionChanged += PayrollDeptFilter_SelectionChanged;
        ViewPayrollContainer.PayrollFunctionTabsSelectionChanged += PayrollFunctionTabs_SelectionChanged;
        ViewPayrollContainer.PayrollMonthlySummarySelectionChanged += PayrollMonthlySummary_SelectionChanged;
        ViewPayrollContainer.PayrollPeriodSelectorSelectionChanged += PayrollPeriodSelector_SelectionChanged;
        ViewPayrollContainer.PayrollSummaryYearSelectionChanged += PayrollSummaryYear_SelectionChanged;
        ViewPayrollContainer.PayrollSearchTextChanged += PayrollSearch_TextChanged;
        ViewFashionContainer.ActionRequested += DispatchFeatureViewAction;
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
