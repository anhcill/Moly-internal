using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class PayrollWorkEntriesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly ApiClient.PayrollPeriodItem _period;
    private readonly string _businessSegment;
    private readonly bool _canEdit;

    private sealed record EmployeeOption(Guid Id, string DisplayName);
    private sealed record WorkTypeOption(string Code, string Name);

    public PayrollWorkEntriesWindow(
        ApiClient apiClient, ApiClient.PayrollPeriodItem period, string businessSegment)
    {
        _apiClient = apiClient;
        _period = period;
        _businessSegment = businessSegment;
        _canEdit = period.Status < 2;
        InitializeComponent();

        PeriodText.Text = $"{period.Name} · {period.StartDate:dd/MM/yyyy}–{period.EndDate:dd/MM/yyyy}";
        WorkDatePicker.SelectedDate = DateTime.Today <= period.EndDate.ToDateTime(TimeOnly.MinValue)
            ? DateTime.Today : period.EndDate.ToDateTime(TimeOnly.MinValue);
        WorkTypeCombo.ItemsSource = new[]
        {
            new WorkTypeOption("QUESTION_POSTED", "Đề đã đăng"),
            new WorkTypeOption("QUESTION_COMPLETED", "Đề đã hoàn thành"),
            new WorkTypeOption("PROJECT", "Dự án / hạng mục"),
            new WorkTypeOption("SALES_COMMISSION", "Hoa hồng sale"),
            new WorkTypeOption("STUDENT_REFERRAL", "Học viên giới thiệu / tuyển được"),
            new WorkTypeOption("MARKETING_REFERRAL", "Marketing — người giới thiệu"),
            new WorkTypeOption("MARKETING", "Marketing — đầu việc khác"),
            new WorkTypeOption("OTHER", "Đầu việc khác")
        };
        WorkTypeCombo.SelectedIndex = 0;
        AddButton.IsEnabled = _canEdit;
        VoidButton.IsEnabled = _canEdit;

        Loaded += async (_, _) =>
        {
            await LoadEmployeesAsync();
            await RefreshEntriesAsync();
        };
    }

    public bool Changed { get; private set; }

    private async Task LoadEmployeesAsync()
    {
        var data = await _apiClient.GetEmployeesAsync(
            search: EmployeeSearchBox.Text.Trim(),
            status: "Active",
            businessSegment: _businessSegment,
            pageSize: 50);
        if (data is null)
        {
            ShowError("Không tải được danh sách nhân sự. Hãy kiểm tra kết nối và quyền xem nhân sự.");
            return;
        }

        var selectedId = EmployeeCombo.SelectedValue as Guid?;
        EmployeeCombo.ItemsSource = data.Items
            .Select(e => new EmployeeOption(e.Id, $"{e.EmployeeCode} — {e.FullName}"))
            .ToList();
        if (selectedId.HasValue)
            EmployeeCombo.SelectedValue = selectedId.Value;
        if (EmployeeCombo.SelectedIndex < 0 && data.Items.Count > 0)
            EmployeeCombo.SelectedIndex = 0;
    }

    private async Task RefreshEntriesAsync()
    {
        var entries = await _apiClient.GetPayrollWorkEntriesAsync(_period.Id);
        if (entries is null)
        {
            ShowError("Không tải được danh sách đầu việc của kỳ lương.");
            return;
        }

        EntriesGrid.ItemsSource = entries;
        var activeEntries = entries.Where(entry => !entry.IsVoided).ToList();
        SummaryText.Text = $"{activeEntries.Count:N0} đầu việc đang tính · Tổng tiền công {activeEntries.Sum(entry => entry.Amount):N0} đ";
        var postedQuestionCount = activeEntries.Where(entry => entry.WorkType == "QUESTION_POSTED")
            .Sum(entry => entry.Quantity);
        var completedQuestionCount = activeEntries.Where(entry => entry.WorkType == "QUESTION_COMPLETED")
            .Sum(entry => entry.Quantity);
        var projectCount = activeEntries.Where(entry => entry.WorkType == "PROJECT").Sum(entry => entry.Quantity);
        var referralCount = activeEntries.Where(entry => entry.WorkType is "STUDENT_REFERRAL" or "MARKETING_REFERRAL")
            .Sum(entry => entry.Quantity);
        SummaryText.Text += $" · Đề đăng: {postedQuestionCount:0.##} · Đề hoàn thành: {completedQuestionCount:0.##}" +
            $" · Dự án: {projectCount:0.##} · Người giới thiệu: {referralCount:0}";
    }

    private void WorkType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkTypeCombo.SelectedValue is not string workType) return;
        var (unit, reference, hint) = workType switch
        {
            "QUESTION_POSTED" or "QUESTION_COMPLETED" =>
                ("số đề", "Mã đề", "Nhập số đề đã làm và đơn giá mỗi đề. Dùng mã đề hoặc mã lô duy nhất để tránh trả trùng."),
            "PROJECT" =>
                ("số dự án / hạng mục", "Mã dự án / hạng mục", "Nhập số dự án hoặc hạng mục đã hoàn thành và đơn giá tương ứng."),
            "STUDENT_REFERRAL" or "MARKETING_REFERRAL" =>
                ("số người", "Mã chiến dịch / đợt giới thiệu", "Nhập số người giới thiệu thành công, đơn giá mỗi người và mã đợt duy nhất. Có thể gắn chứng từ đối soát."),
            "SALES_COMMISSION" =>
                ("số đơn", "Mã đơn hàng", "Nhập số đơn được hưởng hoa hồng và đơn giá hoa hồng mỗi đơn."),
            _ => ("số lượng", "Mã đầu việc", "Nhập số lượng hoàn thành và đơn giá của đầu việc này.")
        };
        ReferenceLabel.Text = $"{reference} *";
        QuantityLabel.Text = $"{unit} *";
        UnitRateLabel.Text = $"Đơn giá (đồng/{unit}) *";
        EntryHintText.Text = hint;
    }

    private async void SearchEmployees_Click(object sender, RoutedEventArgs e) =>
        await LoadEmployeesAsync();

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit || EmployeeCombo.SelectedValue is not Guid employeeId ||
            WorkTypeCombo.SelectedValue is not string workType ||
            WorkDatePicker.SelectedDate is not DateTime workDate ||
            string.IsNullOrWhiteSpace(ReferenceCodeBox.Text) || string.IsNullOrWhiteSpace(TitleBox.Text) ||
            !TryAmount(QuantityBox.Text, out var quantity) || quantity <= 0 ||
            !TryAmount(UnitRateBox.Text, out var unitRate) || unitRate <= 0)
        {
            ShowError("Hãy chọn nhân sự, loại đầu việc, mã tham chiếu, ngày và nhập số lượng/đơn giá dương.");
            return;
        }

        if (workType is ("STUDENT_REFERRAL" or "MARKETING_REFERRAL") && quantity != decimal.Truncate(quantity))
        {
            ShowError("Số người giới thiệu phải là số nguyên dương.");
            return;
        }

        AddButton.IsEnabled = false;
        try
        {
            var model = new ApiClient.CreatePayrollWorkEntryModel(
                employeeId, workType, ReferenceCodeBox.Text.Trim(), TitleBox.Text.Trim(),
                DateOnly.FromDateTime(workDate), quantity, unitRate,
                string.IsNullOrWhiteSpace(EvidenceBox.Text) ? null : EvidenceBox.Text.Trim());
            var created = await _apiClient.AddPayrollWorkEntryAsync(_period.Id, model);
            if (created is null)
            {
                ShowError(_apiClient.LastManagementOperationError ?? "Không ghi được đầu việc: phản hồi máy chủ thiếu dữ liệu.");
                return;
            }

            Changed = true;
            ReferenceCodeBox.Clear();
            TitleBox.Clear();
            EvidenceBox.Clear();
            await RefreshEntriesAsync();
        }
        finally
        {
            AddButton.IsEnabled = _canEdit;
        }
    }

    private async void Void_Click(object sender, RoutedEventArgs e)
    {
        if (!_canEdit || EntriesGrid.SelectedItem is not ApiClient.PayrollWorkEntryItem entry || entry.IsVoided)
        {
            ShowError("Hãy chọn một đầu việc đang tính để hủy.");
            return;
        }

        if (MessageBox.Show(this,
                $"Hủy tiền công cho {entry.ReferenceCode} — {entry.Title} ({entry.Amount:N0} đ)?\nPhiếu lương sẽ được cập nhật ngay.",
                "Xác nhận hủy đầu việc", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        VoidButton.IsEnabled = false;
        try
        {
            if (!await _apiClient.VoidPayrollWorkEntryAsync(entry.Id))
            {
                ShowError(_apiClient.LastManagementOperationError ?? "Không hủy được đầu việc: phản hồi máy chủ không rõ nguyên nhân.");
                return;
            }
            Changed = true;
            await RefreshEntriesAsync();
        }
        finally
        {
            VoidButton.IsEnabled = _canEdit;
        }
    }

    private static bool TryAmount(string value, out decimal amount) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out amount) ||
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "Đầu việc và tiền công", MessageBoxButton.OK, MessageBoxImage.Warning);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
