using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop.Views;

public partial class CustomersView : UserControl
{
    public CustomersView()
    {
        InitializeComponent();
    }

    public CustomersViewModel? ViewModel { get; private set; }
    public string SearchText => CustomerSearchBox.Text;

    public CscaStudentGroupItem? SelectedPaidStudentGroup =>
        PaidStudentsDataGrid.SelectedItem as CscaStudentGroupItem;

    public CscaStudentGroupItem? SelectedTrialStudentGroup =>
        TrialStudentsDataGrid.SelectedItem as CscaStudentGroupItem;

    // For backwards compatibility:
    public ApiClient.CscaStudentDirectoryItem? SelectedStudent =>
        SelectedPaidStudentGroup?.PrimaryEnrollment ?? SelectedTrialStudentGroup?.PrimaryEnrollment;

    public event RoutedEventHandler? SearchChanged;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? CreateStudentRequested;
    public event RoutedEventHandler? EditStudentRequested;
    public event RoutedEventHandler? DeleteStudentRequested;
    public event RoutedEventHandler? ConvertTrialRequested;

    public void SetViewModel(CustomersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void CustomerSearch_TextChanged(object sender, TextChangedEventArgs e) =>
        SearchChanged?.Invoke(sender, e);

    private void RefreshCustomers_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(sender, e);

    private void AddStudent_Click(object sender, RoutedEventArgs e) =>
        CreateStudentRequested?.Invoke(sender, e);

    // Paid students actions
    private void EditPaidStudent_Click(object sender, RoutedEventArgs e) =>
        EditStudentRequested?.Invoke(sender, e);

    private void DeletePaidStudent_Click(object sender, RoutedEventArgs e) =>
        DeleteStudentRequested?.Invoke(sender, e);

    private void EditPaidStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CscaStudentGroupItem item)
        {
            PaidStudentsDataGrid.SelectedItem = item;
        }
        EditStudentRequested?.Invoke(sender, e);
    }

    private void DeletePaidStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CscaStudentGroupItem item)
        {
            PaidStudentsDataGrid.SelectedItem = item;
        }
        DeleteStudentRequested?.Invoke(sender, e);
    }

    private void PaidStudentsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPaidStudentGroup != null)
        {
            EditStudentRequested?.Invoke(sender, e);
        }
    }

    // Trial students actions
    private void EditTrialStudent_Click(object sender, RoutedEventArgs e) =>
        EditStudentRequested?.Invoke(sender, e);

    private void DeleteTrialStudent_Click(object sender, RoutedEventArgs e) =>
        DeleteStudentRequested?.Invoke(sender, e);

    private void EditTrialStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CscaStudentGroupItem item)
        {
            TrialStudentsDataGrid.SelectedItem = item;
        }
        EditStudentRequested?.Invoke(sender, e);
    }

    private void DeleteTrialStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CscaStudentGroupItem item)
        {
            TrialStudentsDataGrid.SelectedItem = item;
        }
        DeleteStudentRequested?.Invoke(sender, e);
    }

    private void TrialStudentsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedTrialStudentGroup != null)
        {
            EditStudentRequested?.Invoke(sender, e);
        }
    }

    private void ConvertSelectedTrialStudent_Click(object sender, RoutedEventArgs e) =>
        ConvertTrialRequested?.Invoke(sender, e);

    private void ConvertTrialStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CscaStudentGroupItem item)
        {
            TrialStudentsDataGrid.SelectedItem = item;
        }
        ConvertTrialRequested?.Invoke(sender, e);
    }
}
