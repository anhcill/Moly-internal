using System.Windows;
using System.Windows.Controls;
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

    public event RoutedEventHandler? SearchChanged;
    public event RoutedEventHandler? RefreshRequested;

    public void SetViewModel(CustomersViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    private void CustomerSearch_TextChanged(object sender, TextChangedEventArgs e) =>
        SearchChanged?.Invoke(sender, e);

    private void RefreshCustomers_Click(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(sender, e);
}
