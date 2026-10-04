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
    private async void RefreshReceipts_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải nhà cung cấp và phiếu nhập kho...", async () =>
        {
            await LoadSuppliersAsync(SupplierSearchBox.Text);
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
        });
    }

    private void SupplierSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        SupplierSearchPlaceholder.Visibility = string.IsNullOrEmpty(SupplierSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-suppliers", "Đang tìm nhà cung cấp...", () => LoadSuppliersAsync(SupplierSearchBox.Text));
    }

    private void ReceiptSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        ReceiptSearchPlaceholder.Visibility = string.IsNullOrEmpty(ReceiptSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-receipts", "Đang tìm phiếu nhập kho...", () => LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text));
    }

    private async void InventoryBalancesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (InventoryBalancesDataGrid.SelectedItem is ApiClient.InventoryBalanceItem balance)
        {
            InventorySelectedContextText.Text = $"Đã chọn {balance.Sku}. Mở tab Nhật ký giao dịch để xem lịch sử riêng của SKU này.";
            await LoadInventoryMovementsAsync(balance.ProductVariantId);
        }
        else
        {
            InventorySelectedContextText.Text = "Chọn một SKU để xem nhật ký giao dịch của SKU đó ở tab Nhật ký giao dịch.";
        }
    }

    private void InventoryBalancesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.InventoryBalanceItem)
        {
            StockAdjustmentDialog_Click(sender, e);
        }
    }

    private void InventoryMovementsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject source ||
            ItemsControl.ContainerFromElement(grid, source) is not DataGridRow ||
            grid.SelectedItem is not ApiClient.InventoryMovementItem movement)
            return;

        MessageBox.Show(
            $"SKU: {movement.Sku}\nLoại: {movement.MovementTypeLabel}\nBiến động: {movement.QuantityDelta:+#;-#;0}\nThời gian: {movement.MovementDate:dd/MM/yyyy HH:mm:ss}\nTham chiếu: {movement.ReferenceType ?? "—"}\nGhi chú: {movement.Notes ?? "—"}\n\nSổ kho là dữ liệu truy vết nên không sửa/xóa trực tiếp. Nếu kiểm kê chênh lệch, hãy nhấp đúp dòng Tồn kho để lập điều chỉnh.",
            "Chi tiết giao dịch kho", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CreateSupplierDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!PromptDialog.TryShow(this, "Tạo nhà cung cấp", SupplierFields(), out var values)) return;

        var success = await _apiClient.CreateSupplierAsync(
            values["code"], values["name"], values["contact"], values["phoneNumbers"], values["email"], values["address"], values["bankAccounts"]);
        if (success)
        {
            MessageBox.Show($"Đã tạo nhà cung cấp '{values["code"]}' và lưu vào database.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadSuppliersAsync(SupplierSearchBox.Text);
        }
        else
        {
            MessageBox.Show("Tạo nhà cung cấp thất bại. Bạn cần có quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditSupplierDialog_Click(object sender, RoutedEventArgs e)
    {
        if (SuppliersDataGrid.SelectedItem is not ApiClient.SupplierItem supplier)
        {
            MessageBox.Show("Hãy chọn nhà cung cấp cần sửa.", "Chưa chọn nhà cung cấp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!PromptDialog.TryShow(this, $"Sửa nhà cung cấp: {supplier.Code}", SupplierFields(supplier), out var values)) return;

        var success = await _apiClient.UpdateSupplierAsync(
            supplier.Id, values["code"], values["name"], values["contact"], values["phoneNumbers"], values["email"], values["address"], values["bankAccounts"]);
        if (success)
        {
            await LoadSuppliersAsync(SupplierSearchBox.Text);
            MessageBox.Show("Đã cập nhật nhà cung cấp.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật nhà cung cấp. Kiểm tra mã không trùng và quyền Products.Manage.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteSupplier_Click(object sender, RoutedEventArgs e)
    {
        if (SuppliersDataGrid.SelectedItem is not ApiClient.SupplierItem supplier)
        {
            MessageBox.Show("Hãy chọn nhà cung cấp cần xóa.", "Chưa chọn nhà cung cấp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (MessageBox.Show($"Xóa nhà cung cấp '{supplier.Code} — {supplier.Name}'?", "Xác nhận xóa", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.DeleteSupplierAsync(supplier.Id))
        {
            await LoadSuppliersAsync(SupplierSearchBox.Text);
            MessageBox.Show("Đã xóa nhà cung cấp.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể xóa. Nhà cung cấp đã có phiếu nhập sẽ được giữ để bảo toàn lịch sử.", "Không thể xóa", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SuppliersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SuppliersDataGrid.SelectedItem is ApiClient.SupplierItem)
            EditSupplierDialog_Click(sender, e);
    }

    private static IReadOnlyList<PromptField> SupplierFields(ApiClient.SupplierItem? supplier = null) => new[]
    {
        new PromptField("code", "Mã nhà cung cấp", supplier?.Code),
        new PromptField("name", "Tên nhà cung cấp", supplier?.Name),
        new PromptField("contact", "Người liên hệ", supplier?.ContactName, IsRequired: false),
        new PromptField("phoneNumbers", "Số điện thoại (mỗi dòng một số)", supplier?.PhoneNumbersDisplay, IsRequired: false, IsMultiline: true),
        new PromptField("email", "Email", supplier?.Email, IsRequired: false),
        new PromptField("bankAccounts", "Tài khoản ngân hàng (mỗi dòng: Ngân hàng - Số TK - Chủ TK)", supplier?.BankAccounts, IsRequired: false, IsMultiline: true),
        new PromptField("address", "Địa chỉ", supplier?.Address, IsRequired: false, IsMultiline: true)
    };

    private async void EditPurchaseReceiptDialog_Click(object sender, RoutedEventArgs e)
    {
        if (PurchaseReceiptsDataGrid.SelectedItem is not ApiClient.PurchaseReceiptItemModel receipt)
        {
            ShowToast("Hãy chọn phiếu nhập cần sửa.");
            return;
        }

        var detail = await _apiClient.GetPurchaseReceiptDetailAsync(receipt.Id);
        var suppliers = await _apiClient.GetSuppliersAsync();
        if (detail is null || suppliers is null)
        {
            MessageBox.Show("Không tải được chi tiết phiếu nhập hoặc danh sách nhà cung cấp.", "Không thể sửa", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.Equals(detail.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Phiếu đã hủy chỉ được xem lịch sử, không thể sửa.", "Phiếu đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var supplierOptions = suppliers.Items
            .Select(s => new PromptOption(s.Id.ToString(), $"{s.Code} — {s.Name}"))
            .ToArray();
        var lineSummary = string.Join(", ", detail.Items.Select(i => $"{i.Sku} × {i.Quantity:N0}"));
        if (!PromptDialog.TryShow(this, $"Sửa phiếu nhập — {detail.ReceiptNumber}", new[]
        {
            new PromptField("supplierId", "Nhà cung cấp", detail.SupplierId.ToString(), Options: supplierOptions),
            new PromptField("notes", $"Ghi chú · {detail.Items.Count} SKU: {lineSummary}", detail.Notes, IsRequired: false, IsMultiline: true)
        }, out var values)) return;

        if (!Guid.TryParse(values["supplierId"], out var supplierId))
        {
            MessageBox.Show("Nhà cung cấp không hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (await _apiClient.UpdatePurchaseReceiptHeaderAsync(receipt.Id, supplierId, values["notes"]))
        {
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
            MessageBox.Show("Đã cập nhật thông tin phiếu nhập. Số lượng và giá được giữ nguyên để khớp sổ kho.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật phiếu nhập. Kiểm tra quyền Inventory.Receipt và trạng thái phiếu.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CancelPurchaseReceipt_Click(object sender, RoutedEventArgs e)
    {
        if (PurchaseReceiptsDataGrid.SelectedItem is not ApiClient.PurchaseReceiptItemModel receipt)
        {
            ShowToast("Hãy chọn phiếu nhập cần hủy.");
            return;
        }
        if (string.Equals(receipt.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            ShowToast("Phiếu này đã hủy rồi.");
            return;
        }
        if (!PromptDialog.TryShow(this, $"Hủy / hoàn kho — {receipt.ReceiptNumber}", new[]
        {
            new PromptField("reason", "Lý do hủy / hoàn kho", IsRequired: false, IsMultiline: true)
        }, out var values)) return;
        if (MessageBox.Show(
                $"Xác nhận hủy {receipt.ReceiptNumber}?\nHệ thống sẽ tạo giao dịch hoàn kho âm, giảm tồn kho và giữ nguyên phiếu/chứng từ để truy vết.",
                "Xác nhận hủy / hoàn kho", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.CancelPurchaseReceiptAsync(receipt.Id, values["reason"]))
        {
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
            await LoadInventoryBalancesAsync();
            await LoadInventoryMovementsAsync();
            MessageBox.Show("Đã hủy phiếu và tạo giao dịch hoàn kho. Lịch sử phiếu vẫn được giữ để kiểm tra.", "Đã hủy / hoàn kho", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể hủy vì tồn khả dụng không đủ hoặc phiếu đã phát sinh sử dụng. Hãy xử lý đơn giữ hàng/giao hàng trước.", "Không thể hủy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PurchaseReceiptsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid &&
            e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.PurchaseReceiptItemModel)
        {
            EditPurchaseReceiptDialog_Click(sender, e);
        }
    }

    private async void CreateReceiptDialog_Click(object sender, RoutedEventArgs e)
    {
        var suppliersData = await _apiClient.GetSuppliersAsync();
        var fashionVariants = await _apiClient.GetActiveFashionVariantsAsync();
        var warehouses = await _apiClient.GetWarehousesAsync();

        if (suppliersData == null || suppliersData.Items.Count == 0)
        {
            MessageBox.Show("Chưa có nhà cung cấp trong hệ thống. Vui lòng tạo nhà cung cấp trước!", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (fashionVariants == null || fashionVariants.Count == 0)
        {
            MessageBox.Show("Chưa có SKU BUY đang hoạt động để nhập thành phẩm. SKU MAKE phải đi qua luồng Sản xuất; nếu cần nhập mua ngoài, hãy tạo SKU với nguồn hàng BUY.", "Thông Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (warehouses == null || warehouses.Count == 0)
        {
            MessageBox.Show("Chưa có kho hoạt động. Vào mục Danh mục kho để tạo kho trước khi lập phiếu nhập.", "Chưa có kho", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var supplierOptions = suppliersData.Items
            .Select(s => new PromptOption(s.Id.ToString(), $"{s.Code} — {s.Name}"))
            .ToList();
        var variantOptions = fashionVariants
            .Select(v => new PromptOption(v.Id.ToString(), $"{v.Sku} — {v.ProductName} ({v.Color}/{v.Size})"))
            .ToList();
        var warehouseOptions = warehouses
            .Select(w => new PromptOption(w.Id.ToString(), $"{w.Code} — {w.Name}{(w.IsDefault ? " (mặc định)" : string.Empty)}"))
            .ToList();

        if (!PromptDialog.TryShow(this, "Lập phiếu nhập kho", new[]
        {
            new PromptField("supplierId", "Nhà cung cấp", Options: supplierOptions),
            new PromptField("warehouseId", "Nhập vào kho", warehouses.FirstOrDefault(w => w.IsDefault)?.Id.ToString() ?? warehouses[0].Id.ToString(), Options: warehouseOptions),
            new PromptField("variantId", "SKU nhập kho", Options: variantOptions),
            new PromptField("quantity", "Số lượng"),
            new PromptField("unitPrice", "Đơn giá nhập"),
            new PromptField("notes", "Ghi chú", IsRequired: false)
        }, out var values)) return;

        if (!int.TryParse(values["quantity"], out var qty) || qty <= 0 ||
            !decimal.TryParse(values["unitPrice"], out var unitPrice) || unitPrice < 0)
        {
            MessageBox.Show("Số lượng phải lớn hơn 0; đơn giá phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var selectedSupplier = suppliersData.Items.First(s => s.Id.ToString() == values["supplierId"]);
        var selectedVariant = fashionVariants.First(v => v.Id.ToString() == values["variantId"]);
        var selectedWarehouse = warehouses.FirstOrDefault(w => w.Id.ToString() == values["warehouseId"]);
        if (selectedWarehouse is null)
        {
            MessageBox.Show("Kho nhập không hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var itemsReq = new List<ApiClient.CreatePurchaseReceiptItemReq>
        {
            new(selectedVariant.Id, qty, unitPrice)
        };

        var attachmentPaths = PickReceiptAttachments();
        var createdReceipt = await _apiClient.CreatePurchaseReceiptAsync(selectedSupplier.Id, values["notes"], itemsReq, selectedWarehouse.Id);
        if (createdReceipt is not null)
        {
            var uploadedCount = 0;
            var failedAttachments = new List<string>();
            foreach (var attachmentPath in attachmentPaths)
            {
                var uploaded = await _apiClient.UploadPurchaseReceiptAttachmentAsync(createdReceipt.Id, attachmentPath);
                if (uploaded is null)
                {
                    failedAttachments.Add(Path.GetFileName(attachmentPath));
                }
                else
                {
                    uploadedCount++;
                }
            }

            var message = $"Đã lập phiếu nhập {createdReceipt.ReceiptNumber} — {qty} chiếc SKU '{selectedVariant.Sku}' vào {selectedWarehouse.Name}.\n" +
                          "Tồn kho và sổ giao dịch đã cập nhật từ API.";
            if (attachmentPaths.Count > 0)
            {
                message += $"\n\nChứng từ Cloudinary: {uploadedCount}/{attachmentPaths.Count} tệp đã lưu.";
                if (failedAttachments.Count > 0)
                    message += $"\nChưa lưu được: {string.Join(", ", failedAttachments)}";
            }

            MessageBox.Show(message, failedAttachments.Count == 0 ? "Thành công" : "Lập phiếu thành công, chứng từ cần thử lại",
                MessageBoxButton.OK, failedAttachments.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
            await LoadInventoryBalancesAsync();
            await LoadInventoryMovementsAsync();
        }
        else
        {
            MessageBox.Show("Lập phiếu nhập kho thất bại. Vui lòng kiểm tra quyền Inventory.Receipt.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static IReadOnlyList<string> PickReceiptAttachments()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn ảnh/PDF hóa đơn đính kèm (có thể bỏ qua)",
            Filter = "Ảnh hoặc PDF (*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf)|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    private void OpenReceiptAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } || string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Phiếu này chưa có ảnh/PDF hóa đơn đính kèm.", "Chứng từ", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Không thể mở chứng từ: {ex.Message}", "Lỗi mở chứng từ", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void UploadReceiptAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: Guid receiptId })
            return;

        var attachmentPaths = PickReceiptAttachments();
        if (attachmentPaths.Count == 0)
            return;

        var uploadedCount = 0;
        var failedAttachments = new List<string>();
        await RunWithBusyAsync("Đang tải chứng từ lên Cloudinary...", async () =>
        {
            foreach (var attachmentPath in attachmentPaths)
            {
                var uploaded = await _apiClient.UploadPurchaseReceiptAttachmentAsync(receiptId, attachmentPath);
                if (uploaded is null)
                    failedAttachments.Add(Path.GetFileName(attachmentPath));
                else
                    uploadedCount++;
            }

            await LoadPurchaseReceiptsAsync(ReceiptSearchBox.Text);
        });

        var message = $"Đã lưu {uploadedCount}/{attachmentPaths.Count} tệp chứng từ lên Cloudinary.";
        if (failedAttachments.Count > 0)
            message += $"\nChưa lưu được: {string.Join(", ", failedAttachments)}";

        MessageBox.Show(message, failedAttachments.Count == 0 ? "Thành công" : "Cần thử lại",
            MessageBoxButton.OK, failedAttachments.Count == 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

}
