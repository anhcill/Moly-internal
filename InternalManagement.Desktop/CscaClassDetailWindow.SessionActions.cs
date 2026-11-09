using System.Windows;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private async void AddLessonSession_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Thêm buổi học bổ sung", await LessonSessionFieldsAsync(), out var values)) return;
        if (!TryLessonSessionValues(values, out var lessonDate, out var startTime, out var endTime, out var classroomId)) return;
        if (string.Equals(values["status"], "Cancelled", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(values["status"], "Completed", StringComparison.OrdinalIgnoreCase))
        {
            ShowInvalid("Buổi mới chỉ được tạo ở trạng thái Đã lên lịch hoặc Đổi lịch.");
            return;
        }
        await SaveAsync(() => _apiClient.CreateCscaLessonSessionAsync(_classId, lessonDate, startTime, endTime,
                classroomId, Null(values["meetingUrl"]), Null(values["notes"]), values["status"],
                sessionKind: "Supplemental", curriculumAction: "Append"),
            "Đã thêm buổi học bổ sung; các buổi hiện có không bị dồn lịch.");
    }

    private async void CancelLessonSession_Click(object sender, RoutedEventArgs e)
    {
        var available = _calendarSessions
            .Where(session => !string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                              !string.Equals(session.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            .OrderBy(session => session.LessonDate).ThenBy(session => session.StartTime)
            .ToList();
        if (available.Count == 0)
        {
            ShowInvalid("Không có buổi học đang hoạt động để hủy.");
            return;
        }
        var options = available.Select(session => new PromptOption(
            session.Id.ToString(),
            $"{session.LessonDate:dd/MM/yyyy} {session.StartTime:hh\\:mm}-{session.EndTime:hh\\:mm}"))
            .ToArray();
        if (!PromptDialog.TryShow(this, "Hủy buổi học", new[]
        {
            new PromptField("sessionId", "Buổi cần hủy", options[0].Value, Options: options),
            new PromptField("reason", "Lý do hủy", "Giáo viên bận")
        }, out var values)) return;
        if (!Guid.TryParse(values["sessionId"], out var sessionId) || string.IsNullOrWhiteSpace(values["reason"]))
        {
            ShowInvalid("Hãy chọn buổi học và nhập lý do hủy.");
            return;
        }
        await SaveAsync(() => _apiClient.CancelCscaLessonSessionAsync(_classId, sessionId, values["reason"].Trim()),
            "Đã hủy buổi, loại khỏi lương và dồn giáo trình nếu đây là buổi thường.");
    }
}
