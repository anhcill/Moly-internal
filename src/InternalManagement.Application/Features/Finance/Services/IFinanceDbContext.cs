using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Application.Features.Finance.Services;

public interface IFinanceDbContext
{
    DbSet<BusinessDocumentLink> BusinessDocumentLinks { get; }
    DbSet<BusinessDocument> BusinessDocuments { get; }
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<CashAccount> CashAccounts { get; }
    DbSet<Company> Companies { get; }
    DbSet<CscaClass> CscaClasses { get; }
    DbSet<FinanceCategory> FinanceCategories { get; }
    DbSet<FinanceTransaction> FinanceTransactions { get; }
    DbSet<InterviewCustomer> InterviewCustomers { get; }
    DbSet<OrderCostSnapshot> OrderCostSnapshots { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<ProductionOrderOutput> ProductionOrderOutputs { get; }
    DbSet<ProductionOrder> ProductionOrders { get; }
    DbSet<ProfitAllocation> ProfitAllocations { get; }
    DbSet<Return> Returns { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesSettlement> SalesSettlements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
