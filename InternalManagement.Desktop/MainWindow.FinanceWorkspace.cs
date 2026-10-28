using System.Windows;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private async Task LoadFinanceWorkspaceAsync()
    {
        await ViewFinanceWorkspaceContainer.LoadAsync(_apiClient);
        SetViewStatus("Đã tải bảng tài chính vận hành");
    }

    private void RefreshFinanceWorkspace_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang tải bảng tài chính vận hành...", LoadFinanceWorkspaceAsync);
}
