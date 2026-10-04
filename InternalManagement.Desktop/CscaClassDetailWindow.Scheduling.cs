using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private async void AddSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo lịch học", await ScheduleFieldsAsync(), out var values)) return;
        if (!TryScheduleValues(values, out var dayOfWeek, out var startTime, out var endTime)) return;
        if (!TryOptionalGuid(values["classroomId"], out var classroomId)) return;

        await SaveAsync(
            () => _apiClient.AddCscaScheduleAsync(_classId, dayOfWeek, startTime, endTime,
                null, Null(values["meetingUrl"]), Null(values["notes"]), classroomId),
            "Đã tạo lịch học cho lớp.");
    }

    private async void EditSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SchedulesDataGrid.SelectedItem is not ScheduleRow schedule)
        {
            ShowInvalid("Hãy chọn một lịch học cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, "Sửa lịch học", await ScheduleFieldsAsync(schedule), out var values)) return;
        if (!TryScheduleValues(values, out var dayOfWeek, out var startTime, out var endTime)) return;
        if (!TryOptionalGuid(values["classroomId"], out var classroomId)) return;

        await SaveAsync(
            () => _apiClient.UpdateCscaScheduleAsync(_classId, schedule.Id, dayOfWeek, startTime, endTime,
                classroomId.HasValue ? null : schedule.Room, Null(values["meetingUrl"]), Null(values["notes"]), classroomId),
            "Đã cập nhật lịch học.");
    }

    private void SchedulesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SchedulesDataGrid.SelectedItem is ScheduleRow)
            EditSchedule_Click(sender, e);
    }

    private void EditScheduleRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScheduleRow schedule })
        {
            SchedulesDataGrid.SelectedItem = schedule;
            EditSchedule_Click(sender, e);
        }
    }

    private void RemoveScheduleRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScheduleRow schedule })
        {
            SchedulesDataGrid.SelectedItem = schedule;
            RemoveSchedule_Click(sender, e);
        }
    }

    private void OpenScheduleMeeting_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ScheduleRow schedule }
            || !Uri.TryCreate(schedule.MeetingUrl, UriKind.Absolute, out var meetingUri)
            || (meetingUri.Scheme != Uri.UriSchemeHttp && meetingUri.Scheme != Uri.UriSchemeHttps))
        {
            ShowInvalid("Link học trực tuyến không hợp lệ. Hãy cập nhật link bắt đầu bằng http:// hoặc https://.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(meetingUri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể mở link học:\n{ex.Message}", "Không thể mở link", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RemoveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (SchedulesDataGrid.SelectedItem is not ScheduleRow schedule)
        {
            ShowInvalid("Hãy chọn một lịch học cần xóa.");
            return;
        }

        if (MessageBox.Show(this, "Xóa lịch học đã chọn khỏi lớp?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveAsync(
            () => _apiClient.RemoveCscaScheduleAsync(_classId, schedule.Id),
            "Đã xóa lịch học.");
    }

    private async void ManageClassrooms_Click(object sender, RoutedEventArgs e)
    {
        var classroomWindow = new CscaClassroomWindow(_apiClient) { Owner = this };
        classroomWindow.ShowDialog();
        await LoadDetailAsync();
    }

}
