using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{

    private sealed class StudentRow
    {
        public Guid Id { get; init; }
        public string StudentName { get; init; } = string.Empty;
        public int? Age { get; init; }
        public string? Hometown { get; init; }
        public string? Email { get; init; }
        public string? PhoneNumber { get; init; }
        public decimal TuitionFee { get; init; }
        public decimal DiscountAmount { get; init; }
        public string? DiscountNote { get; init; }
        public decimal PayableAmount => Math.Max(0, TuitionFee - DiscountAmount);
        public decimal PaidAmount { get; init; }
        public decimal DebtAmount => Math.Max(0, PayableAmount - PaidAmount);
        public DateTime? DebtDueDate { get; init; }
        public int PaymentStatusValue { get; init; }
        public string PaymentStatusLabel => CscaClassDetailWindow.PaymentStatusLabel(PaymentStatusValue);
        public DateTime JoinedAt { get; init; }
        public string? Notes { get; init; }

        public static StudentRow From(ApiClient.CscaStudentItem student, decimal tuitionFee) => new()
        {
            Id = student.Id,
            StudentName = student.StudentName,
            Age = student.Age,
            Hometown = student.Hometown,
            Email = student.Email,
            PhoneNumber = student.PhoneNumber,
            TuitionFee = tuitionFee,
            DiscountAmount = student.DiscountAmount,
            DiscountNote = student.DiscountNote,
            PaidAmount = student.PaidAmount,
            DebtDueDate = student.DebtDueDate,
            PaymentStatusValue = student.PaymentStatus,
            JoinedAt = student.JoinedAt,
            Notes = student.Notes
        };
    }

    private sealed class TeacherRow
    {
        public Guid Id { get; init; }
        public Guid EmployeeId { get; init; }
        public string EmployeeName { get; init; } = string.Empty;
        public string? EmployeeCode { get; init; }
        public string RoleInClass { get; init; } = string.Empty;
        public decimal CompensationRate { get; init; }
        public string? Notes { get; init; }

        public static TeacherRow From(ApiClient.CscaStaffItem teacher) => new()
        {
            Id = teacher.Id,
            EmployeeId = teacher.EmployeeId,
            EmployeeName = teacher.EmployeeName,
            EmployeeCode = teacher.EmployeeCode,
            RoleInClass = teacher.RoleInClass,
            CompensationRate = teacher.CompensationRate,
            Notes = teacher.Notes
        };
    }

    private sealed class ScheduleRow
    {
        public Guid Id { get; init; }
        public int DayOfWeek { get; init; }
        public string DayLabel { get; init; } = string.Empty;
        public TimeSpan StartTime { get; init; }
        public TimeSpan EndTime { get; init; }
        public Guid? ClassroomId { get; init; }
        public string? Room { get; init; }
        public string? MeetingUrl { get; init; }
        public string? Notes { get; init; }
        public string TimeLabel => $"{StartTime:hh\\:mm} – {EndTime:hh\\:mm}";
        public string DurationLabel => FormatDuration(EndTime - StartTime);
        public bool HasMeetingUrl => !string.IsNullOrWhiteSpace(MeetingUrl);
        public string LocationLabel
        {
            get
            {
                var locations = new List<string>();
                if (!string.IsNullOrWhiteSpace(Room)) locations.Add(Room);
                if (HasMeetingUrl) locations.Add("Trực tuyến");
                return locations.Count == 0 ? "Chưa thiết lập phòng / hình thức" : string.Join(" • ", locations);
            }
        }

        public static ScheduleRow From(ApiClient.CscaScheduleItem schedule) => new()
        {
            Id = schedule.Id,
            DayOfWeek = schedule.DayOfWeek,
            DayLabel = schedule.DayOfWeek switch
            {
                0 => "Chủ nhật",
                1 => "Thứ 2",
                2 => "Thứ 3",
                3 => "Thứ 4",
                4 => "Thứ 5",
                5 => "Thứ 6",
                6 => "Thứ 7",
                _ => $"Ngày {schedule.DayOfWeek}"
            },
            StartTime = schedule.StartTime,
            EndTime = schedule.EndTime,
            ClassroomId = schedule.ClassroomId,
            Room = schedule.ClassroomName ?? schedule.Room,
            MeetingUrl = schedule.MeetingUrl,
            Notes = schedule.Notes
        };
    }

    private sealed class LessonSessionRow
    {
        public Guid Id { get; init; }
        public DateOnly LessonDate { get; init; }
        public TimeSpan StartTime { get; init; }
        public TimeSpan EndTime { get; init; }
        public Guid? ClassroomId { get; init; }
        public string? ClassroomName { get; init; }
        public string? MeetingUrl { get; init; }
        public string? Notes { get; init; }
        public string Status { get; init; } = "Scheduled";
        public string TimeLabel => $"{StartTime:hh\\:mm} – {EndTime:hh\\:mm}";
        public string LocationLabel
        {
            get
            {
                var locations = new List<string>();
                if (!string.IsNullOrWhiteSpace(ClassroomName)) locations.Add(ClassroomName);
                if (!string.IsNullOrWhiteSpace(MeetingUrl)) locations.Add("Trực tuyến");
                return locations.Count == 0 ? "Chưa thiết lập phòng / hình thức" : string.Join(" • ", locations);
            }
        }
        public string StatusLabel => Status switch
        {
            "Scheduled" => "Đã lên lịch",
            "Rescheduled" => "Đổi lịch",
            "Cancelled" => "Đã hủy",
            "Completed" => "Hoàn thành",
            _ => Status
        };

        public static LessonSessionRow From(ApiClient.CscaLessonSessionItem session) => new()
        {
            Id = session.Id,
            LessonDate = session.LessonDate,
            StartTime = session.StartTime,
            EndTime = session.EndTime,
            ClassroomId = session.ClassroomId,
            ClassroomName = session.ClassroomName,
            MeetingUrl = session.MeetingUrl,
            Notes = session.Notes,
            Status = session.Status
        };
    }

    private sealed class AttendanceRow
    {
        public Guid StudentId { get; init; }
        public string StudentName { get; init; } = string.Empty;
        public string Status { get; init; } = "Unmarked";
        public DateTime? CheckInAt { get; init; }
        public string? Notes { get; init; }
        public string StatusLabel => Status switch
        {
            "Present" => "Có mặt",
            "Late" => "Đi muộn",
            "Absent" => "Vắng",
            "Excused" => "Có phép",
            _ => "Chưa điểm danh"
        };
        public string CheckInLabel => CheckInAt?.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture) ?? "—";

        public static AttendanceRow From(ApiClient.CscaLessonAttendanceItem attendance) => new()
        {
            StudentId = attendance.StudentId,
            StudentName = attendance.StudentName,
            Status = attendance.Status,
            CheckInAt = attendance.CheckInAt,
            Notes = attendance.Notes
        };
    }

    private sealed class ScheduleWeekDay(int dayOfWeek, string dayLabel, IReadOnlyList<ScheduleRow> lessons)
    {
        public int DayOfWeek { get; } = dayOfWeek;
        public string DayLabel { get; } = dayLabel;
        public IReadOnlyList<ScheduleRow> Lessons { get; } = lessons;
        public bool HasLessons => Lessons.Count > 0;
        public string LessonCountLabel => Lessons.Count == 0
            ? "Không có buổi"
            : Lessons.Count == 1 ? "1 buổi học" : $"{Lessons.Count} buổi học";
    }
}
