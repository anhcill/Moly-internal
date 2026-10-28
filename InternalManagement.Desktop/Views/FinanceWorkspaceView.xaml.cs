using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class FinanceWorkspaceView : UserControl
{
    private ApiClient? _apiClient;
    private ApiClient.FinanceWorkspaceReportItem? _report;
    private bool _working;
    private readonly Dictionary<string, string> _pendingOperationKeys = new(StringComparer.Ordinal);
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private Window DialogOwner => Window.GetWindow(this);

    public FinanceWorkspaceView()
    {
        InitializeComponent();
        ForecastFromMonthPicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ForecastHorizonCombo.ItemsSource = new[] { 3, 6, 9, 12, 24 };
        ForecastHorizonCombo.SelectedItem = 6;
    }

    public void SetApiClient(ApiClient apiClient) => _apiClient = apiClient;
    public event RoutedEventHandler? RefreshRequested;
    public DateTime FromMonth => new(ForecastFromMonthPicker.SelectedDate!.Value.Year, ForecastFromMonthPicker.SelectedDate.Value.Month, 1);
    public void SetCanManage(bool canManage)
    {
        foreach (var button in Descendants(this).OfType<Button>().Where(x => Equals(x.Tag, "FinanceWrite"))) button.IsEnabled = canManage;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    public async Task LoadAsync(ApiClient apiClient, CancellationToken ct = default)
    {
        _apiClient = apiClient;
        if (!ForecastFromMonthPicker.SelectedDate.HasValue) throw new InvalidOperationException("Chọn tháng bắt đầu báo cáo.");
        var collection = Number(CollectionPercentBox.Text, "Tỷ lệ thu", 0, 100);
        var factor = Number(CostFactorBox.Text, "Hệ số chi phí", 0, 10);
        var opening = Number(OpeningCashBox.Text, "Tiền đầu kỳ", decimal.MinValue, decimal.MaxValue);
        var report = await apiClient.GetFinanceWorkspaceAsync(FromMonth, (int)(ForecastHorizonCombo.SelectedItem ?? 6), collection, factor, opening, ct);
        if (report is null) throw new InvalidOperationException("API không trả về báo cáo tài chính.");
        SetReport(report);
    }

    public void SetReport(ApiClient.FinanceWorkspaceReportItem report)
    {
        _report = report;
        FreshnessText.Text = $"Tính đến {(report.AsOf == default ? DateTime.Now : report.AsOf.AddHours(7)):dd/MM/yyyy HH:mm}";
        ReportWarningsText.Text = string.Join("\n", report.Warnings);
        CurrentCashMetric.Text = $"{report.CurrentCash:N0} đ";
        StudentDebtMetric.Text = $"{report.StudentDebt:N0} đ";
        TeacherPayableMetric.Text = $"{report.TeacherPayable:N0} đ";
        ForecastClosingMetric.Text = $"{report.ForecastClosingCash:N0} đ";
        MonthlyRowsGrid.ItemsSource = report.Months;
        ReceivablesGrid.ItemsSource = report.Receivables;
        TeacherCostsGrid.ItemsSource = report.TeacherCosts;
        PlanItemsGrid.ItemsSource = report.PlannedMovements;
        CashMovementsGrid.ItemsSource = report.CashMovements ?? [];
        DrawChart();
    }

    private static decimal Number(string? text, string label, decimal min, decimal max)
    {
        if ((!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value)) || value < min || value > max)
            throw new InvalidOperationException($"{label} phải là số từ {min:N0} đến {max:N0}.");
        return value;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, e);
    private void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) return;
        try
        {
            var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = $"finance-{DateTime.Today:yyyy-MM}.csv" };
            if (dialog.ShowDialog(DialogOwner) != true) return;
            var csv = new StringBuilder("Month,Kind,OpeningCash,Inflow,Outflow,ActualInflow,ActualOutflow,ForecastInflow,ForecastOutflow,StudentCollections,TeacherPayments,PlannedIncome,PlannedExpense,ClosingCash\r\n");
            foreach (var row in _report.Months)
                csv.AppendLine(string.Join(',', new object[] { row.Month.ToString("yyyy-MM", CultureInfo.InvariantCulture), row.Kind, row.OpeningCash, row.TotalInflow, row.TotalOutflow, row.ActualInflow, row.ActualOutflow, row.ForecastInflow, row.ForecastOutflow, row.StudentCollections, row.TeacherPayments, row.PlannedIncome, row.PlannedExpense, row.ClosingCash }.Select(Csv)));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private static string Csv(object value) => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    private void ShowError(string message) => MessageBox.Show(DialogOwner, message, "Tài chính vận hành", MessageBoxButton.OK, MessageBoxImage.Warning);

    // Keys survive a failed request. Repeating the same submitted values reuses the key.
    private async Task ExecuteAsync(string fingerprint, Func<string, Task<bool>> action)
    {
        if (_working || _apiClient is null) return;
        if (!_pendingOperationKeys.TryGetValue(fingerprint, out var key)) _pendingOperationKeys[fingerprint] = key = Guid.NewGuid().ToString("N");
        _working = true; WorkspaceTabs.IsEnabled = false;
        var committed = false;
        try
        {
            await action(key);
            committed = true;
            _pendingOperationKeys.Remove(fingerprint);
            await LoadAsync(_apiClient);
        }
        catch (Exception ex) { ShowError(committed ? $"Đã lưu giao dịch. Chưa tải lại được báo cáo: {ex.Message}\nBấm Tải báo cáo để cập nhật số liệu." : $"{ex.Message}\n\nNếu kết nối gián đoạn, nhập lại cùng nội dung để thử lại giao dịch an toàn."); }
        finally { _working = false; WorkspaceTabs.IsEnabled = true; }
    }

    private bool CashPrompt(string title, decimal initial, decimal maximum, out decimal amount, out DateTime date, out string notes, IReadOnlyList<PromptField>? extras = null, Action<IReadOnlyDictionary<string, string>>? readExtra = null, bool allowZero = false)
    {
        amount = 0; date = default; notes = "";
        var fields = new List<PromptField> { new("amount", "Số tiền", initial.ToString(CultureInfo.InvariantCulture)), new("date", "Ngày giao dịch", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date), new("notes", "Diễn giải", IsRequired: false) };
        if (extras is not null) fields.AddRange(extras);
        if (!PromptDialog.TryShow(DialogOwner, title, fields, out var values)) return false;
        try
        {
            amount = Number(values["amount"], "Số tiền", allowZero ? 0 : 1, maximum);
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)) throw new InvalidOperationException("Ngày giao dịch không hợp lệ.");
            if (date.Date > DateTime.Today) throw new InvalidOperationException("Giao dịch thực tế không dùng ngày tương lai. Hãy tạo kế hoạch thu chi cho khoản dự kiến.");
            notes = values["notes"];
            readExtra?.Invoke(values);
            return true;
        }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }
    private string Fingerprint(string type, Guid id, decimal amount, DateTime date, string notes) => FormattableString.Invariant($"{type}|{id}|{amount}|{date:yyyy-MM-dd}|{notes}");

    private async void ReceiveStudentDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) { ShowError("Chọn học viên cần ghi nhận thu."); return; }
        if (row.OutstandingAmount <= 0) { ShowError("Học viên không còn công nợ cần thu."); return; }
        if (CashPrompt($"Thu học phí · {row.StudentName}", row.OutstandingAmount, row.OutstandingAmount, out var amount, out var date, out var notes))
            await ExecuteAsync(Fingerprint("receipt", row.Id, amount, date, notes), key => _apiClient!.ReceiveStudentDebtAsync(row.Id, amount, BusinessUtc(date), notes, key));
    }
    private async void RefundStudent_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) { ShowError("Chọn học viên cần hoàn tiền."); return; }
        var available = row.PaidAmount - row.RefundedAmount;
        if (available <= 0) { ShowError("Học viên không có số tiền đã thu có thể hoàn."); return; }
        if (CashPrompt($"Hoàn tiền · {row.StudentName}", available, available, out var amount, out var date, out var notes))
            await ExecuteAsync(Fingerprint("refund", row.Id, amount, date, notes), key => _apiClient!.RefundStudentDebtAsync(row.Id, amount, BusinessUtc(date), notes, key));
    }

    private async void ScheduleStudentDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) { ShowError("Chọn học viên cần hẹn ngày thu."); return; }
        if (!PromptDialog.TryShow(DialogOwner, $"Hẹn ngày thu · {row.StudentName}", new[]
            { new PromptField("date", "Ngày hẹn thu (hết ngày)", (row.LocalDueDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date) }, out var values)) return;
        if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { ShowError("Ngày hẹn thu không hợp lệ."); return; }
        // A debt is due through the selected day in Vietnam, rather than at midnight.
        var dueAt = BusinessUtc(date.AddDays(1).AddTicks(-1));
        await ExecuteAsync($"receivable-due-{row.Id}-{date:yyyy-MM-dd}", _ => _apiClient!.UpdateStudentReceivableAsync(row.Id, dueAt));
    }

    private async void CancelStudentDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) { ShowError("Chọn công nợ cần hủy."); return; }
        if (row.PaidAmount - row.RefundedAmount != 0) { ShowError("Chỉ hủy công nợ khi tiền đã thu ròng bằng 0. Hãy hoàn trả toàn bộ khoản đã thu trước khi hủy."); return; }
        if (MessageBox.Show(DialogOwner, $"Hủy công nợ của {row.StudentName}?\n\nKhoản này sẽ ngừng theo dõi và không còn trong dự báo thu. Chỉ được hủy khi tiền đã thu ròng bằng 0; thao tác này không tự hoàn tiền.", "Hủy công nợ", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ExecuteAsync($"receivable-cancel-{row.Id}", _ => _apiClient!.UpdateStudentReceivableAsync(row.Id, row.DueDate, cancelled: true));
    }

    private ApiClient.FinanceTeacherCostItem? SelectedTeacher()
    {
        if (TeacherCostsGrid.SelectedItem is ApiClient.FinanceTeacherCostItem row && row.EmployeeId != Guid.Empty) return row;
        ShowError("Chọn giáo viên đã liên kết với hồ sơ nhân sự."); return null;
    }
    private async void IssueAdvance_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedTeacher(); if (row is null) return;
        if (CashPrompt($"Ứng tiền · {row.TeacherName}", 0, decimal.MaxValue, out var amount, out var date, out var notes))
            await ExecuteAsync(Fingerprint("advance", row.EmployeeId, amount, date, notes), key => _apiClient!.IssueTeacherAdvanceAsync(row.EmployeeId, amount, BusinessUtc(date), notes, key));
    }
    private async void ReturnAdvance_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedTeacher(); if (row is null) return;
        var advances = row.Advances?.Where(x => x.RemainingAmount > 0).ToList() ?? [];
        if (advances.Count == 0) { ShowError("Không có khoản ứng còn dư để hoàn."); return; }
        var selected = advances[0];
        var choices = advances.Select(x => new PromptOption(x.Id.ToString(), $"{x.IssuedAt:dd/MM/yyyy} · dư {x.RemainingAmount:N0} đ · {x.Description}")).ToArray();
        if (!CashPrompt($"Hoàn ứng · {row.TeacherName}", 0, decimal.MaxValue, out var amount, out var date, out var notes, new[] { new PromptField("advance", "Khoản ứng", selected.Id.ToString(), Options: choices) }, values => selected = advances.First(x => x.Id.ToString() == values["advance"]))) return;
        if (amount > selected.RemainingAmount) { ShowError("Số tiền hoàn vượt dư của khoản ứng đã chọn."); return; }
        await ExecuteAsync(Fingerprint("return", selected.Id, amount, date, notes), key => _apiClient!.ReturnTeacherAdvanceAsync(selected.Id, amount, BusinessUtc(date), notes, key));
    }
    private async void PayTeacher_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedTeacher(); if (row is null) return;
        if (row.UnpaidEarned <= 0) { ShowError("Giáo viên không còn công đã kiếm chưa thanh toán."); return; }
        var applyAdvance = 0m;
        if (!CashPrompt($"Thanh toán công · {row.TeacherName}", row.UnpaidEarned, row.UnpaidEarned, out var amount, out var date, out var notes,
                new[] { new PromptField("applyAmount", $"Cấn trừ tiền ứng (còn {row.RemainingAdvance:N0} đ)", "0") },
                values => applyAdvance = Number(values["applyAmount"], "Số tiền cấn trừ", 0, row.RemainingAdvance), allowZero: true)) return;
        if (amount + applyAdvance <= 0 || amount + applyAdvance > row.UnpaidEarned) { ShowError("Tổng tiền trả và cấn trừ phải lớn hơn 0 và không vượt công chưa thanh toán."); return; }
        await ExecuteAsync(Fingerprint("payment", row.EmployeeId, amount, date, notes + "|" + applyAdvance.ToString(CultureInfo.InvariantCulture)), key => _apiClient!.PayTeacherAsync(row.EmployeeId, amount, BusinessUtc(date), notes, key, applyAdvance));
    }
    private async void AddPlanItem_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        var fields = new[] { new PromptField("title", "Nội dung"), new PromptField("amount", "Số tiền"), new PromptField("date", "Ngày dự kiến đầu tiên", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date), new PromptField("type", "Loại", "Expense", Options: new[] { new PromptOption("Income", "Thu"), new PromptOption("Expense", "Chi") }), new PromptField("recurrence", "Lặp lại", "None", Options: new[] { new PromptOption("None", "Một lần"), new PromptOption("Monthly", "Hàng tháng") }), new PromptField("count", "Số kỳ (1–24)", "1"), new PromptField("notes", "Ghi chú", IsRequired: false) };
        if (!PromptDialog.TryShow(DialogOwner, "Thêm kế hoạch thu chi", fields, out var values)) return;
        try
        {
            var amount = Number(values["amount"], "Số tiền", 1, decimal.MaxValue);
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new InvalidOperationException("Ngày dự kiến không hợp lệ.");
            if (!int.TryParse(values["count"], out var count) || count < 1 || count > 24 || (values["recurrence"] == "None" && count != 1)) throw new InvalidOperationException("Số kỳ từ 1 đến 24. Kế hoạch một lần chỉ có 1 kỳ.");
            var fingerprint = Fingerprint("plan", Guid.Empty, amount, date, string.Join('|', values["title"], values["type"], values["recurrence"], values["count"], values["notes"]));
            await ExecuteAsync(fingerprint, key => _apiClient!.CreateFinancePlanAsync(BusinessUtc(date), values["type"], values["title"], amount, values["recurrence"], values["notes"], key, count));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }
    private async void SettlePlanItem_Click(object sender, RoutedEventArgs e) => await ChangePlanAsync(true);
    private async void CancelPlanItem_Click(object sender, RoutedEventArgs e) => await ChangePlanAsync(false);
    private async Task ChangePlanAsync(bool settle)
    {
        if (PlanItemsGrid.SelectedItem is not ApiClient.FinancePlanItem row) { ShowError("Chọn kế hoạch thu chi."); return; }
        if (row.Status != "Planned") { ShowError("Chỉ có thể xử lý kế hoạch đang dự kiến."); return; }
        if (MessageBox.Show(DialogOwner, settle ? $"Ghi nhận {row.TypeLabel.ToLower(Vi)} thực tế {row.Amount:N0} đ cho {row.Title}?" : $"Hủy kế hoạch {row.Title}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ExecuteAsync($"plan-{settle}-{row.Id}", _ => settle ? _apiClient!.SettleFinancePlanAsync(row.Id) : _apiClient!.CancelFinancePlanAsync(row.Id));
    }
    private static DateTime BusinessUtc(DateTime date) => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).UtcDateTime;

    private void FinanceChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();
    private void DrawChart()
    {
        if (FinanceChartCanvas is null) return;
        FinanceChartCanvas.Children.Clear();
        var rows = _report?.Months?.ToList(); if (rows is null || rows.Count == 0) return;
        var width = FinanceChartCanvas.ActualWidth; var height = FinanceChartCanvas.ActualHeight;
        if (width < 150 || height < 90) return;
        const double left = 58, top = 18, bottom = 38, right = 20;
        var plotWidth = width - left - right; var plotHeight = height - top - bottom;
        var values = rows.SelectMany(x => new[] { x.TotalInflow, x.TotalOutflow, x.ClosingCash }).Append(0m).ToArray();
        var min = Math.Min(0m, values.Min()); var max = Math.Max(1m, values.Max()); var range = max - min;
        double Y(decimal amount) => top + (double)((max - amount) / range) * plotHeight;
        var step = plotWidth / rows.Count;
        for (var i = 0; i <= 4; i++)
        {
            var value = min + range * i / 4; var y = Y(value);
            FinanceChartCanvas.Children.Add(new Line { X1 = left, X2 = width - right, Y1 = y, Y2 = y, Stroke = value == 0 ? Brushes.SlateGray : Brushes.LightGray, StrokeThickness = value == 0 ? 1.2 : .6 });
            Label($"{value / 1_000_000m:0.#}tr", 0, y - 7, Brushes.Gray, 9);
        }
        var series = new[] { (Brush: (Brush)Brushes.SeaGreen, Values: rows.Select(x => x.TotalInflow).ToArray()), (Brush: (Brush)Brushes.IndianRed, Values: rows.Select(x => x.TotalOutflow).ToArray()), (Brush: (Brush)Brushes.Indigo, Values: rows.Select(x => x.ClosingCash).ToArray()) };
        for (var i = 0; i < rows.Count; i++)
        {
            var x = left + step * (i + .5); var forecast = rows[i].Kind.Equals("Forecast", StringComparison.OrdinalIgnoreCase); var mixed = rows[i].Kind.Equals("Mixed", StringComparison.OrdinalIgnoreCase);
            var shade = new Rectangle { Width = step, Height = plotHeight, Fill = forecast ? new SolidColorBrush(Color.FromArgb(16, 99, 102, 241)) : mixed ? new SolidColorBrush(Color.FromArgb(20, 245, 158, 11)) : Brushes.Transparent };
            Canvas.SetLeft(shade, left + i * step); Canvas.SetTop(shade, top); FinanceChartCanvas.Children.Insert(0, shade);
            foreach (var data in series)
            {
                var y = Y(data.Values[i]);
                if (i > 0) FinanceChartCanvas.Children.Add(new Line { X1 = x - step, X2 = x, Y1 = Y(data.Values[i - 1]), Y2 = y, Stroke = data.Brush, StrokeThickness = 2, StrokeDashArray = forecast || mixed ? new DoubleCollection { 4, 3 } : null });
                var point = new Ellipse { Width = 5, Height = 5, Fill = data.Brush, ToolTip = $"{rows[i].MonthLabel} · {rows[i].KindLabel}: {data.Values[i]:N0} đ" }; Canvas.SetLeft(point, x - 2.5); Canvas.SetTop(point, y - 2.5); FinanceChartCanvas.Children.Add(point);
            }
            Label(rows[i].MonthLabel, x - 21, height - 33, Brushes.SlateGray, rows.Count > 12 ? 8 : 9);
            Label(forecast ? "DB" : mixed ? "TT+DB" : "TT", x - 11, height - 19, forecast ? Brushes.Indigo : mixed ? Brushes.DarkGoldenrod : Brushes.SlateGray, 8);
        }
    }
    private void Label(string text, double x, double y, Brush color, double size)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = color }; Canvas.SetLeft(block, x); Canvas.SetTop(block, y); FinanceChartCanvas.Children.Add(block);
    }
}
