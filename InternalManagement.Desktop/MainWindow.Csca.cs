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

        if (!PromptDialog.TryShow(this, "Tạo lớp học mới", new[]
        {
            new PromptField("courseId", "Khóa học", InitialValue: defaultCourseId, Options: courses.Items
                .Select(course => new PromptOption(course.Id.ToString(), course.Title))
                .ToArray()),
            new PromptField("code", "Mã lớp (ví dụ: CSCA-2026-K03)"),
            new PromptField("name", "Tên lớp học"),
            new PromptField("batch", "Đợt / Khóa tuyển sinh"),
            new PromptField("tuitionFee", "Học phí (VND)", InitialValue: "0")
        }, out var values)) return;

        if (!decimal.TryParse(values["tuitionFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var tuitionFee) || tuitionFee < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var code = values["code"];
        var name = values["name"];
        if (!Guid.TryParse(values["courseId"], out var courseId))
        {
            ShowToast("Hãy chọn khóa học cho lớp.", true);
            return;
        }

        await RunWithBusyAsync("Đang tạo lớp học mới...", async () =>
        {
            var success = await _apiClient.CreateCscaClassAsync(code, name, values["batch"], string.Empty, tuitionFee, courseId);
            if (success)
            {
                MessageBox.Show($"Tạo thành công lớp học: '{code}' - '{name}'!\n\nBước tiếp theo: chọn lớp và mở Chi tiết lớp → Lịch học để lập thời khóa biểu.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCscaClassesAsync();
                await LoadCoursesAsync();
            }
            else
            {
                MessageBox.Show("Tạo lớp học thất bại. Bạn cần có quyền CscaClasses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        });
    }

    private async void EditCscaClass_Click(object sender, RoutedEventArgs e)
    {
        if (CscaClassesDataGrid.SelectedItem is not ApiClient.CscaClassItem cls)
        {
            ShowToast("Hãy chọn một lớp học để sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa lớp {cls.Code}", new[]
        {
            new PromptField("name", "Tên lớp", cls.Name),
            new PromptField("batch", "Đợt / khóa", cls.Batch),
            new PromptField("tuitionFee", "Học phí", cls.TuitionFee.ToString("0", CultureInfo.InvariantCulture)),
            new PromptField("status", "Trạng thái", cls.Status, Options: new[]
            {
                new PromptOption("Active", "Active (Đang hoạt động)"),
                new PromptOption("Completed", "Completed (Đã kết thúc)"),
                new PromptOption("Cancelled", "Cancelled (Đã hủy)")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["tuitionFee"], NumberStyles.Number, CultureInfo.InvariantCulture, out var tuitionFee) || tuitionFee < 0)
        {
            MessageBox.Show("Học phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await RunWithBusyAsync("Đang cập nhật lớp học...", async () =>
        {
            var success = await _apiClient.UpdateCscaClassAsync(cls.Id, values["name"], values["batch"], cls.Schedule, tuitionFee, cls.StartDate, cls.EndDate, values["status"]);
            if (success)
            {
                MessageBox.Show($"Cập nhật thông tin lớp {cls.Code} thành công!", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCscaClassesAsync();
            }
            else
            {
                MessageBox.Show("Cập nhật lớp thất bại. Bạn cần có quyền CscaClasses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
            var success = await _apiClient.DeleteCscaClassAsync(cls.Id);
            if (success)
            {
                MessageBox.Show($"Đã xóa thành công lớp học: '{cls.Code}'.", "Thành Công", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadCscaClassesAsync();
                await LoadCoursesAsync();
            }
            else
            {
                MessageBox.Show("Xóa lớp học thất bại. Bạn cần có quyền CscaClasses.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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
