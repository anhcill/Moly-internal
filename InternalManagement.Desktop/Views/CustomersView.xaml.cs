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

    public ApiClient.CscaStudentDirectoryItem? SelectedStudent =>
        StudentsDataGrid.SelectedItem as ApiClient.CscaStudentDirectoryItem;

    public event RoutedEventHandler? SearchChanged;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? CreateStudentRequested;
    public event RoutedEventHandler? EditStudentRequested;
    public event RoutedEventHandler? DeleteStudentRequested;

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

    private void EditStudent_Click(object sender, RoutedEventArgs e) =>
        EditStudentRequested?.Invoke(sender, e);

    private void DeleteStudent_Click(object sender, RoutedEventArgs e) =>
        DeleteStudentRequested?.Invoke(sender, e);

    private void EditStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ApiClient.CscaStudentDirectoryItem item)
        {
            StudentsDataGrid.SelectedItem = item;
        }
        EditStudentRequested?.Invoke(sender, e);
    }

    private void DeleteStudentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is ApiClient.CscaStudentDirectoryItem item)
        {
            StudentsDataGrid.SelectedItem = item;
        }
        DeleteStudentRequested?.Invoke(sender, e);
    }

    private void StudentsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedStudent != null)
        {
            EditStudentRequested?.Invoke(sender, e);
        }
    }
}
