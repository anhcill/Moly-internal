using System.Windows;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private async Task LoadFinanceWorkspaceAsync()
    {
        await ViewFinanceWorkspaceContainer.LoadAsync(_apiClient);
        SetViewStatus("Đã tải bảng tài chính vận hành");
    }

    private async Task LoadCashAccountsAndAuditAsync()
    {
        await ViewCashAccountsAuditContainer.LoadAsync(_apiClient);
    }

    private void RefreshFinanceWorkspace_Click(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang đối soát toàn bộ dữ liệu tài chính...", LoadFinanceHubAsync);

    private async Task LoadFinanceHubAsync()
    {
        await LoadCompanyFinanceAsync();
        await LoadFinanceWorkspaceAsync();
        await LoadCashAccountsAndAuditAsync();
        await LoadCostBreakEvenAsync();
    }

    private void FinanceWorkspaceDataChanged(object sender, RoutedEventArgs e)
        => _ = RunWithBusyAsync("Đang đồng nhất các tab tài chính...", async () =>
        {
            await LoadCompanyFinanceAsync();
            if (!ReferenceEquals(sender, ViewFinanceWorkspaceContainer))
                await LoadFinanceWorkspaceAsync();
            if (!ReferenceEquals(sender, ViewCashAccountsAuditContainer))
                await LoadCashAccountsAndAuditAsync();
            await LoadCostBreakEvenAsync();
        });
}
