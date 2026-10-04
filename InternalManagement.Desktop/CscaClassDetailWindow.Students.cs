using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassDetailWindow
{
    private async void AddStudent_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;

        if (!CscaStudentDialog.TryShow(
            this,
            _detail.Code,
            _detail.Name,
            _detail.TuitionFee,
            out var res,
            apiClient: _apiClient) || res is null) return;

        await SaveAsync(
            () => _apiClient.EnrollCscaStudentAsync(
                _classId, res.StudentName, res.Email, res.PhoneNumber, res.Age,
                res.Hometown, res.PaidAmount, res.PaymentStatus, res.Notes, res.DebtDueDate,
                res.DiscountAmount, res.DiscountNote),
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
            student.DebtDueDate,
            student.DiscountAmount,
            student.DiscountNote,
            student.PayableAmount);

        if (!CscaStudentDialog.TryShow(
            this,
            _detail.Code,
            _detail.Name,
            _detail.TuitionFee,
            out var res,
            student: studentItem,
            apiClient: _apiClient) || res is null) return;

        await SaveAsync(
            () => _apiClient.UpdateCscaStudentAsync(
                _classId, student.Id, res.StudentName, res.Email, res.PhoneNumber, res.Age,
                res.Hometown, res.PaidAmount, res.PaymentStatus, res.Notes, res.DebtDueDate,
                res.DiscountAmount, res.DiscountNote),
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

}
