using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;
using Microsoft.Win32;

namespace InternalManagement.Desktop;

public partial class CscaClassFinanceDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _classId;
    private ApiClient.CscaClassDetailItem? _detail;
    private List<StudentFinanceItem> _allStudents = new();

    public CscaClassFinanceDetailWindow(ApiClient apiClient, Guid classId)
    {
        InitializeComponent();
        Services.ThemeService.SetWindowDarkMode(this, Services.ThemeService.Instance.IsDark);
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _classId = classId;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    public async Task LoadDataAsync()
    {
        SetBusy(true, "Đang tải chi tiết tài chính học viên...");
        try
        {
            var detail = await _apiClient.GetCscaClassDetailAsync(_classId);
            if (detail is null)
            {
                MessageBox.Show(this, "Không thể tải thông tin lớp học. Vui lòng kiểm tra lại quyền truy cập hoặc kết nối mạng.", "Lỗi tải dữ liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
                Close();
                return;
            }

            _detail = detail;
            Title = $"Chi tiết tài chính — {detail.Code} ({detail.Name})";
            ClassCodeText.Text = detail.Code;
            ClassNameText.Text = detail.Name;

            var isTrial = detail.IsTrial || detail.TuitionFee == 0;
            if (isTrial)
            {
                ClassTypeBadge.Background = (Brush)new BrushConverter().ConvertFromString("#FEF3C7")!;
                ClassTypeBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#FDE68A")!;
                ClassTypeText.Foreground = (Brush)new BrushConverter().ConvertFromString("#B45309")!;
                ClassTypeText.Text = "LỚP HỌC THỬ (0 đ)";
            }
            else
            {
                ClassTypeBadge.Background = (Brush)new BrushConverter().ConvertFromString("#ECFDF5")!;
                ClassTypeBadge.BorderBrush = (Brush)new BrushConverter().ConvertFromString("#A7F3D0")!;
                ClassTypeText.Foreground = (Brush)new BrushConverter().ConvertFromString("#065F46")!;
                ClassTypeText.Text = "LỚP CHÍNH THỨC";
            }

            ClassSubtitleText.Text = $"Khóa học: {detail.CourseTitle}  •  Đợt: {detail.Batch}  •  Học phí niêm yết: {detail.TuitionFee:N0} đ/học viên";

            _allStudents = detail.Students
                .Select(s => StudentFinanceItem.From(s, detail.TuitionFee))
                .OrderBy(s => s.StudentName)
                .ToList();

            ApplyFilter();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể tải chi tiết lớp:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyFilter()
    {
        var query = _allStudents.AsEnumerable();

        var search = SearchBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s =>
                s.StudentName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(s.StudentCode) && s.StudentCode.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(s.PhoneNumber) && s.PhoneNumber.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(s.Email) && s.Email.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        if (PaymentStatusFilterCombo.SelectedIndex > 0)
        {
            switch (PaymentStatusFilterCombo.SelectedIndex)
            {
                case 1: // Đã đóng đủ
                    query = query.Where(s => s.PaymentStatusValue == 2 || (s.PayableAmount > 0 && s.DebtAmount == 0));
                    break;
                case 2: // Đóng một phần
                    query = query.Where(s => s.PaymentStatusValue == 1 || (s.PaidAmount > 0 && s.DebtAmount > 0));
                    break;
                case 3: // Còn nợ tiền
                    query = query.Where(s => s.DebtAmount > 0);
                    break;
                case 4: // Chưa đóng
                    query = query.Where(s => s.PaymentStatusValue == 0 || s.PaidAmount == 0);
                    break;
            }
        }

        var filtered = query.ToList();
        StudentsFinanceDataGrid.ItemsSource = filtered;

        // Cập nhật card thống kê tổng thể của toàn bộ lớp
        MetricStudentCount.Text = $"{_allStudents.Count} học viên";
        MetricPayable.Text = $"{_allStudents.Sum(s => s.PayableAmount):N0} đ";
        MetricPaid.Text = $"{_allStudents.Sum(s => s.PaidAmount):N0} đ";
        MetricDebt.Text = $"{_allStudents.Sum(s => s.DebtAmount):N0} đ";

        FooterStatusText.Text = $"Hiển thị: {filtered.Count}/{_allStudents.Count} học viên  •  Tổng thực thu hiển thị: {filtered.Sum(s => s.PaidAmount):N0} đ  •  Tổng nợ hiển thị: {filtered.Sum(s => s.DebtAmount):N0} đ";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private void PaymentStatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
            ApplyFilter();
    }

    private async void StudentsFinanceDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (StudentsFinanceDataGrid.SelectedItem is not StudentFinanceItem student || _detail is null)
            return;

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
            student.PayableAmount,
            student.StudentCode);

        if (!CscaStudentDialog.TryShow(
            this,
            _detail.Code,
            _detail.Name,
            _detail.TuitionFee,
            out var res,
            student: studentItem,
            apiClient: _apiClient,
            defaultClassId: _classId) || res is null) return;

        SetBusy(true, "Đang cập nhật số tiền đóng của học viên...");
        try
        {
            var ok = await _apiClient.UpdateCscaStudentAsync(
                _classId, student.Id, res.StudentName, res.Email, res.PhoneNumber, res.Age,
                res.Hometown, res.PaidAmount, res.PaymentStatus, res.Notes, res.DebtDueDate,
                res.DiscountAmount, res.DiscountNote);

            if (!ok)
            {
                MessageBox.Show(this, "Không thể lưu cập nhật học viên. Vui lòng thử lại.", "Cập nhật thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi khi cập nhật học viên:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
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
            out var res,
            apiClient: _apiClient,
            defaultClassId: _classId) || res is null) return;

        SetBusy(true, "Đang ghi danh học viên vào lớp...");
        try
        {
            var ok = await _apiClient.EnrollCscaStudentAsync(
                _classId, res.StudentName, res.Email, res.PhoneNumber, res.Age,
                res.Hometown, res.PaidAmount, res.PaymentStatus, res.Notes, res.DebtDueDate,
                res.DiscountAmount, res.DiscountNote);

            if (!ok)
            {
                MessageBox.Show(this, "Không thể thêm học viên vào lớp. Vui lòng thử lại.", "Thêm học viên thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Lỗi khi thêm học viên:\n{ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDataAsync();
    }

    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        var items = StudentsFinanceDataGrid.ItemsSource as List<StudentFinanceItem> ?? _allStudents;
        if (items.Count == 0)
        {
            MessageBox.Show(this, "Không có dữ liệu học viên để xuất file.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Xuất danh sách học viên đóng tiền",
            Filter = "Tệp CSV UTF-8 (*.csv)|*.csv",
            FileName = $"TaiChinh_{_detail?.Code ?? "CSCA"}_{DateTime.Today:yyyyMMdd}.csv"
        };

        if (saveDialog.ShowDialog(this) != true) return;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Mã HV,Họ và tên,Điện thoại,Email,Học phí gốc,Giảm giá,Lý do giảm giá,Cần thu,Đã đóng,Còn nợ,Ngày đóng/vào lớp,Trạng thái,Hẹn trả nợ,Ghi chú");
            foreach (var s in items)
            {
                sb.AppendLine(string.Join(",",
                    EscapeCsv(s.StudentCode ?? string.Empty),
                    EscapeCsv(s.StudentName),
                    EscapeCsv(s.PhoneNumber ?? string.Empty),
                    EscapeCsv(s.Email ?? string.Empty),
                    s.TuitionFee.ToString("F0", CultureInfo.InvariantCulture),
                    s.DiscountAmount.ToString("F0", CultureInfo.InvariantCulture),
                    EscapeCsv(s.DiscountNote ?? string.Empty),
                    s.PayableAmount.ToString("F0", CultureInfo.InvariantCulture),
                    s.PaidAmount.ToString("F0", CultureInfo.InvariantCulture),
                    s.DebtAmount.ToString("F0", CultureInfo.InvariantCulture),
                    EscapeCsv(s.JoinedAtFormatted),
                    EscapeCsv(s.PaymentStatusLabel),
                    EscapeCsv(s.DebtDueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty),
                    EscapeCsv(s.Notes ?? string.Empty)
                ));
            }

            File.WriteAllText(saveDialog.FileName, sb.ToString(), new UTF8Encoding(true));
            MessageBox.Show(this, $"Đã xuất thành công {items.Count} học viên ra file CSV.", "Xuất thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không thể xuất file:\n{ex.Message}", "Lỗi xuất file", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SetBusy(bool isBusy, string message = "")
    {
        BusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Text = string.IsNullOrWhiteSpace(message) ? "Đang xử lý..." : message;
    }

    public sealed class StudentFinanceItem
    {
        public Guid Id { get; init; }
        public string? StudentCode { get; init; }
        public string StudentName { get; init; } = string.Empty;
        public int? Age { get; init; }
        public string? Hometown { get; init; }
        public string? Email { get; init; }
        public string? PhoneNumber { get; init; }
        public decimal TuitionFee { get; init; }
        public decimal DiscountAmount { get; init; }
        public string? DiscountNote { get; init; }
        public decimal PayableAmount => Math.Max(0, TuitionFee - DiscountAmount);
        public decimal PaidAmount { get; init; }
        public decimal DebtAmount => Math.Max(0, PayableAmount - PaidAmount);
        public DateTime? DebtDueDate { get; init; }
        public int PaymentStatusValue { get; init; }
        public string PaymentStatusLabel => PaymentStatusText(PaymentStatusValue);
        public DateTime JoinedAt { get; init; }
        public string JoinedAtFormatted => JoinedAt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        public string? Notes { get; init; }

        public static string PaymentStatusText(int status) => status switch
        {
            0 => "Chưa đóng",
            1 => "Đóng một phần",
            2 => "Đã đóng đủ",
            3 => "Thất bại",
            4 => "Hoàn tiền",
            5 => "Đã hủy",
            _ => "Chưa rõ"
        };

        public static StudentFinanceItem From(ApiClient.CscaStudentItem student, decimal tuitionFee) => new()
        {
            Id = student.Id,
            StudentCode = student.StudentCode,
            StudentName = student.StudentName,
            Age = student.Age,
            Hometown = student.Hometown,
            Email = student.Email,
            PhoneNumber = student.PhoneNumber,
            TuitionFee = tuitionFee,
            DiscountAmount = student.DiscountAmount,
            DiscountNote = student.DiscountNote,
            PaidAmount = student.PaidAmount,
            DebtDueDate = student.DebtDueDate,
            PaymentStatusValue = student.PaymentStatus,
            JoinedAt = student.JoinedAt,
            Notes = student.Notes
        };
    }
}
