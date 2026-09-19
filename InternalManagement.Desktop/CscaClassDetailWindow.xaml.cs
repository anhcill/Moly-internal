using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _classId;
    private bool _hasLoaded;
    private ApiClient.CscaClassDetailItem? _detail;

    public CscaClassDetailWindow(ApiClient apiClient, Guid classId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _classId = classId;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_hasLoaded) return;
        _hasLoaded = true;
        await LoadDetailAsync();
    }

    private async Task LoadDetailAsync()
    {
        SetBusy(true, "Đang tải thông tin lớp, học viên và giáo viên...");
        try
        {
            var detail = await _apiClient.GetCscaClassDetailAsync(_classId);
            if (detail is null)
            {
                MessageBox.Show(this, "Không tải được chi tiết lớp. Vui lòng kiểm tra quyền truy cập hoặc thử lại.", "Không tải được dữ liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _detail = detail;
            RenderDetail(detail);
            await LoadLessonSessionsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không tải được chi tiết lớp:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private void RenderDetail(ApiClient.CscaClassDetailItem detail)
    {
        Title = $"Chi tiết lớp {detail.Code}";
        ClassTitleText.Text = $"{detail.Code} — {detail.Name}";
        var scheduleRows = detail.Schedules
            .OrderBy(schedule => schedule.DayOfWeek == 0 ? 7 : schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartTime)
            .Select(ScheduleRow.From)
            .ToList();
        var scheduleSummary = string.IsNullOrWhiteSpace(detail.Schedule)
            ? "Chưa thiết lập thời khóa biểu"
            : detail.Schedule;

        ClassSubtitleText.Text = $"{detail.Batch}  •  {scheduleSummary}  •  Mở ngày {FormatDate(detail.CreatedAt)}";
        StudentCountText.Text = detail.Students.Count.ToString(CultureInfo.CurrentCulture);
        RevenueText.Text = Money(detail.FinancialSummary.ActualRevenue);
        DebtText.Text = Money(Math.Max(0, detail.FinancialSummary.ExpectedRevenue - detail.FinancialSummary.ActualRevenue));

        ClassCodeText.Text = detail.Code;
        ClassNameText.Text = detail.Name;
        ClassBatchText.Text = detail.Batch;
        ClassScheduleText.Text = scheduleSummary;
        ClassTuitionText.Text = Money(detail.TuitionFee);
        ClassStatusText.Text = detail.Status;
        ClassStartDateText.Text = FormatDate(detail.StartDate);
        ClassEndDateText.Text = FormatDate(detail.EndDate);
        ClassCourseText.Text = detail.CourseTitle;
        FinancialSummaryText.Text =
            $"Dự thu: {Money(detail.FinancialSummary.ExpectedRevenue)}    |    Đã thu: {Money(detail.FinancialSummary.ActualRevenue)}    |    Còn nợ: {Money(Math.Max(0, detail.FinancialSummary.ExpectedRevenue - detail.FinancialSummary.ActualRevenue))}    |    Lợi nhuận: {Money(detail.FinancialSummary.NetProfit)}";

        StudentsDataGrid.ItemsSource = detail.Students
            .Select(student => StudentRow.From(student, detail.TuitionFee))
            .ToList();
        TeachersDataGrid.ItemsSource = detail.Staff
            .Select(TeacherRow.From)
            .ToList();
        SchedulesDataGrid.ItemsSource = scheduleRows;
        WeeklyTimetableItems.ItemsSource = BuildWeeklyTimetable(scheduleRows);

        var weeklyDuration = scheduleRows.Aggregate(TimeSpan.Zero, (total, schedule) => total + (schedule.EndTime - schedule.StartTime));
        ScheduleSummaryText.Text = scheduleRows.Count == 0
            ? "Chưa có buổi nào. Hãy thêm buổi học đầu tiên để tạo thời khóa biểu."
            : $"{scheduleRows.Count} buổi/tuần  •  Tổng thời lượng {FormatDuration(weeklyDuration)}";
        ScheduleCoverageText.Text = scheduleRows.Count == 0
            ? "Chưa thiết lập"
            : $"{scheduleRows.Select(schedule => schedule.DayOfWeek).Distinct().Count()} ngày học/tuần";
    }

    private async void EditClass_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;

        if (!PromptDialog.TryShow(this, $"Sửa thông tin lớp {_detail.Code}", new[]
        {
            new PromptField("name", "Tên lớp", _detail.Name),
            new PromptField("batch", "Đợt / khóa", _detail.Batch),
            new PromptField("tuitionFee", "Học phí / học viên", _detail.TuitionFee.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("startDate", "Ngày bắt đầu (yyyy-MM-dd)", _detail.StartDate?.ToString("yyyy-MM-dd"), IsRequired: false),
            new PromptField("endDate", "Ngày kết thúc (yyyy-MM-dd)", _detail.EndDate?.ToString("yyyy-MM-dd"), IsRequired: false),
            new PromptField("status", "Trạng thái", _detail.Status, Options: new[]
            {
                new PromptOption("Active", "Đang hoạt động"),
                new PromptOption("Completed", "Đã hoàn thành"),
                new PromptOption("Cancelled", "Đã hủy")
            })
        }, out var values)) return;

        if (!TryMoney(values["tuitionFee"], out var tuitionFee) || tuitionFee < 0)
        {
            ShowInvalid("Học phí phải là số không âm.");
            return;
        }

        if (!TryDate(values["startDate"], out var startDate) || !TryDate(values["endDate"], out var endDate))
        {
            ShowInvalid("Ngày phải đúng định dạng yyyy-MM-dd.");
            return;
        }

        await SaveAsync(
            () => _apiClient.UpdateCscaClassAsync(_classId, values["name"], values["batch"], _detail.Schedule, tuitionFee, startDate, endDate, values["status"]),
            "Cập nhật thông tin lớp thành công.");
    }

    private async void DeleteClass_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;

        var confirmation = MessageBox.Show(
            this,
            $"Bạn có chắc muốn xóa lớp '{_detail.Code} — {_detail.Name}'?\nDữ liệu sẽ được ẩn khỏi danh sách lớp.",
            "Xác nhận xóa lớp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;

        SetBusy(true, "Đang xóa lớp...");
        try
        {
            if (!await _apiClient.DeleteCscaClassAsync(_classId))
            {
                MessageBox.Show(this, "Không thể xóa lớp. Tài khoản hiện tại có thể chưa có quyền quản lý lớp.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(this, "Đã xóa lớp học. Bạn có thể xem lại danh sách lớp để tiếp tục quản lý.", "Đã xóa lớp", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể xóa lớp:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, string.Empty);
        }
    }

    private async void AddStudent_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;

        if (!CscaStudentDialog.TryShow(
            this,
            _detail.Code,
            _detail.Name,
            _detail.TuitionFee,
            out var res) || res is null) return;

        await SaveAsync(
            () => _apiClient.EnrollCscaStudentAsync(
                _classId, res.StudentName, res.Email, res.PhoneNumber, res.Age,
                res.Hometown, res.PaidAmount, res.PaymentStatus, res.Notes, res.DebtDueDate),
            "Đã thêm học viên vào lớp.");
    }

    private async void EditStudent_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null || StudentsDataGrid.SelectedItem is not StudentRow student)
        {
            ShowInvalid("Hãy chọn một học viên cần sửa.");
            return;
        }

        var studentItem = _detail.Students.FirstOrDefault(s => s.Id == student.Id) ?? new ApiClient.CscaStudentItem(
            student.Id,
            _classId,
            student.StudentName,
            student.Age,
            student.Hometown,
            student.Email,
            student.PhoneNumber,
            student.PaidAmount,
            student.PaymentStatusValue,
            student.JoinedAt,
            student.Notes,
            student.DebtDueDate);

        if (!CscaStudentDialog.TryShow(
            this,
            _detail.Code,
            _detail.Name,
            _detail.TuitionFee,
            out var res,
            studentItem) || res is null) return;

        await SaveAsync(
            () => _apiClient.UpdateCscaStudentAsync(
                _classId, student.Id, res.StudentName, res.Email, res.PhoneNumber, res.Age,
                res.Hometown, res.PaidAmount, res.PaymentStatus, res.Notes, res.DebtDueDate),
            "Đã cập nhật thông tin học viên và công nợ.");
    }

    private void StudentsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StudentsDataGrid.SelectedItem is StudentRow)
            EditStudent_Click(sender, e);
    }

    private async void RemoveStudent_Click(object sender, RoutedEventArgs e)
    {
        if (StudentsDataGrid.SelectedItem is not StudentRow student)
        {
            ShowInvalid("Hãy chọn một học viên cần xóa.");
            return;
        }

        if (MessageBox.Show(this, $"Xóa học viên '{student.StudentName}' khỏi lớp?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveAsync(
            () => _apiClient.RemoveCscaStudentAsync(_classId, student.Id),
            "Đã xóa học viên khỏi lớp.");
    }

    private async void AddTeacher_Click(object sender, RoutedEventArgs e)
    {
        var employees = await _apiClient.GetEmployeesAsync(businessSegment: "CSCA");
        if (employees is null || employees.Items.Count == 0)
        {
            MessageBox.Show(this, "Chưa có nhân sự CSCA để phân công. Hãy tạo hồ sơ nhân sự trước.", "Chưa có nhân sự", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var assignedIds = _detail?.Staff.Select(item => item.EmployeeId).ToHashSet() ?? [];
        var options = employees.Items
            .Where(employee => !assignedIds.Contains(employee.Id))
            .Select(employee => new PromptOption(employee.Id.ToString(), $"{employee.FullName} — {employee.EmployeeCode}"))
            .ToList();
        if (options.Count == 0)
        {
            ShowInvalid("Tất cả nhân sự CSCA hiện đã được phân công vào lớp này.");
            return;
        }

        if (!PromptDialog.TryShow(this, "Phân công giáo viên / nhân sự", new[]
        {
            new PromptField("employeeId", "Nhân sự", Options: options),
            new PromptField("role", "Vai trò", "Teacher", Options: RoleOptions()),
            new PromptField("compensation", "Thù lao", "0"),
            new PromptField("notes", "Ghi chú", IsRequired: false)
        }, out var values)) return;

        if (!Guid.TryParse(values["employeeId"], out var employeeId) || !TryMoney(values["compensation"], out var compensation) || compensation < 0)
        {
            ShowInvalid("Nhân sự hoặc mức thù lao không hợp lệ.");
            return;
        }

        await SaveAsync(
            () => _apiClient.AssignStaffAsync(_classId, employeeId, values["role"], compensation, values["notes"]),
            "Đã phân công giáo viên / nhân sự vào lớp.");
    }

    private async void EditTeacher_Click(object sender, RoutedEventArgs e)
    {
        if (TeachersDataGrid.SelectedItem is not TeacherRow teacher)
        {
            ShowInvalid("Hãy chọn một giáo viên / nhân sự cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa phân công — {teacher.EmployeeName}", new[]
        {
            new PromptField("role", "Vai trò", teacher.RoleInClass, Options: RoleOptions()),
            new PromptField("compensation", "Thù lao", teacher.CompensationRate.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("notes", "Ghi chú", teacher.Notes, IsRequired: false)
        }, out var values)) return;

        if (!TryMoney(values["compensation"], out var compensation) || compensation < 0)
        {
            ShowInvalid("Mức thù lao phải là số không âm.");
            return;
        }

        await SaveAsync(
            () => _apiClient.UpdateCscaStaffAsync(_classId, teacher.Id, values["role"], compensation, values["notes"]),
            "Đã cập nhật phân công giáo viên / nhân sự.");
    }

    private void TeachersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TeachersDataGrid.SelectedItem is TeacherRow)
            EditTeacher_Click(sender, e);
    }

    private async void RemoveTeacher_Click(object sender, RoutedEventArgs e)
    {
        if (TeachersDataGrid.SelectedItem is not TeacherRow teacher)
        {
            ShowInvalid("Hãy chọn một giáo viên / nhân sự cần hủy phân công.");
            return;
        }

        if (MessageBox.Show(this, $"Hủy phân công '{teacher.EmployeeName}' khỏi lớp?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        await SaveAsync(
            () => _apiClient.RemoveCscaStaffAsync(_classId, teacher.Id),
            "Đã hủy phân công giáo viên / nhân sự.");
    }

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

    private async Task SaveAsync(Func<Task<bool>> action, string successMessage)
    {
        SetBusy(true, "Đang lưu dữ liệu...");
        try
        {
            if (!await action())
            {
                MessageBox.Show(this, "Lưu dữ liệu thất bại. Tài khoản hiện tại có thể chưa có quyền quản lý lớp.", "Không thể lưu", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private sealed class StudentRow
    {
        public Guid Id { get; init; }
        public string StudentName { get; init; } = string.Empty;
        public int? Age { get; init; }
        public string? Hometown { get; init; }
        public string? Email { get; init; }
        public string? PhoneNumber { get; init; }
        public decimal TuitionFee { get; init; }
        public decimal PaidAmount { get; init; }
        public decimal DebtAmount => Math.Max(0, TuitionFee - PaidAmount);
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
