using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    // ── Day 11: HR & Employees Loaders & Handlers ──

    private EmployeesViewModel EmployeeViewModel => ViewEmployeesContainer.ViewModel!;

    private async Task LoadDepartmentsFilterAsync()
    {
        try
        {
            if (await EmployeeViewModel.LoadDepartmentsAsync())
                SetLoadedStatus("Phòng ban", EmployeeViewModel.Departments.Count);
            else
                SetViewStatus("Không tải được danh sách phòng ban. Vui lòng thử lại.", isError: true);
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được phòng ban: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private async Task LoadEmployeesAsync()
    {
        if (!_isUiReady) return;

        try
        {
            if (await EmployeeViewModel.LoadAsync(_activeSegment))
                SetLoadedStatus("Nhân sự", EmployeeViewModel.Profiles.Count);
            else
                SetViewStatus("Không tải được danh sách nhân sự. Vui lòng thử lại.", isError: true);
        }
        catch (Exception ex)
        {
            SetViewStatus($"Không tải được nhân sự: {ToFriendlyError(ex)}", isError: true);
        }
    }

    private void EmployeeSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUiReady) _ = DebounceSearchAsync(SegmentViewKey("employees-search"), "Đang tìm nhân sự...", LoadEmployeesAsync);
    }

    private void EmployeeDeptFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiReady) _ = DebounceSearchAsync(SegmentViewKey("employees-filter"), "Đang lọc nhân sự...", LoadEmployeesAsync);
    }
    private void RefreshEmployees_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải danh sách nhân sự...", () => LoadEmployeesAsync());

    private async void EmployeesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid ||
            e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(grid, source) is not DataGridRow ||
            grid.SelectedItem is not ApiClient.EmployeeItem employee)
        {
            return;
        }

        await ShowEmployeeDialogAsync(employee);
    }

}
