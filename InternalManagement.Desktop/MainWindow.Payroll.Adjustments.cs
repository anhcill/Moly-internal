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
using InternalManagement.Desktop.Views;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private async void OpenPayrollWorkEntries_Click(object sender, RoutedEventArgs e)
    {
        var period = GetSelectedPayrollPeriod();
        if (period is null)
        {
            ShowToast("Hãy chọn kỳ lương trước khi ghi nhận đầu việc.", true);
            return;
        }

        var dialog = new PayrollWorkEntriesWindow(_apiClient, period, _activeSegment) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await LoadPayrollPeriodsAsync(period.Id);
            PayrollFunctionTabs.SelectedIndex = 1;
            ShowToast("Đã cập nhật đầu việc và tổng tiền công trong kỳ lương.");
        }
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

        if (employee.EmploymentType == 1 && employee.PartTimeCalculationMethod == 2)
        {
            ShowToast("Nhân sự theo đầu việc có đơn giá riêng từng dòng. Hãy dùng Đầu việc / hoa hồng.", true);
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

}
