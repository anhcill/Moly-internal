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

    private async void AddMakeupSession_Click(object sender, RoutedEventArgs e)
    {
        var cancelled = _calendarSessions
            .Where(s => string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.LessonDate)
            .ThenByDescending(s => s.StartTime)
            .ToList();
        var replacementOptions = new[] { new PromptOption(string.Empty, "-- Không gắn buổi hủy cụ thể --") }
            .Concat(cancelled.Select(s => new PromptOption(
                s.Id.ToString(),
                $"{s.LessonDate:dd/MM/yyyy} {s.StartTime:hh\\:mm}-{s.EndTime:hh\\:mm}")))
            .ToArray();

        var fields = new List<PromptField>
        {
            new("replacementId", "Dạy bù cho buổi đã hủy", replacementOptions[0].Value, IsRequired: false, Options: replacementOptions)
        };
        fields.AddRange(await LessonSessionFieldsAsync());

        if (!PromptDialog.TryShow(this, "Tạo buổi dạy bù (Makeup)", fields.ToArray(), out var values)) return;
        if (!TryLessonSessionValues(values, out var lessonDate, out var startTime, out var endTime, out var classroomId)) return;
        Guid? replacementForSessionId = Guid.TryParse(values.GetValueOrDefault("replacementId"), out var parsedId)
            ? parsedId : null;

        await SaveAsync(() => _apiClient.CreateCscaLessonSessionAsync(
                _classId,
                lessonDate,
                startTime,
                endTime,
                classroomId,
                Null(values["meetingUrl"]),
                Null(values["notes"]),
                status: "Scheduled",
                sessionKind: "Makeup",
                replacementForSessionId: replacementForSessionId,
                curriculumAction: "ReuseCancelled"),
            "Đã tạo buổi dạy bù (Makeup) thành công.");
    }

    private async void CompleteAndAttendSession_Click(object sender, RoutedEventArgs e)
    {
        var sessions = _calendarSessions
            .Where(s => !string.Equals(s.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.LessonDate)
            .ThenByDescending(s => s.StartTime)
            .ToList();
        if (sessions.Count == 0)
        {
            ShowInvalid("Chưa có buổi học nào để điểm danh hoặc chốt hoàn thành.");
            return;
        }

        var sessionOptions = sessions.Select(s => new PromptOption(
            s.Id.ToString(),
            $"{s.LessonDate:dd/MM/yyyy} {s.StartTime:hh\\:mm}-{s.EndTime:hh\\:mm} [{s.Status}]"))
            .ToArray();
        if (!PromptDialog.TryShow(this, "Chọn buổi học để điểm danh & chốt lương", new[]
        {
            new PromptField("sessionId", "Buổi học", sessionOptions[0].Value, Options: sessionOptions),
            new PromptField("markCompleted", "Chốt hoàn thành buổi dạy (Completed -> đưa vào tính lương GV)", "yes", Options: new[]
            {
                new PromptOption("yes", "Có — Chốt Completed để tính lương giáo viên"),
                new PromptOption("no", "Chỉ lưu điểm danh học viên, giữ trạng thái hiện tại")
            })
        }, out var pickValues)) return;

        if (!Guid.TryParse(pickValues["sessionId"], out var sessionId))
        {
            ShowInvalid("Buổi học không hợp lệ.");
            return;
        }

        var targetSession = sessions.First(s => s.Id == sessionId);
        var markCompleted = pickValues["markCompleted"] == "yes";
        var students = _detail?.Students
            .Where(s => s.PaymentStatus != 3 && s.PaymentStatus != 4)
            .OrderBy(s => s.StudentName)
            .ToList() ?? [];

        var existingAttendance = await _apiClient.GetCscaLessonAttendanceAsync(_classId, sessionId)
            ?? Array.Empty<ApiClient.CscaLessonAttendanceItem>();
        var existingByStudent = existingAttendance.ToDictionary(a => a.StudentId, a => a.Status);

        IReadOnlyDictionary<string, string>? attendanceValues = null;
        if (students.Count > 0)
        {
            var attendanceStatusOptions = new[]
            {
                new PromptOption("Present", "Có mặt (Present)"),
                new PromptOption("Late", "Đi muộn (Late)"),
                new PromptOption("Absent", "Vắng mặt (Absent)"),
                new PromptOption("Excused", "Có phép (Excused)")
            };
            var attendanceFields = students.Select(student => new PromptField(
                student.Id.ToString(),
                $"{student.StudentName}" + (string.IsNullOrWhiteSpace(student.StudentCode) ? string.Empty : $" ({student.StudentCode})"),
                existingByStudent.TryGetValue(student.Id, out var currentStatus) ? currentStatus : "Present",
                Options: attendanceStatusOptions)).ToArray();

            if (!PromptDialog.TryShow(
                    this,
                    $"Điểm danh buổi {targetSession.LessonDate:dd/MM/yyyy} {targetSession.StartTime:hh\\:mm}-{targetSession.EndTime:hh\\:mm}",
                    attendanceFields,
                    out attendanceValues))
            {
                return;
            }
        }

        await SaveAsync(async () =>
        {
            if (attendanceValues is not null)
            {
                foreach (var student in students)
                {
                    if (!attendanceValues.TryGetValue(student.Id.ToString(), out var status)) continue;
                    await _apiClient.UpsertCscaLessonAttendanceAsync(
                        _classId,
                        sessionId,
                        student.Id,
                        status,
                        notes: null,
                        checkInAt: status is "Present" or "Late" ? DateTime.UtcNow : null);
                }
            }

            if (markCompleted && !string.Equals(targetSession.Status, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                await _apiClient.UpdateCscaLessonSessionAsync(
                    _classId,
                    sessionId,
                    targetSession.LessonDate,
                    targetSession.StartTime,
                    targetSession.EndTime,
                    targetSession.ClassroomId,
                    targetSession.MeetingUrl,
                    targetSession.Notes,
                    status: "Completed",
                    changeReason: "Đã xác nhận hoàn thành buổi dạy & điểm danh.");
            }

            return true;
        }, markCompleted
            ? "Đã lưu điểm danh và chốt hoàn thành (Completed) buổi dạy — sẵn sàng đồng bộ vào bảng lương."
            : "Đã lưu điểm danh học viên cho buổi học.");
    }

    private async void RemoveLessonSession_Click(object sender, RoutedEventArgs e)
    {
        if (_calendarSessions.Count == 0)
        {
            ShowInvalid("Chưa có buổi học nào để xóa.");
            return;
        }

        var options = _calendarSessions
            .OrderByDescending(s => s.LessonDate)
            .ThenByDescending(s => s.StartTime)
            .Select(s => new PromptOption(
                s.Id.ToString(),
                $"{s.LessonDate:dd/MM/yyyy} {s.StartTime:hh\\:mm}-{s.EndTime:hh\\:mm} [{s.Status}]"))
            .ToArray();
        if (!PromptDialog.TryShow(this, "Xóa vĩnh viễn buổi học", new[]
        {
            new PromptField("sessionId", "Buổi cần xóa", options[0].Value, Options: options)
        }, out var values)) return;

        if (!Guid.TryParse(values["sessionId"], out var sessionId))
        {
            ShowInvalid("Buổi học không hợp lệ.");
            return;
        }

        if (MessageBox.Show(this, "Xóa vĩnh viễn buổi học này khỏi hệ thống?", "Xác nhận xóa buổi học",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveAsync(() => _apiClient.RemoveCscaLessonSessionAsync(_classId, sessionId),
            "Đã xóa buổi học khỏi lớp.");
    }
}
