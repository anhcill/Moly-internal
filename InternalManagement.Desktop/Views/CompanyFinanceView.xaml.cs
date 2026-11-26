using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class CompanyFinanceView : UserControl
{
    private ApiClient.CompanyFinancialOverviewItem? _overview;

    public CompanyFinanceView()
    {
        InitializeComponent();
        FinanceFromDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, 1, 1);
        FinanceToDatePicker.SelectedDate = DateTime.Today;
        ThemeService.Instance.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.Instance.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppTheme _) => Dispatcher.BeginInvoke(DrawChart);

    public event RoutedEventHandler? RefreshRequested;

    public DateTime? FromDate => FinanceFromDatePicker.SelectedDate;
    public DateTime? ToDateInclusive => FinanceToDatePicker.SelectedDate?.Date.AddDays(1).AddTicks(-1);

    public void SetOverview(ApiClient.CompanyFinancialOverviewItem? overview)
    {
        _overview = overview;
        CompanyFinanceAreasDataGrid.ItemsSource = overview?.Areas ?? Array.Empty<ApiClient.FinancialAreaSummaryItem>();
        CompanyFinanceEmptyText.Visibility = overview is not null && overview.Areas.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        DrawChart();
        if (overview is null) return;

        MetricCompanyIncome.Text = $"{overview.CompanyTotal.TotalIncome:N0} đ";
        MetricCompanyExpense.Text = $"{overview.CompanyTotal.TotalExpense:N0} đ";
        MetricCompanyNetCash.Text = $"{overview.CompanyTotal.NetCashFlow:N0} đ";
        MetricCompanyProfit.Text = $"{overview.CompanyTotal.OperatingProfit:N0} đ";
        UnclassifiedFinanceWarning.Visibility = overview.HasUnclassifiedTransactions
            ? Visibility.Visible : Visibility.Collapsed;
        UnclassifiedFinanceWarningText.Text = overview.HasUnclassifiedTransactions
            ? $"Cần phân loại {overview.UnclassifiedCashFlow.TransactionCount:N0} giao dịch: thu {overview.UnclassifiedCashFlow.TotalIncome:N0} đ, chi {overview.UnclassifiedCashFlow.TotalExpense:N0} đ."
            : string.Empty;
    }

    private void CompanyAreasChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    private void DrawChart()
    {
        if (CompanyAreasChartCanvas is not null)
            FinanceChartRenderer.DrawCompanyAreasComparisonChart(CompanyAreasChartCanvas, _overview?.Areas);
    }

    private void RefreshCompanyFinance_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, e);

    private async void PrintOverview_Click(object sender, RoutedEventArgs e)
    {
        if (_overview is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Chưa có dữ liệu tổng hợp theo mảng để in.", "Tổng hợp tài chính", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var html = FinanceReportExporter.BuildCompanyOverviewHtml(_overview, FromDate, FinanceToDatePicker.SelectedDate);
            await FinanceReportExporter.SaveAndOpenHtmlReportAsync("bao-cao-tong-hop-mang", html);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Lỗi xuất báo cáo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportOverviewExcel_Click(object sender, RoutedEventArgs e)
    {
        if (_overview is null)
        {
            MessageBox.Show(Window.GetWindow(this), "Chưa có dữ liệu tổng hợp theo mảng để xuất Excel.", "Tổng hợp tài chính", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var dialog = new SaveFileDialog
            {
                Title = "Xuất báo cáo tổng hợp theo mảng (Excel)",
                Filter = "Báo cáo Excel (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"TongHopTaiChinhMang_{DateTime.Today:yyyyMMdd}.xlsx"
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            var bytes = FinanceExcelExporter.BuildCompanyOverviewWorkbook(_overview, FromDate, FinanceToDatePicker.SelectedDate);
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            if (MessageBox.Show(Window.GetWindow(this),
                    $"Đã xuất báo cáo thành công:\n{dialog.FileName}\n\nMở tệp Excel ngay?",
                    "Xuất Excel thành công", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), ex.Message, "Lỗi xuất Excel", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
