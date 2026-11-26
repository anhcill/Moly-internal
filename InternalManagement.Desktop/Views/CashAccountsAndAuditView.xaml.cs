using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class CashAccountsAndAuditView : UserControl
{
    private ApiClient? _apiClient;
    private IReadOnlyList<ApiClient.CashAccountItem> _accounts = [];
    private IReadOnlyList<ApiClient.FinanceAuditEntryItem> _auditEntries = [];
    private bool _canManage = true;
    private bool _working;

    private Window DialogOwner => Window.GetWindow(this);

    public CashAccountsAndAuditView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? FinancialDataChanged;

    public IReadOnlyList<ApiClient.CashAccountItem> CashAccounts => _accounts;

    public void SetApiClient(ApiClient apiClient) => _apiClient = apiClient;

    public void SetCanManage(bool canManage)
    {
        _canManage = canManage;
        foreach (var button in Descendants(this).OfType<Button>().Where(x => Equals(x.Tag, "FinanceWrite")))
            button.IsEnabled = canManage;
    }

    public async Task LoadAsync(ApiClient apiClient, CancellationToken ct = default)
    {
        _apiClient = apiClient;
        var (fromUtc, toUtc, search) = ReadAuditFilters();

        var accountsTask = apiClient.GetCashAccountsAsync(ct);
        var auditTask = apiClient.GetFinanceAuditTrailAsync(fromUtc, toUtc, search, 200, ct);
        await Task.WhenAll(accountsTask, auditTask);

        RenderAccounts(accountsTask.Result);
        RenderAuditTrail(auditTask.Result);
    }

    private void RenderAccounts(IReadOnlyList<ApiClient.CashAccountItem> accounts)
    {
        _accounts = accounts;
        var cards = accounts.Select(CashAccountCardItem.From).ToList();
        CashAccountsList.ItemsSource = cards;

        var hasAccounts = cards.Count > 0;
        CashAccountsList.Visibility = hasAccounts ? Visibility.Visible : Visibility.Collapsed;
        EmptyAccountsText.Visibility = hasAccounts ? Visibility.Collapsed : Visibility.Visible;

        var total = accounts.Sum(a => a.CurrentBalance);
        TotalBalanceText.Text = $"Tổng số dư: {total:N0} đ";

        if (total < 0)
        {
            TotalBalanceBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceDangerBgBrush");
            TotalBalanceBadge.SetResourceReference(Border.BorderBrushProperty, "SurfaceDangerBorderBrush");
            TotalBalanceText.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceDangerTextBrush");
        }
        else
        {
            TotalBalanceBadge.SetResourceReference(Border.BackgroundProperty, "SurfaceSuccessBgBrush");
            TotalBalanceBadge.SetResourceReference(Border.BorderBrushProperty, "SurfaceSuccessBorderBrush");
            TotalBalanceText.SetResourceReference(TextBlock.ForegroundProperty, "SurfaceSuccessTextBrush");
        }
    }

    private void RenderAuditTrail(IReadOnlyList<ApiClient.FinanceAuditEntryItem> entries)
    {
        _auditEntries = entries;
        AuditTrailGrid.ItemsSource = entries;
        AuditCountText.Text = entries.Count > 0 ? $"{entries.Count} bản ghi" : string.Empty;
        AuditEmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private (DateTime? FromUtc, DateTime? ToUtc, string? Search) ReadAuditFilters()
    {
        var fromDate = AuditFromDatePicker.SelectedDate;
        var toDate = AuditToDatePicker.SelectedDate;
        if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            throw new InvalidOperationException("Ngày bắt đầu không được lớn hơn ngày kết thúc.");

        DateTime? fromUtc = fromDate.HasValue ? BusinessUtc(fromDate.Value.Date) : null;
        DateTime? toUtc = toDate.HasValue ? BusinessUtc(toDate.Value.Date.AddDays(1).AddTicks(-1)) : null;
        var search = NullIfWhiteSpace(AuditSearchBox.Text);
        return (fromUtc, toUtc, search);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;
        await RunBusyAsync(() => LoadAsync(_apiClient));
    }

    private async void PrintAudit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var html = FinanceReportExporter.BuildCashAccountsAuditHtml(
                _accounts, _auditEntries, AuditFromDatePicker.SelectedDate, AuditToDatePicker.SelectedDate);
            await FinanceReportExporter.SaveAndOpenHtmlReportAsync("bao-cao-quy-kiem-toan", html);
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void ExportAuditExcel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Xuất báo cáo Quỹ & Nhật ký Kiểm toán (Excel)",
                Filter = "Báo cáo Excel (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"QuyVaKiemToan_{DateTime.Today:yyyyMMdd}.xlsx"
            };
            if (dialog.ShowDialog(DialogOwner) != true) return;
            var bytes = FinanceExcelExporter.BuildCashAccountsAuditWorkbook(
                _accounts, _auditEntries, AuditFromDatePicker.SelectedDate, AuditToDatePicker.SelectedDate);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            if (MessageBox.Show(DialogOwner,
                    $"Đã xuất báo cáo thành công:\n{dialog.FileName}\n\nMở tệp Excel ngay?",
                    "Xuất Excel thành công", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private async void FilterAudit_Click(object sender, RoutedEventArgs e)
    {
        if (_apiClient is null) return;
        await RunBusyAsync(async () =>
        {
            var (fromUtc, toUtc, search) = ReadAuditFilters();
            var entries = await _apiClient.GetFinanceAuditTrailAsync(fromUtc, toUtc, search, 200);
            RenderAuditTrail(entries);
        });
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!_canManage || _working || _apiClient is null) return;

        var fields = new[]
        {
            new PromptField("code", "Mã quỹ (vd: TM, VCB)"),
            new PromptField("name", "Tên quỹ / tài khoản"),
            new PromptField("bankName", "Ngân hàng", IsRequired: false),
            new PromptField("accountNumber", "Số tài khoản", IsRequired: false),
            new PromptField("openingBalance", "Số dư đầu kỳ (VNĐ)", "0")
        };

        if (!PromptDialog.TryShow(DialogOwner, "Thêm quỹ / tài khoản ngân hàng", fields, out var values)) return;

        try
        {
            var code = values["code"].Trim().ToUpperInvariant();
            var name = values["name"].Trim();
            var bankName = NullIfWhiteSpace(values["bankName"]);
            var accountNumber = NullIfWhiteSpace(values["accountNumber"]);
            var openingBalance = ParseDecimal(values["openingBalance"], "Số dư đầu kỳ", decimal.MinValue, decimal.MaxValue);

            await RunBusyAsync(async () =>
            {
                var created = await _apiClient.CreateCashAccountAsync(code, name, accountNumber, bankName, openingBalance);
                if (created is null)
                    throw new InvalidOperationException("Không tạo được tài khoản quỹ.");
                await LoadAsync(_apiClient);
                FinancialDataChanged?.Invoke(this, new RoutedEventArgs());
            });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async void AdjustAccount_Click(object sender, RoutedEventArgs e)
    {
        if (!_canManage || _working || _apiClient is null) return;
        if (_accounts.Count == 0)
        {
            ShowError("Chưa có tài khoản quỹ nào. Hãy tạo quỹ / tài khoản trước khi điều chỉnh số dư.");
            return;
        }

        var accountOptions = _accounts
            .Select(a => new PromptOption(a.Id.ToString(), $"{a.Code} · {a.Name} ({a.CurrentBalance:N0} đ)"))
            .ToArray();
        var typeOptions = new[]
        {
            new PromptOption("Income", "Cộng thêm (+)"),
            new PromptOption("Expense", "Trừ bớt (-)")
        };

        var fields = new[]
        {
            new PromptField("accountId", "Tài khoản quỹ", accountOptions[0].Value, Options: accountOptions),
            new PromptField("type", "Loại điều chỉnh", "Income", Options: typeOptions),
            new PromptField("amount", "Số tiền (VNĐ)"),
            new PromptField("date", "Ngày giao dịch", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Kind: PromptFieldKind.Date),
            new PromptField("description", "Nội dung điều chỉnh")
        };

        if (!PromptDialog.TryShow(DialogOwner, "Điều chỉnh số dư quỹ", fields, out var values)) return;

        try
        {
            if (!Guid.TryParse(values["accountId"], out var accountId))
                throw new InvalidOperationException("Tài khoản quỹ không hợp lệ.");
            var type = values["type"];
            var amount = ParseDecimal(values["amount"], "Số tiền", 1, decimal.MaxValue);
            if (!DateTime.TryParseExact(values["date"], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                throw new InvalidOperationException("Ngày giao dịch không hợp lệ.");
            if (date.Date > DateTime.Today)
                throw new InvalidOperationException("Ngày điều chỉnh không dùng ngày tương lai.");
            var description = values["description"].Trim();

            await RunBusyAsync(async () =>
            {
                var ok = await _apiClient.AdjustCashAccountAsync(accountId, type, amount, BusinessUtc(date), description);
                if (!ok)
                    throw new InvalidOperationException("Không điều chỉnh được số dư quỹ.");
                await LoadAsync(_apiClient);
                FinancialDataChanged?.Invoke(this, new RoutedEventArgs());
            });
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void AuditSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (AuditSearchPlaceholder is not null)
            AuditSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(AuditSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void AuditSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            FilterAudit_Click(sender, e);
    }

    private void AuditTrailGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AuditTrailGrid.SelectedItem is not ApiClient.FinanceAuditEntryItem row) return;
        MessageBox.Show(
            DialogOwner,
            $"{row.ActionLabel} · {row.SignedAmountDisplay}\n\n" +
            $"Thời gian: {row.LocalOccurredAt:dd/MM/yyyy HH:mm}\n" +
            $"Người thao tác: {row.Actor}\n" +
            $"Nội dung: {row.Description}\n" +
            $"Tham chiếu: {row.ReferenceLabel}",
            "Chi tiết kiểm toán",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (_working) return;
        _working = true;
        RootGrid.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            _working = false;
            RootGrid.IsEnabled = true;
            SetCanManage(_canManage);
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(DialogOwner, message, "Quỹ & Kiểm toán", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal ParseDecimal(string? text, string label, decimal min, decimal max)
    {
        var cleaned = text?.Trim() ?? string.Empty;
        if ((!decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.CurrentCulture, out var value)
             && !decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out value)
             && !decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            || value < min || value > max)
        {
            throw new InvalidOperationException($"{label} phải là số hợp lệ.");
        }
        return value;
    }

    private static DateTime BusinessUtc(DateTime date) =>
        new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).UtcDateTime;

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed record CashAccountCardItem(
        string Code,
        string Name,
        string BankSummary,
        decimal CurrentBalance,
        bool IsNegative)
    {
        public static CashAccountCardItem From(ApiClient.CashAccountItem item)
        {
            var parts = new[] { item.BankName?.Trim(), item.AccountNumber?.Trim() }
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
            var summary = parts.Length > 0 ? string.Join(" · ", parts) : "Tiền mặt / Nội bộ";
            return new CashAccountCardItem(item.Code, item.Name, summary, item.CurrentBalance, item.CurrentBalance < 0);
        }
    }
}
