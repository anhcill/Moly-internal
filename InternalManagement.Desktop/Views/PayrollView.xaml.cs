using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace InternalManagement.Desktop.Views;

public partial class PayrollView : UserControl
{
    public PayrollView() => InitializeComponent();

    public event RoutedEventHandler? AddPayrollAdjustmentRequested;
    public event RoutedEventHandler? ApprovePayrollRequested;
    public event RoutedEventHandler? CalculatePayrollRequested;
    public event RoutedEventHandler? CancelPayrollRequested;
    public event RoutedEventHandler? CreatePayrollPeriodRequested;
    public event RoutedEventHandler? DeleteSelectedPayrollAdjustmentRequested;
    public event RoutedEventHandler? EditPayrollBaseSalaryRequested;
    public event RoutedEventHandler? ExportPayrollExcelRequested;
    public event RoutedEventHandler? MarkPaidRequested;
    public event RoutedEventHandler? OpenPayrollWorkEntriesRequested;
    public event RoutedEventHandler? PublishPayrollRequested;
    public event RoutedEventHandler? RefreshPayrollRequested;
    public event RoutedEventHandler? RefreshPayrollMonthlySummaryRequested;
    public event RoutedEventHandler? SubmitReviewRequested;
    public event RoutedEventHandler? ViewPayslipDetailRequested;
    public event MouseButtonEventHandler? PayrollAdjustmentEmployeesDoubleClickRequested;
    public event MouseButtonEventHandler? PayslipsDataGridDoubleClickRequested;
    public event SelectionChangedEventHandler? PayrollAdjustmentEmployeesSelectionChanged;
    public event SelectionChangedEventHandler? PayrollAdjustmentsSelectionChanged;
    public event SelectionChangedEventHandler? PayrollDeptFilterSelectionChanged;
    public event SelectionChangedEventHandler? PayrollFunctionTabsSelectionChanged;
    public event SelectionChangedEventHandler? PayrollMonthlySummarySelectionChanged;
    public event SelectionChangedEventHandler? PayrollPeriodSelectorSelectionChanged;
    public event SelectionChangedEventHandler? PayrollSummaryYearSelectionChanged;
    public event TextChangedEventHandler? PayrollSearchTextChanged;

    private void AddPayrollAdjustment_Click(object sender, RoutedEventArgs e) => AddPayrollAdjustmentRequested?.Invoke(sender, e);
    private void ApprovePayroll_Click(object sender, RoutedEventArgs e) => ApprovePayrollRequested?.Invoke(sender, e);
    private void CalculatePayroll_Click(object sender, RoutedEventArgs e) => CalculatePayrollRequested?.Invoke(sender, e);
    private void CancelPayroll_Click(object sender, RoutedEventArgs e) => CancelPayrollRequested?.Invoke(sender, e);
    private void CreatePayrollPeriod_Click(object sender, RoutedEventArgs e) => CreatePayrollPeriodRequested?.Invoke(sender, e);
    private void DeleteSelectedPayrollAdjustment_Click(object sender, RoutedEventArgs e) => DeleteSelectedPayrollAdjustmentRequested?.Invoke(sender, e);
    private void EditPayrollBaseSalary_Click(object sender, RoutedEventArgs e) => EditPayrollBaseSalaryRequested?.Invoke(sender, e);
    private void ExportPayrollExcel_Click(object sender, RoutedEventArgs e) => ExportPayrollExcelRequested?.Invoke(sender, e);
    private void MarkPaid_Click(object sender, RoutedEventArgs e) => MarkPaidRequested?.Invoke(sender, e);
    private void OpenPayrollWorkEntries_Click(object sender, RoutedEventArgs e) => OpenPayrollWorkEntriesRequested?.Invoke(sender, e);
    private void PublishPayroll_Click(object sender, RoutedEventArgs e) => PublishPayrollRequested?.Invoke(sender, e);
    private void RefreshPayroll_Click(object sender, RoutedEventArgs e) => RefreshPayrollRequested?.Invoke(sender, e);
    private void RefreshPayrollMonthlySummary_Click(object sender, RoutedEventArgs e) => RefreshPayrollMonthlySummaryRequested?.Invoke(sender, e);
    private void SubmitReview_Click(object sender, RoutedEventArgs e) => SubmitReviewRequested?.Invoke(sender, e);
    private void ViewPayslipDetail_Click(object sender, RoutedEventArgs e) => ViewPayslipDetailRequested?.Invoke(sender, e);
    private void PayrollAdjustmentEmployees_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PayrollAdjustmentEmployeesDoubleClickRequested?.Invoke(sender, e);
    private void PayslipsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => PayslipsDataGridDoubleClickRequested?.Invoke(sender, e);
    private void PayrollAdjustmentEmployees_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollAdjustmentEmployeesSelectionChanged?.Invoke(sender, e);
    private void PayrollAdjustments_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollAdjustmentsSelectionChanged?.Invoke(sender, e);
    private void PayrollDeptFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollDeptFilterSelectionChanged?.Invoke(sender, e);
    private void PayrollFunctionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollFunctionTabsSelectionChanged?.Invoke(sender, e);
    private void PayrollMonthlySummary_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollMonthlySummarySelectionChanged?.Invoke(sender, e);
    private void PayrollPeriodSelector_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollPeriodSelectorSelectionChanged?.Invoke(sender, e);
    private void PayrollSummaryYear_SelectionChanged(object sender, SelectionChangedEventArgs e) => PayrollSummaryYearSelectionChanged?.Invoke(sender, e);
    private void PayrollSearch_TextChanged(object sender, TextChangedEventArgs e) => PayrollSearchTextChanged?.Invoke(sender, e);
}
