using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private async Task SaveAsync(Func<Task<bool>> action, string successMessage)
    {
        SetBusy(true, "Đang lưu dữ liệu...");
        try
        {
            if (!await action())
            {
                MessageBox.Show(this, "Lưu dữ liệu thất bại. Vui lòng kiểm tra lại thông tin nhập hoặc quyền tài khoản.", "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadDetailAsync();
            LoadingText.Text = successMessage;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể lưu dữ liệu:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, LoadingText.Text);
        }
    }

    private IReadOnlyList<PromptField> StudentFields(StudentRow? student = null)
    {
        return new[]
        {
            new PromptField("studentName", "Họ và tên", student?.StudentName),
            new PromptField("age", "Tuổi", student?.Age?.ToString(CultureInfo.InvariantCulture), IsRequired: false),
            new PromptField("hometown", "Quê quán", student?.Hometown, IsRequired: false),
            new PromptField("email", "Email", student?.Email, IsRequired: false),
            new PromptField("phone", "Số điện thoại", student?.PhoneNumber, IsRequired: false),
            new PromptField("discountAmount", "Giảm giá học nhiều khóa", student?.DiscountAmount.ToString("0", CultureInfo.InvariantCulture) ?? "0", IsRequired: false),
            new PromptField("discountNote", "Lý do giảm giá", student?.DiscountNote, IsRequired: false),
            new PromptField("paidAmount", "Số tiền đã đóng", student?.PaidAmount.ToString("0", CultureInfo.InvariantCulture) ?? "0"),
            new PromptField("debtDueDate", "Ngày hẹn trả nợ (yyyy-MM-dd)", student?.DebtDueDate?.ToString("yyyy-MM-dd"), IsRequired: false),
            new PromptField("paymentStatus", "Trạng thái học phí", GetPaymentStatusValue(student?.PaymentStatusValue), Options: PaymentStatusOptions()),
            new PromptField("notes", "Ghi chú", student?.Notes, IsRequired: false)
        };
    }

    private async Task<IReadOnlyList<PromptField>> ScheduleFieldsAsync(ScheduleRow? schedule = null)
    {
        var rooms = await _apiClient.GetCscaClassroomsAsync();
        var roomOptions = BuildClassroomOptions(rooms, schedule?.ClassroomId);
        return new[]
        {
            new PromptField("dayOfWeek", "Thứ", schedule?.DayOfWeek.ToString(CultureInfo.InvariantCulture) ?? "1", Options: DayOptions()),
            new PromptField("startTime", "Giờ bắt đầu (HH:mm)", schedule?.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "18:00"),
            new PromptField("endTime", "Giờ kết thúc (HH:mm)", schedule?.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "20:00"),
            new PromptField("classroomId", "Phòng học", schedule?.ClassroomId?.ToString(), IsRequired: false, Options: roomOptions),
            new PromptField("meetingUrl", "Link học trực tuyến (https://...)", schedule?.MeetingUrl, IsRequired: false),
            new PromptField("notes", "Ghi chú", schedule?.Notes, IsRequired: false)
        };
    }

    private async Task<IReadOnlyList<PromptField>> LessonSessionFieldsAsync(LessonSessionRow? session = null)
    {
        var rooms = await _apiClient.GetCscaClassroomsAsync();
        return new[]
        {
            new PromptField("lessonDate", "Ngày học (yyyy-MM-dd)", session?.LessonDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new PromptField("startTime", "Giờ bắt đầu (HH:mm)", session?.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "18:00"),
            new PromptField("endTime", "Giờ kết thúc (HH:mm)", session?.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture) ?? "20:00"),
            new PromptField("classroomId", "Phòng học", session?.ClassroomId?.ToString(), IsRequired: false, Options: BuildClassroomOptions(rooms, session?.ClassroomId)),
            new PromptField("status", "Trạng thái", session?.Status ?? "Scheduled", Options: LessonStatusOptions()),
            new PromptField("meetingUrl", "Link học trực tuyến (https://...)", session?.MeetingUrl, IsRequired: false),
            new PromptField("notes", "Ghi chú", session?.Notes, IsRequired: false)
        };
    }

    private static IReadOnlyList<PromptOption> BuildClassroomOptions(IReadOnlyList<ApiClient.CscaClassroomItem>? rooms, Guid? selectedRoomId)
    {
        var options = new List<PromptOption> { new(string.Empty, "-- Học online / chưa chọn phòng --") };
        if (rooms is not null)
        {
            options.AddRange(rooms.Select(room => new PromptOption(
                room.Id.ToString(),
                $"{room.Code} — {room.Name}{(room.Capacity.HasValue ? $" ({room.Capacity} chỗ)" : string.Empty)}")));
        }
        return options;
    }

    private static IReadOnlyList<PromptOption> DayOptions() => new[]
    {
        new PromptOption("0", "Chủ nhật"),
        new PromptOption("1", "Thứ 2"),
        new PromptOption("2", "Thứ 3"),
        new PromptOption("3", "Thứ 4"),
        new PromptOption("4", "Thứ 5"),
        new PromptOption("5", "Thứ 6"),
        new PromptOption("6", "Thứ 7")
    };

    private static IReadOnlyList<PromptOption> PaymentStatusOptions() => new[]
    {
        new PromptOption("0", "Chưa đóng / Pending"),
        new PromptOption("1", "Đóng một phần / Partial"),
        new PromptOption("2", "Đã đóng đủ / Paid"),
        new PromptOption("3", "Thất bại / Failed"),
        new PromptOption("4", "Hoàn tiền / Refunded"),
        new PromptOption("5", "Đã hủy / Cancelled")
    };

    private static IReadOnlyList<PromptOption> RoleOptions() => new[]
    {
        new PromptOption("Teacher", "Giáo viên"),
        new PromptOption("TeachingAssistant", "Trợ giảng"),
        new PromptOption("Mentor", "Mentor")
    };

    private static IReadOnlyList<PromptOption> LessonStatusOptions() => new[]
    {
        new PromptOption("Scheduled", "Đã lên lịch"),
        new PromptOption("Rescheduled", "Đổi lịch"),
        new PromptOption("Cancelled", "Đã hủy"),
        new PromptOption("Completed", "Hoàn thành")
    };

    private static string GetPaymentStatusValue(int? status) => status?.ToString(CultureInfo.InvariantCulture) ?? "0";

    private bool TryLessonSessionValues(
        IReadOnlyDictionary<string, string> values,
        out DateOnly lessonDate,
        out TimeSpan startTime,
        out TimeSpan endTime,
        out Guid? classroomId)
    {
        lessonDate = default;
        startTime = default;
        endTime = default;
        classroomId = null;
        if (!TryDateOnly(values["lessonDate"], out lessonDate))
        {
            ShowInvalid("Ngày học phải đúng định dạng yyyy-MM-dd.");
            return false;
        }
        if (!TimeSpan.TryParseExact(values["startTime"], @"hh\:mm", CultureInfo.InvariantCulture, out startTime)
            || !TimeSpan.TryParseExact(values["endTime"], @"hh\:mm", CultureInfo.InvariantCulture, out endTime)
            || startTime >= endTime)
        {
            ShowInvalid("Giờ học phải đúng dạng HH:mm và giờ bắt đầu phải trước giờ kết thúc.");
            return false;
        }
        return TryOptionalGuid(values["classroomId"], out classroomId);
    }

    private static bool TryDateOnly(string value, out DateOnly date) => DateOnly.TryParseExact(
        value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private bool TryOptionalGuid(string value, out Guid? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (Guid.TryParse(value, out var parsed))
        {
            id = parsed;
            return true;
        }
        ShowInvalid("Phòng học được chọn không hợp lệ. Hãy tải lại danh mục phòng học.");
        return false;
    }

    private static IReadOnlyList<ScheduleWeekDay> BuildWeeklyTimetable(IReadOnlyCollection<ScheduleRow> schedules)
    {
        var dayDefinitions = new[]
        {
            (1, "Thứ 2"), (2, "Thứ 3"), (3, "Thứ 4"), (4, "Thứ 5"),
            (5, "Thứ 6"), (6, "Thứ 7"), (0, "Chủ nhật")
        };

        return dayDefinitions
            .Select(day => new ScheduleWeekDay(
                day.Item1,
                day.Item2,
                schedules.Where(schedule => schedule.DayOfWeek == day.Item1).OrderBy(schedule => schedule.StartTime).ToList()))
            .ToList();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        var hours = (int)duration.TotalHours;
        return duration.Minutes == 0
            ? $"{hours} giờ"
            : $"{hours} giờ {duration.Minutes} phút";
    }

    private bool TryStudentValues(
        IReadOnlyDictionary<string, string> values,
        out int? age,
        out decimal paidAmount,
        out int paymentStatus,
        out DateTime? debtDueDate)
    {
        age = null;
        paidAmount = 0;
        paymentStatus = 0;
        debtDueDate = null;

        if (!string.IsNullOrWhiteSpace(values["age"]))
        {
            if (!int.TryParse(values["age"], out var parsedAge) || parsedAge is < 1 or > 120)
            {
                ShowInvalid("Tuổi phải là số từ 1 đến 120.");
                return false;
            }
            age = parsedAge;
        }

        if (!TryMoney(values["paidAmount"], out paidAmount) || paidAmount < 0)
        {
            ShowInvalid("Số tiền đã đóng phải là số không âm.");
            return false;
        }

        if (!int.TryParse(values["paymentStatus"], out paymentStatus) || paymentStatus is < 0 or > 5)
        {
            ShowInvalid("Trạng thái học phí không hợp lệ.");
            return false;
        }

        if (!TryDate(values["debtDueDate"], out debtDueDate))
        {
            ShowInvalid("Ngày hẹn trả phải đúng định dạng yyyy-MM-dd.");
            return false;
        }

        return true;
    }

    private bool TryScheduleValues(
        IReadOnlyDictionary<string, string> values,
        out int dayOfWeek,
        out TimeSpan startTime,
        out TimeSpan endTime)
    {
        dayOfWeek = 0;
        startTime = default;
        endTime = default;
        if (!int.TryParse(values["dayOfWeek"], out dayOfWeek) || dayOfWeek is < 0 or > 6 ||
            !TimeSpan.TryParseExact(values["startTime"].Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out startTime) ||
            !TimeSpan.TryParseExact(values["endTime"].Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out endTime) ||
            endTime <= startTime)
        {
            ShowInvalid("Thứ hoặc giờ học không hợp lệ. Hãy nhập giờ theo HH:mm và giờ kết thúc phải lớn hơn giờ bắt đầu.");
            return false;
        }

        return true;
    }

    private static bool TryMoney(string text, out decimal value)
    {
        var normalized = (text ?? string.Empty).Trim().Replace(" ", string.Empty).Replace(",", string.Empty).Replace(".", string.Empty);
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryDate(string text, out DateTime? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            value = date;
            return true;
        }

        value = null;
        return false;
    }

    private void SetBusy(bool isBusy, string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBusy(isBusy, message));
            return;
        }

        LoadingText.Text = message;
        DetailTabs.IsEnabled = !isBusy;
        if (CscaBusyOverlay != null)
        {
            CscaBusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            CscaBusyText.Text = string.IsNullOrWhiteSpace(message) ? "Đang xử lý dữ liệu..." : message;
        }
        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
    }

    private void ShowInvalid(string message)
        => MessageBox.Show(this, message, "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Money(decimal amount) => $"{amount:N0} đ";

    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FormatDate(DateTime? value) => value.HasValue ? value.Value.ToLocalTime().ToString("dd/MM/yyyy") : "—";

    private static string PaymentStatusLabel(int status) => status switch
    {
        1 => "Đóng một phần",
        2 => "Đã đóng đủ",
        3 => "Thất bại",
        4 => "Hoàn tiền",
        5 => "Đã hủy",
        _ => "Chưa đóng"
    };
}
