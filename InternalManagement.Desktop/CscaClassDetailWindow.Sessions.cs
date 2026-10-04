using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private async Task LoadLessonSessionsAsync()
    {
        var sessions = await _apiClient.GetCscaLessonSessionsAsync(_classId);
        LessonSessionsDataGrid.ItemsSource = sessions?.Select(LessonSessionRow.From).ToList() ?? [];
        LessonAttendanceDataGrid.ItemsSource = null;
        AttendanceSessionText.Text = sessions is null
            ? "Không tải được buổi học. Hãy thử tải lại chi tiết lớp."
            : sessions.Count == 0
                ? "Chưa có buổi học theo ngày. Dùng “Tạo từ lịch tuần” hoặc thêm buổi riêng."
                : "Chọn một buổi học ở phía trên để điểm danh.";
    }

    private async void AddLessonSession_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Thêm buổi học theo ngày", await LessonSessionFieldsAsync(), out var values)) return;
        if (!TryLessonSessionValues(values, out var lessonDate, out var startTime, out var endTime, out var classroomId)) return;

        await SaveLessonSessionAsync(
            () => _apiClient.CreateCscaLessonSessionAsync(_classId, lessonDate, startTime, endTime, classroomId,
                Null(values["meetingUrl"]), Null(values["notes"]), values["status"]),
            "Đã tạo buổi học theo ngày.");
    }

    private async void GenerateLessonSessions_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo buổi học từ lịch tuần", new[]
        {
            new PromptField("fromDate", "Từ ngày (yyyy-MM-dd)", DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new PromptField("toDate", "Đến ngày (yyyy-MM-dd)", DateOnly.FromDateTime(DateTime.Today.AddMonths(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        }, out var values)) return;
        if (!TryDateOnly(values["fromDate"], out var fromDate) || !TryDateOnly(values["toDate"], out var toDate))
        {
            ShowInvalid("Ngày phải đúng định dạng yyyy-MM-dd.");
            return;
        }

        SetBusy(true, "Đang tạo buổi học từ lịch tuần...");
        try
        {
            var created = await _apiClient.GenerateCscaLessonSessionsAsync(_classId, fromDate, toDate);
            if (!created.HasValue)
            {
                ShowInvalid("Không thể tạo buổi học. Kiểm tra lịch tuần, phòng học và các xung đột.");
                return;
            }

            await LoadLessonSessionsAsync();
            LoadingText.Text = created.Value == 0 ? "Không có buổi mới; các ngày trong khoảng đã được tạo." : $"Đã tạo {created.Value} buổi học từ lịch tuần.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể tạo buổi học:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, LoadingText.Text);
        }
    }

    private async void EditLessonSession_Click(object sender, RoutedEventArgs e)
    {
        if (LessonSessionsDataGrid.SelectedItem is not LessonSessionRow session)
        {
            ShowInvalid("Hãy chọn buổi học cần đổi lịch hoặc chỉnh sửa.");
            return;
        }
        if (!PromptDialog.TryShow(this, "Đổi lịch / sửa buổi học", await LessonSessionFieldsAsync(session), out var values)) return;
        if (!TryLessonSessionValues(values, out var lessonDate, out var startTime, out var endTime, out var classroomId)) return;

        await SaveLessonSessionAsync(
            () => _apiClient.UpdateCscaLessonSessionAsync(_classId, session.Id, lessonDate, startTime, endTime, classroomId,
                Null(values["meetingUrl"]), Null(values["notes"]), values["status"]),
            "Đã cập nhật buổi học.");
    }

    private void LessonSessionsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LessonSessionsDataGrid.SelectedItem is LessonSessionRow)
            EditLessonSession_Click(sender, e);
    }

    private async void CancelLessonSession_Click(object sender, RoutedEventArgs e)
    {
        if (LessonSessionsDataGrid.SelectedItem is not LessonSessionRow session)
        {
            ShowInvalid("Hãy chọn buổi học cần hủy.");
            return;
        }
        if (MessageBox.Show(this, $"Hủy buổi học ngày {session.LessonDate:dd/MM/yyyy}? Dữ liệu điểm danh sẽ được giữ để tra cứu.", "Xác nhận hủy buổi", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveLessonSessionAsync(
            () => _apiClient.UpdateCscaLessonSessionAsync(_classId, session.Id, session.LessonDate, session.StartTime, session.EndTime,
                session.ClassroomId, session.MeetingUrl, session.Notes, "Cancelled"),
            "Đã hủy buổi học.");
    }

    private async void RemoveLessonSession_Click(object sender, RoutedEventArgs e)
    {
        if (LessonSessionsDataGrid.SelectedItem is not LessonSessionRow session)
        {
            ShowInvalid("Hãy chọn buổi học cần xóa.");
            return;
        }
        if (MessageBox.Show(this, $"Xóa hẳn buổi học ngày {session.LessonDate:dd/MM/yyyy}? Điểm danh của buổi này cũng sẽ bị xóa.", "Xác nhận xóa buổi", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveLessonSessionAsync(
            () => _apiClient.RemoveCscaLessonSessionAsync(_classId, session.Id),
            "Đã xóa buổi học.");
    }

    private async void LessonSessionsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LessonSessionsDataGrid.SelectedItem is not LessonSessionRow session)
        {
            LessonAttendanceDataGrid.ItemsSource = null;
            return;
        }

        await LoadLessonAttendanceAsync(session);
    }

    private async Task LoadLessonAttendanceAsync(LessonSessionRow session)
    {
        AttendanceSessionText.Text = $"Đang tải điểm danh buổi {session.LessonDate:dd/MM/yyyy}, {session.TimeLabel}...";
        var attendances = await _apiClient.GetCscaLessonAttendanceAsync(_classId, session.Id);
        if (attendances is null)
        {
            LessonAttendanceDataGrid.ItemsSource = null;
            AttendanceSessionText.Text = "Không tải được điểm danh của buổi học.";
            return;
        }

        LessonAttendanceDataGrid.ItemsSource = attendances.Select(AttendanceRow.From).ToList();
        AttendanceSessionText.Text = $"Buổi {session.LessonDate:dd/MM/yyyy}, {session.TimeLabel} — chọn học viên rồi ghi nhận trạng thái.";
    }

    private async void MarkAttendancePresent_Click(object sender, RoutedEventArgs e) => await MarkAttendanceAsync("Present");
    private async void MarkAttendanceLate_Click(object sender, RoutedEventArgs e) => await MarkAttendanceAsync("Late");
    private async void MarkAttendanceAbsent_Click(object sender, RoutedEventArgs e) => await MarkAttendanceAsync("Absent");
    private async void MarkAttendanceExcused_Click(object sender, RoutedEventArgs e) => await MarkAttendanceAsync("Excused");

    private async Task MarkAttendanceAsync(string status)
    {
        if (LessonSessionsDataGrid.SelectedItem is not LessonSessionRow session || LessonAttendanceDataGrid.SelectedItem is not AttendanceRow attendance)
        {
            ShowInvalid("Hãy chọn buổi học và học viên cần điểm danh.");
            return;
        }

        SetBusy(true, "Đang lưu điểm danh...");
        try
        {
            if (!await _apiClient.UpsertCscaLessonAttendanceAsync(_classId, session.Id, attendance.StudentId, status,
                checkInAt: status is "Present" or "Late" ? DateTime.UtcNow : null))
            {
                ShowInvalid("Không thể lưu điểm danh. Buổi học có thể đã bị hủy hoặc bạn chưa có quyền.");
                return;
            }
            await LoadLessonAttendanceAsync(session);
            LoadingText.Text = "Đã lưu điểm danh.";
        }
        finally
        {
            SetBusy(false, LoadingText.Text);
        }
    }

    private async Task SaveLessonSessionAsync(Func<Task<bool>> action, string successMessage)
    {
        SetBusy(true, "Đang lưu buổi học...");
        try
        {
            if (!await action())
            {
                ShowInvalid("Không thể lưu buổi học. Kiểm tra lại phòng, giờ học hoặc quyền quản lý lớp.");
                return;
            }
            await LoadLessonSessionsAsync();
            LoadingText.Text = successMessage;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể lưu buổi học:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, LoadingText.Text);
        }
    }

}
