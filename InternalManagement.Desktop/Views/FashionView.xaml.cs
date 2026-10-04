using System.Windows;
using System.Windows.Controls;

namespace InternalManagement.Desktop.Views;

public partial class FashionView : UserControl
{
    public FashionView() => InitializeComponent();

    public event EventHandler<FeatureViewActionEventArgs>? ActionRequested;

    private void AddVariantDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(AddVariantDialog_Click), sender, e));

    private void CancelPurchaseReceipt_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CancelPurchaseReceipt_Click), sender, e));

    private void CancelSalesOrder_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CancelSalesOrder_Click), sender, e));

    private void CreateMaterialDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateMaterialDialog_Click), sender, e));

    private void CreateProductDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateProductDialog_Click), sender, e));

    private void CreateProductionOrderDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateProductionOrderDialog_Click), sender, e));

    private void CreateReceiptDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateReceiptDialog_Click), sender, e));

    private void CreateSalesOrderDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateSalesOrderDialog_Click), sender, e));

    private void CreateSupplierDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateSupplierDialog_Click), sender, e));

    private void CreateWarehouseDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(CreateWarehouseDialog_Click), sender, e));

    private void DeleteFashionProduct_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeleteFashionProduct_Click), sender, e));

    private void DeleteFashionVariant_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeleteFashionVariant_Click), sender, e));

    private void DeleteSupplier_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeleteSupplier_Click), sender, e));

    private void DeleteWarehouse_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeleteWarehouse_Click), sender, e));

    private void DeliverSalesOrder_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(DeliverSalesOrder_Click), sender, e));

    private void EditFashionProductDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditFashionProductDialog_Click), sender, e));

    private void EditFashionVariantDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditFashionVariantDialog_Click), sender, e));

    private void EditPurchaseReceiptDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditPurchaseReceiptDialog_Click), sender, e));

    private void EditSalesOrderDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditSalesOrderDialog_Click), sender, e));

    private void EditSupplierDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditSupplierDialog_Click), sender, e));

    private void EditWarehouseDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(EditWarehouseDialog_Click), sender, e));

    private void ExportInventory_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ExportInventory_Click), sender, e));

    private void IssueSalesOrderDocument_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(IssueSalesOrderDocument_Click), sender, e));

    private void OpenReceiptAttachment_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(OpenReceiptAttachment_Click), sender, e));

    private void OpenSalesOrders_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(OpenSalesOrders_Click), sender, e));

    private void RecordSalesOrderPayment_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RecordSalesOrderPayment_Click), sender, e));

    private void RefreshFashionProducts_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshFashionProducts_Click), sender, e));

    private void RefreshFashionVariants_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshFashionVariants_Click), sender, e));

    private void RefreshInventory_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshInventory_Click), sender, e));

    private void RefreshInventoryMovements_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshInventoryMovements_Click), sender, e));

    private void RefreshManufacturing_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshManufacturing_Click), sender, e));

    private void RefreshReceipts_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshReceipts_Click), sender, e));

    private void RefreshSalesOrders_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshSalesOrders_Click), sender, e));

    private void RefreshSelectedInventoryMovements_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshSelectedInventoryMovements_Click), sender, e));

    private void RefreshWarehouses_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(RefreshWarehouses_Click), sender, e));

    private void SimulatePricingDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SimulatePricingDialog_Click), sender, e));

    private void StockAdjustmentDialog_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(StockAdjustmentDialog_Click), sender, e));

    private void UploadReceiptAttachment_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(UploadReceiptAttachment_Click), sender, e));

    private void ViewSalesOrderDetails_Click(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ViewSalesOrderDetails_Click), sender, e));

    private void FashionProductsDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionProductsDataGrid_MouseDoubleClick), sender, e));

    private void FashionVariantsDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionVariantsDataGrid_MouseDoubleClick), sender, e));

    private void InventoryBalancesDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(InventoryBalancesDataGrid_MouseDoubleClick), sender, e));

    private void InventoryMovementsDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(InventoryMovementsDataGrid_MouseDoubleClick), sender, e));

    private void PurchaseReceiptsDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(PurchaseReceiptsDataGrid_MouseDoubleClick), sender, e));

    private void SalesOrdersDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SalesOrdersDataGrid_MouseDoubleClick), sender, e));

    private void SuppliersDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SuppliersDataGrid_MouseDoubleClick), sender, e));

    private void WarehousesDataGrid_MouseDoubleClick(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(WarehousesDataGrid_MouseDoubleClick), sender, e));

    private void FashionCollectionFilterCombo_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionCollectionFilterCombo_SelectionChanged), sender, e));

    private void FashionOrderStatusFilterCombo_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionOrderStatusFilterCombo_SelectionChanged), sender, e));

    private void FashionProductsDataGrid_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionProductsDataGrid_SelectionChanged), sender, e));

    private void FashionTabs_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionTabs_SelectionChanged), sender, e));

    private void FashionVariantProductSelector_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionVariantProductSelector_SelectionChanged), sender, e));

    private void InventoryBalancesDataGrid_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(InventoryBalancesDataGrid_SelectionChanged), sender, e));

    private void InventoryWarehouseSelector_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(InventoryWarehouseSelector_SelectionChanged), sender, e));

    private void SalesOrderFilter_SelectionChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SalesOrderFilter_SelectionChanged), sender, e));

    private void FashionProductSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(FashionProductSearch_TextChanged), sender, e));

    private void InventorySearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(InventorySearch_TextChanged), sender, e));

    private void ProductionOrderSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ProductionOrderSearch_TextChanged), sender, e));

    private void ReceiptSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(ReceiptSearch_TextChanged), sender, e));

    private void SalesOrderSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SalesOrderSearch_TextChanged), sender, e));

    private void SupplierSearch_TextChanged(object sender, RoutedEventArgs e) =>
        ActionRequested?.Invoke(this, new FeatureViewActionEventArgs(nameof(SupplierSearch_TextChanged), sender, e));
}
