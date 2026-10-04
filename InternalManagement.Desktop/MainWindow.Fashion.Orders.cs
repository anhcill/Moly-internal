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
    private async void SimulatePricingDialog_Click(object sender, RoutedEventArgs e)
    {
        if (FashionVariantsDataGrid.SelectedItem is not ApiClient.FashionVariantItem variant)
        {
            MessageBox.Show("Hãy chọn một SKU ở bảng biến thể trước khi mô phỏng giá.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Mô phỏng giá — {variant.Sku}", new[]
        {
            new PromptField("channel", "Kênh bán", InitialValue: "STORE"),
            new PromptField("listPrice", "Giá niêm yết", InitialValue: variant.SellingPrice.ToString("0.##")),
            new PromptField("discount", "Voucher", InitialValue: "0"),
            new PromptField("targetMargin", "Biên mục tiêu (0..1)", InitialValue: "0.30")
        }, out var values)) return;

        if (!decimal.TryParse(values["listPrice"], out var listPrice) || !decimal.TryParse(values["discount"], out var discount) || !decimal.TryParse(values["targetMargin"], out var targetMargin))
        {
            MessageBox.Show("Giá, voucher và biên mục tiêu phải là số hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = await _apiClient.SimulatePricingAsync(variant.Id, values["channel"], listPrice, discount, targetMargin);
        if (result == null)
        {
            MessageBox.Show("Không mô phỏng được giá. Kiểm tra chính sách phí của kênh bán và quyền Pricing.Simulator.", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"SKU: {result.Sku}\nGiá vốn: {result.UnitCost:N0} đ\nDoanh thu ròng: {result.NetRevenue:N0} đ\nLợi nhuận: {result.Profit:N0} đ\nMargin: {result.Margin:P1}", "Kết quả mô phỏng giá", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void CreateSalesOrderDialog_Click(object sender, RoutedEventArgs e)
    {
        var variant = FashionVariantsDataGrid.SelectedItem as ApiClient.FashionVariantItem;
        if (variant is null)
        {
            var variants = await _apiClient.GetActiveSellableFashionVariantsAsync();
            if (variants is null || variants.Count == 0)
            {
                MessageBox.Show("Chưa có SKU đang hoạt động để bán. Hãy tạo sản phẩm và biến thể trước.", "Chưa có SKU", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var skuOptions = variants
                .OrderBy(item => item.ProductName)
                .ThenBy(item => item.Sku)
                .Select(item => new PromptOption(item.Id.ToString(), $"{item.Sku} — {item.ProductName}{(string.IsNullOrWhiteSpace(item.Color) ? string.Empty : $" · {item.Color}")}{(string.IsNullOrWhiteSpace(item.Size) ? string.Empty : $" · {item.Size}")} (tồn: {item.OnHandQuantity:N0})"))
                .ToArray();
            if (!PromptDialog.TryShow(this, "Chọn sản phẩm cho đơn hàng", new[]
            {
                new PromptField("variant", "SKU cần bán", Options: skuOptions)
            }, out var selected) || !Guid.TryParse(selected["variant"], out var variantId))
                return;

            variant = variants.FirstOrDefault(item => item.Id == variantId);
            if (variant is null)
            {
                MessageBox.Show("SKU đã chọn không còn hợp lệ. Hãy tải lại danh sách sản phẩm.", "Không tìm thấy SKU", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        var warehouses = await _apiClient.GetWarehousesAsync();
        var warehouseOptions = warehouses?
            .Where(item => item.IsActive)
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => item.Name)
            .Select(item => new PromptOption(item.Id.ToString(), $"{item.Name} ({item.Code}){(item.IsDefault ? " — mặc định" : string.Empty)}"))
            .ToArray() ?? Array.Empty<PromptOption>();
        var orderFields = new List<PromptField>
        {
            new PromptField("sourceSystem", "Kênh bán", Options: new[]
            {
                new PromptOption("STORE", "Cửa hàng"),
                new PromptOption("FACEBOOK", "Facebook / Inbox"),
                new PromptOption("WEBSITE_AODAI", "Website Áo Dài"),
                new PromptOption("SHOPEE", "Shopee"),
                new PromptOption("TIKTOK", "TikTok Shop"),
                new PromptOption("ZALO", "Zalo")
            }),
            new PromptField("customerName", "Tên khách hàng"),
            new PromptField("customerPhone", "Số điện thoại", IsRequired: false),
            new PromptField("sourceOrderId", "Mã đơn ngoài", IsRequired: false),
            new PromptField("shippingAddress", "Địa chỉ giao hàng", IsRequired: false, IsMultiline: true),
            new PromptField("quantity", "Số lượng", InitialValue: "1"),
            new PromptField("unitPrice", "Đơn giá", InitialValue: variant.SellingPrice.ToString("0.##")),
            new PromptField("discount", "Giảm giá", InitialValue: "0"),
            new PromptField("shippingCustomerPaid", "Khách trả phí ship", InitialValue: "0"),
            new PromptField("shippingShopSubsidy", "Shop hỗ trợ ship", InitialValue: "0"),
            new PromptField("advertisingCost", "Chi phí quảng cáo", InitialValue: "0"),
            new PromptField("packagingCost", "Đóng gói", InitialValue: "0"),
            new PromptField("otherSellingExpense", "Chi phí bán khác", InitialValue: "0")
        };
        if (warehouseOptions.Length > 0)
        {
            orderFields.Insert(5, new PromptField("warehouseId", "Kho xuất / giữ hàng", Options: warehouseOptions));
        }
        if (!PromptDialog.TryShow(this, $"Tạo đơn hàng — {variant.Sku}", orderFields, out var values)) return;

        if (!int.TryParse(values["quantity"], out var quantity) || quantity <= 0 ||
            !decimal.TryParse(values["unitPrice"], out var unitPrice) || unitPrice < 0 ||
            !decimal.TryParse(values["discount"], out var discount) || discount < 0 ||
            !decimal.TryParse(values["shippingCustomerPaid"], out var shippingCustomerPaid) || shippingCustomerPaid < 0 ||
            !decimal.TryParse(values["shippingShopSubsidy"], out var shippingShopSubsidy) || shippingShopSubsidy < 0 ||
            !decimal.TryParse(values["advertisingCost"], out var advertisingCost) || advertisingCost < 0 ||
            !decimal.TryParse(values["packagingCost"], out var packagingCost) || packagingCost < 0 ||
            !decimal.TryParse(values["otherSellingExpense"], out var otherSellingExpense) || otherSellingExpense < 0)
        {
            MessageBox.Show("Số lượng phải lớn hơn 0; giá và các khoản chi phí phải là số không âm.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var orderItems = new List<ApiClient.CreateSalesOrderItemReq>
        {
            new(variant.Id, quantity, unitPrice)
        };
        var orderItemLabels = new List<string>
        {
            $"• {variant.Sku}: {quantity:N0} × {unitPrice:N0} đ"
        };
        while (MessageBox.Show("Bạn có muốn thêm SKU khác vào cùng đơn hàng không?", "Thêm sản phẩm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            var variants = await _apiClient.GetActiveSellableFashionVariantsAsync();
            var availableVariants = variants?.Where(item => orderItems.All(line => line.ProductVariantId != item.Id))
                .OrderBy(item => item.ProductName).ThenBy(item => item.Sku).ToArray() ?? Array.Empty<ApiClient.FashionVariantItem>();
            if (availableVariants.Length == 0)
            {
                MessageBox.Show("Không còn SKU đang hoạt động để thêm vào đơn này.", "Không còn SKU", MessageBoxButton.OK, MessageBoxImage.Information);
                break;
            }

            var skuOptions = availableVariants.Select(item => new PromptOption(item.Id.ToString(),
                $"{item.Sku} — {item.ProductName}{(string.IsNullOrWhiteSpace(item.Color) ? string.Empty : $" · {item.Color}")}{(string.IsNullOrWhiteSpace(item.Size) ? string.Empty : $" · {item.Size}")} (giá: {item.SellingPrice:N0} đ · tồn: {item.OnHandQuantity:N0})")).ToArray();
            if (!PromptDialog.TryShow(this, "Thêm sản phẩm vào đơn", new[]
            {
                new PromptField("variant", "SKU cần thêm", Options: skuOptions),
                new PromptField("quantity", "Số lượng", InitialValue: "1"),
                new PromptField("unitPrice", "Đơn giá")
            }, out var itemValues))
                break;
            if (!Guid.TryParse(itemValues["variant"], out var itemVariantId) ||
                !int.TryParse(itemValues["quantity"], out var itemQuantity) || itemQuantity <= 0 ||
                !decimal.TryParse(itemValues["unitPrice"], out var itemUnitPrice) || itemUnitPrice < 0)
            {
                MessageBox.Show("SKU, số lượng và đơn giá của sản phẩm thêm vào chưa hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                continue;
            }

            var selectedVariant = availableVariants.First(item => item.Id == itemVariantId);
            orderItems.Add(new ApiClient.CreateSalesOrderItemReq(itemVariantId, itemQuantity, itemUnitPrice));
            orderItemLabels.Add($"• {selectedVariant.Sku}: {itemQuantity:N0} × {itemUnitPrice:N0} đ");
        }

        Guid? warehouseId = null;
        if (values.TryGetValue("warehouseId", out var selectedWarehouse))
        {
            if (!Guid.TryParse(selectedWarehouse, out var parsedWarehouseId))
            {
                MessageBox.Show("Kho xuất hàng không hợp lệ.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            warehouseId = parsedWarehouseId;
        }

        var orderPreview = string.Join(Environment.NewLine, orderItemLabels);
        var grossPreview = orderItems.Sum(line => line.Quantity * line.UnitPrice);
        if (MessageBox.Show(
                $"Khách: {values["customerName"]}\n" +
                $"Kênh: {values["sourceSystem"]}\n" +
                $"Tạm tính hàng: {grossPreview:N0} đ\n\n" +
                $"Sản phẩm:\n{orderPreview}\n\n" +
                "Tạo đơn và giữ hàng trong kho đã chọn?",
                "Xác nhận tạo đơn hàng", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var result = await _apiClient.CreateSalesOrderAsync(
            values["sourceSystem"],
            string.IsNullOrWhiteSpace(values["sourceOrderId"]) ? null : values["sourceOrderId"],
            values["customerName"],
            discount,
            shippingCustomerPaid,
            shippingShopSubsidy,
            orderItems,
            values["customerPhone"],
            values["shippingAddress"],
            advertisingCost,
            packagingCost,
            otherSellingExpense,
            warehouseId);

        if (result == null)
        {
            MessageBox.Show("Không tạo được đơn hàng. Kiểm tra tồn khả dụng, chính sách giá và quyền Orders.Create.", "Tạo đơn thất bại", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show($"Đã tạo {result.OrderNumber}.\nDoanh thu thuần: {result.NetSalesAmount:N0} đ\nGiá vốn: {result.ActualCogs:N0} đ\nLợi nhuận: {result.Profit:N0} đ\nMargin: {result.Margin:P1}", "Tạo đơn thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        await LoadFashionDataAsync();
        await LoadSalesOrdersAsync();
    }

    private async Task LoadSalesOrdersAsync(string? search = null)
    {
        var data = await _apiClient.GetSalesOrdersAsync(search);
        if (data is null)
        {
            SalesOrdersDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được đơn hàng. Vui lòng thử lại.", isError: true);
            return;
        }

        _allSalesOrders = data.Items.ToList();
        ApplySalesOrderFilters();
        SetLoadedStatus("Đơn hàng", _allSalesOrders.Count);
    }

    private void ApplySalesOrderFilters()
    {
        if (SalesOrderChannelFilter == null || SalesOrderStatusFilter == null)
            return;

        var channel = SalesOrderChannelFilter.SelectedValue as string ?? "ALL";
        var statusText = SalesOrderStatusFilter.SelectedValue as string ?? "ALL";
        var query = _allSalesOrders.AsEnumerable();
        if (!string.Equals(channel, "ALL", StringComparison.OrdinalIgnoreCase))
            query = query.Where(order => string.Equals(order.SourceSystem, channel, StringComparison.OrdinalIgnoreCase));
        if (int.TryParse(statusText, out var status))
            query = query.Where(order => order.Status == status);

        var items = query.ToList();
        SalesOrdersDataGrid.ItemsSource = items;
        var value = items.Sum(order => order.TotalAmount);
        var profit = items.Sum(order => order.Profit);
        SalesOrderSummaryText.Text = $"{items.Count:N0} đơn · Tổng thanh toán {value:N0} đ · Lợi nhuận dự tính {profit:N0} đ";
    }

    private void SalesOrderSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUiReady) return;
        SalesOrderSearchPlaceholder.Visibility = string.IsNullOrEmpty(SalesOrderSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _ = DebounceSearchAsync("fashion-orders", "Đang tìm đơn hàng...", () => LoadSalesOrdersAsync(SalesOrderSearchBox.Text));
    }

    private void SalesOrderFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUiReady)
            ApplySalesOrderFilters();
    }

    private void OpenSalesOrders_Click(object sender, RoutedEventArgs e)
    {
        FashionTabs.SelectedIndex = 8;
    }

    private async void RefreshSalesOrders_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyAsync("Đang tải đơn hàng...", () => LoadSalesOrdersAsync(SalesOrderSearchBox.Text));
    }

    private async void EditSalesOrderDialog_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần sửa.");
            return;
        }
        if (order.Status != 0)
        {
            MessageBox.Show("Đơn đã giao hoặc đã hủy không thể sửa trực tiếp. Hãy dùng giao hàng, đổi trả hoặc chứng từ điều chỉnh để giữ đúng sổ kho và doanh thu.", "Không thể sửa trực tiếp", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa đơn hàng — {order.OrderNumber}", new[]
        {
            new PromptField("customer", "Tên khách hàng", order.CustomerName),
            new PromptField("phone", "Số điện thoại", order.CustomerPhone, IsRequired: false),
            new PromptField("external", "Mã đơn ngoài", order.SourceOrderId, IsRequired: false),
            new PromptField("address", "Địa chỉ giao hàng", order.ShippingAddress, IsRequired: false, IsMultiline: true)
        }, out var values)) return;

        if (await _apiClient.UpdateSalesOrderHeaderAsync(order.Id, values["customer"], values["phone"], values["address"], values["external"]))
        {
            await LoadSalesOrdersAsync(SalesOrderSearchBox.Text);
            MessageBox.Show("Đã cập nhật thông tin liên hệ của đơn hàng.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể cập nhật đơn hàng. Kiểm tra trạng thái đơn, mã đơn ngoài và quyền Orders.Manage.", "Không thể cập nhật", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CancelSalesOrder_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần hủy.");
            return;
        }
        if (order.Status != 0)
        {
            MessageBox.Show("Chỉ hủy trực tiếp đơn còn chờ xử lý. Đơn đã giao phải đi qua luồng đổi trả.", "Không thể hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!PromptDialog.TryShow(this, $"Hủy đơn — {order.OrderNumber}", new[]
        {
            new PromptField("reason", "Lý do hủy", IsRequired: false, IsMultiline: true)
        }, out var values)) return;
        if (MessageBox.Show($"Hủy đơn {order.OrderNumber}? Hệ thống sẽ giải phóng số hàng đang giữ trong kho.", "Xác nhận hủy đơn", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (await _apiClient.CancelSalesOrderAsync(order.Id, values["reason"]))
        {
            await LoadSalesOrdersAsync(SalesOrderSearchBox.Text);
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
            MessageBox.Show("Đã hủy đơn và giải phóng lượng hàng đã giữ.", "Đã hủy đơn", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể hủy vì dữ liệu giữ kho không còn khớp hoặc đơn đã chuyển trạng thái.", "Không thể hủy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeliverSalesOrder_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần giao.");
            return;
        }

        var fulfillment = await _apiClient.GetSalesOrderFulfillmentAsync(order.Id);
        if (fulfillment is null)
        {
            MessageBox.Show("Không tải được chi tiết giao hàng của đơn.", "Không thể giao hàng", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var remaining = fulfillment.Items.Where(item => item.RemainingQuantity > 0).ToArray();
        if (remaining.Length == 0)
        {
            MessageBox.Show("Đơn này không còn sản phẩm cần giao.", "Không còn hàng cần giao", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var summary = string.Join(", ", remaining.Select(item => $"{item.Sku} × {item.RemainingQuantity:N0}"));
        if (MessageBox.Show($"Giao toàn bộ phần còn lại của {order.OrderNumber}?\n{summary}", "Xác nhận giao hàng", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var items = remaining.Select(item => new ApiClient.FulfillSalesOrderItemReq(item.ProductVariantId, item.RemainingQuantity)).ToArray();
        if (await _apiClient.DeliverSalesOrderAsync(order.Id, items))
        {
            await LoadSalesOrdersAsync(SalesOrderSearchBox.Text);
            await LoadInventoryBalancesAsync(InventorySearchBox.Text);
            await LoadInventoryMovementsAsync();
            MessageBox.Show("Đã giao hàng và cập nhật tồn kho.", "Giao hàng thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Giao hàng thất bại. Kiểm tra tồn kho đã giữ và trạng thái đơn.", "Không thể giao hàng", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RecordSalesOrderPayment_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần ghi nhận thanh toán.");
            return;
        }
        if (order.Status == 3)
        {
            MessageBox.Show("Không thể ghi nhận thu cho đơn đã hủy.", "Đơn đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Ghi nhận khách thanh toán — {order.OrderNumber}", new[]
        {
            new PromptField("amount", "Số tiền thu", order.TotalAmount.ToString("0.##", CultureInfo.InvariantCulture)),
            new PromptField("reference", "Mã giao dịch / mã chuyển khoản"),
            new PromptField("method", "Phương thức", "Chuyển khoản", Options: new[]
            {
                new PromptOption("Chuyển khoản", "Chuyển khoản ngân hàng"),
                new PromptOption("Tiền mặt", "Tiền mặt"),
                new PromptOption("COD", "COD"),
                new PromptOption("Ví điện tử", "Ví điện tử")
            })
        }, out var values)) return;

        if (!decimal.TryParse(values["amount"], NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            MessageBox.Show("Số tiền thu phải lớn hơn 0.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (await _apiClient.RecordCustomerPaymentAsync(order.Id, amount, values["reference"], values["method"]))
        {
            MessageBox.Show("Đã ghi nhận khoản thu và đồng bộ vào sổ tài chính.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể ghi nhận khoản thu. Kiểm tra mã giao dịch không trùng, số tiền chưa vượt giá trị đơn và quyền Orders.Manage.", "Không thể ghi nhận", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void IssueSalesOrderDocument_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần phát hành chứng từ.");
            return;
        }
        if (order.Status == 3)
        {
            MessageBox.Show("Không thể phát hành chứng từ cho đơn đã hủy.", "Đơn đã hủy", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!PromptDialog.TryShow(this, $"Phát hành chứng từ — {order.OrderNumber}", new[]
        {
            new PromptField("type", "Loại chứng từ", Options: new[]
            {
                new PromptOption("0", "Hóa đơn"),
                new PromptOption("1", "Phiếu bán lẻ")
            })
        }, out var values) || !int.TryParse(values["type"], out var documentType)) return;

        var document = await _apiClient.IssueSalesDocumentAsync(order.Id, documentType);
        if (document is not null)
        {
            MessageBox.Show($"Đã phát hành {document.DocumentNumber}.\nTổng thanh toán: {document.TotalAmount:N0} đ", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Không thể phát hành chứng từ. Mỗi loại chứng từ chỉ được tạo một lần cho đơn hàng.", "Không thể phát hành", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ViewSalesOrderDetails_Click(object sender, RoutedEventArgs e)
    {
        if (SalesOrdersDataGrid.SelectedItem is not ApiClient.SalesOrderSummaryItem order)
        {
            ShowToast("Hãy chọn đơn hàng cần xem chi tiết.");
            return;
        }

        var fulfillmentTask = _apiClient.GetSalesOrderFulfillmentAsync(order.Id);
        var settlementsTask = _apiClient.GetSalesOrderSettlementsAsync(order.Id);
        var documentsTask = _apiClient.GetSalesOrderDocumentsAsync(order.Id);
        await Task.WhenAll(fulfillmentTask, settlementsTask, documentsTask);

        var fulfillment = await fulfillmentTask;
        if (fulfillment is null)
        {
            MessageBox.Show("Không tải được chi tiết giao hàng của đơn.", "Không thể xem chi tiết", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var settlements = await settlementsTask ?? Array.Empty<ApiClient.SalesSettlementItem>();
        var documents = await documentsTask ?? Array.Empty<ApiClient.SalesDocumentItem>();
        var lines = string.Join(Environment.NewLine, fulfillment.Items.Select(item =>
            $"• {item.Sku}: đặt {item.OrderedQuantity:N0}, đã giao {item.DeliveredQuantity:N0}, còn {item.RemainingQuantity:N0}"));
        var confirmedPayment = settlements.Where(item => item.Kind == 0 && item.Status == 1).Sum(item => item.Amount);
        var paymentLines = settlements.Count == 0
            ? "Chưa ghi nhận khoản thu/hoàn tiền."
            : string.Join(Environment.NewLine, settlements.Select(item =>
                $"• {item.KindLabel}: {item.Amount:N0} {item.Currency} — {item.PaymentMethod ?? "Chưa chọn phương thức"} — {item.PaymentReference} ({item.StatusLabel})"));
        var documentLines = documents.Count == 0
            ? "Chưa phát hành hóa đơn/phiếu bán lẻ."
            : string.Join(Environment.NewLine, documents.Select(item =>
                $"• {(item.DocumentType == 0 ? "Hóa đơn" : "Phiếu bán lẻ")} {item.DocumentNumber}: {item.TotalAmount:N0} đ"));

        MessageBox.Show(
            $"ĐƠN {order.OrderNumber}\n" +
            $"Kênh: {order.SourceSystemLabel}\n" +
            $"Kho xuất: {order.WarehouseLabel}\n" +
            $"Mã đơn ngoài: {order.SourceOrderId ?? "—"}\n" +
            $"Trạng thái: {order.StatusLabel}\n\n" +
            $"KHÁCH HÀNG\n{order.CustomerName}\nSĐT: {order.CustomerPhone ?? "—"}\nĐịa chỉ: {order.ShippingAddress ?? "—"}\n\n" +
            $"TÀI CHÍNH\nTổng thanh toán: {order.TotalAmount:N0} đ\nĐã thu xác nhận: {confirmedPayment:N0} đ\nCòn phải thu: {Math.Max(0, order.TotalAmount - confirmedPayment):N0} đ\nLợi nhuận dự tính: {order.Profit:N0} đ\n\n" +
            $"SẢN PHẨM & GIAO HÀNG\n{lines}\n\n" +
            $"THU/HOÀN TIỀN\n{paymentLines}\n\n" +
            $"CHỨNG TỪ\n{documentLines}",
            $"Chi tiết đơn hàng — {order.OrderNumber}", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void SalesOrdersDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source &&
            ItemsControl.ContainerFromElement(grid, source) is DataGridRow &&
            grid.SelectedItem is ApiClient.SalesOrderSummaryItem)
        {
            EditSalesOrderDialog_Click(sender, e);
        }
    }

}
