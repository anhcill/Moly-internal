using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private bool _attendanceReportLoading;

    private async void DetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != DetailTabs || DetailTabs.SelectedItem != AttendanceReportTab || _detail is null)
            return;

        await LoadAttendanceReportAsync();
    }

    private async void RefreshAttendanceReport_Click(object sender, RoutedEventArgs e)
        => await LoadAttendanceReportAsync();

    private async Task LoadAttendanceReportAsync()
    {
        if (_attendanceReportLoading || _detail is null) return;

        var fromDate = AttendanceReportFromDatePicker.SelectedDate is { } from
            ? DateOnly.FromDateTime(from)
            : (DateOnly?)null;
        var toDate = AttendanceReportToDatePicker.SelectedDate is { } to
            ? DateOnly.FromDateTime(to)
            : (DateOnly?)null;
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        {
            AttendanceReportStatusText.Text = "Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.";
            return;
        }

        _attendanceReportLoading = true;
        AttendanceReportStatusText.Text = "Đang tải điểm danh từ hệ thống...";
        try
        {
            var report = await _apiClient.GetCscaAttendanceReportAsync(_classId, fromDate, toDate);
            if (report is null)
            {
                AttendanceReportStatusText.Text = "Không tải được báo cáo điểm danh. Kiểm tra quyền truy cập hoặc thử lại.";
                AttendanceReportSessionsGrid.ItemsSource = null;
                AttendanceReportStudentsGrid.ItemsSource = null;
                return;
            }

            ReportStudentCountText.Text = report.TotalStudents.ToString(CultureInfo.CurrentCulture);
            ReportSessionCountText.Text = report.TotalSessions.ToString(CultureInfo.CurrentCulture);
            ReportPresentCountText.Text = (report.PresentCount + report.LateCount).ToString(CultureInfo.CurrentCulture);
            ReportAbsentCountText.Text = report.AbsentCount.ToString(CultureInfo.CurrentCulture);
            ReportExcusedCountText.Text = report.ExcusedCount.ToString(CultureInfo.CurrentCulture);
            ReportAttendanceRateText.Text = $"{report.AttendanceRatePercent:0.#}%";
            AttendanceReportSessionsGrid.ItemsSource = report.Sessions.Select(AttendanceSessionReportRow.From).ToList();
            AttendanceReportStudentsGrid.ItemsSource = report.Students;
            AttendanceReportStatusText.Text = report.LastSyncedAt is { } syncedAt
                ? $"Đồng bộ gần nhất: {syncedAt.ToLocalTime():dd/MM/yyyy HH:mm}"
                : "Chưa nhận dữ liệu điểm danh từ Web CSCA cho khoảng ngày này.";
        }
        catch (Exception ex)
        {
            AttendanceReportStatusText.Text = $"Không tải được báo cáo: {ex.Message}";
        }
        finally
        {
            _attendanceReportLoading = false;
        }
    }

    private sealed record AttendanceSessionReportRow(
        string DateLabel,
        string TimeLabel,
        int TotalRecords,
        int PresentCount,
        int LateCount,
        int AbsentCount,
        int ExcusedCount,
        string SyncLabel)
    {
        public static AttendanceSessionReportRow From(ApiClient.CscaAttendanceSessionSummaryItem session) => new(
            session.LessonDate.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture),
            $"{session.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture)}–{session.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture)}",
            session.TotalRecords,
            session.PresentCount,
            session.LateCount,
            session.AbsentCount,
            session.ExcusedCount,
            session.LastSyncedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture) ?? "—");
    }
}
