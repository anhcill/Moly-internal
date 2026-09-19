using Microsoft.EntityFrameworkCore;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.InternalData;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Entities.Documents;

namespace InternalManagement.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    // Identity
    DbSet<Company> Companies { get; }
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<Branch> Branches { get; }
    DbSet<Department> Departments { get; }
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRefreshToken> UserRefreshTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Document> Documents { get; }

    // Shared master data & document registry
    DbSet<Party> Parties { get; }
    DbSet<PartyContact> PartyContacts { get; }
    DbSet<PartyExternalIdentity> PartyExternalIdentities { get; }
    DbSet<PartyBusinessProfile> PartyBusinessProfiles { get; }
    DbSet<BusinessDocument> BusinessDocuments { get; }
    DbSet<BusinessDocumentLink> BusinessDocumentLinks { get; }

    // Integration
    DbSet<IntegrationSource> IntegrationSources { get; }
    DbSet<IntegrationRun> IntegrationRuns { get; }
    DbSet<IntegrationInbox> IntegrationInboxes { get; }
    DbSet<IntegrationDeadLetter> IntegrationDeadLetters { get; }
    DbSet<LmsAccountLink> LmsAccountLinks { get; }
    DbSet<LmsCourseLink> LmsCourseLinks { get; }
    DbSet<LmsAccessGrant> LmsAccessGrants { get; }
    DbSet<IntegrationOutbox> IntegrationOutboxes { get; }
    DbSet<LmsSyncStatus> LmsSyncStatuses { get; }
    DbSet<ManagedApplication> ManagedApplications { get; }
    DbSet<ApplicationMembership> ApplicationMemberships { get; }
    DbSet<ApplicationRoleAssignment> ApplicationRoleAssignments { get; }
    DbSet<ApplicationCourseMap> ApplicationCourseMaps { get; }
    DbSet<ApplicationClassMap> ApplicationClassMaps { get; }
    DbSet<ApplicationEntitlement> ApplicationEntitlements { get; }

    // EdTech
    DbSet<Course> Courses { get; }
    DbSet<CourseModule> CourseModules { get; }
    DbSet<Subject> Subjects { get; }
    DbSet<Topic> Topics { get; }
    DbSet<QuestionBank> QuestionBanks { get; }
    DbSet<Question> Questions { get; }
    DbSet<QuestionVersion> QuestionVersions { get; }
    DbSet<QuestionChoice> QuestionChoices { get; }
    DbSet<QuestionTag> QuestionTags { get; }
    DbSet<ContentPublication> ContentPublications { get; }
    DbSet<EdTechCustomer> EdTechCustomers { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<Payment> Payments { get; }

    // CSCA & Interview
    DbSet<CscaClass> CscaClasses { get; }
    DbSet<CscaClassStudent> CscaClassStudents { get; }
    DbSet<CscaClassStaff> CscaClassStaffs { get; }
    DbSet<CscaClassSchedule> CscaClassSchedules { get; }
    DbSet<CscaClassroom> CscaClassrooms { get; }
    DbSet<CscaLessonSession> CscaLessonSessions { get; }
    DbSet<CscaLessonAttendance> CscaLessonAttendances { get; }
    DbSet<InterviewCustomer> InterviewCustomers { get; }
    DbSet<ProfitAllocation> ProfitAllocations { get; }

    // HR & Payroll
    DbSet<Employee> Employees { get; }
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollPolicyVersion> PayrollPolicyVersions { get; }
    DbSet<PayrollAdjustment> PayrollAdjustments { get; }
    DbSet<Payslip> Payslips { get; }
    DbSet<PayrollApproval> PayrollApprovals { get; }

    // Fashion & Inventory
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<PurchaseReceipt> PurchaseReceipts { get; }
    DbSet<PurchaseReceiptItem> PurchaseReceiptItems { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesOrderItem> SalesOrderItems { get; }
    DbSet<Return> Returns { get; }
    DbSet<ReturnItem> ReturnItems { get; }
    DbSet<SalesSettlement> SalesSettlements { get; }
    DbSet<SalesDocument> SalesDocuments { get; }
    DbSet<SalesDocumentItem> SalesDocumentItems { get; }
    DbSet<Material> Materials { get; }
    DbSet<MaterialLot> MaterialLots { get; }
    DbSet<MaterialMovement> MaterialMovements { get; }
    DbSet<Bom> Boms { get; }
    DbSet<BomItem> BomItems { get; }
    DbSet<ProductionOrder> ProductionOrders { get; }
    DbSet<ProductionOrderOutput> ProductionOrderOutputs { get; }
    DbSet<ProductionOrderMaterial> ProductionOrderMaterials { get; }
    DbSet<ProductionOperation> ProductionOperations { get; }
    DbSet<ChannelFeePolicy> ChannelFeePolicies { get; }
    DbSet<OrderCostSnapshot> OrderCostSnapshots { get; }
    DbSet<OrderCostSnapshotItem> OrderCostSnapshotItems { get; }

    // Finance
    DbSet<FinanceCategory> FinanceCategories { get; }
    DbSet<FinanceTransaction> FinanceTransactions { get; }
    DbSet<CashAccount> CashAccounts { get; }
    DbSet<MonthlyClosing> MonthlyClosings { get; }

    // Internal data & customer directories (strictly split by business segment)
    DbSet<InternalCustomer> InternalCustomers { get; }
    DbSet<InternalResource> InternalResources { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
