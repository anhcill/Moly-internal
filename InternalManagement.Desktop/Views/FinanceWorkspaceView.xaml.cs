using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class FinanceWorkspaceView : UserControl
{
    private ApiClient? _apiClient;
    private ApiClient.FinanceWorkspaceReportItem? _report;
    private bool _working;
    private bool _canManage;
    private readonly Dictionary<string, string> _pendingOperationKeys = new(StringComparer.Ordinal);
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private Window DialogOwner => Window.GetWindow(this);

    public FinanceWorkspaceView()
    {
        InitializeComponent();
        ForecastFromMonthPicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ForecastHorizonCombo.ItemsSource = new[] { 3, 6, 9, 12, 24 };
        ForecastHorizonCombo.SelectedItem = 6;
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppTheme _) => Dispatcher.BeginInvoke(DrawAllCharts);

    public void SetApiClient(ApiClient apiClient) => _apiClient = apiClient;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? FinancialDataChanged;
    public DateTime FromMonth => new(ForecastFromMonthPicker.SelectedDate!.Value.Year, ForecastFromMonthPicker.SelectedDate.Value.Month, 1);

    public void SetCanManage(bool canManage)
    {
        _canManage = canManage;
        foreach (var button in Descendants(this).OfType<Button>().Where(x => Equals(x.Tag, "FinanceWrite")))
            button.IsEnabled = canManage;
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
        FreshnessText.Text = $"Cập nhật {(report.AsOf == default ? DateTime.Now : report.AsOf.AddHours(7)):dd/MM HH:mm}";

        var warnings = string.Join(" · ", report.Warnings.Where(w => !string.IsNullOrWhiteSpace(w)));
        ReportWarningsText.Text = warnings;
        ReportWarningsText.Visibility = string.IsNullOrWhiteSpace(warnings) ? Visibility.Collapsed : Visibility.Visible;

        // Executive KPIs
        PeriodInflowMetric.Text = $"{report.PeriodTotalInflow:N0} đ";
        CurrentCashMetric.Text = $"Quỹ sổ: {report.CurrentCash:N0} đ";

        PeriodOutflowMetric.Text = $"{report.PeriodTotalOutflow:N0} đ";
        ForecastClosingMetric.Text = $"Cuối kỳ: {report.ForecastClosingCash:N0} đ";

        var net = report.PeriodNetProfit;
        NetProfitMetric.Text = net >= 0 ? $"+{net:N0} đ" : $"{net:N0} đ";
        NetProfitMetric.Foreground = net >= 0
            ? (Brush)new SolidColorBrush(Color.FromRgb(16, 185, 129))
            : (Brush)new SolidColorBrush(Color.FromRgb(239, 68, 68));
        ProfitMarginMetric.Text = $"Biên lãi: {report.PeriodProfitMarginPercent:0.#}%";

        StudentDebtMetric.Text = $"{report.StudentDebt:N0} đ";
        var debtAlert = DebtReminderHelper.Summarize(report.Receivables);
        OverdueDebtMetric.Text = debtAlert.OverdueCount > 0
            ? $"Quá hạn: {debtAlert.OverdueAmount:N0} đ ({debtAlert.OverdueCount} HV)"
            : debtAlert.DueSoonCount > 0
                ? $"Sắp hạn: {debtAlert.DueSoonAmount:N0} đ ({debtAlert.DueSoonCount} HV)"
                : "Không có quá hạn";
        UpdateDebtAlertBanner(debtAlert);

        TeacherPayableMetric.Text = $"{report.TeacherPayable:N0} đ";
        TeacherAdvanceOpenMetric.Text = $"Ứng còn dư: {report.TotalTeacherAdvancesOpen:N0} đ";

        // Teacher payroll summary bar
        TeacherTotalEarnedText.Text = $"{report.TotalTeacherEarned:N0} đ";
        TeacherTotalAdvanceText.Text = $"{report.TotalTeacherAdvancesOpen:N0} đ";
        TeacherTotalSettledText.Text = $"{report.TotalTeacherSettled:N0} đ";
        TeacherTotalUnpaidText.Text = $"{report.TeacherPayable:N0} đ";

        MonthlyRowsGrid.ItemsSource = report.Months;
        PlanItemsGrid.ItemsSource = report.PlannedMovements;
        ApplyTeacherFilter();
        ApplyReceivablesFilter();
        ApplyLedgerFilter();
        DrawAllCharts();
    }

    private void ApplyTeacherFilter()
    {
        if (_report is null || TeacherCostsGrid is null) return;
        var q = TeacherSearchBox?.Text?.Trim() ?? string.Empty;
        if (TeacherSearchPlaceholder is not null)
            TeacherSearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;

        IEnumerable<ApiClient.FinanceTeacherCostItem> items = _report.TeacherCosts;
        if (!string.IsNullOrWhiteSpace(q))
            items = items.Where(x => x.TeacherName.Contains(q, StringComparison.OrdinalIgnoreCase));

        TeacherCostsGrid.ItemsSource = items.OrderByDescending(x => x.UnpaidEarned).ThenBy(x => x.TeacherName).ToList();
    }

    private void ApplyReceivablesFilter()
    {
        if (_report is null || ReceivablesGrid is null) return;
        var q = ReceivableSearchBox?.Text?.Trim() ?? string.Empty;
        if (ReceivableSearchPlaceholder is not null)
            ReceivableSearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;

        IEnumerable<ApiClient.FinanceReceivableItem> items = _report.Receivables;
        var mode = ReceivableStatusFilter?.SelectedIndex ?? 0;
        if (mode == 1)
            items = items.Where(r => r.Status != "Cancelled" && r.OutstandingAmount > 0);
        else if (mode == 2)
            items = items.Where(r => r.Status != "Cancelled" && r.OutstandingAmount > 0 && r.DueDate.HasValue && r.DueDate.Value < DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(q))
        {
            items = items.Where(r =>
                r.StudentName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (r.ClassName?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        ReceivablesGrid.ItemsSource = items.OrderByDescending(r => r.OutstandingAmount).ToList();
    }

    private void ApplyLedgerFilter()
    {
        if (_report is null || CashMovementsGrid is null) return;
        var q = LedgerSearchBox?.Text?.Trim() ?? string.Empty;
        if (LedgerSearchPlaceholder is not null)
            LedgerSearchPlaceholder.Visibility = string.IsNullOrEmpty(q) ? Visibility.Visible : Visibility.Collapsed;

        IEnumerable<ApiClient.FinanceCashMovementItem> items = _report.CashMovements ?? [];
        var mode = LedgerTypeFilter?.SelectedIndex ?? 0;
        if (mode == 1)
            items = items.Where(m => m.Inflow > 0);
        else if (mode == 2)
            items = items.Where(m => m.Outflow > 0);

        if (!string.IsNullOrWhiteSpace(q))
        {
            items = items.Where(m =>
                m.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                m.ReferenceLabel.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        CashMovementsGrid.ItemsSource = items.OrderByDescending(m => m.OccurredAt).ToList();
    }

    private void TeacherSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyTeacherFilter();
    private void ReceivableFilter_Changed(object sender, RoutedEventArgs e) => ApplyReceivablesFilter();
    private void LedgerFilter_Changed(object sender, RoutedEventArgs e) => ApplyLedgerFilter();

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, WorkspaceTabs) && WorkspaceTabs.SelectedIndex == 0)
            Dispatcher.BeginInvoke(DrawAllCharts);
    }

    private void ChartsCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawAllCharts();

    private void DrawAllCharts()
    {
        if (FinanceChartCanvas is not null) FinanceChartRenderer.DrawMonthlyBarAndBalanceChart(FinanceChartCanvas, _report);
        if (ProfitBreakdownCanvas is not null) FinanceChartRenderer.DrawProfitBreakdownChart(ProfitBreakdownCanvas, _report);
        if (TeacherPayrollCanvas is not null) FinanceChartRenderer.DrawTeacherPayrollChart(TeacherPayrollCanvas, _report);
        if (ReceivablesDonutCanvas is not null) FinanceChartRenderer.DrawReceivablesDonutChart(ReceivablesDonutCanvas, _report);
    }

    private static decimal Number(string? text, string label, decimal min, decimal max)
    {
        if ((!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
             && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            || value < min || value > max)
        {
            throw new InvalidOperationException($"{label} phải là số từ {min:N0} đến {max:N0}.");
        }
        return value;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, e);

    private async void RecordManualIncome_Click(object sender, RoutedEventArgs e)
        => await RecordManualCashTransactionAsync("Income", "Ghi nhận Phiếu Thu");

    private async void RecordManualExpense_Click(object sender, RoutedEventArgs e)
        => await RecordManualCashTransactionAsync("Expense", "Ghi nhận Phiếu Chi");

    private void UpdateDebtAlertBanner(DebtAlertSummary summary)
    {
        if (DebtAlertBanner is null || DebtAlertText is null) return;
        var parts = new List<string>();
        if (summary.OverdueCount > 0)
            parts.Add($"{summary.OverdueCount} học viên quá hạn ({summary.OverdueAmount:N0} đ)");
        if (summary.DueSoonCount > 0)
            parts.Add($"{summary.DueSoonCount} học viên sắp đến hạn trong 3 ngày ({summary.DueSoonAmount:N0} đ)");

        if (parts.Count == 0)
        {
            DebtAlertBanner.Visibility = Visibility.Collapsed;
            return;
        }

        DebtAlertText.Text = "Cảnh báo công nợ: " + string.Join(" · ", parts);
        DebtAlertBanner.Visibility = Visibility.Visible;
    }

    private void JumpToOverdueDebt_Click(object sender, RoutedEventArgs e)
    {
        WorkspaceTabs.SelectedIndex = 3;
        var summary = DebtReminderHelper.Summarize(_report?.Receivables);
        ReceivableStatusFilter.SelectedIndex = summary.OverdueCount > 0 ? 2 : 1;
        ApplyReceivablesFilter();
    }

    private async Task RecordManualCashTransactionAsync(string type, string title)
    {
        if (_working || _apiClient is null) return;
        var presets = type == "Income"
            ? new[]
            {
                new PromptOption("Manual", "Thu khác / Doanh thu ngoài"),
                new PromptOption("CscaTuition", "Thu học phí bổ sung"),
                new PromptOption("Sponsor", "Tài trợ / Hợp tác")
            }
            : new[]
            {
                new PromptOption("OperatingExpense", "Chi vận hành (mặt bằng, điện nước, phần mềm)"),
                new PromptOption("Marketing", "Chi quảng cáo / Marketing"),
                new PromptOption("BonusExpense", "Chi thưởng / Phúc lợi"),
                new PromptOption("Manual", "Chi phí khác")
            };

        var accountOptions = new List<PromptOption> { new("", "-- Quỹ chung (không chỉ định) --") };
        try
        {
            var cashAccounts = await _apiClient.GetCashAccountsAsync();
            foreach (var acc in cashAccounts)
                accountOptions.Add(new PromptOption(acc.Id.ToString(), $"{acc.Code} · {acc.Name} ({acc.CurrentBalance:N0} đ)"));
        }
        catch
        {
            // Keep manual entry usable even if cash accounts list cannot be fetched.
        }

        var fields = new List<PromptField>
        {
            new("amount", "Số tiền (VNĐ)"),
            new("date", "Ngày giao dịch", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date),
            new("category", "Nhóm khoản mục", presets[0].Value, Options: presets)
        };
        if (accountOptions.Count > 1)
            fields.Add(new PromptField("cashAccount", "Quỹ / Tài khoản ngân hàng", "", IsRequired: false, Options: accountOptions));
        fields.Add(new PromptField("description", "Nội dung chi tiết"));

        if (!PromptDialog.TryShow(DialogOwner, title, fields, out var values)) return;

        try
        {
            var amount = Number(values["amount"], "Số tiền", 1, decimal.MaxValue);
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("Ngày giao dịch không hợp lệ.");
            if (date.Date > DateTime.Today)
                throw new InvalidOperationException("Phiếu thu/chi thực tế không dùng ngày tương lai. Hãy dùng tab Kế hoạch thu chi.");
            var description = values["description"].Trim();
            var refType = values["category"];
            Guid? cashAccountId = values.TryGetValue("cashAccount", out var rawAccount) && Guid.TryParse(rawAccount, out var parsedAccountId)
                ? parsedAccountId
                : null;

            await ExecuteAsync(
                Fingerprint($"manual-{type}-{cashAccountId}", Guid.Empty, amount, date, description),
                _ => _apiClient!.CreateFinanceLedgerTransactionWithAccountAsync(type, amount, BusinessUtc(date), description, cashAccountId, refType));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ExportPnlPdf_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) { ShowError("Chưa có dữ liệu báo cáo để xuất."); return; }
        try
        {
            var html = FinanceReportExporter.BuildMonthlyPnlHtml(_report);
            await FinanceReportExporter.SaveAndOpenHtmlReportAsync($"bao-cao-tai-chinh-{FromMonth:yyyy-MM}", html);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void PrintTeacherStatement_Click(object sender, RoutedEventArgs e)
    {
        if (TeacherCostsGrid.SelectedItem is not ApiClient.FinanceTeacherCostItem row)
        {
            ShowError("Chọn giáo viên cần in phiếu quyết toán thù lao.");
            return;
        }
        try
        {
            var html = FinanceReportExporter.BuildTeacherStatementHtml(row, _report?.AsOf);
            await FinanceReportExporter.SaveAndOpenHtmlReportAsync($"phieu-luong-{row.TeacherName}", html);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void PrintStudentReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row)
        {
            ShowError("Chọn học viên cần in phiếu thu / sao kê học phí.");
            return;
        }
        try
        {
            var html = FinanceReportExporter.BuildStudentReceiptHtml(row, _report?.AsOf);
            await FinanceReportExporter.SaveAndOpenHtmlReportAsync($"phieu-thu-{row.StudentName}", html);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void CopyZaloReminder_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row)
        {
            ShowError("Chọn học viên cần tạo tin nhắn nhắc học phí.");
            return;
        }
        if (row.OutstandingAmount <= 0)
        {
            ShowError("Học viên này đã hoàn tất học phí, không còn công nợ để nhắc.");
            return;
        }
        try
        {
            var message = DebtReminderHelper.BuildZaloReminderMessage(row);
            Clipboard.SetText(message);
            MessageBox.Show(
                DialogOwner,
                $"Đã sao chép tin nhắn nhắc học phí vào Clipboard — dán trực tiếp vào Zalo:\n\n{message}",
                "Copy nhắc Zalo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_report is null) { ShowError("Chưa có dữ liệu báo cáo để xuất."); return; }
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Xuất báo cáo tài chính chuẩn kế toán",
                Filter = "Báo cáo Excel nhiều sheet (*.xlsx)|*.xlsx|Tệp CSV UTF-8 (*.csv)|*.csv",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"BaoCaoTaiChinh_{FromMonth:yyyy-MM}.xlsx"
            };
            if (dialog.ShowDialog(DialogOwner) != true) return;

            var isCsv = string.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase);
            if (isCsv)
            {
                var csv = new StringBuilder();
                csv.AppendLine("Tháng,Loại dữ liệu,Số dư đầu kỳ,Tổng Thu,Tổng Chi,Lãi/Lỗ tháng,Thu học viên,Chi giáo viên,Số dư cuối kỳ");
                foreach (var row in _report.Months)
                {
                    csv.AppendLine(string.Join(',', new object[]
                    {
                        row.MonthLabel, row.KindLabel, row.OpeningCash, row.TotalInflow, row.TotalOutflow,
                        row.NetProfit, row.StudentCollections, row.TeacherPayments, row.ClosingCash
                    }.Select(Csv)));
                }
                csv.AppendLine(string.Join(',', new object[]
                {
                    "TỔNG CỘNG", string.Empty, string.Empty, _report.PeriodTotalInflow, _report.PeriodTotalOutflow,
                    _report.PeriodNetProfit, _report.Months.Sum(x => x.StudentCollections), _report.Months.Sum(x => x.TeacherPayments), _report.ForecastClosingCash
                }.Select(Csv)));
                await File.WriteAllTextAsync(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            }
            else
            {
                IReadOnlyList<ApiClient.CashAccountItem>? accounts = null;
                if (_apiClient is not null)
                {
                    try { accounts = await _apiClient.GetCashAccountsAsync(); } catch { }
                }
                var bytes = FinanceExcelExporter.BuildFinanceWorkspaceWorkbook(_report, accounts);
                await File.WriteAllBytesAsync(dialog.FileName, bytes);
            }

            if (MessageBox.Show(DialogOwner,
                    $"Đã xuất báo cáo thành công:\n{dialog.FileName}\n\nBạn có muốn mở tệp ngay không?",
                    "Xuất báo cáo thành công", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private static string Csv(object value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        if (text.Length > 0 && (text[0] is '=' or '+' or '@' || (text[0] == '-' && !decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out _))))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
    private void ShowError(string message) => MessageBox.Show(DialogOwner, message, "Tài chính vận hành", MessageBoxButton.OK, MessageBoxImage.Warning);

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
            FinancialDataChanged?.Invoke(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            ShowError(committed
                ? $"Đã lưu giao dịch. Chưa tải lại được báo cáo: {ex.Message}\nBấm Tải lại để cập nhật."
                : $"{ex.Message}\n\nNhập lại cùng nội dung để thử lại giao dịch an toàn.");
        }
        finally { _working = false; WorkspaceTabs.IsEnabled = true; }
    }

    private bool CashPrompt(string title, decimal initial, decimal maximum, out decimal amount, out DateTime date, out string notes, IReadOnlyList<PromptField>? extras = null, Action<IReadOnlyDictionary<string, string>>? readExtra = null, bool allowZero = false)
    {
        amount = 0; date = default; notes = "";
        var fields = new List<PromptField>
        {
            new("amount", "Số tiền (VNĐ)", initial.ToString(CultureInfo.InvariantCulture)),
            new("date", "Ngày giao dịch", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date),
            new("notes", "Diễn giải", IsRequired: false)
        };
        if (extras is not null) fields.AddRange(extras);
        if (!PromptDialog.TryShow(DialogOwner, title, fields, out var values)) return false;
        try
        {
            amount = Number(values["amount"], "Số tiền", allowZero ? 0 : 1, maximum);
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                throw new InvalidOperationException("Ngày giao dịch không hợp lệ.");
            if (date.Date > DateTime.Today)
                throw new InvalidOperationException("Giao dịch thực tế không dùng ngày tương lai.");
            notes = values["notes"];
            readExtra?.Invoke(values);
            return true;
        }
        catch (Exception ex) { ShowError(ex.Message); return false; }
    }

    private string Fingerprint(string type, Guid id, decimal amount, DateTime date, string notes)
        => FormattableString.Invariant($"{type}|{id}|{amount}|{date:yyyy-MM-dd}|{notes}");

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
        if (available <= 0) { ShowError("Học viên không có khoản đã thu có thể hoàn."); return; }
        if (CashPrompt($"Hoàn tiền · {row.StudentName}", available, available, out var amount, out var date, out var notes))
            await ExecuteAsync(Fingerprint("refund", row.Id, amount, date, notes), key => _apiClient!.RefundStudentDebtAsync(row.Id, amount, BusinessUtc(date), notes, key));
    }

    private async void ScheduleStudentDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) { ShowError("Chọn học viên cần hẹn ngày thu."); return; }
        if (!PromptDialog.TryShow(DialogOwner, $"Hẹn ngày thu · {row.StudentName}", new[]
            { new PromptField("date", "Ngày hẹn thu", (row.LocalDueDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date) }, out var values)) return;
        if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) { ShowError("Ngày hẹn thu không hợp lệ."); return; }
        var dueAt = BusinessUtc(date.AddDays(1).AddTicks(-1));
        await ExecuteAsync($"receivable-due-{row.Id}-{date:yyyy-MM-dd}", _ => _apiClient!.UpdateStudentReceivableAsync(row.Id, dueAt));
    }

    private async void CancelStudentDebt_Click(object sender, RoutedEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) { ShowError("Chọn công nợ cần hủy."); return; }
        if (row.PaidAmount - row.RefundedAmount != 0) { ShowError("Chỉ hủy công nợ khi tiền đã thu ròng bằng 0. Hãy hoàn tiền trước khi hủy."); return; }
        if (MessageBox.Show(DialogOwner, $"Hủy công nợ của {row.StudentName}?", "Hủy công nợ", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
        if (!CashPrompt($"Hoàn ứng · {row.TeacherName}", selected.RemainingAmount, decimal.MaxValue, out var amount, out var date, out var notes, new[] { new PromptField("advance", "Khoản ứng", selected.Id.ToString(), Options: choices) }, values => selected = advances.First(x => x.Id.ToString() == values["advance"]))) return;
        if (amount > selected.RemainingAmount) { ShowError("Số tiền hoàn vượt dư của khoản ứng đã chọn."); return; }
        await ExecuteAsync(Fingerprint("return", selected.Id, amount, date, notes), key => _apiClient!.ReturnTeacherAdvanceAsync(selected.Id, amount, BusinessUtc(date), notes, key));
    }

    private async void PayTeacher_Click(object sender, RoutedEventArgs e)
    {
        var row = SelectedTeacher(); if (row is null) return;
        if (row.UnpaidEarned <= 0)
        {
            ShowError("Giáo viên này không còn số tiền phải chuyển sau khi cấn trừ ứng.");
            return;
        }

        var fields = new List<PromptField>
        {
            new("amount", $"Tiền mặt chuyển trả (tối đa {row.UnpaidEarned:N0} đ)", row.UnpaidEarned.ToString(CultureInfo.InvariantCulture)),
            new("date", "Ngày chuyển lương", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date),
            new("notes", "Diễn giải chuyển khoản", $"Thanh toán lương {row.TeacherName}", IsRequired: false)
        };
        if (row.RemainingAdvance > 0)
        {
            fields.Insert(1, new PromptField("applyAdvance", $"Cấn trừ thêm từ ứng dư (tối đa {Math.Min(row.RemainingAdvance, row.UnpaidEarned):N0} đ)", "0", IsRequired: false));
        }

        if (!PromptDialog.TryShow(DialogOwner, $"Chuyển lương · {row.TeacherName}", fields, out var values)) return;
        try
        {
            var cashAmount = Number(values["amount"], "Tiền mặt chuyển trả", 0, row.UnpaidEarned);
            var applyAdvance = values.TryGetValue("applyAdvance", out var rawApply) && !string.IsNullOrWhiteSpace(rawApply)
                ? Number(rawApply, "Tiền cấn trừ ứng", 0, Math.Min(row.RemainingAdvance, row.UnpaidEarned))
                : 0m;
            if (cashAmount + applyAdvance <= 0)
                throw new InvalidOperationException("Tổng tiền chuyển và cấn ứng phải lớn hơn 0.");
            if (cashAmount + applyAdvance > row.UnpaidEarned)
                throw new InvalidOperationException($"Tổng tiền chuyển + cấn ứng không được vượt {row.UnpaidEarned:N0} đ.");
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("Ngày chuyển lương không hợp lệ.");
            if (date.Date > DateTime.Today)
                throw new InvalidOperationException("Ngày chuyển lương không dùng ngày tương lai.");

            var notes = values["notes"];
            await ExecuteAsync(
                Fingerprint($"payment-{applyAdvance}", row.EmployeeId, cashAmount, date, notes),
                key => _apiClient!.PayTeacherAsync(row.EmployeeId, cashAmount, BusinessUtc(date), notes, key, applyAdvance));
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void SyncMolyCoursePayroll_Click(object sender, RoutedEventArgs e)
    {
        if (_working || _apiClient is null) return;
        _working = true;
        WorkspaceTabs.IsEnabled = false;
        try
        {
            await _apiClient.SyncCscaTeacherCostsForFinanceAsync();
            await LoadAsync(_apiClient);
            FinancialDataChanged?.Invoke(this, new RoutedEventArgs());
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _working = false; WorkspaceTabs.IsEnabled = true; }
    }

    private async void TeacherCostsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TeacherCostsGrid.SelectedItem is not ApiClient.FinanceTeacherCostItem row) return;
        var detail = $"{row.TeacherName}\n\n" +
            $"Công buổi đã dạy: {row.GrossEarnedAmount:N0} đ\n" +
            $"Thưởng: +{row.BonusAmount:N0} đ · Khấu trừ: -{row.DeductionAmount:N0} đ\n" +
            $"Công thực tế ròng: {row.EarnedAmount:N0} đ\n" +
            $"Đã quyết toán/cấn ứng: {row.SettledAmount:N0} đ\n" +
            $"Ứng còn dư: {row.RemainingAdvance:N0} đ\n" +
            $"Còn phải chuyển: {row.UnpaidEarned:N0} đ\n" +
            $"Trạng thái LMS: {row.PaymentStatusLabel}";
        if (!_canManage)
        {
            MessageBox.Show(DialogOwner, detail, "Chi tiết lương giáo viên", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(DialogOwner, detail + "\n\nSửa thưởng / khấu trừ / trạng thái trên MolyCourse?", "Chi tiết lương giáo viên", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

        var statuses = new[]
        {
            new PromptOption("pending", "Chờ thanh toán"),
            new PromptOption("processing", "Đang xử lý"),
            new PromptOption("paid", "Đã thanh toán"),
            new PromptOption("hold", "Tạm giữ")
        };
        var fields = new[]
        {
            new PromptField("bonusAmount", "Tiền thưởng", row.BonusAmount.ToString(CultureInfo.InvariantCulture)),
            new PromptField("deductionAmount", "Tiền khấu trừ", row.DeductionAmount.ToString(CultureInfo.InvariantCulture)),
            new PromptField("paymentStatus", "Trạng thái LMS", row.PaymentStatus.ToLowerInvariant(), Options: statuses),
            new PromptField("notes", "Ghi chú quyết toán", IsRequired: false, IsMultiline: true)
        };
        if (!PromptDialog.TryShow(DialogOwner, $"Sửa quyết toán · {row.TeacherName}", fields, out var values)) return;
        try
        {
            var bonus = Number(values["bonusAmount"], "Tiền thưởng", 0, decimal.MaxValue);
            var deduction = Number(values["deductionAmount"], "Tiền khấu trừ", 0, decimal.MaxValue);
            _working = true;
            WorkspaceTabs.IsEnabled = false;
            await _apiClient!.UpdateCscaTeacherSettlementAsync(row.EmployeeId, bonus, deduction, values["paymentStatus"], values["notes"]);
            await LoadAsync(_apiClient);
            FinancialDataChanged?.Invoke(this, new RoutedEventArgs());
        }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { _working = false; WorkspaceTabs.IsEnabled = true; }
    }

    private void ReceivablesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ReceivablesGrid.SelectedItem is not ApiClient.FinanceReceivableItem row) return;
        MessageBox.Show(DialogOwner,
            $"{row.StudentName} ({row.ClassName})\n\nCần thu: {row.PayableAmount:N0} đ\nĐã thu ròng: {row.PaidAmount - row.RefundedAmount:N0} đ\nCòn nợ: {row.OutstandingAmount:N0} đ\nHạn thu: {(row.LocalDueDate.HasValue ? row.LocalDueDate.Value.ToString("dd/MM/yyyy") : "Chưa hẹn")}",
            "Chi tiết công nợ", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void MonthlyRowsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MonthlyRowsGrid.SelectedItem is not ApiClient.FinanceMonthlyRowItem row) return;
        MessageBox.Show(DialogOwner,
            $"Tháng {row.MonthLabel} ({row.KindLabel})\n\nĐầu kỳ: {row.OpeningCash:N0} đ\nTổng Thu: {row.TotalInflow:N0} đ\nTổng Chi: {row.TotalOutflow:N0} đ\nLãi / Lỗ tháng: {row.NetProfitDisplay}\nCuối kỳ: {row.ClosingCash:N0} đ",
            "Chi tiết dòng tiền tháng", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PlanItemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlanItemsGrid.SelectedItem is not ApiClient.FinancePlanItem row) return;
        MessageBox.Show(DialogOwner,
            $"{row.Title}\n\nLoại: {row.TypeLabel} · Số tiền: {row.Amount:N0} đ\nNgày dự kiến: {row.LocalPlannedDate:dd/MM/yyyy} · Lặp: {row.RecurrenceLabel}\nTrạng thái: {row.StatusLabel}\nGhi chú: {row.Notes ?? "—"}",
            "Chi tiết kế hoạch", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CashMovementsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CashMovementsGrid.SelectedItem is not ApiClient.FinanceCashMovementItem row) return;
        MessageBox.Show(DialogOwner,
            $"{row.Description}\n\nNgày: {row.LocalOccurredAt:dd/MM/yyyy HH:mm}\nLoại: {row.TypeLabel}\nThu: {row.Inflow:N0} đ · Chi: {row.Outflow:N0} đ\nNguồn: {row.ReferenceLabel}",
            "Chi tiết phiếu thu chi", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void AddPlanItem_Click(object sender, RoutedEventArgs e)
    {
        if (_working) return;
        var fields = new[]
        {
            new PromptField("title", "Nội dung"),
            new PromptField("amount", "Số tiền (VNĐ)"),
            new PromptField("date", "Ngày dự kiến", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date),
            new PromptField("type", "Loại", "Expense", Options: new[] { new PromptOption("Income", "Thu"), new PromptOption("Expense", "Chi") }),
            new PromptField("recurrence", "Lặp lại", "None", Options: new[] { new PromptOption("None", "Một lần"), new PromptOption("Monthly", "Hàng tháng") }),
            new PromptField("count", "Số kỳ (1–24)", "1"),
            new PromptField("notes", "Ghi chú", IsRequired: false)
        };
        if (!PromptDialog.TryShow(DialogOwner, "Thêm kế hoạch thu chi", fields, out var values)) return;
        try
        {
            var amount = Number(values["amount"], "Số tiền", 1, decimal.MaxValue);
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("Ngày dự kiến không hợp lệ.");
            if (!int.TryParse(values["count"], out var count) || count < 1 || count > 24 || (values["recurrence"] == "None" && count != 1))
                throw new InvalidOperationException("Số kỳ từ 1 đến 24 (một lần chỉ có 1 kỳ).");
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
        if (row.Status != "Planned") { ShowError("Chỉ xử lý được kế hoạch đang ở trạng thái Dự kiến."); return; }
        if (MessageBox.Show(DialogOwner, settle ? $"Ghi nhận {row.TypeLabel.ToLower(Vi)} thực tế {row.Amount:N0} đ cho {row.Title}?" : $"Hủy kế hoạch {row.Title}?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await ExecuteAsync($"plan-{settle}-{row.Id}", _ => settle ? _apiClient!.SettleFinancePlanAsync(row.Id) : _apiClient!.CancelFinancePlanAsync(row.Id));
    }

    private static DateTime BusinessUtc(DateTime date)
        => new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).UtcDateTime;
}
