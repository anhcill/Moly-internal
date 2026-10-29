using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private DateTime _calendarWeekStart;
    private bool _calendarInitialized;
    private IReadOnlyList<ApiClient.CscaLessonSessionItem> _calendarSessions = [];
    private double _preferredCalendarVerticalOffset;

    private async Task LoadScheduleCalendarSessionsAsync()
    {
        try
        {
            var sessions = await _apiClient.GetCscaLessonSessionsAsync(_classId);
            _calendarSessions = sessions ?? [];
            if (sessions is null)
                ScheduleCoverageText.Text += "  •  Chưa tải được buổi học theo ngày";
            else if (sessions.Count > 0)
                ScheduleCoverageText.Text += $"  •  {sessions.Count} buổi đã tạo; sửa lịch tuần không đổi các buổi đã tạo";
        }
        catch (Exception ex)
        {
            _calendarSessions = [];
            ScheduleCoverageText.Text += $"  •  Lỗi tải buổi học theo ngày: {ex.Message}";
        }
        RenderScheduleCalendar(SchedulesDataGrid.ItemsSource as IReadOnlyList<ScheduleRow> ?? []);
    }

    private void RenderScheduleCalendar(IReadOnlyList<ScheduleRow> schedules)
    {
        if (!_calendarInitialized)
        {
            var today = DateTime.Today;
            var start = _detail?.StartDate?.Date;
            var end = _detail?.EndDate?.Date;
            var anchor = start.HasValue && (today < start || (end.HasValue && today > end)) ? start.Value : today;
            _calendarWeekStart = anchor.AddDays(-((int)anchor.DayOfWeek + 6) % 7);
            _calendarInitialized = true;
        }

        var weekEnd = _calendarWeekStart.AddDays(6);
        ScheduleWeekLabel.Text = $"{_calendarWeekStart:dd/MM} – {weekEnd:dd/MM/yyyy}";
        var firstHour = Math.Min(6, Math.Min(
            schedules.Count == 0 ? 6 : schedules.Min(s => s.StartTime.Hours),
            _calendarSessions.Count == 0 ? 6 : _calendarSessions.Min(s => s.StartTime.Hours)));
        var lastHour = Math.Max(22, Math.Max(
            schedules.Count == 0 ? 22 : (int)Math.Ceiling(schedules.Max(s => s.EndTime.TotalHours)),
            _calendarSessions.Count == 0 ? 22 : (int)Math.Ceiling(_calendarSessions.Max(s => s.EndTime.TotalHours))));
        const double hourHeight = 52;
        var calendarHeight = (lastHour - firstHour) * hourHeight;

        // 1. Cấu hình hàng Header cố định (Thứ 2 - Chủ nhật)
        ScheduleHeaderGrid.Children.Clear();
        ScheduleHeaderGrid.ColumnDefinitions.Clear();
        ScheduleHeaderGrid.RowDefinitions.Clear();
        ScheduleHeaderGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        ScheduleHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        for (var day = 0; day < 7; day++)
            ScheduleHeaderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });

        var corner = new Border
        {
            Background = Brush("#F8FAFC"),
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(0, 0, 1, 1),
            Child = new TextBlock
            {
                Text = "GIỜ",
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#94A3B8"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(corner, 0);
        ScheduleHeaderGrid.Children.Add(corner);

        // 2. Cấu hình thân thời khóa biểu (các giờ và ô buổi học theo từng ngày)
        ScheduleCalendarGrid.Children.Clear();
        ScheduleCalendarGrid.ColumnDefinitions.Clear();
        ScheduleCalendarGrid.RowDefinitions.Clear();
        ScheduleCalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(calendarHeight) });
        ScheduleCalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        for (var day = 0; day < 7; day++)
            ScheduleCalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 100 });

        var gutter = new Canvas { Height = calendarHeight, Background = Brushes.White };
        for (var hour = firstHour; hour <= lastHour; hour++)
        {
            var label = new TextBlock { Text = $"{hour:00}:00", FontSize = 11, Foreground = Brush("#64748B") };
            Canvas.SetTop(label, Math.Min(calendarHeight - 16, Math.Max(0, (hour - firstHour) * hourHeight - 7)));
            Canvas.SetLeft(label, 8);
            gutter.Children.Add(label);
        }
        var gutterBorder = new Border
        {
            Child = gutter,
            BorderBrush = Brush("#E2E8F0"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background = Brushes.White
        };
        Grid.SetColumn(gutterBorder, 0);
        ScheduleCalendarGrid.Children.Add(gutterBorder);

        for (var dayIndex = 0; dayIndex < 7; dayIndex++)
        {
            var date = _calendarWeekStart.AddDays(dayIndex);
            var dayOfWeek = (int)date.DayOfWeek;
            var dateIsInCourse = (_detail?.StartDate is null || date.Date >= _detail.StartDate.Value.Date)
                && (_detail?.EndDate is null || date.Date <= _detail.EndDate.Value.Date);
            var isToday = date.Date == DateTime.Today;
            var header = new Border
            {
                Background = isToday ? Brush("#EEF2FF") : Brushes.White,
                BorderBrush = Brush("#E2E8F0"),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new StackPanel
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = dayIndex == 6 ? "CHỦ NHẬT" : $"THỨ {dayIndex + 2}", FontSize = 10, FontWeight = FontWeights.SemiBold, Foreground = Brush("#64748B"), HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = date.ToString("dd/MM"), FontSize = 16, FontWeight = FontWeights.Bold, Foreground = isToday ? Brush("#4F46E5") : Brush("#0F172A"), HorizontalAlignment = HorizontalAlignment.Center }
                    }
                }
            };
            Grid.SetColumn(header, dayIndex + 1);
            ScheduleHeaderGrid.Children.Add(header);

            var canvas = new Canvas { Height = calendarHeight, Background = dateIsInCourse ? Brushes.White : Brush("#F8FAFC") };
            canvas.SizeChanged += (s, e) =>
            {
                var newW = Math.Max(70, e.NewSize.Width - 10);
                foreach (UIElement child in canvas.Children)
                {
                    if (child is Button btn)
                        btn.Width = newW;
                    else if (child is Border b && b.Height == 1)
                        b.Width = e.NewSize.Width;
                }
            };
            canvas.SizeChanged += (s, e) =>
            {
                var newW = Math.Max(70, e.NewSize.Width - 10);
                foreach (UIElement child in canvas.Children)
                {
                    if (child is Button btn)
                        btn.Width = newW;
                    else if (child is Border b && b.Height == 1)
                        b.Width = e.NewSize.Width;
                }
            };
            for (var hour = firstHour; hour <= lastHour; hour++)
            {
                var line = new Border { Height = 1, Width = 150, Background = Brush("#E9EEF5") };
                Canvas.SetTop(line, (hour - firstHour) * hourHeight);
                canvas.Children.Add(line);
            }
            var dailySessions = _calendarSessions
                .Where(s => s.LessonDate == DateOnly.FromDateTime(date))
                .ToList();
            if (dateIsInCourse || dailySessions.Count > 0)
            {
                if (dailySessions.Count == 0)
                {
                    foreach (var schedule in schedules.Where(s => s.DayOfWeek == dayOfWeek &&
                                 !HasMovedSessionForSchedule(s.Id, date)))
                        AddCalendarEvent(canvas, schedule.StartTime, schedule.EndTime, schedule.TimeLabel,
                            _detail?.Name ?? "Buổi học", $"Lịch cố định · chưa tạo buổi · {schedule.LocationLabel}", schedule,
                            "#EDE9FE", "#A5B4FC", firstHour, hourHeight);
                }
                else
                    foreach (var session in dailySessions)
                    {
                        var row = LessonSessionRow.From(session);
                        AddCalendarEvent(canvas, session.StartTime, session.EndTime,
                            row.TimeLabel,
                            _detail?.Name ?? "Buổi học", session.Status == "Cancelled" ? "Đã hủy" :
                            $"{row.StatusLabel} · {row.LocationLabel}",
                            session, session.Status == "Cancelled" ? "#F1F5F9" : "#DBEAFE",
                            session.Status == "Cancelled" ? "#CBD5E1" : "#93C5FD", firstHour, hourHeight);
                    }
            }
            var dayBorder = new Border { Child = canvas, BorderBrush = Brush("#E2E8F0"), BorderThickness = new Thickness(0, 0, 1, 0) };
            Grid.SetColumn(dayBorder, dayIndex + 1);
            ScheduleCalendarGrid.Children.Add(dayBorder);
        }

        // Tính vị trí cuộn tối ưu cho ca tối (mặc định ~16:30 hoặc 1 giờ trước buổi tối sớm nhất)
        double targetHour = 16.5;
        var eveningTimes = schedules
            .Where(s => s.StartTime.TotalHours >= 16.0)
            .Select(s => s.StartTime.TotalHours)
            .Concat(_calendarSessions
                .Where(s => s.StartTime.TotalHours >= 16.0)
                .Select(s => s.StartTime.TotalHours))
            .ToList();

        if (eveningTimes.Count > 0)
        {
            var earliestEvening = eveningTimes.Min();
            targetHour = Math.Max(firstHour, earliestEvening - 1.0);
        }

        _preferredCalendarVerticalOffset = Math.Max(0, (targetHour - firstHour) * hourHeight);

        // Tự động cuộn xuống ca tối
        ScrollCalendarToEvening();
    }

    private bool HasMovedSessionForSchedule(Guid scheduleId, DateTime scheduledDate)
    {
        var weekStart = scheduledDate.Date.AddDays(-((int)scheduledDate.DayOfWeek + 6) % 7);
        var weekEnd = weekStart.AddDays(7);
        return _calendarSessions.Any(session =>
            session.ScheduleId == scheduleId &&
            string.Equals(session.Status, "Rescheduled", StringComparison.OrdinalIgnoreCase) &&
            session.LessonDate != DateOnly.FromDateTime(scheduledDate) &&
            session.LessonDate >= DateOnly.FromDateTime(weekStart) &&
            session.LessonDate < DateOnly.FromDateTime(weekEnd));
    }

    private void AddCalendarEvent(Canvas canvas, TimeSpan start, TimeSpan end, string time,
        string title, string subtitle, object item, string fill, string stroke, int firstHour, double hourHeight)
    {
        var eventButton = new Button
        {
            Tag = item,
            Width = 138,
            Height = Math.Max(34, (end - start).TotalHours * hourHeight - 4),
            Padding = new Thickness(8, 5, 5, 5),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Background = Brush(fill),
            BorderBrush = Brush(stroke),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = $"{time} · {subtitle}\nBấm để xem và chỉnh sửa.",
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = time, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = Brush("#1E3A8A") },
                    new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 11, Foreground = Brush("#1E40AF") },
                    new TextBlock { Text = subtitle, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 10, Foreground = Brush("#475569") }
                }
            }
        };
        eventButton.Click += ScheduleCalendarEvent_Click;
        Canvas.SetTop(eventButton, (start.TotalHours - firstHour) * hourHeight + 2);
        Canvas.SetLeft(eventButton, 5);
        canvas.Children.Add(eventButton);
    }

    private static Brush Brush(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;

    private void PreviousScheduleWeek_Click(object sender, RoutedEventArgs e)
    {
        _calendarWeekStart = _calendarWeekStart.AddDays(-7);
        RenderScheduleCalendar(SchedulesDataGrid.ItemsSource as IReadOnlyList<ScheduleRow> ?? []);
    }

    private void CurrentScheduleWeek_Click(object sender, RoutedEventArgs e)
    {
        var today = DateTime.Today;
        _calendarWeekStart = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        RenderScheduleCalendar(SchedulesDataGrid.ItemsSource as IReadOnlyList<ScheduleRow> ?? []);
    }

    private void NextScheduleWeek_Click(object sender, RoutedEventArgs e)
    {
        _calendarWeekStart = _calendarWeekStart.AddDays(7);
        RenderScheduleCalendar(SchedulesDataGrid.ItemsSource as IReadOnlyList<ScheduleRow> ?? []);
    }

    public void ScrollCalendarToEvening()
    {
        if (ScheduleCalendarScrollViewer == null) return;

        void ApplyScroll()
        {
            if (ScheduleCalendarScrollViewer == null) return;
            var offset = _preferredCalendarVerticalOffset > 0 ? _preferredCalendarVerticalOffset : 546;
            ScheduleCalendarScrollViewer.ScrollToVerticalOffset(offset);
            if (ScheduleHeaderScrollViewer != null)
            {
                ScheduleHeaderScrollViewer.ScrollToHorizontalOffset(ScheduleCalendarScrollViewer.HorizontalOffset);
            }
        }

        ApplyScroll();
        Dispatcher.BeginInvoke((Action)ApplyScroll, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    public void ScrollCalendarToMorning()
    {
        if (ScheduleCalendarScrollViewer == null) return;
        ScheduleCalendarScrollViewer.ScrollToVerticalOffset(0);
        if (ScheduleHeaderScrollViewer != null)
        {
            ScheduleHeaderScrollViewer.ScrollToHorizontalOffset(ScheduleCalendarScrollViewer.HorizontalOffset);
        }
    }

    private void ScrollToEvening_Click(object sender, RoutedEventArgs e)
    {
        ScrollCalendarToEvening();
    }

    private void ScrollToMorning_Click(object sender, RoutedEventArgs e)
    {
        ScrollCalendarToMorning();
    }

    private void ScheduleCalendarScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 && ScheduleHeaderScrollViewer != null)
        {
            ScheduleHeaderScrollViewer.ScrollToHorizontalOffset(e.HorizontalOffset);
        }
    }

    private void ScheduleHeaderScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ScheduleCalendarScrollViewer != null)
        {
            ScheduleCalendarScrollViewer.ScrollToVerticalOffset(ScheduleCalendarScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private async void ScheduleCalendarEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduleRow schedule })
        {
            SchedulesDataGrid.SelectedItem = schedule;
            EditSchedule_Click(sender, e);
        }
        else if (sender is Button { Tag: ApiClient.CscaLessonSessionItem session })
        {
            var row = LessonSessionRow.From(session);
            if (!PromptDialog.TryShow(this, $"Buổi học {row.LessonDate:dd/MM/yyyy}", await LessonSessionFieldsAsync(row), out var values)) return;
            if (!TryLessonSessionValues(values, out var lessonDate, out var startTime, out var endTime, out var classroomId)) return;
            var timeChanged = lessonDate != row.LessonDate || startTime != row.StartTime || endTime != row.EndTime;
            var changeReason = Null(values["changeReason"]) ?? (timeChanged ? "Đổi lịch học" : null);
            await SaveAsync(() => _apiClient.UpdateCscaLessonSessionAsync(_classId, row.Id, lessonDate,
                startTime, endTime, classroomId, Null(values["meetingUrl"]), Null(values["notes"]), values["status"], changeReason),
                "Đã lưu thay đổi buổi học.");
        }
    }

    private async void SetupCourseSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;
        var schedules = SchedulesDataGrid.ItemsSource as IReadOnlyList<ScheduleRow> ?? [];
        if (schedules.Count == 0)
        {
            ShowInvalid("Hãy thêm ít nhất một khung giờ cố định trong tuần trước khi thiết lập kỳ học.");
            return;
        }
        var defaultStart = _detail.StartDate?.Date ?? DateTime.Today;
        if (!PromptDialog.TryShow(this, "Thiết lập lịch cố định cho khóa học", new[]
        {
            new PromptField("weeklyCount", "Số buổi học mỗi tuần", schedules.Count.ToString(CultureInfo.InvariantCulture)),
            new PromptField("fromDate", "Ngày bắt đầu", defaultStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date),
            new PromptField("toDate", "Ngày kết thúc", (_detail.EndDate?.Date ?? defaultStart.AddMonths(2)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date)
        }, out var values)) return;
        if (!int.TryParse(values["weeklyCount"], NumberStyles.None, CultureInfo.InvariantCulture, out var weeklyCount)
            || weeklyCount is < 1 or > 14 || weeklyCount != schedules.Count)
        {
            ShowInvalid($"Số buổi mỗi tuần phải bằng số khung giờ cố định đã tạo ({schedules.Count}). Hãy thêm hoặc xóa khung giờ trước.");
            return;
        }
        if (!TryDateOnly(values["fromDate"], out var fromDate) || !TryDateOnly(values["toDate"], out var toDate)
            || toDate < fromDate || toDate.DayNumber - fromDate.DayNumber > 366)
        {
            ShowInvalid("Ngày bắt đầu/kết thúc phải đúng yyyy-MM-dd, theo thứ tự và trong tối đa 366 ngày.");
            return;
        }
        if (_calendarSessions.Any(s => s.LessonDate < fromDate || s.LessonDate > toDate))
        {
            ShowInvalid("Đã có buổi học nằm ngoài khoảng ngày mới. Hãy giữ hoặc mở rộng kỳ học để không làm mất tính nhất quán của lịch.");
            return;
        }

        SetBusy(true, "Đang lưu kỳ học và tạo các buổi theo lịch cố định...");
        try
        {
            if (!await _apiClient.UpdateCscaClassAsync(_classId, _detail.Name, _detail.Batch, _detail.Schedule,
                    _detail.TuitionFee, fromDate.ToDateTime(TimeOnly.MinValue), toDate.ToDateTime(TimeOnly.MinValue),
                    _detail.Status, _detail.CourseId))
            {
                ShowInvalid("Không lưu được ngày bắt đầu và kết thúc của lớp. Hãy kiểm tra quyền hoặc dữ liệu lớp.");
                return;
            }
            var created = await _apiClient.GenerateCscaLessonSessionsAsync(_classId, fromDate, toDate);
            await LoadDetailAsync();
            if (!created.HasValue)
            {
                ShowInvalid("Đã lưu thời hạn lớp nhưng chưa thể tạo buổi học. Hãy kiểm tra xung đột phòng/lịch rồi bấm Thiết lập kỳ học để thử lại.");
                return;
            }
            LoadingText.Text = created.Value == 0
                ? "Lịch cố định đã được lưu; các buổi trong kỳ đã tồn tại."
                : $"Đã lưu {weeklyCount} buổi/tuần đến {toDate:dd/MM/yyyy} và tạo {created.Value} buổi học.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể thiết lập kỳ học:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false, LoadingText.Text);
        }
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

        if (_calendarSessions.Any(s => s.ScheduleId == schedule.Id) &&
            MessageBox.Show(this,
                "Khung giờ này đã sinh buổi học. Thay đổi lịch tuần chỉ áp dụng cho các buổi tạo sau; các buổi đã tạo phải sửa riêng trên lịch. Tiếp tục?",
                "Lịch đã có buổi học", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
            return;

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
        if (_calendarSessions.Any(s => s.ScheduleId == schedule.Id))
        {
            ShowInvalid("Khung giờ này đã sinh buổi học theo ngày. Hãy xử lý các buổi học tương ứng trước khi xóa lịch cố định.");
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
