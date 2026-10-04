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
    private void FashionProductSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        FashionProductSearchPlaceholder.Visibility = string.IsNullOrEmpty(FashionProductSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-products", "Đang tìm sản phẩm thời trang...", () => LoadFashionProductsAsync(FashionProductSearchBox.Text));
    }

    private void RefreshFashionProducts_Click(object sender, RoutedEventArgs e) => _ = RunWithBusyAsync("Đang tải sản phẩm thời trang...", () => LoadFashionProductsAsync(FashionProductSearchBox.Text));

    private async void FashionProductsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is ApiClient.FashionProductItem prod)
        {
            _isFashionSelectionSyncing = true;
            FashionVariantProductSelector.SelectedItem = prod;
            _isFashionSelectionSyncing = false;
            await LoadFashionVariantsAsync(prod);
        }
        else
        {
            FashionVariantsDataGrid.ItemsSource = null;
            FashionVariantContextText.Text = "Chưa chọn sản phẩm. Vào tab Sản phẩm để chọn một dòng.";
        }
    }

    private async void FashionVariantProductSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isFashionSelectionSyncing || FashionVariantProductSelector.SelectedItem is not ApiClient.FashionProductItem prod)
        {
            return;
        }

        _isFashionSelectionSyncing = true;
        FashionProductsDataGrid.SelectedItem = prod;
        _isFashionSelectionSyncing = false;
        await LoadFashionVariantsAsync(prod);
    }

    private async Task LoadFashionVariantsAsync(ApiClient.FashionProductItem product)
    {
        FashionVariantContextText.Text = $"Đang xem SKU của {product.Code} — {product.Name}";
        var detail = await _apiClient.GetFashionProductDetailAsync(product.Id);
        if (detail is null)
        {
            FashionVariantsDataGrid.ItemsSource = Array.Empty<object>();
            FashionVariantContextText.Text = $"Không tải được SKU của {product.Code}. Vui lòng thử lại.";
            return;
        }

        FashionVariantsDataGrid.ItemsSource = detail.Variants;
        FashionVariantContextText.Text = $"{product.Code} — {product.Name} · {detail.Variants.Count:N0} biến thể";
    }

    private async void RefreshFashionVariants_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantProductSelector.SelectedItem is ApiClient.FashionProductItem product)
        {
            await RunWithBusyAsync("Đang tải biến thể SKU...", () => LoadFashionVariantsAsync(product));
            return;
        }

        await RunWithBusyAsync("Đang tải sản phẩm thời trang...", () => LoadFashionProductsAsync());
    }

    private async void CreateProductDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo sản phẩm thời trang", new[]
        {
            new PromptField("code", "Mã sản phẩm"),
            new PromptField("name", "Tên sản phẩm"),
            new PromptField("category", "Danh mục", "Thời trang"),
            new PromptField("description", "Mô tả", IsRequired: false)
        }, out var values)) return;

        var success = await _apiClient.CreateFashionProductAsync(
            values["code"], values["name"], values["category"], values["description"]);
        if (success)
        {
            MessageBox.Show($"Đã tạo sản phẩm '{values["code"]}' và lưu vào database.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadFashionProductsAsync();
        }
        else
        {
            MessageBox.Show("Tạo sản phẩm thất bại. Bạn cần có quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditFashionProductDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is not ApiClient.FashionProductItem product)
        {
            ShowToast("Hãy chọn sản phẩm cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa sản phẩm — {product.Code}", ProductFields(product), out var values)) return;

        var isActive = values["isActive"] == "true";
        var success = await _apiClient.UpdateFashionProductAsync(
            product.Id, values["code"], values["name"], values["category"], values["description"], isActive);
        if (success)
        {
            await LoadFashionProductsAsync(FashionProductSearchBox.Text);
            MessageBox.Show("Đã cập nhật sản phẩm.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật sản phẩm. Kiểm tra mã không trùng và quyền Products.Manage.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteFashionProduct_Click(object sender, RoutedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is not ApiClient.FashionProductItem product)
        {
            ShowToast("Hãy chọn sản phẩm cần xóa.");
            return;
        }

        if (MessageBox.Show(
                $"Xóa sản phẩm '{product.Code} — {product.Name}'?\nChỉ xóa được sản phẩm chưa có nhập kho, đơn hàng hoặc tồn kho. Nếu đã phát sinh, hãy dùng Sửa để chuyển sang Ngừng dùng.",
                "Xác nhận xóa sản phẩm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteFashionProductAsync(product.Id))
        {
            FashionVariantsDataGrid.ItemsSource = null;
            await LoadFashionProductsAsync(FashionProductSearchBox.Text);
            MessageBox.Show("Đã xóa sản phẩm chưa phát sinh dữ liệu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa sản phẩm đã có SKU phát sinh tồn kho, phiếu nhập hoặc đơn hàng. Hãy chọn Sửa và chuyển sang Ngừng dùng.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FashionProductsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.FashionProductItem)
        {
            EditFashionProductDialog_Click(sender, e);
        }
    }

    private static IReadOnlyList<PromptField> ProductFields(ApiClient.FashionProductItem? product = null) => new[]
    {
        new PromptField("code", "Mã sản phẩm", product?.Code),
        new PromptField("name", "Tên sản phẩm", product?.Name),
        new PromptField("category", "Danh mục", product?.Category ?? "Thời trang", IsRequired: false),
        new PromptField("description", "Mô tả", product?.Description, IsRequired: false, IsMultiline: true),
        new PromptField("isActive", "Trạng thái", product is null || product.IsActive ? "true" : "false", Options: new[]
        {
            new PromptOption("true", "Đang dùng"),
            new PromptOption("false", "Ngừng dùng (giữ lịch sử)")
        })
    };

    private async void AddVariantDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionProductsDataGrid.SelectedItem is not ApiClient.FashionProductItem prod)
        {
            MessageBox.Show("Vui lòng chọn 1 sản phẩm trong bảng để thêm biến thể SKU.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Thêm SKU cho {prod.Name}", new[]
        {
            new PromptField("sku", "Mã SKU"),
            new PromptField("barcode", "Mã vạch", IsRequired: false),
            new PromptField("color", "Màu", IsRequired: false),
            new PromptField("size", "Cỡ", IsRequired: false),
            new PromptField("cost", "Giá vốn"),
            new PromptField("selling", "Giá bán"),
            new PromptField("sourcingType", "Nguồn hàng", Options: new[]
            {
                new PromptOption("0", "MAKE — Tự sản xuất"),
                new PromptOption("1", "BUY — Mua ngoài")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["cost"], out var costPrice) || !decimal.TryParse(values["selling"], out var sellingPrice) || costPrice < 0 || sellingPrice < 0)
        {
            MessageBox.Show("Giá vốn và giá bán phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(values["sourcingType"], out var sourcingType) || sourcingType is < 0 or > 1)
        {
            MessageBox.Show("Nguồn hàng không hợp lệ. Chọn MAKE hoặc BUY.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var success = await _apiClient.AddProductVariantAsync(prod.Id, values["sku"], values["barcode"], values["color"], values["size"], costPrice, sellingPrice, sourcingType);
        if (success)
        {
            MessageBox.Show($"Đã thêm SKU '{values["sku"]}' và lưu vào database.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadFashionVariantsAsync(prod);
            await LoadFashionProductsAsync();
        }
        else
        {
            MessageBox.Show("Thêm biến thể SKU thất bại. Bạn cần có quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditFashionVariantDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantsDataGrid.SelectedItem is not ApiClient.FashionVariantItem variant)
        {
            ShowToast("Hãy chọn SKU cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa SKU — {variant.Sku}", VariantFields(variant), out var values)) return;

        if (!TryReadVariantValues(values, out var costPrice, out var sellingPrice, out var sourcingType))
            return;

        var success = await _apiClient.UpdateProductVariantAsync(
            variant.Id, values["sku"], values["barcode"], values["color"], values["size"],
            costPrice, sellingPrice, sourcingType, values["isActive"] == "true");
        if (success)
        {
            await RefreshSelectedFashionProductAsync();
            MessageBox.Show("Đã cập nhật SKU.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật SKU. SKU đã có phát sinh không thể đổi nguồn hàng; kiểm tra mã không trùng và quyền Products.Manage.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteFashionVariant_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantsDataGrid.SelectedItem is not ApiClient.FashionVariantItem variant)
        {
            ShowToast("Hãy chọn SKU cần xóa.");
            return;
        }

        if (MessageBox.Show(
                $"Xóa SKU '{variant.Sku}'?\nChỉ xóa được SKU chưa có tồn kho, phiếu nhập hoặc đơn hàng. Nếu đã phát sinh, hãy dùng Sửa để chuyển sang Ngừng dùng.",
                "Xác nhận xóa SKU", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteProductVariantAsync(variant.Id))
        {
            await RefreshSelectedFashionProductAsync();
            MessageBox.Show("Đã xóa SKU chưa phát sinh dữ liệu.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa SKU đã có phát sinh. Hãy chuyển SKU sang Ngừng dùng để vẫn giữ lịch sử.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void FashionVariantsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.FashionVariantItem)
        {
            EditFashionVariantDialog_Click(sender, e);
        }
    }

    private static IReadOnlyList<PromptField> VariantFields(ApiClient.FashionVariantItem variant) => new[]
    {
        new PromptField("sku", "Mã SKU", variant.Sku),
        new PromptField("barcode", "Mã vạch", variant.Barcode, IsRequired: false),
        new PromptField("color", "Màu", variant.Color, IsRequired: false),
        new PromptField("size", "Cỡ", variant.Size, IsRequired: false),
        new PromptField("cost", "Giá vốn", variant.CostPrice.ToString("0.##", CultureInfo.InvariantCulture)),
        new PromptField("selling", "Giá bán", variant.SellingPrice.ToString("0.##", CultureInfo.InvariantCulture)),
        new PromptField("sourcingType", "Nguồn hàng", variant.SourcingType.ToString(CultureInfo.InvariantCulture), Options: new[]
        {
            new PromptOption("0", "MAKE — Tự sản xuất"),
            new PromptOption("1", "BUY — Mua ngoài")
        }),
        new PromptField("isActive", "Trạng thái", variant.IsActive ? "true" : "false", Options: new[]
        {
            new PromptOption("true", "Đang dùng"),
            new PromptOption("false", "Ngừng dùng (giữ lịch sử)")
        })
    };

    private static bool TryReadVariantValues(IReadOnlyDictionary<string, string> values, out decimal costPrice, out decimal sellingPrice, out int sourcingType)
    {
        costPrice = 0;
        sellingPrice = 0;
        sourcingType = 0;

        if (!decimal.TryParse(values["cost"], NumberStyles.Number, CultureInfo.InvariantCulture, out costPrice) ||
            !decimal.TryParse(values["selling"], NumberStyles.Number, CultureInfo.InvariantCulture, out sellingPrice) ||
            costPrice < 0 || sellingPrice < 0)
        {
            MessageBox.Show("Giá vốn và giá bán phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (!int.TryParse(values["sourcingType"], out sourcingType) || sourcingType is < 0 or > 1)
        {
            MessageBox.Show("Nguồn hàng không hợp lệ. Chọn MAKE hoặc BUY.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private async Task RefreshSelectedFashionProductAsync()
    {
        if (FashionVariantProductSelector.SelectedItem is ApiClient.FashionProductItem product)
            await LoadFashionVariantsAsync(product);

        await LoadFashionProductsAsync(FashionProductSearchBox.Text);
    }

}
