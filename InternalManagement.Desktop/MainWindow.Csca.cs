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
    // ── CSCA & Interview Actions (Ngày 10) ──

    private void CscaSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        CscaSearchPlaceholder.Visibility = string.IsNullOrEmpty(CscaSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("csca", "Đang tìm dữ liệu CSCA...", async () =>
        {
            await LoadCscaClassesAsync(CscaSearchBox.Text);
            await LoadCscaOnlineAsync(CscaSearchBox.Text);
        });
    }

    private void RefreshCsca_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải dữ liệu CSCA...", async () =>
    {
        await LoadCscaClassesAsync(CscaSearchBox.Text);
        await LoadCscaOnlineAsync(CscaSearchBox.Text);
    });

    private void RefreshCscaOnline_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tải nội dung CSCA từ website...", () => LoadCscaOnlineAsync(CscaSearchBox.Text));

    private void CscaClassesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => _ = ShowSelectedCscaClassDetailsAsync();

    private void ViewCscaClassDetails_Click(object sender, RoutedEventArgs e)
        => _ = ShowSelectedCscaClassDetailsAsync();

    private void ViewCscaFinanceDetails_Click(object sender, RoutedEventArgs e)
        => _ = ShowSelectedCscaFinanceDetailsAsync();

    private void CscaFinanceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        => _ = ShowSelectedCscaFinanceDetailsAsync();

    private void CscaFinanceDataGrid_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        _ = ShowSelectedCscaFinanceDetailsAsync();
    }

    private async Task ShowSelectedCscaFinanceDetailsAsync()
    {
        if (CscaFinanceDataGrid.SelectedItem is not ApiClient.CscaClassItem selectedClass)
        {
            ShowToast("Hãy chọn một lớp để xem chi tiết tài chính học viên.");
            return;
        }

        var detailWindow = new CscaClassFinanceDetailWindow(_apiClient, selectedClass.Id)
        {
            Owner = this
        };
        detailWindow.ShowDialog();
        await LoadCscaClassesAsync(CscaSearchBox.Text);
        await LoadCscaFinanceAsync();
    }

    private async Task ShowSelectedCscaClassDetailsAsync()
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem selectedClass)
        {
            ShowToast("Hãy chọn một lớp CSCA để xem chi tiết.");
            return;
        }

        var detailWindow = new CscaClassDetailWindow(_apiClient, selectedClass.Id)
        {
            Owner = this
        };
        detailWindow.ShowDialog();
        await LoadCscaClassesAsync(CscaSearchBox.Text);
    }

    private async void CreateCscaClassDialog_Click(object sender, RoutedEventArgs e)
    {
        var courses = await _apiClient.GetCoursesAsync();
        if (courses is null)
        {
            ShowToast("Không tải được danh sách khóa học. Hãy thử lại trước khi tạo lớp.", true);
            return;
        }

        if (courses.Items.Count == 0)
        {
            MessageBox.Show(this, "Hãy tạo khóa học trước, sau đó mới thêm lớp học vào khóa học đó.", "Chưa có khóa học", MessageBoxButton.OK, MessageBoxImage.Information);
            NavCourses.IsChecked = true;
            return;
        }

        var defaultCourseId = _selectedCourseFilterId?.ToString() ?? courses.Items[0].Id.ToString();
        var allClasses = await _apiClient.GetAllCscaClassesAsync() ?? Array.Empty<ApiClient.CscaClassItem>();
        var liveSourceOptions = new[] { new PromptOption(string.Empty, "-- Chọn lớp Live nguồn nếu tạo lớp Record --") }
            .Concat(allClasses
                .Where(item => string.Equals(item.ClassType, "LIVE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.CourseTitle).ThenBy(item => item.Code)
                .Select(item => new PromptOption(item.Id.ToString(), $"{item.Code} — {item.Name} [{item.CourseTitle}]")))
            .ToArray();

        var nextCode = await _apiClient.GetNextCscaClassCodeAsync();
        if (string.IsNullOrWhiteSpace(nextCode))
        {
            nextCode = ComputeFallbackNextClassCode();
        }

        if (!PromptDialog.TryShow(this, "Tạo lớp học mới", new[]
        {
            new PromptField("courseId", "Khóa học", InitialValue: defaultCourseId, Options: courses.Items
                .Select(course => new PromptOption(course.Id.ToString(), course.Title))
                .ToArray()),
            new PromptField("billingType", "Hình thức học phí", InitialValue: "paid", Options: new[]
            {
                new PromptOption("paid", "Lớp chính thức (có học phí)"),
                new PromptOption("trial", "Lớp học thử (miễn phí 0 đ)")
            }),
            new PromptField("learningMode", "Hình thức học", InitialValue: "LIVE", Options: new[]
            {
                new PromptOption("LIVE", "Live — học trực tiếp theo lịch"),
                new PromptOption("RECORDING", "Record — xem bản ghi từ lớp Live")
            }),
            new PromptField("sourceClassId", "Lớp Live nguồn (bắt buộc với Record)", Options: liveSourceOptions),
            new PromptField("code", "Mã lớp (tự động tăng, có thể sửa)", InitialValue: nextCode),
            new PromptField("name", "Tên lớp học"),
            new PromptField("batch", "Đợt / Khóa tuyển sinh"),
            new PromptField("tuitionFee", "Học phí (VND) - tự động về 0đ nếu là lớp học thử", InitialValue: "0")
        }, out var values)) return;

        var isTrial = values.TryGetValue("billingType", out var billingType) && billingType == "trial";
        var learningMode = values.TryGetValue("learningMode", out var selectedMode) ? selectedMode : "LIVE";
        decimal tuitionFee = 0;
        if (isTrial)
        {
            tuitionFee = 0;
        }
        else if (!decimal.TryParse(values["tuitionFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedFee) || parsedFee < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        else
        {
            tuitionFee = parsedFee;
        }

        var code = string.IsNullOrWhiteSpace(values["code"]) ? nextCode : values["code"].Trim();
        var name = values["name"];
        if (!Guid.TryParse(values["courseId"], out var courseId))
        {
            ShowToast("Hãy chọn khóa học cho lớp.", true);
            return;
        }
        Guid? sourceClassId = Guid.TryParse(values.GetValueOrDefault("sourceClassId"), out var parsedSourceId)
            ? parsedSourceId : null;
        if (learningMode == "RECORDING")
        {
            if (isTrial || tuitionFee <= 0)
            {
                ShowToast("Lớp Record phải là lớp có học phí lớn hơn 0.", true);
                return;
            }
            var sourceClass = allClasses.FirstOrDefault(item => item.Id == sourceClassId);
            if (sourceClass is null || sourceClass.CourseId != courseId ||
                !string.Equals(sourceClass.ClassType, "LIVE", StringComparison.OrdinalIgnoreCase))
            {
                ShowToast("Hãy chọn lớp Live nguồn thuộc đúng khóa học đã chọn.", true);
                return;
            }
        }
        else
        {
            sourceClassId = null;
        }

        await RunWithBusyAsync("Đang tạo lớp học mới...", async () =>
        {
            await _apiClient.CreateCscaClassAsync(code, name, values["batch"], string.Empty, tuitionFee, courseId,
                isTrial: isTrial, classType: learningMode, sourceClassId: sourceClassId);
            await LoadCscaClassesAsync();
            await LoadCoursesAsync();
            var nextStep = learningMode == "RECORDING"
                ? "Lớp Record sẽ kế thừa buổi học, học liệu và bản ghi từ lớp Live nguồn; không cần tạo lịch riêng."
                : "Bước tiếp theo: chọn lớp và mở Chi tiết lớp → Lịch học để lập thời khóa biểu.";
            MessageBox.Show($"Tạo thành công lớp {learningMode}: '{code}' - '{name}'!\n\n{nextStep}", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private string ComputeFallbackNextClassCode()
    {
        var year = DateTime.UtcNow.Year;
        var prefix = $"CSCA-{year}-";
        var maxSeq = 0;

        if (CscaClassesDataGrid.ItemsSource is IEnumerable<ApiClient.CscaClassItem> items)
        {
            foreach (var item in items)
            {
                var raw = item.Code?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(raw)) continue;

                if (raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = raw.Substring(prefix.Length).Trim();
                    var digits = new string(suffix.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var seq) && seq > maxSeq)
                        maxSeq = seq;
                }
                else if (raw.StartsWith("CSCA-", StringComparison.OrdinalIgnoreCase))
                {
                    var suffix = raw.Substring("CSCA-".Length).Trim();
                    var digits = new string(suffix.Where(char.IsDigit).ToArray());
                    if (int.TryParse(digits, out var seq) && seq > maxSeq)
                        maxSeq = seq;
                }
                else if (raw.All(char.IsDigit) && int.TryParse(raw, out var numSeq))
                {
                    if (numSeq > maxSeq)
                        maxSeq = numSeq;
                }
            }
        }

        var nextSeq = Math.Max(maxSeq + 1, 1);
        return $"{prefix}{nextSeq:D2}";
    }

    private async void EditCscaClass_Click(object sender, RoutedEventArgs e)
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem cls)
        {
            ShowToast("Hãy chọn một lớp học để sửa.");
            return;
        }

        var allClasses = await _apiClient.GetAllCscaClassesAsync() ?? Array.Empty<ApiClient.CscaClassItem>();
        var sourceOptions = new[] { new PromptOption(string.Empty, "-- Không chọn --") }
            .Concat(allClasses
                .Where(item => item.Id != cls.Id && item.CourseId == cls.CourseId &&
                    string.Equals(item.ClassType, "LIVE", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(item.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.Code)
                .Select(item => new PromptOption(item.Id.ToString(), $"{item.Code} — {item.Name}")))
            .ToArray();

        if (!PromptDialog.TryShow(this, $"Sửa lớp {cls.Code}", new[]
        {
            new PromptField("name", "Tên lớp", cls.Name),
            new PromptField("batch", "Đợt / khóa", cls.Batch),
            new PromptField("billingType", "Hình thức học phí", cls.IsTrial || cls.TuitionFee == 0 ? "trial" : "paid", Options: new[]
            {
                new PromptOption("paid", "Lớp chính thức (có học phí)"),
                new PromptOption("trial", "Lớp học thử (miễn phí 0 đ)")
            }),
            new PromptField("learningMode", "Hình thức học", cls.ClassType, Options: new[]
            {
                new PromptOption("LIVE", "Live — học trực tiếp theo lịch"),
                new PromptOption("RECORDING", "Record — xem bản ghi từ lớp Live")
            }),
            new PromptField("sourceClassId", "Lớp Live nguồn (bắt buộc với Record)", cls.SourceClassId?.ToString() ?? string.Empty, Options: sourceOptions),
            new PromptField("tuitionFee", "Học phí", cls.TuitionFee.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("status", "Trạng thái", cls.Status, Options: new[]
            {
                new PromptOption("Active", "Active (Đang hoạt động)"),
                new PromptOption("Completed", "Completed (Đã kết thúc)"),
                new PromptOption("Cancelled", "Cancelled (Đã hủy)")
            })
        }, out var values)) return;

        var isTrial = values.TryGetValue("billingType", out var billingType) && billingType == "trial";
        var learningMode = values.TryGetValue("learningMode", out var selectedMode) ? selectedMode : cls.ClassType;
        decimal tuitionFee = 0;
        if (isTrial)
        {
            tuitionFee = 0;
        }
        else if (!decimal.TryParse(values["tuitionFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var parsedFee) || parsedFee < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        else
        {
            tuitionFee = parsedFee;
        }
        Guid? sourceClassId = Guid.TryParse(values.GetValueOrDefault("sourceClassId"), out var parsedSourceId)
            ? parsedSourceId : null;
        if (learningMode == "RECORDING" && (isTrial || tuitionFee <= 0 || sourceClassId is null))
        {
            ShowToast("Lớp Record phải có học phí lớn hơn 0 và chọn lớp Live nguồn.", true);
            return;
        }
        if (learningMode == "LIVE") sourceClassId = null;

        await RunWithBusyAsync("Đang cập nhật lớp học...", async () =>
        {
            await _apiClient.UpdateCscaClassAsync(cls.Id, values["name"], values["batch"], cls.Schedule, tuitionFee, cls.StartDate, cls.EndDate, values["status"],
                isTrial: isTrial, classType: learningMode, sourceClassId: sourceClassId);
            await LoadCscaClassesAsync();
            MessageBox.Show($"Cập nhật thông tin lớp {cls.Code} thành công!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void DeleteCscaClass_Click(object sender, RoutedEventArgs e)
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem cls)
        {
            ShowToast("Hãy chọn một lớp học cần xóa.");
            return;
        }

        var confirmation = MessageBox.Show(
            $"Bạn có chắc chắn muốn xóa lớp học '{cls.Code} — {cls.Name}'?",
            "Xác nhận xóa lớp học",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang xóa lớp học...", async () =>
        {
            await _apiClient.DeleteCscaClassAsync(cls.Id);
            await LoadCscaClassesAsync();
            await LoadCoursesAsync();
            MessageBox.Show($"Đã xóa thành công lớp học: '{cls.Code}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async void SyncInterviewFromWebsite_Click(object sender, RoutedEventArgs e)
    {
        var result = await _apiClient.TriggerSyncAsync("WEBSITE_INTERVIEW", "InterviewCustomers");
        if (result is null)
        {
            MessageBox.Show(
                "Đồng bộ CSCA Interview thất bại. Hãy kiểm tra Integrations__CscaInterview__IntegrationKey hoặc BearerToken và CustomersPath trên API.",
                "Lỗi Đồng Bộ CSCA Interview",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var image = result.RecordsFailed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information;
        MessageBox.Show(
            $"Đồng bộ CSCA Interview hoàn tất!\nĐã đọc: {result.RecordsRead}\nĐã ghi: {result.RecordsWritten}\nBỏ qua: {result.RecordsSkipped}\nLỗi: {result.RecordsFailed}",
            "Kết Quả Đồng Bộ CSCA Interview",
            MessageBoxButton.OK,
            image);

        await LoadInterviewCustomersAsync(ViewInterviewContainer.SearchText);
    }

    private async void CreateInterviewCustomerDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo hồ sơ khách hàng Interview", new[]
        {
            new PromptField("name", "Họ và tên"),
            new PromptField("email", "Email"),
            new PromptField("phone", "Số điện thoại"),
            new PromptField("packageName", "Tên gói dịch vụ"),
            new PromptField("sessions", "Số buổi", InitialValue: "1"),
            new PromptField("paidAmount", "Số tiền đã thanh toán", InitialValue: "0")
        }, out var values)) return;

        if (!int.TryParse(values["sessions"], out var sessions) || sessions <= 0 ||
            !decimal.TryParse(values["paidAmount"], out var paidAmount) || paidAmount < 0)
        {
            MessageBox.Show("Số buổi phải lớn hơn 0; số tiền thanh toán phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = values["name"];
        var success = await _apiClient.CreateInterviewCustomerAsync(name, values["email"], values["phone"], values["packageName"], sessions, paidAmount);
        if (success)
        {
            MessageBox.Show($"Tạo thành công hồ sơ phỏng vấn: '{name}'!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadInterviewCustomersAsync();
        }
        else
        {
            MessageBox.Show("Tạo hồ sơ Interview thất bại. Bạn cần có quyền InterviewCustomers.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

}
