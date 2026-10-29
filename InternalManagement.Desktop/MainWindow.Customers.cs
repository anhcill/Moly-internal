using System.Windows;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.ViewModels;

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

    private async void ConvertTrialCustomerStudent_Click(object? sender, RoutedEventArgs e)
    {
        var trialGroup = ViewCustomersContainer.SelectedTrialStudentGroup;
        if (trialGroup is null)
        {
            ShowToast("Hãy chọn một học viên học thử để chuyển sang lớp chính thức.");
            return;
        }

        var classesData = await _apiClient.GetCscaClassesAsync();
        var allPaidClasses = (classesData?.Items ?? Array.Empty<ApiClient.CscaClassItem>())
            .Where(c => !c.IsTrial && c.TuitionFee > 0)
            .ToList();

        if (allPaidClasses.Count == 0)
        {
            MessageBox.Show(
                this,
                "Hiện chưa có lớp chính thức (có học phí) nào trong hệ thống. Vui lòng tạo lớp chính thức trong phần 'Quản lý đào tạo' trước.",
                "Chưa có lớp chính thức",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var primary = trialGroup.PrimaryEnrollment;
        var prefill = new ApiClient.CscaStudentItem(
            Guid.Empty,
            allPaidClasses[0].Id,
            trialGroup.StudentName,
            trialGroup.Age,
            trialGroup.Hometown,
            trialGroup.Email,
            trialGroup.PhoneNumber,
            PaidAmount: 0,
            PaymentStatus: 0,
            JoinedAt: DateTime.UtcNow,
            Notes: $"Chuyển từ lớp học thử ({trialGroup.ClassesSummary})",
            DebtDueDate: null,
            DiscountAmount: 0,
            DiscountNote: null,
            PayableAmount: allPaidClasses[0].TuitionFee,
            StudentCode: trialGroup.StudentCode);

        if (!CscaStudentDialog.TryShowForConversion(
            this,
            allPaidClasses[0].Code,
            allPaidClasses[0].Name,
            allPaidClasses[0].TuitionFee,
            out var res,
            out var selectedClassId,
            prefillStudent: prefill,
            apiClient: _apiClient,
            availableClasses: allPaidClasses,
            defaultClassId: allPaidClasses[0].Id) || res is null)
        {
            return;
        }

        await RunWithBusyAsync($"Đang chuyển học viên '{res.StudentName}' sang lớp chính thức...", async () =>
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
                var targetClass = allPaidClasses.FirstOrDefault(c => c.Id == selectedClassId);
                ShowToast($"Đã chuyển thành công học viên '{res.StudentName}' sang lớp chính thức '{targetClass?.Code ?? ""}'.");
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
            student.PayableAmount,
            student.StudentCode);

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
        var group = ViewCustomersContainer.SelectedPaidStudentGroup ?? ViewCustomersContainer.SelectedTrialStudentGroup;
        var student = ViewCustomersContainer.SelectedStudent;
        if (student is null)
        {
            ShowToast("Hãy chọn một học viên cần xóa.");
            return;
        }

        var message = group != null && group.ClassCount > 1
            ? $"Học viên '{student.StudentName}' hiện đang tham gia {group.ClassCount} lớp ({group.ClassesSummary}).\nBạn có chắc chắn muốn xóa học viên khỏi lớp '{student.ClassCode}'?"
            : $"Bạn có chắc chắn muốn xóa học viên '{student.StudentName}' khỏi lớp '{student.ClassCode}' ({student.CourseTitle})?";

        var confirm = MessageBox.Show(
            this,
            message,
            "Xác nhận xóa học viên",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        await RunWithBusyAsync("Đang xóa học viên khỏi lớp...", async () =>
        {
            var success = await _apiClient.RemoveCscaStudentAsync(student.ClassId, student.Id);
            if (success)
            {
                ShowToast($"Đã xóa học viên '{student.StudentName}' khỏi lớp '{student.ClassCode}'.");
                await LoadCustomersAsync(ViewCustomersContainer.SearchText);
            }
        });
    }
}
