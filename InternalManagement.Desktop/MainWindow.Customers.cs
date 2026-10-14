using System.Windows;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private async void CreateCustomerStudent_Click(object? sender, RoutedEventArgs e)
    {
        var classesData = await _apiClient.GetCscaClassesAsync();
        var classes = classesData?.Items ?? Array.Empty<ApiClient.CscaClassItem>();
        if (classes.Count == 0)
        {
            MessageBox.Show(
                this,
                "Chưa có lớp học nào trong hệ thống. Vui lòng tạo lớp học ở mục 'Quản lý đào tạo' trước khi thêm học viên.",
                "Chưa có lớp học",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var firstClass = classes[0];
        if (!CscaStudentDialog.TryShow(
            this,
            firstClass.Code,
            firstClass.Name,
            firstClass.TuitionFee,
            out var res,
            out var selectedClassId,
            student: null,
            apiClient: _apiClient,
            availableClasses: classes,
            defaultClassId: firstClass.Id) || res is null)
        {
            return;
        }

        await RunWithBusyAsync("Đang thêm học viên vào lớp...", async () =>
        {
            var success = await _apiClient.EnrollCscaStudentAsync(
                selectedClassId,
                res.StudentName,
                res.Email,
                res.PhoneNumber,
                res.Age,
                res.Hometown,
                res.PaidAmount,
                res.PaymentStatus,
                res.Notes,
                res.DebtDueDate,
                res.DiscountAmount,
                res.DiscountNote);

            if (success)
            {
                ShowToast($"Đã thêm thành công học viên '{res.StudentName}' vào lớp.");
                await LoadCustomersAsync(ViewCustomersContainer.SearchText);
            }
        });
    }

    private async void EditCustomerStudent_Click(object? sender, RoutedEventArgs e)
    {
        var student = ViewCustomersContainer.SelectedStudent;
        if (student is null)
        {
            ShowToast("Hãy chọn một học viên cần sửa.");
            return;
        }

        var studentItem = new ApiClient.CscaStudentItem(
            student.Id,
            student.ClassId,
            student.StudentName,
            student.Age,
            student.Hometown,
            student.Email,
            student.PhoneNumber,
            student.PaidAmount,
            student.PaymentStatus,
            student.JoinedAt,
            student.Notes,
            student.DebtDueDate,
            student.DiscountAmount,
            student.DiscountNote,
            student.PayableAmount);

        if (!CscaStudentDialog.TryShow(
            this,
            student.ClassCode,
            student.ClassName,
            student.TuitionFee,
            out var res,
            student: studentItem,
            apiClient: _apiClient) || res is null)
        {
            return;
        }

        await RunWithBusyAsync("Đang cập nhật thông tin học viên...", async () =>
        {
            var success = await _apiClient.UpdateCscaStudentAsync(
                student.ClassId,
                student.Id,
                res.StudentName,
                res.Email,
                res.PhoneNumber,
                res.Age,
                res.Hometown,
                res.PaidAmount,
                res.PaymentStatus,
                res.Notes,
                res.DebtDueDate,
                res.DiscountAmount,
                res.DiscountNote);

            if (success)
            {
                ShowToast($"Đã cập nhật thông tin học viên '{res.StudentName}'.");
                await LoadCustomersAsync(ViewCustomersContainer.SearchText);
            }
        });
    }

    private async void DeleteCustomerStudent_Click(object? sender, RoutedEventArgs e)
    {
        var student = ViewCustomersContainer.SelectedStudent;
        if (student is null)
        {
            ShowToast("Hãy chọn một học viên cần xóa.");
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Bạn có chắc chắn muốn xóa học viên '{student.StudentName}' khỏi lớp '{student.ClassCode}' ({student.CourseTitle})?",
            "Xác nhận xóa học viên",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang xóa học viên khỏi lớp...", async () =>
        {
            var success = await _apiClient.RemoveCscaStudentAsync(student.ClassId, student.Id);
            if (success)
            {
                ShowToast($"Đã xóa học viên '{student.StudentName}' khỏi lớp.");
                await LoadCustomersAsync(ViewCustomersContainer.SearchText);
            }
        });
    }
}
