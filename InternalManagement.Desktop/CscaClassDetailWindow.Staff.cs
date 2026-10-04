using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
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

}
