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
            await LoadScheduleCalendarSessionsAsync();
            if (DetailTabs.SelectedItem == AttendanceReportTab)
                await LoadAttendanceReportAsync();
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
            $"Dự thu: {Money(detail.FinancialSummary.ExpectedRevenue)}    |    Đã thu: {Money(detail.FinancialSummary.ActualRevenue)}    |    Còn nợ: {Money(Math.Max(0, detail.FinancialSummary.ExpectedRevenue - detail.FinancialSummary.ActualRevenue))}    |    Sau thù lao GV: {Money(detail.FinancialSummary.NetProfit)}";

        var studentRows = detail.Students
            .Select(student => StudentRow.From(student, detail.TuitionFee))
            .ToList();
        StudentsDataGrid.ItemsSource = studentRows;
        StudentFinanceDataGrid.ItemsSource = studentRows;
        var teacherRows = detail.Staff
            .Select(TeacherRow.From)
            .ToList();
        TeachersDataGrid.ItemsSource = teacherRows;
        TeacherFinanceDataGrid.ItemsSource = teacherRows;
        SchedulesDataGrid.ItemsSource = scheduleRows;
        RenderScheduleCalendar(scheduleRows);

        var weeklyDuration = scheduleRows.Aggregate(TimeSpan.Zero, (total, schedule) => total + (schedule.EndTime - schedule.StartTime));
        ScheduleSummaryText.Text = scheduleRows.Count == 0
            ? "Chưa có khung giờ cố định. Hãy thêm các buổi học hằng tuần."
            : $"{scheduleRows.Count} buổi/tuần  •  Tổng thời lượng {FormatDuration(weeklyDuration)}";
        ScheduleCoverageText.Text = scheduleRows.Count == 0
            ? "Sau khi thêm khung giờ, chọn Thiết lập kỳ học để đặt ngày kết thúc và tạo buổi học."
            : $"{scheduleRows.Select(schedule => schedule.DayOfWeek).Distinct().Count()} ngày học/tuần  •  {FormatDate(detail.StartDate)} – {FormatDate(detail.EndDate)}";
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

}
