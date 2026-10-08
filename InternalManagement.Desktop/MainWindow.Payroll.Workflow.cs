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
            ShowToast(_apiClient.LastManagementOperationError ?? "Không hủy được kỳ lương: phản hồi máy chủ thiếu dữ liệu.", true);
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
                MessageBox.Show(_apiClient.LastManagementOperationError ?? "Tính lương thất bại: phản hồi máy chủ thiếu kết quả.", "Lỗi tính lương", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(_apiClient.LastManagementOperationError ?? "Gửi duyệt thất bại: phản hồi máy chủ thiếu kết quả.", "Lỗi gửi duyệt", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(_apiClient.LastManagementOperationError ?? "Phê duyệt thất bại: phản hồi máy chủ thiếu kết quả.", "Lỗi phê duyệt", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(_apiClient.LastManagementOperationError ?? "Xác nhận chi lương thất bại: phản hồi máy chủ thiếu kết quả.", "Lỗi xác nhận chi", MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(_apiClient.LastManagementOperationError ?? "Phát hành phiếu lương thất bại: phản hồi máy chủ thiếu kết quả.", "Lỗi phát hành", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshPayroll_Click(object sender, RoutedEventArgs e)
    {
        _ = RunWithBusyAsync("Đang tải bảng tính lương...", () => LoadPayrollPeriodsAsync());
    }

}
