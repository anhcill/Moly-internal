using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    // ── EdTech Course / Question Actions (Ngày 9 Core) ──

    private void CourseSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        CourseSearchPlaceholder.Visibility = string.IsNullOrEmpty(CourseSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("courses", "Đang tìm khóa học...", () => LoadCoursesAsync(CourseSearchBox.Text));
    }

    private void RefreshCourses_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải danh sách khóa học...", () => LoadCoursesAsync(CourseSearchBox.Text));
    private void RefreshSyncRuns_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải lịch sử đồng bộ và Dead-Letter...", () => LoadSyncRunsAsync());
    private void RefreshDeadLetters_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải Dead-Letter Queue...", LoadDeadLettersAsync);
    private void RefreshLmsIntegration_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải dữ liệu đồng bộ Web CSCA...", LoadLmsIntegrationAsync);

    private void LmsCourseMappingsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LmsCourseMappingsDataGrid.SelectedItem is not ApiClient.LmsCourseMappingItem mapping)
        {
            LmsSelectedCourseText.Text = "Chọn một khóa học từ bảng bên dưới";
            LmsSelectedCourseStatusBadge.Text = "⚪ Chưa chọn khóa học";
            LmsSelectedCourseStatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            LmsSelectedCourseStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            LmsExternalCourseIdBox.Text = string.Empty;
            LmsCourseSlugBox.Text = string.Empty;
            LmsCourseIdBox.Text = string.Empty;
            LmsEnableAccessCheckBox.IsChecked = false;
            return;
        }

        LmsSelectedCourseText.Text = mapping.CourseTitle;
        LmsExternalCourseIdBox.Text = mapping.ExternalCourseId ?? mapping.CourseSourceId;

        var existingSlug = mapping.LmsCourseSlug ?? mapping.CourseSlug;
        LmsCourseSlugBox.Text = !string.IsNullOrWhiteSpace(existingSlug) ? existingSlug : GenerateSlug(mapping.CourseTitle);
        LmsCourseIdBox.Text = mapping.LmsCourseId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

        var isActive = string.Equals(mapping.Status, "Success", StringComparison.OrdinalIgnoreCase);
        LmsEnableAccessCheckBox.IsChecked = isActive;

        if (isActive)
        {
            LmsSelectedCourseStatusBadge.Text = "ĐÃ KÍCH HOẠT CẤP QUYỀN";
            LmsSelectedCourseStatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            LmsSelectedCourseStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
        }
        else if (string.Equals(mapping.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            LmsSelectedCourseStatusBadge.Text = "ĐÃ LIÊN KẾT (CHƯA BẬT)";
            LmsSelectedCourseStatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(254, 243, 199));
            LmsSelectedCourseStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(146, 64, 14));
        }
        else
        {
            LmsSelectedCourseStatusBadge.Text = "⚪ CHƯA LIÊN KẾT WEB";
            LmsSelectedCourseStatusBadgeBorder.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249));
            LmsSelectedCourseStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139));
        }
    }

    private void AutoGenerateSlug_Click(object sender, RoutedEventArgs e)
    {
        if (LmsCourseMappingsDataGrid.SelectedItem is ApiClient.LmsCourseMappingItem mapping && !string.IsNullOrWhiteSpace(mapping.CourseTitle))
        {
            LmsCourseSlugBox.Text = GenerateSlug(mapping.CourseTitle);
            ShowToast("Đã tự động tạo đường dẫn slug chuẩn từ tên khóa học.");
        }
        else if (!string.IsNullOrWhiteSpace(LmsSelectedCourseText.Text) && LmsSelectedCourseText.Text != "Chọn một khóa học từ bảng bên dưới")
        {
            LmsCourseSlugBox.Text = GenerateSlug(LmsSelectedCourseText.Text);
            ShowToast("Đã tự động tạo đường dẫn slug chuẩn.");
        }
        else
        {
            ShowToast("Vui lòng click chọn một khóa học trong bảng trước khi tạo slug.");
        }
    }

    private static string GenerateSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                if (c == 'đ' || c == 'Đ') sb.Append('d');
                else sb.Append(c);
            }
        }
        var cleaned = Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), @"[^a-z0-9\s-]", "");
        cleaned = Regex.Replace(cleaned, @"\s+", "-").Trim('-');
        return cleaned;
    }

    private async void SaveLmsMapping_Click(object sender, RoutedEventArgs e)
    {
        if (LmsCourseMappingsDataGrid.SelectedItem is not ApiClient.LmsCourseMappingItem mapping)
        {
            ShowToast("Vui lòng chọn một khóa học từ danh sách trước khi lưu.", isError: true);
            return;
        }

        long? lmsCourseId = null;
        var rawLmsCourseId = LmsCourseIdBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(rawLmsCourseId))
        {
            if (!long.TryParse(rawLmsCourseId, out var parsedLmsCourseId) || parsedLmsCourseId <= 0)
            {
                ShowToast("Mã số Web (LMS course ID) phải là số nguyên dương.", isError: true);
                return;
            }

            lmsCourseId = parsedLmsCourseId;
        }

        var slug = LmsCourseSlugBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            slug = GenerateSlug(mapping.CourseTitle);
            LmsCourseSlugBox.Text = slug;
        }

        var enableAccess = LmsEnableAccessCheckBox.IsChecked == true;
        if (enableAccess)
        {
            var confirmation = MessageBox.Show(
                $"Kích hoạt đồng bộ sang Web CSCA Course cho khóa '{mapping.CourseTitle}'?\n\nSau khi kích hoạt, bất kỳ học viên nào hoàn thành học phí của khóa này sẽ được tự động cấp quyền vào học trên Web Course ngay lập tức.",
                "Xác nhận kích hoạt cấp quyền Web Course",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes) return;
        }

        await RunWithBusyAsync("Đang lưu cấu hình đồng bộ Web CSCA...", async () =>
        {
            var saved = await _apiClient.UpsertLmsCourseMappingAsync(
                mapping.CourseId,
                new ApiClient.LmsCourseMappingRequest(
                    string.IsNullOrWhiteSpace(LmsExternalCourseIdBox.Text) ? null : LmsExternalCourseIdBox.Text.Trim(),
                    lmsCourseId,
                    string.IsNullOrWhiteSpace(slug) ? null : slug,
                    enableAccess));
            if (saved is null)
                throw new InvalidOperationException("Không lưu được cấu hình liên kết Web. Vui lòng kiểm tra quyền thao tác hoặc thông tin mã/slug.");

            ShowToast(enableAccess
                ? "Đã kích hoạt tự động cấp quyền Web CSCA Course thành công!"
                : "Đã lưu cấu hình liên kết (đang ở trạng thái tạm dừng cấp quyền).");
            await LoadLmsIntegrationAsync();
        });
    }

    private async void RetryLmsOutbox_Click(object sender, RoutedEventArgs e)
    {
        if (LmsOutboxDataGrid.SelectedItem is not ApiClient.LmsOutboxItem item)
        {
            ShowToast("Vui lòng chọn một dòng lệnh trong danh sách để thử lại.");
            return;
        }

        if (string.Equals(item.Status, "Success", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Lệnh này đã được đồng bộ thành công sang Web, không cần thử lại.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Thử lại lệnh '{item.EventDisplayName}' sang Web CSCA Course?\n\nHệ thống sẽ đưa lệnh này trở lại hàng chờ để gửi sang Web.",
            "Xác nhận thử lại đồng bộ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang đưa lệnh về hàng chờ gửi lại...", async () =>
        {
            if (!await _apiClient.RetryLmsOutboxAsync(item.Id))
                throw new InvalidOperationException("Không thể đưa lệnh LMS về hàng chờ.");

            ShowToast("Đã đưa lệnh về hàng chờ để gửi lại sang Web.");
            await LoadLmsIntegrationAsync();
        });
    }

    private async void DispatchLmsOutbox_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            "Đồng bộ ngay các lệnh đang chờ sang Web CSCA Course?\n\nHệ thống sẽ gửi tài khoản và quyền truy cập của học viên đã đóng học phí sang Web Course để học viên có thể đăng nhập học ngay.",
            "Xác nhận đồng bộ sang Web Course",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang gửi dữ liệu sang Web CSCA Course...", async () =>
        {
            var result = await _apiClient.DispatchLmsOutboxAsync();
            if (result is null)
                throw new InvalidOperationException("Không gửi được dữ liệu sang Web Course. Vui lòng kiểm tra kết nối mạng và khóa bảo mật.");

            ShowToast($"Đồng bộ Web Course hoàn tất: {result.Succeeded} thành công, {result.Retrying} đang thử lại, {result.DeadLettered} cần kiểm tra.");
            await LoadLmsIntegrationAsync();
        });
    }

    private async void RetryDeadLetter_Click(object sender, RoutedEventArgs e)
    {
        if (ViewSyncContainer.SelectedDeadLetter is not { } deadLetter)
        {
            ShowToast("Vui lòng chọn một bản ghi Dead-Letter trước khi retry.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Retry bản ghi '{deadLetter.SourceId}' từ {deadLetter.SourceSystem}?\nHệ thống sẽ ghi nhận lần thử lại mới.",
            "Xác nhận retry Dead-Letter",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang retry bản ghi Dead-Letter...", async () =>
        {
            var success = await _apiClient.RetryDeadLetterAsync(deadLetter.Id);
            if (!success)
            {
                throw new InvalidOperationException("Retry thất bại hoặc bản ghi không còn tồn tại.");
            }

            ShowToast("Retry Dead-Letter thành công.");
            await LoadSyncRunsAsync();
        });
    }

    private void ExportSyncRuns_Click(object sender, RoutedEventArgs e)
    {
        var rows = ViewSyncContainer.Runs.ToList();
        ExportCsv(
            "Xuất lịch sử đồng bộ",
            "lich-su-dong-bo.csv",
            ["Nguồn", "Loại thực thể", "Bắt đầu", "Kết thúc", "Đã đọc", "Đã ghi", "Bỏ qua", "Lỗi", "Trạng thái", "Chi tiết lỗi"],
            rows.Select(item => new[]
            {
                item.SourceSystem,
                item.EntityType,
                item.StartedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                item.CompletedAt?.ToString("dd/MM/yyyy HH:mm:ss") ?? string.Empty,
                item.RecordsRead.ToString(),
                item.RecordsWritten.ToString(),
                item.RecordsSkipped.ToString(),
                item.RecordsFailed.ToString(),
                item.StatusLabel,
                item.ErrorMessage ?? string.Empty
            }));
    }

    private void ExportInventory_Click(object sender, RoutedEventArgs e)
    {
        var rows = InventoryBalancesDataGrid.Items.OfType<ApiClient.InventoryBalanceItem>().ToList();
        ExportCsv(
            "Xuất tồn kho",
            "ton-kho.csv",
            ["Mã SKU", "Sản phẩm", "Màu", "Cỡ", "Tồn thực tế", "Đang giữ", "Khả dụng", "Cập nhật lúc"],
            rows.Select(item => new[]
            {
                item.Sku,
                item.ProductName ?? string.Empty,
                item.Color ?? string.Empty,
                item.Size ?? string.Empty,
                item.OnHandQuantity.ToString(),
                item.ReservedQuantity.ToString(),
                item.AvailableQuantity.ToString(),
                item.LastUpdated.ToString("dd/MM/yyyy HH:mm:ss")
            }));
    }

    private void ExportCsv(string title, string defaultFileName, IReadOnlyList<string> headers, IEnumerable<IEnumerable<string>> rows)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = title,
            FileName = defaultFileName,
            Filter = "Tệp CSV UTF-8 (*.csv)|*.csv|Tất cả tệp (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var materializedRows = rows.Select(row => row.ToArray()).ToList();
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", headers.Select(CsvEscape)));
            foreach (var row in materializedRows)
            {
                builder.AppendLine(string.Join(",", row.Select(CsvEscape)));
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            ShowToast($"Đã xuất {materializedRows.Count:N0} dòng ra {Path.GetFileName(dialog.FileName)}.");
        }
        catch (Exception ex)
        {
            ShowToast($"Không thể xuất file: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private async void CreateCourseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo khóa học", new[]
        {
            new PromptField("title", "Tên khóa học"),
            new PromptField("price", "Học phí", InitialValue: "0"),
            new PromptField("description", "Mô tả", IsRequired: false)
        }, out var values)) return;

        if (!decimal.TryParse(values["price"], out var price) || price < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var newTitle = values["title"];
        await RunWithBusyAsync("Đang tạo khóa học mới...", async () =>
        {
            var success = await _apiClient.CreateCourseAsync(newTitle, price, values["description"]);
            if (success)
            {
                MessageBox.Show($"Tạo thành công khóa học: '{newTitle}'!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCoursesAsync();
                await LoadDashboardMetricsAsync();
            }
            else
            {
                MessageBox.Show("Tạo khóa học thất bại. Bạn có quyền Courses.Manage không?", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void EditCourseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (CoursesDataGrid.SelectedItem is not ApiClient.CourseItem course)
        {
            ShowToast("Vui lòng chọn một khóa học cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa khóa học — {course.Title}", new[]
        {
            new PromptField("title", "Tên khóa học", course.Title),
            new PromptField("price", "Học phí (VND)", course.Price.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("slug", "Slug URL", course.Slug, IsRequired: false),
            new PromptField("description", "Mô tả", course.Description, IsRequired: false),
            new PromptField("status", "Trạng thái", course.Status, Options: new[]
            {
                new PromptOption("Published", "Published (Đã xuất bản)"),
                new PromptOption("Draft", "Draft (Bản nháp)"),
                new PromptOption("Archived", "Archived (Lưu trữ)")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["price"], NumberStyles.Number, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunWithBusyAsync("Đang cập nhật khóa học...", async () =>
        {
            var success = await _apiClient.UpdateCourseAsync(course.Id, values["title"], price, values["description"], values["status"], values["slug"]);
            if (success)
            {
                MessageBox.Show($"Cập nhật thành công khóa học: '{values["title"]}'!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCoursesAsync();
                await LoadDashboardMetricsAsync();
            }
            else
            {
                MessageBox.Show("Cập nhật khóa học thất bại. Vui lòng kiểm tra kết nối hoặc quyền hạn.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void DeleteCourse_Click(object sender, RoutedEventArgs e)
    {
        if (CoursesDataGrid.SelectedItem is not ApiClient.CourseItem course)
        {
            ShowToast("Vui lòng chọn một khóa học cần xóa.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa khóa học '{course.Title}' (Mã: {course.CourseSourceId})?\nLưu ý: Không thể xóa khóa học đang có lớp học hoạt động.",
            "Xác nhận xóa khóa học",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang xóa khóa học...", async () =>
        {
            var success = await _apiClient.DeleteCourseAsync(course.Id);
            if (success)
            {
                MessageBox.Show($"Đã xóa khóa học: '{course.Title}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCoursesAsync();
                await LoadDashboardMetricsAsync();
            }
            else
            {
                MessageBox.Show("Không thể xóa khóa học. Khóa học có thể đang chứa lớp học hoạt động hoặc bạn không có quyền Courses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private void CoursesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => FilterClassesBySelectedCourse();

    private void ViewCourseClasses_Click(object sender, RoutedEventArgs e)
        => FilterClassesBySelectedCourse();

    private void FilterClassesBySelectedCourse()
    {
        if (CoursesDataGrid.SelectedItem is not ApiClient.CourseItem course)
        {
            ShowToast("Vui lòng chọn một khóa học.");
            return;
        }

        _selectedCourseFilterId = course.Id;
        _selectedCourseFilterTitle = course.Title;

        CourseModuleTabControl.SelectedItem = TabItemClasses;

        if (CscaCourseFilterComboBox.ItemsSource is IEnumerable<CourseFilterOption> options)
        {
            var match = options.FirstOrDefault(o => o.Id == course.Id);
            if (match != null)
            {
                CscaCourseFilterComboBox.SelectedItem = match;
            }
        }

        ApplyCscaClassFilter();
    }

    private void ClearCscaCourseFilter_Click(object sender, RoutedEventArgs e)
    {
        _selectedCourseFilterId = null;
        _selectedCourseFilterTitle = null;
        CscaCourseFilterComboBox.SelectedIndex = 0;
        ApplyCscaClassFilter();
    }

    private void CscaCourseFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady) return;
        if (CscaCourseFilterComboBox.SelectedItem is CourseFilterOption opt)
        {
            _selectedCourseFilterId = opt.Id;
            _selectedCourseFilterTitle = opt.Id.HasValue ? opt.Title : null;
            ApplyCscaClassFilter();
        }
    }

    private void CourseModuleTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl tabControl) return;
        if (tabControl.SelectedItem is TabItem selectedTab)
        {
            if (selectedTab == TabItemClasses)
            {
                if (_allCscaClasses.Count == 0)
                {
                    _ = LoadCscaClassesAsync();
                }
            }
        }
    }

    private void CreateQuestionDialog_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Chức năng tạo câu hỏi tương tác hoàn chỉnh sẽ mở modal nhập chi tiết. Đang hỗ trợ tạo tự động qua API & Sync.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void PublishQuestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is Guid versionId)
        {
            var res = MessageBox.Show(
                "Bạn có chắc chắn muốn phê duyệt và xuất bản câu hỏi này vào ngân hàng đề thi chính thức?",
                "Xác Nhận Xuất Bản",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                btn.IsEnabled = false;
                var success = await _apiClient.PublishQuestionVersionAsync(versionId, "Đã phê duyệt xuất bản từ WPF Desktop Client.");
                if (success)
                {
                    MessageBox.Show("Xuất bản câu hỏi thành công! Trạng thái đã chuyển sang Published.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                    await LoadQuestionsAsync(ViewQuestionsContainer.SearchText);
                }
                else
                {
                    MessageBox.Show("Xuất bản thất bại. Bạn cần có quyền Questions.Publish.", "Quyền Hạn", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                btn.IsEnabled = true;
            }
        }
    }

}
