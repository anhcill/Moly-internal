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
    // ── Day 11: Attendance & Excel Import Loaders & Handlers ──

    private async Task LoadAttendanceAsync()
    {
        try
        {
            DateOnly? fromDate = ViewAttendanceContainer.FromDate;
            DateOnly? toDate = ViewAttendanceContainer.ToDate;

            var records = await _apiClient.GetAttendanceRecordsAsync(fromDate, toDate, businessSegment: _activeSegment);
            if (records != null)
            {
                ViewAttendanceContainer.SetRecords(records.Items);
                SetLoadedStatus("Chấm công", records.Items.Count);
            }
            else
            {
                ViewAttendanceContainer.SetRecords(null);
                SetViewStatus("Không tải được dữ liệu chấm công. Vui lòng thử lại.", isError: true);
            }

            var summary = await _apiClient.GetAttendanceSummaryAsync(fromDate, toDate, businessSegment: _activeSegment);
            if (summary != null)
            {
                ViewAttendanceContainer.SetSummary(summary);
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

}
