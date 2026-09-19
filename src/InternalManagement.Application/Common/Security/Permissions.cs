namespace InternalManagement.Application.Common.Security;

public static class Permissions
{
    // Auth & Identity
    public const string UsersView = "Permissions.Users.View";
    public const string UsersManage = "Permissions.Users.Manage";
    public const string RolesView = "Permissions.Roles.View";
    public const string RolesManage = "Permissions.Roles.Manage";
    public const string AuditLogsView = "Permissions.AuditLogs.View";

    // EdTech & Content
    public const string CoursesView = "Permissions.Courses.View";
    public const string CoursesManage = "Permissions.Courses.Manage";
    public const string QuestionsView = "Permissions.Questions.View";
    public const string QuestionsManage = "Permissions.Questions.Manage";
    public const string QuestionsPublish = "Permissions.Questions.Publish";
    public const string EdTechCustomersView = "Permissions.EdTechCustomers.View";
    public const string SubscriptionsManage = "Permissions.Subscriptions.Manage";
    public const string PaymentsView = "Permissions.Payments.View";

    // CSCA & Interview
    public const string CscaClassesView = "Permissions.CscaClasses.View";
    public const string CscaClassesManage = "Permissions.CscaClasses.Manage";
    public const string CscaStudentsManage = "Permissions.CscaStudents.Manage";
    public const string InterviewCustomersView = "Permissions.InterviewCustomers.View";
    public const string InterviewCustomersManage = "Permissions.InterviewCustomers.Manage";

    // HR & Payroll
    public const string EmployeesView = "Permissions.Employees.View";
    public const string EmployeesManage = "Permissions.Employees.Manage";
    public const string AttendanceImport = "Permissions.Attendance.Import";
    public const string PayrollViewAll = "Permissions.Payroll.ViewAll";
    public const string PayrollCalculate = "Permissions.Payroll.Calculate";
    public const string PayrollApprove = "Permissions.Payroll.Approve";
    public const string PayrollPublish = "Permissions.Payroll.Publish";
    public const string PayrollViewPersonal = "Permissions.Payroll.ViewPersonal";

    // Fashion & Inventory
    public const string ProductsView = "Permissions.Products.View";
    public const string ProductsManage = "Permissions.Products.Manage";
    public const string InventoryView = "Permissions.Inventory.View";
    public const string InventoryReceipt = "Permissions.Inventory.Receipt";
    public const string InventoryAdjust = "Permissions.Inventory.Adjust";
    public const string MaterialsView = "Permissions.Materials.View";
    public const string MaterialsManage = "Permissions.Materials.Manage";
    public const string ManufacturingView = "Permissions.Manufacturing.View";
    public const string ManufacturingManage = "Permissions.Manufacturing.Manage";
    public const string OrdersView = "Permissions.Orders.View";
    public const string OrdersManage = "Permissions.Orders.Manage";
    public const string ReturnsManage = "Permissions.Returns.Manage";
    public const string PricingSimulator = "Permissions.Pricing.Simulate";
    public const string ChannelFeePolicyManage = "Permissions.Pricing.PolicyManage";

    // Finance
    public const string FinanceTransactionsView = "Permissions.FinanceTransactions.View";
    public const string FinanceTransactionsManage = "Permissions.FinanceTransactions.Manage";
    public const string FinanceReportsView = "Permissions.FinanceReports.View";

    // Internal data & customer directories
    public const string InternalCustomersView = "Permissions.InternalCustomers.View";
    public const string InternalCustomersManage = "Permissions.InternalCustomers.Manage";
    public const string InternalResourcesView = "Permissions.InternalResources.View";
    public const string InternalResourcesManage = "Permissions.InternalResources.Manage";

    // System & Sync
    public const string SystemSyncView = "Permissions.SystemSync.View";
    public const string SystemSyncTrigger = "Permissions.SystemSync.Trigger";
    public const string DeadLettersManage = "Permissions.DeadLetters.Manage";

    public static readonly IReadOnlyList<string> All = new[]
    {
        UsersView, UsersManage, RolesView, RolesManage, AuditLogsView,
        CoursesView, CoursesManage, QuestionsView, QuestionsManage, QuestionsPublish, EdTechCustomersView, SubscriptionsManage, PaymentsView,
        CscaClassesView, CscaClassesManage, CscaStudentsManage, InterviewCustomersView, InterviewCustomersManage,
        EmployeesView, EmployeesManage, AttendanceImport, PayrollViewAll, PayrollCalculate, PayrollApprove, PayrollPublish, PayrollViewPersonal,
        ProductsView, ProductsManage, InventoryView, InventoryReceipt, InventoryAdjust, MaterialsView, MaterialsManage, ManufacturingView, ManufacturingManage, OrdersView, OrdersManage, ReturnsManage, PricingSimulator, ChannelFeePolicyManage,
        FinanceTransactionsView, FinanceTransactionsManage, FinanceReportsView,
        InternalCustomersView, InternalCustomersManage, InternalResourcesView, InternalResourcesManage,
        SystemSyncView, SystemSyncTrigger, DeadLettersManage
    };
}
