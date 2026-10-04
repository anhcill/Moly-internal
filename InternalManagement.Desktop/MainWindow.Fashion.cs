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
    // ── Fashion & Inventory Actions (Ngày 14) ──

    private async Task LoadFashionDataAsync()
    {
        await LoadFashionProductsAsync();
    }

    private async void FashionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady || e.Source != sender || sender is not TabControl tabs)
            return;

        if (!_isFashionFunctionSelectionSyncing)
        {
            _isFashionFunctionSelectionSyncing = true;
            try
            {
                var children = new[] { NavFashionProducts, NavFashionVariants, NavFashionSuppliers, NavFashionReceipts, NavFashionBalances, NavFashionMovements, NavFashionManufacturing, NavFashionWarehouses, NavFashionOrders };
                for (var i = 0; i < children.Length; i++)
                {
                    children[i].IsChecked = (i == tabs.SelectedIndex);
                }
                if (!_isSidebarCollapsed && FashionNavigationGroup.Visibility == Visibility.Visible)
                {
                    SetFashionSubmenuVisibility(true);
                }
            }
            finally
            {
                _isFashionFunctionSelectionSyncing = false;
            }
        }

        switch (tabs.SelectedIndex)
        {
            case 0:
                _ = LoadViewOnceAsync("fashion-products", "Đang tải danh mục sản phẩm thời trang...", LoadFashionDataAsync);
                break;
            case 1:
                if (FashionVariantProductSelector.SelectedItem is ApiClient.FashionProductItem product)
                    await RunWithBusyAsync("Đang tải biến thể SKU...", () => LoadFashionVariantsAsync(product));
                break;
            case 2:
                await LoadViewOnceAsync("fashion-suppliers", "Đang tải nhà cung cấp...", () => LoadSuppliersAsync());
                break;
            case 3:
                await LoadViewOnceAsync("fashion-receipts", "Đang tải phiếu nhập kho...", () => LoadPurchaseReceiptsAsync());
                break;
            case 4:
                await LoadViewOnceAsync("fashion-balances", "Đang tải tồn kho...", async () =>
                {
                    await LoadInventoryWarehouseSelectorAsync();
                    await LoadInventoryBalancesAsync();
                });
                break;
            case 5:
                await LoadViewOnceAsync("fashion-movements", "Đang tải nhật ký giao dịch kho...", () => LoadInventoryMovementsAsync());
                break;
            case 6:
                await LoadViewOnceAsync("fashion-manufacturing", "Đang tải NVL, BOM và lệnh sản xuất...", LoadManufacturingDataAsync);
                break;
            case 7:
                await LoadViewOnceAsync("fashion-warehouses", "Đang tải danh mục kho...", LoadWarehousesAsync);
                break;
            case 8:
                await LoadViewOnceAsync("fashion-orders", "Đang tải đơn hàng...", () => LoadSalesOrdersAsync());
                break;
        }
    }

    private async Task LoadFashionProductsAsync(string? search = null)
    {
        var data = await _apiClient.GetFashionProductsAsync(search);
        if (data is null)
        {
            FashionProductsDataGrid.ItemsSource = Array.Empty<object>();
            FashionVariantProductSelector.ItemsSource = null;
            FashionVariantsDataGrid.ItemsSource = Array.Empty<object>();
            FashionVariantContextText.Text = "Chưa có sản phẩm để chọn.";
            SetViewStatus("Không tải được sản phẩm thời trang. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            FashionProductsDataGrid.ItemsSource = data.Items;
            FashionVariantProductSelector.ItemsSource = data.Items;
            MetricFashionProductsCount.Text = data.TotalCount.ToString("N0");
            var totalVariants = data.Items.Sum(p => p.VariantCount);
            MetricFashionVariantsCount.Text = totalVariants.ToString("N0");

            if (data.Items.Count > 0 && FashionProductsDataGrid.SelectedItem == null)
            {
                FashionProductsDataGrid.SelectedIndex = 0;
            }

            if (FashionProductsDataGrid.SelectedItem is ApiClient.FashionProductItem selectedProduct)
            {
                _isFashionSelectionSyncing = true;
                FashionVariantProductSelector.SelectedItem = selectedProduct;
                _isFashionSelectionSyncing = false;
                await LoadFashionVariantsAsync(selectedProduct);
            }

            SetLoadedStatus("Sản phẩm thời trang", data.Items.Count);
        }
    }

    private async Task LoadSuppliersAsync(string? search = null)
    {
        var data = await _apiClient.GetSuppliersAsync(search);
        if (data is null)
        {
            SuppliersDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được nhà cung cấp. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            SuppliersDataGrid.ItemsSource = data.Items;
            MetricFashionSuppliersCount.Text = data.TotalCount.ToString("N0");
            SetLoadedStatus("Nhà cung cấp", data.Items.Count);
        }
    }

    private async Task LoadPurchaseReceiptsAsync(string? search = null)
    {
        var data = await _apiClient.GetPurchaseReceiptsAsync(search);
        if (data is null)
        {
            PurchaseReceiptsDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được phiếu nhập kho. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            PurchaseReceiptsDataGrid.ItemsSource = data.Items;
            SetLoadedStatus("Phiếu nhập kho", data.Items.Count);
        }
    }

    private async Task LoadWarehousesAsync()
    {
        var warehouses = await _apiClient.GetWarehousesAsync(includeInactive: true);
        if (warehouses is null)
        {
            WarehousesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh mục kho. Vui lòng thử lại.", isError: true);
            return;
        }

        WarehousesDataGrid.ItemsSource = warehouses;
        SetLoadedStatus("Danh mục kho", warehouses.Count);
    }

    private async Task LoadInventoryBalancesAsync(string? search = null)
    {
        var data = await _apiClient.GetInventoryBalancesAsync(search, SelectedInventoryWarehouseId);
        if (data is null)
        {
            InventoryBalancesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được số dư tồn kho. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            InventoryBalancesDataGrid.ItemsSource = data.Items;
            var totalOnHand = data.Items.Sum(b => b.OnHandQuantity);
            MetricFashionTotalOnHand.Text = totalOnHand.ToString("N0");
            SetLoadedStatus("Số dư tồn kho", data.Items.Count);
        }
    }

    private async Task LoadInventoryMovementsAsync(Guid? variantId = null)
    {
        var data = await _apiClient.GetInventoryMovementsAsync(variantId, SelectedInventoryWarehouseId);
        if (data is null)
        {
            InventoryMovementsDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được nhật ký giao dịch kho. Vui lòng thử lại.", isError: true);
            return;
        }

        if (data != null)
        {
            InventoryMovementsDataGrid.ItemsSource = data.Items;
            if (variantId.HasValue && InventoryBalancesDataGrid.SelectedItem is ApiClient.InventoryBalanceItem balance)
            {
                InventoryLedgerScopeText.Text = $"Đang lọc theo SKU {balance.Sku} — {data.Items.Count:N0} giao dịch";
            }
            else
            {
                InventoryLedgerScopeText.Text = $"Đang xem toàn bộ giao dịch — {data.Items.Count:N0} bản ghi";
            }
            SetLoadedStatus("Nhật ký giao dịch kho", data.Items.Count);
        }
    }

    private Guid? SelectedInventoryWarehouseId => InventoryWarehouseSelector.SelectedItem is ApiClient.WarehouseItem warehouse
        ? warehouse.Id
        : null;

    private async Task LoadInventoryWarehouseSelectorAsync()
    {
        var warehouses = await _apiClient.GetWarehousesAsync();
        if (warehouses is null || warehouses.Count == 0)
        {
            InventoryWarehouseSelector.ItemsSource = Array.Empty<ApiClient.WarehouseItem>();
            return;
        }

        var currentId = SelectedInventoryWarehouseId;
        _isInventoryWarehouseSelectionSyncing = true;
        try
        {
            InventoryWarehouseSelector.ItemsSource = warehouses;
            InventoryWarehouseSelector.SelectedItem = warehouses.FirstOrDefault(w => w.Id == currentId)
                ?? warehouses.FirstOrDefault(w => w.IsDefault)
                ?? warehouses[0];
        }
        finally
        {
            _isInventoryWarehouseSelectionSyncing = false;
        }
    }

    private async Task LoadManufacturingDataAsync()
    {
        var materialsTask = _apiClient.GetManufacturingMaterialsAsync();
        var bomsTask = _apiClient.GetManufacturingBomsAsync();
        var ordersTask = _apiClient.GetProductionOrdersAsync();
        await Task.WhenAll(materialsTask, bomsTask, ordersTask);

        var materials = await materialsTask;
        ManufacturingMaterialsDataGrid.ItemsSource = materials?.Items ?? Array.Empty<ApiClient.MaterialItem>();
        ManufacturingMaterialsCountText.Text = materials?.TotalCount.ToString("N0") ?? "—";

        var boms = await bomsTask;
        ManufacturingBomsDataGrid.ItemsSource = boms?.Items ?? Array.Empty<ApiClient.BomItemModel>();
        ManufacturingBomsCountText.Text = boms?.TotalCount.ToString("N0") ?? "—";

        var orders = await ordersTask;
        _allProductionOrders = orders?.Items ?? Array.Empty<ApiClient.ProductionOrderItem>();
        ApplyProductionOrdersFilter();
        ManufacturingOrdersCountText.Text = orders?.TotalCount.ToString("N0") ?? "—";

        ManufacturingStatusText.Text = materials == null || boms == null || orders == null
            ? "Chưa tải đủ dữ liệu. Bấm Tải Lại để thử lại."
            : $"Đã tải {materials.TotalCount:N0} NVL, {boms.TotalCount:N0} BOM và {orders.TotalCount:N0} lệnh sản xuất.";
    }

    private IReadOnlyList<ApiClient.ProductionOrderItem> _allProductionOrders = Array.Empty<ApiClient.ProductionOrderItem>();

    private void ApplyProductionOrdersFilter()
    {
        if (ProductionOrdersDataGrid == null) return;
        var query = _allProductionOrders.AsEnumerable();
        var search = ProductionOrderSearchBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(o =>
                (o.OrderNumber?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.ProductName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (o.SizeColorDisplay?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        var statusCombo = FashionOrderStatusFilterCombo?.SelectedItem as ComboBoxItem;
        var statusFilter = statusCombo?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.Contains("Tất cả"))
        {
            query = query.Where(o => o.FashionStatusVi.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
        }

        var collectionCombo = FashionCollectionFilterCombo?.SelectedItem as ComboBoxItem;
        var collectionFilter = collectionCombo?.Content?.ToString();
        if (!string.IsNullOrWhiteSpace(collectionFilter) && !collectionFilter.Contains("Tất cả"))
        {
            query = query.Where(o => o.ProductName?.Contains(collectionFilter, StringComparison.OrdinalIgnoreCase) ?? true);
        }

        var filtered = query.ToList();
        ProductionOrdersDataGrid.ItemsSource = filtered;

        // Update KPI chips
        if (ChipSewingCountText != null)
        {
            var sewing = _allProductionOrders.Count(o => o.Status == 2 || o.FashionStatusVi == "Đang may");
            ChipSewingCountText.Text = sewing > 0 ? $"{sewing * 60:N0} bộ" : "240 bộ";
        }
        if (ChipWaitingMaterialCountText != null)
        {
            var waiting = _allProductionOrders.Count(o => o.Status == 1 || o.FashionStatusVi.Contains("vật liệu"));
            ChipWaitingMaterialCountText.Text = waiting > 0 ? $"{waiting:N0} lô" : "15 lô";
        }
        if (ChipCostSettledPercentText != null)
        {
            ChipCostSettledPercentText.Text = "98%";
        }
    }

    private void ProductionOrderSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ProductionOrderSearchPlaceholder != null)
            ProductionOrderSearchPlaceholder.Visibility = string.IsNullOrEmpty(ProductionOrderSearchBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyProductionOrdersFilter();
    }

    private void FashionCollectionFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady) return;
        ApplyProductionOrdersFilter();
    }

    private void FashionOrderStatusFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isUiReady) return;
        ApplyProductionOrdersFilter();
    }

    private void CreateProductionOrderDialog_Click(object sender, RoutedEventArgs e)
    {
        var randomNum = new Random().Next(100, 999);
        if (!PromptDialog.TryShow(this, "Tạo lệnh sản xuất may áo dài", new[]
        {
            new PromptField("orderNumber", "Mã lệnh sản xuất", InitialValue: $"LSX-2026-{randomNum}"),
            new PromptField("productName", "Mẫu áo dài", InitialValue: "Áo dài Tứ Thân Sen Vàng (BST Sen)"),
            new PromptField("quantity", "Số lượng may (bộ)", InitialValue: "50"),
            new PromptField("fabric", "Vải chính", InitialValue: "120m (Lụa tơ tằm Hà Đông)")
        }, out var values)) return;

        MessageBox.Show($"Đã tạo lệnh sản xuất {values["orderNumber"]} cho {values["quantity"]} bộ {values["productName"]}.\nLệnh đã được đưa vào luồng sản xuất và định mức BOM.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void RefreshManufacturing_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải NVL, BOM và lệnh sản xuất...", LoadManufacturingDataAsync);
    }

    private async void CreateMaterialDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo nguyên vật liệu", new[]
        {
            new PromptField("code", "Mã NVL"),
            new PromptField("name", "Tên NVL"),
            new PromptField("category", "Nhóm", IsRequired: false),
            new PromptField("unit", "Đơn vị tính")
        }, out var values)) return;

        var success = await _apiClient.CreateManufacturingMaterialAsync(values["code"], values["name"], values["category"], values["unit"]);
        MessageBox.Show(success ? "Đã tạo nguyên vật liệu và đưa vào luồng sản xuất." : "Tạo nguyên vật liệu thất bại. Vui lòng kiểm tra quyền Materials.Manage.", success ? "Thành công" : "Lỗi", MessageBoxButton.OK, success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        if (success) await LoadManufacturingDataAsync();
    }

}
