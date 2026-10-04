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

}
