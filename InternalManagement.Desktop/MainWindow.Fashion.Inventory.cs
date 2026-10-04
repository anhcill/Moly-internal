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

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private void InventorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        InventorySearchPlaceholder.Visibility = string.IsNullOrEmpty(InventorySearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-balances", "Đang tìm tồn kho...", () => LoadInventoryBalancesAsync(InventorySearchBox.Text));
    }

    private void InventoryWarehouseSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || _isInventoryWarehouseSelectionSyncing)
            return;

        _ = RunWithBusyAsync("Đang tải tồn kho theo kho đã chọn...", async () =>
        {
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
        });
    }

    private async void RefreshWarehouses_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải danh mục kho...", LoadWarehousesAsync);
    }

    private async void CreateWarehouseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Thêm kho", WarehouseFields(), out var values)) return;

        var success = await _apiClient.CreateWarehouseAsync(
            values["code"], values["name"], values["address"], values["isActive"] == "true", values["isDefault"] == "true");
        if (success)
        {
            await LoadWarehousesAsync();
            MessageBox.Show("Đã tạo kho. Nếu đây là kho mặc định, phiếu nhập mới sẽ dùng kho này.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể tạo kho. Kiểm tra mã không trùng và quyền Inventory.Adjust.", "Không thể tạo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditWarehouseDialog_Click(object sender, RoutedEventArgs e)
    {
        if (WarehousesDataGrid.SelectedItem is not ApiClient.WarehouseItem warehouse)
        {
            ShowToast("Hãy chọn kho cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa kho — {warehouse.Code}", WarehouseFields(warehouse), out var values)) return;

        var success = await _apiClient.UpdateWarehouseAsync(
            warehouse.Id, values["code"], values["name"], values["address"], values["isActive"] == "true", values["isDefault"] == "true");
        if (success)
        {
            await LoadWarehousesAsync();
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            MessageBox.Show("Đã cập nhật kho.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật kho. Kho mặc định phải đang hoạt động; hãy chọn kho mặc định khác trước khi ngừng dùng kho hiện tại.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteWarehouse_Click(object sender, RoutedEventArgs e)
    {
        if (WarehousesDataGrid.SelectedItem is not ApiClient.WarehouseItem warehouse)
        {
            ShowToast("Hãy chọn kho cần xóa.");
            return;
        }

        if (MessageBox.Show(
                $"Xóa kho '{warehouse.Code} — {warehouse.Name}'?\nChỉ xóa được kho chưa có tồn kho hay chứng từ. Kho đã phát sinh phải Ngừng dùng để giữ lịch sử.",
                "Xác nhận xóa kho", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteWarehouseAsync(warehouse.Id))
        {
            await LoadWarehousesAsync();
            MessageBox.Show("Đã xóa kho chưa phát sinh dữ liệu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa kho mặc định hoặc kho đã có tồn/chứng từ. Hãy chọn Sửa để ngừng dùng kho đó.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void WarehousesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.WarehouseItem)
        {
            EditWarehouseDialog_Click(sender, e);
        }
    }

    private static IReadOnlyList<PromptField> WarehouseFields(ApiClient.WarehouseItem? warehouse = null) => new[]
    {
        new PromptField("code", "Mã kho", warehouse?.Code),
        new PromptField("name", "Tên kho", warehouse?.Name),
        new PromptField("address", "Địa chỉ", warehouse?.Address, IsRequired: false, IsMultiline: true),
        new PromptField("isActive", "Trạng thái", warehouse is null || warehouse.IsActive ? "true" : "false", Options: new[]
        {
            new PromptOption("true", "Đang hoạt động"),
            new PromptOption("false", "Ngừng dùng (giữ lịch sử)")
        }),
        new PromptField("isDefault", "Dùng làm kho mặc định", warehouse?.IsDefault == true ? "true" : "false", Options: new[]
        {
            new PromptOption("false", "Không"),
            new PromptOption("true", "Có — dùng khi lập phiếu nhập")
        })
    };

    private async void RefreshInventoryMovements_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải nhật ký giao dịch kho...", () => LoadInventoryMovementsAsync());
    }

    private async void RefreshSelectedInventoryMovements_Click(object sender, RoutedEventArgs e)
    {
        var balance = InventoryBalancesDataGrid.SelectedItem as ApiClient.InventoryBalanceItem;
        await RunWithBusyAsync(
            balance is null ? "Đang tải toàn bộ nhật ký giao dịch kho..." : $"Đang tải nhật ký của SKU {balance.Sku}...",
            () => LoadInventoryMovementsAsync(balance?.ProductVariantId));
    }

    private void RefreshInventory_Click(object sender, RoutedEventArgs e)
    {
        _ = RunWithBusyAsync("Đang tải tồn kho...", () => LoadInventoryBalancesAsync(InventorySearchBox.Text));
    }

    private async void StockAdjustmentDialog_Click(object sender, RoutedEventArgs e)
    {
        if (InventoryBalancesDataGrid.SelectedItem is not ApiClient.InventoryBalanceItem balance)
        {
            MessageBox.Show("Vui lòng chọn 1 dòng tồn kho trong bảng trên để điều chỉnh kiểm kê.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Điều chỉnh tồn kho — {balance.Sku}", new[]
        {
            new PromptField("delta", "Biến động số lượng (+ nhập thêm, - giảm đi)"),
            new PromptField("notes", "Lý do kiểm kê")
        }, out var values)) return;

        if (!int.TryParse(values["delta"], out var delta) || delta == 0)
        {
            MessageBox.Show("Biến động phải là số nguyên khác 0.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var success = await _apiClient.AdjustStockAsync(balance.ProductVariantId, delta, values["notes"], SelectedInventoryWarehouseId);
        if (success)
        {
            MessageBox.Show($"Đã điều chỉnh {delta:+#;-#;0} cho SKU '{balance.Sku}'. Sổ giao dịch đã ghi nhận.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
        }
        else
        {
            MessageBox.Show("Điều chỉnh tồn kho thất bại. Vui lòng kiểm tra quyền Inventory.Adjust.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

}
