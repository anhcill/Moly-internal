namespace InternalManagement.Domain.Enums;

public enum BusinessUnitCode
{
    EDTECH,
    CSCA,
    INTERVIEW,
    FASHION,
    HQ
}

public enum UserRoleType
{
    SuperAdmin,
    SystemAdmin,
    Director,
    HrStaff,
    PayrollAccountant,
    ContentEditor,
    QuestionAuthor,
    ContentReviewer,
    Teacher,
    TeachingAssistant,
    CustomerService,
    WarehouseManager,
    OrderManager,
    PaymentAccountant,
    Employee
}

public enum PaymentStatus
{
    Pending,
    Partial,
    Paid,
    Failed,
    Refunded,
    Cancelled
}

public enum SubscriptionStatus
{
    Pending,
    Active,
    Expired,
    Canceled
}

public enum QuestionPublicationStatus
{
    Draft,
    UnderReview,
    Published,
    Archived
}

public enum PayrollStatus
{
    Draft,
    Calculated,
    Reviewing,
    Approved,
    Paid,
    Published,
    /// <summary>
    /// Kỳ lương chỉ được hủy khi còn bản nháp. Dữ liệu lịch sử/audit được giữ lại,
    /// tuyệt đối không xóa cứng kỳ lương để tránh mất dấu nghiệp vụ.
    /// </summary>
    Cancelled
}

/// <summary>
/// Loại hợp đồng dùng để chọn công thức tính lương.
/// Tên giá trị được giữ ổn định để API/UI có thể dùng trực tiếp.
/// </summary>
public enum EmploymentType
{
    FULL_TIME,
    PART_TIME
}

/// <summary>
/// Đơn vị tính lương áp dụng cho nhân sự part-time.
/// </summary>
public enum PartTimeCalculationMethod
{
    HOURLY,
    SHIFT
}

public enum InventoryMovementType
{
    PurchaseReceipt,
    SalesOrderDelivery,
    CustomerReturn,
    SupplierReturn,
    StockAdjustment,
    Scrap,
    ProductionOutput,
    MaterialConsumption,
    ProductionScrap
}

public enum SourcingType
{
    Make,
    Buy
}

public enum MaterialMovementType
{
    PurchaseReceipt,
    ProductionConsumption,
    StockAdjustment,
    Scrap,
    Return
}

public enum ProductionOrderStatus
{
    Draft,
    Released,
    InProgress,
    Completed,
    Closed,
    Cancelled
}

public enum CostStatus
{
    Standard,
    Estimated,
    Actual,
    Provisional
}

public enum SalesOrderStatus
{
    Pending,
    Processing,
    Completed,
    Cancelled,
    Returned
}

public enum SalesDocumentType
{
    Invoice,
    RetailReceipt
}

/// <summary>Direction of a confirmed cash movement for a Fashion order.</summary>
public enum SalesSettlementKind
{
    CustomerPayment,
    CustomerRefund
}

/// <summary>
/// A settlement must be explicitly confirmed before it is posted to finance.
/// Approval of a product return is deliberately not itself a money movement.
/// </summary>
public enum SalesSettlementStatus
{
    Pending,
    Confirmed,
    Failed,
    Voided
}

public enum IntegrationStatus
{
    Pending,
    Processing,
    Success,
    Failed,
    DeadLetter,
    Skipped
}

/// <summary>
/// Provisioning state of the account stored by CSCA Course LMS. This is not
/// a payment status: an account can exist while the learner has no access.
/// </summary>
public enum LmsAccountStatus
{
    PendingPayment,
    Active,
    Suspended,
    Revoked,
    ProvisioningFailed
}

/// <summary>
/// Per-course entitlement state sent to and enforced by CSCA Course LMS.
/// </summary>
public enum LmsAccessGrantStatus
{
    PendingPayment,
    Active,
    Suspended,
    Revoked,
    Expired,
    SyncFailed
}

/// <summary>Lifecycle of a website/application registered in InternalManagement.</summary>
public enum ManagedApplicationStatus
{
    Active,
    Disabled,
    Retired
}

/// <summary>Lifecycle of a canonical Party inside one managed application.</summary>
public enum ApplicationMembershipStatus
{
    Pending,
    Active,
    Suspended,
    Revoked
}

/// <summary>Scope carried by a website role assignment.</summary>
public enum ApplicationScopeType
{
    Application,
    Course,
    Class
}

public enum ApplicationRoleAssignmentStatus
{
    Active,
    Revoked
}

/// <summary>Central access state sent to a managed application.</summary>
public enum ApplicationEntitlementStatus
{
    PendingPayment,
    Active,
    Suspended,
    Revoked,
    Expired
}

public enum TransactionType
{
    Income,
    Expense
}
