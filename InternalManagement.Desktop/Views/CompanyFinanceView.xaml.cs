using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class CompanyFinanceView : UserControl
{
    public CompanyFinanceView()
    {
        InitializeComponent();
        FinanceFromDatePicker.SelectedDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        FinanceToDatePicker.SelectedDate = DateTime.Today;
    }

    public event RoutedEventHandler? RefreshRequested;

    public DateTime? FromDate => FinanceFromDatePicker.SelectedDate;
    public DateTime? ToDateInclusive => FinanceToDatePicker.SelectedDate?.Date.AddDays(1).AddTicks(-1);

    public void SetOverview(ApiClient.CompanyFinancialOverviewItem? overview)
    {
        CompanyFinanceAreasDataGrid.ItemsSource = overview?.Areas ?? Array.Empty<ApiClient.FinancialAreaSummaryItem>();
        CompanyFinanceEmptyText.Visibility = overview is not null && overview.Areas.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        if (overview is null) return;

        MetricCompanyIncome.Text = $"{overview.CompanyTotal.TotalIncome:N0} đ";
        MetricCompanyExpense.Text = $"{overview.CompanyTotal.TotalExpense:N0} đ";
        MetricCompanyNetCash.Text = $"{overview.CompanyTotal.NetCashFlow:N0} đ";
        MetricCompanyProfit.Text = $"{overview.CompanyTotal.OperatingProfit:N0} đ";
        UnclassifiedFinanceWarning.Visibility = overview.HasUnclassifiedTransactions
            ? Visibility.Visible : Visibility.Collapsed;
        UnclassifiedFinanceWarningText.Text = overview.HasUnclassifiedTransactions
            ? $"Cần phân loại {overview.UnclassifiedCashFlow.TransactionCount:N0} giao dịch: thu {overview.UnclassifiedCashFlow.TotalIncome:N0} đ, chi {overview.UnclassifiedCashFlow.TotalExpense:N0} đ. {overview.CalculationNote}"
            : overview.CalculationNote;
    }

    private void RefreshCompanyFinance_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
}
