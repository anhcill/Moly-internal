using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Security;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence;

public partial class DatabaseSeeder
{
    private async Task SeedFashionAndInventoryAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var fashionBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "FASHION", ct);
        var fashionWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.CompanyId == company.Id && w.Code == "FASHION-MAIN", ct);
        if (fashionWarehouse == null)
        {
            fashionWarehouse = new Domain.Entities.Fashion.Warehouse
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "FASHION-MAIN",
                Name = "Kho Thời trang chính",
                IsActive = true,
                IsDefault = true
            };
            _context.Warehouses.Add(fashionWarehouse);
            await _context.SaveChangesAsync(ct);
        }

        // 1. Seed Suppliers if none exist
        var sup1 = await _context.Suppliers.FirstOrDefaultAsync(s => s.CompanyId == company.Id && (s.Code == "SUP-001" || s.Code == "SUP-AODAI-01"), ct);
        if (sup1 == null)
        {
            sup1 = new Domain.Entities.Fashion.Supplier
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "SUP-AODAI-01",
                Name = "Xưởng Dệt & May Lụa Áo Dài Vạn Phúc",
                ContactName = "Nguyễn Văn Hùng",
                Phone = "0988112233",
                Email = "hung.nguyen@luavanphuc.vn",
                Address = "Làng lụa Vạn Phúc, Hà Đông, Hà Nội"
            };
            _context.Suppliers.Add(sup1);
        }

        var sup2 = await _context.Suppliers.FirstOrDefaultAsync(s => s.CompanyId == company.Id && (s.Code == "SUP-002" || s.Code == "SUP-AODAI-02"), ct);
        if (sup2 == null)
        {
            sup2 = new Domain.Entities.Fashion.Supplier
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "SUP-AODAI-02",
                Name = "Xưởng May Đo & Thêu Tay Áo Dài Huế",
                ContactName = "Trần Thị Mai",
                Phone = "0977888999",
                Email = "mai.tran@aodaihue.vn",
                Address = "Phường Phú Hội, TP. Huế"
            };
            _context.Suppliers.Add(sup2);
        }
        await _context.SaveChangesAsync(ct);

        // 2. Seed Products & Variants (Áo Dài MOLY)
        var prodAodaiLua = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.CompanyId == company.Id && (p.Code == "PROD-AODAI-LUA" || p.Code == "PROD-TSHIRT"), ct);
        if (prodAodaiLua == null)
        {
            prodAodaiLua = new Domain.Entities.Fashion.Product
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "PROD-AODAI-LUA",
                Name = "Áo Dài Truyền Thống Lụa Tơ Tằm MOLY",
                Category = "Áo Dài Truyền Thống",
                Description = "Áo dài lụa tơ tằm mềm mại thoáng mát, may thủ công, kiểu dáng thướt tha truyền thống",
                IsActive = true,
                Variants = new List<Domain.Entities.Fashion.ProductVariant>
                {
                    new() { Sku = "AD-LUA-DO-M", Barcode = "8938500101", Color = "Đỏ Tươi", Size = "M", CostPrice = 250000, SellingPrice = 690000, IsActive = true },
                    new() { Sku = "AD-LUA-DO-L", Barcode = "8938500102", Color = "Đỏ Tươi", Size = "L", CostPrice = 250000, SellingPrice = 690000, IsActive = true },
                    new() { Sku = "AD-LUA-XANH-M", Barcode = "8938500103", Color = "Xanh Cốm", Size = "M", CostPrice = 250000, SellingPrice = 690000, IsActive = true },
                    new() { Sku = "AD-LUA-XANH-L", Barcode = "8938500104", Color = "Xanh Cốm", Size = "L", CostPrice = 250000, SellingPrice = 690000, IsActive = true }
                }
            };
            _context.Products.Add(prodAodaiLua);
        }

        var prodAodaiCactan = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.CompanyId == company.Id && (p.Code == "PROD-AODAI-CACTAN" || p.Code == "PROD-POLO"), ct);
        if (prodAodaiCactan == null)
        {
            prodAodaiCactan = new Domain.Entities.Fashion.Product
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "PROD-AODAI-CACTAN",
                Name = "Áo Dài Cách Tân Thêu Hoa Mai MOLY",
                Category = "Áo Dài Cách Tân",
                Description = "Áo dài cách tân trẻ trung thêu tay hoa mai thủ công, phù hợp đi tiệc, sự kiện, chụp ảnh",
                IsActive = true,
                Variants = new List<Domain.Entities.Fashion.ProductVariant>
                {
                    new() { Sku = "AD-CT-HONG-M", Barcode = "8938500201", Color = "Hồng Pastel", Size = "M", CostPrice = 320000, SellingPrice = 850000, IsActive = true },
                    new() { Sku = "AD-CT-HONG-L", Barcode = "8938500202", Color = "Hồng Pastel", Size = "L", CostPrice = 320000, SellingPrice = 850000, IsActive = true }
                }
            };
            _context.Products.Add(prodAodaiCactan);
        }

        var prodAodaiNhung = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.CompanyId == company.Id && (p.Code == "PROD-AODAI-NHUNG" || p.Code == "PROD-HOODIE"), ct);
        if (prodAodaiNhung == null)
        {
            prodAodaiNhung = new Domain.Entities.Fashion.Product
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "PROD-AODAI-NHUNG",
                Name = "Áo Dài Nhung Gấm Đính Ngọc Trai Mùa Lễ Hội",
                Category = "Áo Dài Nhung Gấm",
                Description = "Áo dài chất liệu nhung gấm sang trọng quý phái, cổ áo đính ngọc trai cao cấp",
                IsActive = true,
                Variants = new List<Domain.Entities.Fashion.ProductVariant>
                {
                    new() { Sku = "AD-NHUNG-DO-L", Barcode = "8938500301", Color = "Đỏ Mận", Size = "L", CostPrice = 450000, SellingPrice = 1250000, IsActive = true }
                }
            };
            _context.Products.Add(prodAodaiNhung);
        }
        await _context.SaveChangesAsync(ct);

        // 3. Seed Sample Purchase Receipt & Inventory Movements & Balances if none exist
        if (!await _context.PurchaseReceipts.AnyAsync(ct))
        {
            var variants = await _context.ProductVariants.Where(v => v.Product.CompanyId == company.Id).ToListAsync(ct);
            if (variants.Count >= 3)
            {
                var v1 = variants[0];
                var v2 = variants[1];
                var v3 = variants[2];

                var receipt = new Domain.Entities.Fashion.PurchaseReceipt
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    WarehouseId = fashionWarehouse.Id,
                    SupplierId = sup1.Id,
                    ReceiptNumber = "PR-20260819-001",
                    Status = "Completed",
                    ReceivedAt = DateTime.UtcNow.AddDays(-2),
                    Notes = "Nhập kho đợt 1 bộ sưu tập Áo Dài MOLY",
                    TotalAmount = (30 * v1.CostPrice) + (30 * v2.CostPrice) + (20 * v3.CostPrice),
                    Items = new List<Domain.Entities.Fashion.PurchaseReceiptItem>
                    {
                        new() { ProductVariantId = v1.Id, Quantity = 30, UnitPrice = v1.CostPrice },
                        new() { ProductVariantId = v2.Id, Quantity = 30, UnitPrice = v2.CostPrice },
                        new() { ProductVariantId = v3.Id, Quantity = 20, UnitPrice = v3.CostPrice }
                    }
                };
                _context.PurchaseReceipts.Add(receipt);
                await _context.SaveChangesAsync(ct);

                // Movements
                _context.InventoryMovements.AddRange(
                    new Domain.Entities.Fashion.InventoryMovement
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v1.Id,
                        MovementType = Domain.Enums.InventoryMovementType.PurchaseReceipt,
                        QuantityDelta = 30,
                        UnitCost = v1.CostPrice,
                        ReferenceType = "PurchaseReceipt",
                        ReferenceId = receipt.Id,
                        MovementDate = DateTime.UtcNow.AddDays(-2),
                        Notes = "Nhập kho theo phiếu PR-20260819-001"
                    },
                    new Domain.Entities.Fashion.InventoryMovement
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v2.Id,
                        MovementType = Domain.Enums.InventoryMovementType.PurchaseReceipt,
                        QuantityDelta = 30,
                        UnitCost = v2.CostPrice,
                        ReferenceType = "PurchaseReceipt",
                        ReferenceId = receipt.Id,
                        MovementDate = DateTime.UtcNow.AddDays(-2),
                        Notes = "Nhập kho theo phiếu PR-20260819-001"
                    },
                    new Domain.Entities.Fashion.InventoryMovement
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v3.Id,
                        MovementType = Domain.Enums.InventoryMovementType.PurchaseReceipt,
                        QuantityDelta = 20,
                        UnitCost = v3.CostPrice,
                        ReferenceType = "PurchaseReceipt",
                        ReferenceId = receipt.Id,
                        MovementDate = DateTime.UtcNow.AddDays(-2),
                        Notes = "Nhập kho theo phiếu PR-20260819-001"
                    }
                );

                // Balances
                _context.InventoryBalances.AddRange(
                    new Domain.Entities.Fashion.InventoryBalance
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v1.Id,
                        OnHandQuantity = 30,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    },
                    new Domain.Entities.Fashion.InventoryBalance
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v2.Id,
                        OnHandQuantity = 30,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    },
                    new Domain.Entities.Fashion.InventoryBalance
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v3.Id,
                        OnHandQuantity = 20,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    }
                );

                await _context.SaveChangesAsync(ct);
            }
        }
    }

}
