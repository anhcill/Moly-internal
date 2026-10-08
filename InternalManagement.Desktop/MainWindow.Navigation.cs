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
        SidebarBrandHeader.Margin = isCollapsed ? new Thickness(0, 0, 0, 10) : new Thickness(2, 0, 0, 10);

        if (isCollapsed)
        {
            SidebarBrandDetails.Visibility = Visibility.Collapsed;
            SidebarBrandContainer.HorizontalAlignment = HorizontalAlignment.Center;
            SidebarBrandLogo.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetRow(SidebarToggleButton, 1);
            Grid.SetColumn(SidebarToggleButton, 0);
            Grid.SetColumnSpan(SidebarToggleButton, 2);
            SidebarToggleButton.HorizontalAlignment = HorizontalAlignment.Center;
            SidebarToggleButton.Margin = new Thickness(0, 8, 0, 0);
            SidebarToggleButton.Content = "›";
            SidebarToggleButton.ToolTip = "Mở rộng thanh chức năng";

            UserProfileDetails.Visibility = Visibility.Collapsed;
            UserProfileCard.Padding = new Thickness(8, 10, 8, 10);
            UserAvatarBadge.HorizontalAlignment = HorizontalAlignment.Center;
            UserAvatarBadge.Margin = new Thickness(0);
            Grid.SetRow(LogoutButton, 1);
            Grid.SetColumn(LogoutButton, 0);
            Grid.SetColumnSpan(LogoutButton, 3);
            LogoutButton.HorizontalAlignment = HorizontalAlignment.Center;
            LogoutButton.Margin = new Thickness(0, 8, 0, 0);
            LogoutButton.Width = 32;
            LogoutButton.Height = 32;
            LogoutButton.Padding = new Thickness(0);
        }
        else
        {
            SidebarBrandDetails.Visibility = Visibility.Visible;
            SidebarBrandContainer.HorizontalAlignment = HorizontalAlignment.Left;
            SidebarBrandLogo.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(SidebarToggleButton, 0);
            Grid.SetColumn(SidebarToggleButton, 1);
            Grid.SetColumnSpan(SidebarToggleButton, 1);
            SidebarToggleButton.HorizontalAlignment = HorizontalAlignment.Right;
            SidebarToggleButton.Margin = new Thickness(8, 0, 0, 0);
            SidebarToggleButton.Content = "‹";
            SidebarToggleButton.ToolTip = "Thu gọn thanh chức năng";

            UserProfileDetails.Visibility = Visibility.Visible;
            UserProfileCard.Padding = new Thickness(12);
            UserAvatarBadge.HorizontalAlignment = HorizontalAlignment.Left;
            UserAvatarBadge.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetRow(LogoutButton, 0);
            Grid.SetColumn(LogoutButton, 2);
            Grid.SetColumnSpan(LogoutButton, 1);
            LogoutButton.HorizontalAlignment = HorizontalAlignment.Right;
            LogoutButton.Margin = new Thickness(0);
            LogoutButton.Width = double.NaN;
            LogoutButton.Height = double.NaN;
            LogoutButton.Padding = new Thickness(6);
        }

        ActiveSegmentCard.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SegmentSwitcher.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;

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
            (Item: NavDashboard, Label: "Tổng quan"),
            (Item: NavCompanyFinance, Label: "Dòng tiền & lợi nhuận"),
            (Item: NavSync, Label: "Nhật ký hệ thống"),
            (Item: NavCustomers, Label: "Học viên & khách hàng"),
            (Item: NavCourses, Label: "Khóa học & lớp học"),
            (Item: NavCsca, Label: "Lớp học CSCA"),
            (Item: NavLms, Label: "Đồng bộ Web CSCA"),
            (Item: NavInterview, Label: "Mock Interview"),
            (Item: NavQuestions, Label: "Ngân hàng đề & câu hỏi"),
            (Item: NavTechResources, Label: "Kho đề & tài liệu"),
            (Item: NavTechInternalCustomers, Label: "Đối tác & khách hàng nội bộ"),
            (Item: NavTechEmployees, Label: "Nhân sự & CV"),
            (Item: NavTechEmployeesWorking, Label: "Đang làm việc"),
            (Item: NavTechEmployeesProfiles, Label: "Hồ sơ & CV"),
            (Item: NavTechEmployeesMissingCv, Label: "Thiếu CV"),
            (Item: NavTechEmployeesResigned, Label: "Đã nghỉ việc"),
            (Item: NavTechEmployeesBlacklist, Label: "Blacklist"),
            (Item: NavTechAttendance, Label: "Chấm công"),
            (Item: NavTechPayroll, Label: "Bảng lương"),
            (Item: NavTechPayrollCreate, Label: "Tạo kỳ lương"),
            (Item: NavTechPayrollTable, Label: "Tính & phiếu lương"),
            (Item: NavTechPayrollAdjustments, Label: "Chỉnh lương · thưởng/phạt"),
            (Item: NavTechPayrollReview, Label: "Xem xét & gửi duyệt"),
            (Item: NavTechPayrollWorkflow, Label: "Duyệt · chi · phát hành"),
            (Item: NavTechPayrollMonthly, Label: "Tổng hợp theo tháng"),
            (Item: NavFashionResources, Label: "Kế hoạch & mẫu thiết kế"),
            (Item: NavFashion, Label: "Sản phẩm · kho · đơn hàng"),
            (Item: NavFashionProducts, Label: "Sản phẩm"),
            (Item: NavFashionVariants, Label: "Biến thể & SKU"),
            (Item: NavFashionSuppliers, Label: "Nhà cung cấp"),
            (Item: NavFashionReceipts, Label: "Phiếu nhập kho"),
            (Item: NavFashionManufacturing, Label: "Sản xuất & giá thành"),
            (Item: NavFashionBalances, Label: "Tồn kho"),
            (Item: NavFashionMovements, Label: "Nhật ký kho"),
            (Item: NavFashionWarehouses, Label: "Danh mục kho"),
            (Item: NavFashionOrders, Label: "Đơn hàng"),
            (Item: NavFashionInternalCustomers, Label: "Khách hàng & đối tác nội bộ"),
            (Item: NavFashionEmployees, Label: "Nhân sự & CV"),
            (Item: NavFashionEmployeesWorking, Label: "Đang làm việc"),
            (Item: NavFashionEmployeesProfiles, Label: "Hồ sơ & CV"),
            (Item: NavFashionEmployeesMissingCv, Label: "Thiếu CV"),
            (Item: NavFashionEmployeesResigned, Label: "Đã nghỉ việc"),
            (Item: NavFashionEmployeesBlacklist, Label: "Blacklist"),
            (Item: NavFashionAttendance, Label: "Chấm công"),
            (Item: NavFashionPayroll, Label: "Bảng lương"),
            (Item: NavFashionPayrollCreate, Label: "Tạo kỳ lương"),
            (Item: NavFashionPayrollTable, Label: "Tính & phiếu lương"),
            (Item: NavFashionPayrollAdjustments, Label: "Chỉnh lương · thưởng/phạt"),
            (Item: NavFashionPayrollReview, Label: "Xem xét & gửi duyệt"),
            (Item: NavFashionPayrollWorkflow, Label: "Duyệt · chi · phát hành"),
            (Item: NavFashionPayrollMonthly, Label: "Tổng hợp theo tháng")
        };

        foreach (var navigationItem in navigationItems)
        {
            var expandedLabel = navigationItem.Label;
            navigationItem.Item.Tag = NavigationIconGlyph(navigationItem.Item.Name);
            navigationItem.Item.Content = isCollapsed ? string.Empty : expandedLabel;
            navigationItem.Item.ToolTip = isCollapsed ? expandedLabel : null;
            navigationItem.Item.HorizontalContentAlignment = isCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            navigationItem.Item.Padding = isCollapsed ? new Thickness(0) : new Thickness(11, 0, 11, 0);
            navigationItem.Item.FontSize = isCollapsed ? 14 : 12;
        }
    }

    private static string NavigationIconGlyph(string name) => name switch
    {
        "NavDashboard" => "\uE80F",
        "NavCompanyFinance" => "\uE9F9",
        "NavSync" => "\uE895",
        "NavCustomers" or "NavTechInternalCustomers" or "NavFashionInternalCustomers" => "\uE716",
        "NavCourses" or "NavCsca" => "\uE7BE",
        "NavLms" => "\uE71B",
        "NavInterview" => "\uE77B",
        "NavQuestions" => "\uE8FD",
        "NavTechResources" or "NavFashionResources" => "\uE8B7",
        "NavTechEmployees" or "NavFashionEmployees" => "\uE77B",
        "NavTechAttendance" or "NavFashionAttendance" => "\uE823",
        "NavTechPayroll" or "NavFashionPayroll" => "\uE8EF",
        "NavFashion" => "\uE719",
        _ when name.Contains("Employees") => "\uE8D4",
        _ when name.Contains("Payroll") => "\uE8A5",
        _ when name.Contains("Fashion") => "\uE7B8",
        _ => "\uE8A5"
    };

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
            ViewEmployeesContainer.SelectedTabIndex = index;
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
        ViewEmployeesContainer.SelectedTabIndex = 0;
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
        QuickSyncButton.Content = sender == NavLms
            ? "↻ Đồng bộ khóa đã chọn"
            : "↻ Nhập khóa học từ Website";

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
            ViewHeaderTitle.Text = "Đồng bộ Web CSCA Course";
            ViewHeaderSubtitle.Text = "Liên kết khóa học, tự động kích hoạt tài khoản học viên khi đóng học phí và theo dõi tiến trình gửi Web";
            _ = LoadViewOnceAsync("csca-lms", "Đang tải trạng thái đồng bộ Web CSCA...", LoadLmsIntegrationAsync, "Kiểm tra liên kết khóa học và tài khoản học viên Web");
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
        var title = isCustomers
            ? $"Danh mục khách hàng · {ActiveSegmentName}"
            : _activeSegment == FashionSegment
                ? "Kế hoạch & mẫu thiết kế · Thời trang"
                : "Kho đề & tài liệu · Công nghệ - Giáo dục";
        var help = $"Dữ liệu thuộc riêng mảng {ActiveSegmentName}; hệ thống không trộn với mảng còn lại.";
        var canManage = HasPermission(_currentUser, isCustomers ? "Permissions.InternalCustomers.Manage" : "Permissions.InternalResources.Manage");
        ViewInternalDataContainer.Configure(isCustomers, title, help, canManage);
        ViewHeaderTitle.Text = isCustomers ? "Danh mục khách hàng" : (_activeSegment == FashionSegment ? "Kế hoạch & mẫu thiết kế" : "Kho đề & tài liệu");
        ViewHeaderSubtitle.Text = help;
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

}
