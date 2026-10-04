using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.Views;

public partial class InternalDataView : UserControl
{
    public InternalDataView()
    {
        InitializeComponent();
    }

    public event TextChangedEventHandler? SearchChanged;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? CreateRequested;
    public event RoutedEventHandler? EditRequested;
    public event RoutedEventHandler? DeleteRequested;

    public string SearchText => InternalDataSearchBox.Text.Trim();
    public ApiClient.InternalCustomerItem? SelectedCustomer =>
        InternalCustomersDataGrid.SelectedItem as ApiClient.InternalCustomerItem;
    public ApiClient.InternalResourceItem? SelectedResource =>
        InternalResourcesDataGrid.SelectedItem as ApiClient.InternalResourceItem;

    public void Configure(bool isCustomers, string title, string help, bool canManage)
    {
        InternalCustomersPanel.Visibility = isCustomers ? Visibility.Visible : Visibility.Collapsed;
        InternalResourcesPanel.Visibility = isCustomers ? Visibility.Collapsed : Visibility.Visible;
        InternalDataTitleText.Text = title;
        InternalDataHelpText.Text = help;
        CreateInternalDataButton.Content = isCustomers ? "+ Thêm khách hàng" : "+ Thêm tài liệu";
        var actionVisibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        CreateInternalDataButton.Visibility = actionVisibility;
        EditInternalDataButton.Visibility = actionVisibility;
        DeleteInternalDataButton.Visibility = actionVisibility;
    }

    public void SetCustomers(IEnumerable<ApiClient.InternalCustomerItem>? customers)
    {
        var rows = customers?.ToArray() ?? Array.Empty<ApiClient.InternalCustomerItem>();
        InternalCustomersDataGrid.ItemsSource = rows;
        InternalCustomersEmptyText.Visibility = customers is not null && rows.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetResources(IEnumerable<ApiClient.InternalResourceItem>? resources)
    {
        var rows = resources?.ToArray() ?? Array.Empty<ApiClient.InternalResourceItem>();
        InternalResourcesDataGrid.ItemsSource = rows;
        InternalResourcesEmptyText.Visibility = resources is not null && rows.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InternalDataSearch_TextChanged(object sender, TextChangedEventArgs e) => SearchChanged?.Invoke(sender, e);
    private void RefreshInternalData_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private void CreateInternalData_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(sender, e);
    private void EditInternalData_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(sender, e);
    private void DeleteInternalData_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(sender, e);
}
