using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView() => InitializeComponent();

    public EmployeesViewModel? ViewModel { get; private set; }
    public int SelectedTabIndex
    {
        get => EmployeesTabs.SelectedIndex;
        set => EmployeesTabs.SelectedIndex = value;
    }

    public ApiClient.EmployeeItem? SelectedEmployee => SelectedTabIndex switch
    {
        1 => EmployeesProfilesDataGrid.SelectedItem as ApiClient.EmployeeItem,
        2 => EmployeesMissingCvDataGrid.SelectedItem as ApiClient.EmployeeItem,
        3 => EmployeesResignedDataGrid.SelectedItem as ApiClient.EmployeeItem,
        4 => EmployeesBlacklistDataGrid.SelectedItem as ApiClient.EmployeeItem,
        _ => EmployeesWorkingDataGrid.SelectedItem as ApiClient.EmployeeItem
    };

    public void SetViewModel(EmployeesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    public event RoutedEventHandler? BlacklistRequested;
    public event RoutedEventHandler? CreateRequested;
    public event RoutedEventHandler? DeleteRequested;
    public event RoutedEventHandler? EditRequested;
    public event RoutedEventHandler? OpenCvRequested;
    public event RoutedEventHandler? AssignCvFileRequested;
    public event RoutedEventHandler? AssignCvUrlRequested;
    public event RoutedEventHandler? UpdateProfileRequested;
    public event RoutedEventHandler? ReactivateRequested;
    public event RoutedEventHandler? RefreshRequested;
    public event RoutedEventHandler? ResignRequested;
    public event RoutedEventHandler? UpdateBlacklistReasonRequested;
    public event RoutedEventHandler? UpdateResignedReasonRequested;
    public event MouseButtonEventHandler? OpenEmployeeRequested;
    public event SelectionChangedEventHandler? DepartmentFilterChanged;
    public event SelectionChangedEventHandler? TabChanged;
    public event TextChangedEventHandler? SearchChanged;

    private void BlacklistEmployee_Click(object sender, RoutedEventArgs e) => BlacklistRequested?.Invoke(sender, e);
    private void CreateEmployee_Click(object sender, RoutedEventArgs e) => CreateRequested?.Invoke(sender, e);
    private void DeleteEmployee_Click(object sender, RoutedEventArgs e) => DeleteRequested?.Invoke(sender, e);
    private void EditEmployee_Click(object sender, RoutedEventArgs e) => EditRequested?.Invoke(sender, e);
    private void OpenEmployeeCv_Click(object sender, RoutedEventArgs e) => OpenCvRequested?.Invoke(sender, e);
    private void QuickAssignCvFile_Click(object sender, RoutedEventArgs e) => AssignCvFileRequested?.Invoke(sender, e);
    private void QuickAssignCvUrl_Click(object sender, RoutedEventArgs e) => AssignCvUrlRequested?.Invoke(sender, e);
    private void QuickUpdateProfile_Click(object sender, RoutedEventArgs e) => UpdateProfileRequested?.Invoke(sender, e);
    private void ReactivateEmployee_Click(object sender, RoutedEventArgs e) => ReactivateRequested?.Invoke(sender, e);
    private void RefreshEmployees_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(sender, e);
    private void ResignEmployee_Click(object sender, RoutedEventArgs e) => ResignRequested?.Invoke(sender, e);
    private void UpdateBlacklistReason_Click(object sender, RoutedEventArgs e) => UpdateBlacklistReasonRequested?.Invoke(sender, e);
    private void UpdateResignedReason_Click(object sender, RoutedEventArgs e) => UpdateResignedReasonRequested?.Invoke(sender, e);
    private void EmployeesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenEmployeeRequested?.Invoke(sender, e);
    private void EmployeeDeptFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => DepartmentFilterChanged?.Invoke(sender, e);
    private void EmployeesTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => TabChanged?.Invoke(sender, e);
    private void EmployeeSearch_TextChanged(object sender, TextChangedEventArgs e) => SearchChanged?.Invoke(sender, e);
}
