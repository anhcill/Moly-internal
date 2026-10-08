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
                ShowToast(_apiClient.LastManagementOperationError ?? "Không tạo được kỳ lương: phản hồi máy chủ thiếu dữ liệu.", isError: true);
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

        var departments = EmployeeViewModel.Departments.Count > 0
            ? EmployeeViewModel.Departments
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

}
