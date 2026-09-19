using System.Reflection;
using Microsoft.EntityFrameworkCore;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Common;
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

namespace InternalManagement.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext, IApplicationDbContext
{
    private readonly ICurrentUserService? _currentUserService;
    private readonly IDateTimeProvider? _dateTimeProvider;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUserService = null,
        IDateTimeProvider? dateTimeProvider = null)
        : base(options)
    {
        _currentUserService = currentUserService;
        _dateTimeProvider = dateTimeProvider;
    }

    // Identity
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRefreshToken> UserRefreshTokens => Set<UserRefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Document> Documents => Set<Document>();

    // Shared master data & document registry
    public DbSet<Party> Parties => Set<Party>();
    public DbSet<PartyContact> PartyContacts => Set<PartyContact>();
    public DbSet<PartyExternalIdentity> PartyExternalIdentities => Set<PartyExternalIdentity>();
    public DbSet<PartyBusinessProfile> PartyBusinessProfiles => Set<PartyBusinessProfile>();
    public DbSet<BusinessDocument> BusinessDocuments => Set<BusinessDocument>();
    public DbSet<BusinessDocumentLink> BusinessDocumentLinks => Set<BusinessDocumentLink>();

    // Integration
    public DbSet<IntegrationSource> IntegrationSources => Set<IntegrationSource>();
    public DbSet<IntegrationRun> IntegrationRuns => Set<IntegrationRun>();
    public DbSet<IntegrationInbox> IntegrationInboxes => Set<IntegrationInbox>();
    public DbSet<IntegrationDeadLetter> IntegrationDeadLetters => Set<IntegrationDeadLetter>();
    public DbSet<LmsAccountLink> LmsAccountLinks => Set<LmsAccountLink>();
    public DbSet<LmsCourseLink> LmsCourseLinks => Set<LmsCourseLink>();
    public DbSet<LmsAccessGrant> LmsAccessGrants => Set<LmsAccessGrant>();
    public DbSet<IntegrationOutbox> IntegrationOutboxes => Set<IntegrationOutbox>();
    public DbSet<LmsSyncStatus> LmsSyncStatuses => Set<LmsSyncStatus>();
    public DbSet<ManagedApplication> ManagedApplications => Set<ManagedApplication>();
    public DbSet<ApplicationMembership> ApplicationMemberships => Set<ApplicationMembership>();
    public DbSet<ApplicationRoleAssignment> ApplicationRoleAssignments => Set<ApplicationRoleAssignment>();
    public DbSet<ApplicationCourseMap> ApplicationCourseMaps => Set<ApplicationCourseMap>();
    public DbSet<ApplicationClassMap> ApplicationClassMaps => Set<ApplicationClassMap>();
    public DbSet<ApplicationEntitlement> ApplicationEntitlements => Set<ApplicationEntitlement>();

    // EdTech
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseModule> CourseModules => Set<CourseModule>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<QuestionBank> QuestionBanks => Set<QuestionBank>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionVersion> QuestionVersions => Set<QuestionVersion>();
    public DbSet<QuestionChoice> QuestionChoices => Set<QuestionChoice>();
    public DbSet<QuestionTag> QuestionTags => Set<QuestionTag>();
    public DbSet<ContentPublication> ContentPublications => Set<ContentPublication>();
    public DbSet<EdTechCustomer> EdTechCustomers => Set<EdTechCustomer>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Payment> Payments => Set<Payment>();

    // CSCA & Interview
    public DbSet<CscaClass> CscaClasses => Set<CscaClass>();
    public DbSet<CscaClassStudent> CscaClassStudents => Set<CscaClassStudent>();
    public DbSet<CscaClassStaff> CscaClassStaffs => Set<CscaClassStaff>();
    public DbSet<CscaClassroom> CscaClassrooms => Set<CscaClassroom>();
    public DbSet<CscaClassSchedule> CscaClassSchedules => Set<CscaClassSchedule>();
    public DbSet<CscaLessonSession> CscaLessonSessions => Set<CscaLessonSession>();
    public DbSet<CscaLessonAttendance> CscaLessonAttendances => Set<CscaLessonAttendance>();
    public DbSet<InterviewCustomer> InterviewCustomers => Set<InterviewCustomer>();
    public DbSet<ProfitAllocation> ProfitAllocations => Set<ProfitAllocation>();

    // HR & Payroll
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
    public DbSet<PayrollPeriod> PayrollPeriods => Set<PayrollPeriod>();
    public DbSet<PayrollPolicyVersion> PayrollPolicyVersions => Set<PayrollPolicyVersion>();
    public DbSet<PayrollAdjustment> PayrollAdjustments => Set<PayrollAdjustment>();
    public DbSet<Payslip> Payslips => Set<Payslip>();
    public DbSet<PayrollApproval> PayrollApprovals => Set<PayrollApproval>();

    // Fashion & Inventory
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseReceipt> PurchaseReceipts => Set<PurchaseReceipt>();
    public DbSet<PurchaseReceiptItem> PurchaseReceiptItems => Set<PurchaseReceiptItem>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<Return> Returns => Set<Return>();
    public DbSet<ReturnItem> ReturnItems => Set<ReturnItem>();
    public DbSet<SalesSettlement> SalesSettlements => Set<SalesSettlement>();
    public DbSet<SalesDocument> SalesDocuments => Set<SalesDocument>();
    public DbSet<SalesDocumentItem> SalesDocumentItems => Set<SalesDocumentItem>();
    public DbSet<Material> Materials => Set<Material>();
    public DbSet<MaterialLot> MaterialLots => Set<MaterialLot>();
    public DbSet<MaterialMovement> MaterialMovements => Set<MaterialMovement>();
    public DbSet<Bom> Boms => Set<Bom>();
    public DbSet<BomItem> BomItems => Set<BomItem>();
    public DbSet<ProductionOrder> ProductionOrders => Set<ProductionOrder>();
    public DbSet<ProductionOrderOutput> ProductionOrderOutputs => Set<ProductionOrderOutput>();
    public DbSet<ProductionOrderMaterial> ProductionOrderMaterials => Set<ProductionOrderMaterial>();
    public DbSet<ProductionOperation> ProductionOperations => Set<ProductionOperation>();
    public DbSet<ChannelFeePolicy> ChannelFeePolicies => Set<ChannelFeePolicy>();
    public DbSet<OrderCostSnapshot> OrderCostSnapshots => Set<OrderCostSnapshot>();
    public DbSet<OrderCostSnapshotItem> OrderCostSnapshotItems => Set<OrderCostSnapshotItem>();

    // Finance
    public DbSet<FinanceCategory> FinanceCategories => Set<FinanceCategory>();
    public DbSet<FinanceTransaction> FinanceTransactions => Set<FinanceTransaction>();
    public DbSet<CashAccount> CashAccounts => Set<CashAccount>();
    public DbSet<MonthlyClosing> MonthlyClosings => Set<MonthlyClosing>();

    // Internal data & customer directories
    public DbSet<InternalCustomer> InternalCustomers => Set<InternalCustomer>();
    public DbSet<InternalResource> InternalResources => Set<InternalResource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = _dateTimeProvider?.UtcNow ?? DateTime.UtcNow;
        var currentUsername = _currentUserService?.Username ?? "System";

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = utcNow;
                    entry.Entity.CreatedBy = currentUsername;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = utcNow;
                    entry.Entity.UpdatedBy = currentUsername;
                    break;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = utcNow;
                entry.Entity.DeletedBy = currentUsername;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
