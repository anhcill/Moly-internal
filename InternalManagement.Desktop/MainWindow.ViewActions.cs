using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InternalManagement.Desktop.Views;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private void DispatchFeatureViewAction(object? sender, FeatureViewActionEventArgs action)
    {
        switch (action.Action)
        {
            case nameof(UsernameTextBox_GotFocus): UsernameTextBox_GotFocus(action.Sender, action.Args); break;
            case nameof(PasswordBox_GotFocus): PasswordBox_GotFocus(action.Sender, action.Args); break;
            case nameof(PasswordBox_PasswordChanged): PasswordBox_PasswordChanged(action.Sender, action.Args); break;
            case nameof(VisiblePasswordTextBox_GotFocus): VisiblePasswordTextBox_GotFocus(action.Sender, action.Args); break;
            case nameof(CancelForgotPassword_Click): CancelForgotPassword_Click(action.Sender, action.Args); break;
            case nameof(ForgotPasswordButton_Click): ForgotPasswordButton_Click(action.Sender, action.Args); break;
            case nameof(LoginButton_Click): LoginButton_Click(action.Sender, action.Args); break;
            case nameof(LoginServerConfigButton_Click): LoginServerConfigButton_Click(action.Sender, action.Args); break;
            case nameof(PasswordVisibilityButton_Click): PasswordVisibilityButton_Click(action.Sender, action.Args); break;
            case nameof(RememberMeCheckBox_Click): RememberMeCheckBox_Click(action.Sender, action.Args); break;
            case nameof(SubmitForgotPassword_Click): SubmitForgotPassword_Click(action.Sender, action.Args); break;
            case nameof(InputBox_KeyDown): InputBox_KeyDown(action.Sender, (KeyEventArgs)action.Args); break;
            case nameof(PasswordBox_LostFocus): PasswordBox_LostFocus(action.Sender, action.Args); break;
            case nameof(UsernameTextBox_LostFocus): UsernameTextBox_LostFocus(action.Sender, action.Args); break;
            case nameof(VisiblePasswordTextBox_LostFocus): VisiblePasswordTextBox_LostFocus(action.Sender, action.Args); break;
            case nameof(UsernameTextBox_TextChanged): UsernameTextBox_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(VisiblePasswordTextBox_TextChanged): VisiblePasswordTextBox_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(ClearCscaCourseFilter_Click): ClearCscaCourseFilter_Click(action.Sender, action.Args); break;
            case nameof(CreateCourseDialog_Click): CreateCourseDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateCscaClassDialog_Click): CreateCscaClassDialog_Click(action.Sender, action.Args); break;
            case nameof(DeleteCourse_Click): DeleteCourse_Click(action.Sender, action.Args); break;
            case nameof(DeleteCscaClass_Click): DeleteCscaClass_Click(action.Sender, action.Args); break;
            case nameof(EditCourseDialog_Click): EditCourseDialog_Click(action.Sender, action.Args); break;
            case nameof(EditCscaClass_Click): EditCscaClass_Click(action.Sender, action.Args); break;
            case nameof(RefreshCourses_Click): RefreshCourses_Click(action.Sender, action.Args); break;
            case nameof(RefreshCsca_Click): RefreshCsca_Click(action.Sender, action.Args); break;
            case nameof(ViewCourseClasses_Click): ViewCourseClasses_Click(action.Sender, action.Args); break;
            case nameof(ViewCscaClassDetails_Click): ViewCscaClassDetails_Click(action.Sender, action.Args); break;
            case nameof(CoursesDataGrid_MouseDoubleClick): CoursesDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(CscaClassesDataGrid_MouseDoubleClick): CscaClassesDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(CourseModuleTabControl_SelectionChanged): CourseModuleTabControl_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(CscaCourseFilterComboBox_SelectionChanged): CscaCourseFilterComboBox_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(CourseSearch_TextChanged): CourseSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(CscaSearch_TextChanged): CscaSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(AutoGenerateSlug_Click): AutoGenerateSlug_Click(action.Sender, action.Args); break;
            case nameof(DispatchLmsOutbox_Click): DispatchLmsOutbox_Click(action.Sender, action.Args); break;
            case nameof(RefreshLmsIntegration_Click): RefreshLmsIntegration_Click(action.Sender, action.Args); break;
            case nameof(RetryLmsOutbox_Click): RetryLmsOutbox_Click(action.Sender, action.Args); break;
            case nameof(SaveLmsMapping_Click): SaveLmsMapping_Click(action.Sender, action.Args); break;
            case nameof(LmsCourseMappingsDataGrid_SelectionChanged): LmsCourseMappingsDataGrid_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(AddVariantDialog_Click): AddVariantDialog_Click(action.Sender, action.Args); break;
            case nameof(CancelPurchaseReceipt_Click): CancelPurchaseReceipt_Click(action.Sender, action.Args); break;
            case nameof(CancelSalesOrder_Click): CancelSalesOrder_Click(action.Sender, action.Args); break;
            case nameof(CreateMaterialDialog_Click): CreateMaterialDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateProductDialog_Click): CreateProductDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateProductionOrderDialog_Click): CreateProductionOrderDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateReceiptDialog_Click): CreateReceiptDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateSalesOrderDialog_Click): CreateSalesOrderDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateSupplierDialog_Click): CreateSupplierDialog_Click(action.Sender, action.Args); break;
            case nameof(CreateWarehouseDialog_Click): CreateWarehouseDialog_Click(action.Sender, action.Args); break;
            case nameof(DeleteFashionProduct_Click): DeleteFashionProduct_Click(action.Sender, action.Args); break;
            case nameof(DeleteFashionVariant_Click): DeleteFashionVariant_Click(action.Sender, action.Args); break;
            case nameof(DeleteSupplier_Click): DeleteSupplier_Click(action.Sender, action.Args); break;
            case nameof(DeleteWarehouse_Click): DeleteWarehouse_Click(action.Sender, action.Args); break;
            case nameof(DeliverSalesOrder_Click): DeliverSalesOrder_Click(action.Sender, action.Args); break;
            case nameof(EditFashionProductDialog_Click): EditFashionProductDialog_Click(action.Sender, action.Args); break;
            case nameof(EditFashionVariantDialog_Click): EditFashionVariantDialog_Click(action.Sender, action.Args); break;
            case nameof(EditPurchaseReceiptDialog_Click): EditPurchaseReceiptDialog_Click(action.Sender, action.Args); break;
            case nameof(EditSalesOrderDialog_Click): EditSalesOrderDialog_Click(action.Sender, action.Args); break;
            case nameof(EditSupplierDialog_Click): EditSupplierDialog_Click(action.Sender, action.Args); break;
            case nameof(EditWarehouseDialog_Click): EditWarehouseDialog_Click(action.Sender, action.Args); break;
            case nameof(ExportInventory_Click): ExportInventory_Click(action.Sender, action.Args); break;
            case nameof(IssueSalesOrderDocument_Click): IssueSalesOrderDocument_Click(action.Sender, action.Args); break;
            case nameof(OpenReceiptAttachment_Click): OpenReceiptAttachment_Click(action.Sender, action.Args); break;
            case nameof(OpenSalesOrders_Click): OpenSalesOrders_Click(action.Sender, action.Args); break;
            case nameof(RecordSalesOrderPayment_Click): RecordSalesOrderPayment_Click(action.Sender, action.Args); break;
            case nameof(RefreshFashionProducts_Click): RefreshFashionProducts_Click(action.Sender, action.Args); break;
            case nameof(RefreshFashionVariants_Click): RefreshFashionVariants_Click(action.Sender, action.Args); break;
            case nameof(RefreshInventory_Click): RefreshInventory_Click(action.Sender, action.Args); break;
            case nameof(RefreshInventoryMovements_Click): RefreshInventoryMovements_Click(action.Sender, action.Args); break;
            case nameof(RefreshManufacturing_Click): RefreshManufacturing_Click(action.Sender, action.Args); break;
            case nameof(RefreshReceipts_Click): RefreshReceipts_Click(action.Sender, action.Args); break;
            case nameof(RefreshSalesOrders_Click): RefreshSalesOrders_Click(action.Sender, action.Args); break;
            case nameof(RefreshSelectedInventoryMovements_Click): RefreshSelectedInventoryMovements_Click(action.Sender, action.Args); break;
            case nameof(RefreshWarehouses_Click): RefreshWarehouses_Click(action.Sender, action.Args); break;
            case nameof(SimulatePricingDialog_Click): SimulatePricingDialog_Click(action.Sender, action.Args); break;
            case nameof(StockAdjustmentDialog_Click): StockAdjustmentDialog_Click(action.Sender, action.Args); break;
            case nameof(UploadReceiptAttachment_Click): UploadReceiptAttachment_Click(action.Sender, action.Args); break;
            case nameof(ViewSalesOrderDetails_Click): ViewSalesOrderDetails_Click(action.Sender, action.Args); break;
            case nameof(FashionProductsDataGrid_MouseDoubleClick): FashionProductsDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(FashionVariantsDataGrid_MouseDoubleClick): FashionVariantsDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(InventoryBalancesDataGrid_MouseDoubleClick): InventoryBalancesDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(InventoryMovementsDataGrid_MouseDoubleClick): InventoryMovementsDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(PurchaseReceiptsDataGrid_MouseDoubleClick): PurchaseReceiptsDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(SalesOrdersDataGrid_MouseDoubleClick): SalesOrdersDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(SuppliersDataGrid_MouseDoubleClick): SuppliersDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(WarehousesDataGrid_MouseDoubleClick): WarehousesDataGrid_MouseDoubleClick(action.Sender, (MouseButtonEventArgs)action.Args); break;
            case nameof(FashionCollectionFilterCombo_SelectionChanged): FashionCollectionFilterCombo_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(FashionOrderStatusFilterCombo_SelectionChanged): FashionOrderStatusFilterCombo_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(FashionProductsDataGrid_SelectionChanged): FashionProductsDataGrid_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(FashionTabs_SelectionChanged): FashionTabs_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(FashionVariantProductSelector_SelectionChanged): FashionVariantProductSelector_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(InventoryBalancesDataGrid_SelectionChanged): InventoryBalancesDataGrid_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(InventoryWarehouseSelector_SelectionChanged): InventoryWarehouseSelector_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(SalesOrderFilter_SelectionChanged): SalesOrderFilter_SelectionChanged(action.Sender, (SelectionChangedEventArgs)action.Args); break;
            case nameof(FashionProductSearch_TextChanged): FashionProductSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(InventorySearch_TextChanged): InventorySearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(ProductionOrderSearch_TextChanged): ProductionOrderSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(ReceiptSearch_TextChanged): ReceiptSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(SalesOrderSearch_TextChanged): SalesOrderSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            case nameof(SupplierSearch_TextChanged): SupplierSearch_TextChanged(action.Sender, (TextChangedEventArgs)action.Args); break;
            default: throw new InvalidOperationException($"Unhandled feature view action: {action.Action}");
        }
    }
}
