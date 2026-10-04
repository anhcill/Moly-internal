using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Application.Features.Fashion.Services;

public interface IOrderDbContext
{
    DbSet<BusinessDocument> BusinessDocuments { get; }
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<ChannelFeePolicy> ChannelFeePolicies { get; }
    DbSet<Company> Companies { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<OrderCostSnapshot> OrderCostSnapshots { get; }
    DbSet<ProductVariant> ProductVariants { get; }
    DbSet<ReturnItem> ReturnItems { get; }
    DbSet<Return> Returns { get; }
    DbSet<SalesDocument> SalesDocuments { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesSettlement> SalesSettlements { get; }
    DbSet<Warehouse> Warehouses { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
