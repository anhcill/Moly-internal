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
    // ── Quick Sync Trigger Actions ──

    private async void QuickSync_Click(object sender, RoutedEventArgs e)
    {
        if (ViewLmsContainer.Visibility == Visibility.Visible)
        {
            await SyncLmsNowAsync();
            return;
        }

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

}
