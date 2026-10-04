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
    private async Task SeedManufacturingCostingAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var fashionBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "FASHION", ct);
        var fashionWarehouse = await _context.Warehouses.FirstAsync(w => w.CompanyId == company.Id && w.Code == "FASHION-MAIN", ct);
        var product = await _context.Products.Include(x => x.Variants)
            .FirstAsync(x => x.CompanyId == company.Id && x.Code == "PROD-AODAI-LUA", ct);
        var supplier = await _context.Suppliers.FirstAsync(x => x.CompanyId == company.Id && x.Code == "SUP-AODAI-01", ct);

        var materialDefinitions = new[]
        {
            (Code: "MAT-VAI-LUA", Name: "Vải lụa tơ tằm", Category: "Vải", Unit: "m", Quantity: 500m, UnitCost: 150000m),
            (Code: "MAT-VAI-LOT", Name: "Vải lót", Category: "Vải lót", Unit: "m", Quantity: 300m, UnitCost: 50000m),
            (Code: "MAT-REN", Name: "Ren trang trí", Category: "Phụ liệu", Unit: "m", Quantity: 200m, UnitCost: 20000m),
            (Code: "MAT-CHI", Name: "Chỉ may", Category: "Phụ liệu", Unit: "cuộn", Quantity: 100m, UnitCost: 10000m),
            (Code: "MAT-NUT", Name: "Nút áo dài", Category: "Phụ liệu", Unit: "cái", Quantity: 1000m, UnitCost: 2000m),
            (Code: "MAT-DA", Name: "Đá đính áo", Category: "Phụ liệu", Unit: "viên", Quantity: 5000m, UnitCost: 1000m)
        };

        var materials = new Dictionary<string, Material>();
        foreach (var definition in materialDefinitions)
        {
            var material = await _context.Materials.FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.Code == definition.Code, ct);
            if (material == null)
            {
                material = new Material
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    Code = definition.Code,
                    Name = definition.Name,
                    Category = definition.Category,
                    Unit = definition.Unit,
                    IsActive = true
                };
                _context.Materials.Add(material);
                await _context.SaveChangesAsync(ct);
            }
            materials[definition.Code] = material;

            if (!await _context.MaterialLots.AnyAsync(x => x.CompanyId == company.Id && x.MaterialId == material.Id, ct))
            {
                var lot = new MaterialLot
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    MaterialId = material.Id,
                    SupplierId = supplier.Id,
                    LotNumber = $"LOT-SEED-{definition.Code}",
                    QuantityReceived = definition.Quantity,
                    QuantityRemaining = definition.Quantity,
                    UnitCost = definition.UnitCost,
                    Currency = "VND",
                    ReceivedAt = DateTime.UtcNow.AddDays(-5)
                };
                material.QuantityOnHand += definition.Quantity;
                _context.MaterialLots.Add(lot);
                _context.MaterialMovements.Add(new MaterialMovement
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    MaterialId = material.Id,
                    MaterialLotId = lot.Id,
                    MovementType = MaterialMovementType.PurchaseReceipt,
                    QuantityDelta = definition.Quantity,
                    UnitCost = definition.UnitCost,
                    ReferenceType = "MaterialReceipt",
                    ReferenceId = lot.Id,
                    MovementDate = lot.ReceivedAt,
                    Notes = "Seed lô nguyên vật liệu cho costing áo dài"
                });
            }
        }
        await _context.SaveChangesAsync(ct);

        var bom = await _context.Boms.Include(x => x.Items).FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.ProductId == product.Id && x.Status == "Approved", ct);
        if (bom == null)
        {
            bom = new Bom
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                ProductId = product.Id,
                Code = "BOM-AD-LUA",
                VersionNumber = 1,
                Status = "Approved",
                EffectiveFrom = DateTime.UtcNow.AddDays(-5),
                IsActive = true,
                Items = new List<BomItem>
                {
                    new() { MaterialId = materials["MAT-VAI-LUA"].Id, Size = "M", Quantity = 3.0m, WastePercent = 0, Unit = "m", Sequence = 1 },
                    new() { MaterialId = materials["MAT-VAI-LUA"].Id, Size = "L", Quantity = 3.2m, WastePercent = 0, Unit = "m", Sequence = 2 },
                    new() { MaterialId = materials["MAT-VAI-LOT"].Id, Size = "M", Quantity = 1.5m, WastePercent = 0, Unit = "m", Sequence = 3 },
                    new() { MaterialId = materials["MAT-VAI-LOT"].Id, Size = "L", Quantity = 1.6m, WastePercent = 0, Unit = "m", Sequence = 4 },
                    new() { MaterialId = materials["MAT-REN"].Id, Size = null, Quantity = 2.0m, WastePercent = 0, Unit = "m", Sequence = 5 },
                    new() { MaterialId = materials["MAT-CHI"].Id, Size = null, Quantity = 1.0m, WastePercent = 0, Unit = "cuộn", Sequence = 6 },
                    new() { MaterialId = materials["MAT-NUT"].Id, Size = null, Quantity = 10m, WastePercent = 0, Unit = "cái", Sequence = 7 },
                    new() { MaterialId = materials["MAT-DA"].Id, Size = null, Quantity = 50m, WastePercent = 0, Unit = "viên", Sequence = 8 }
                }
            };
            _context.Boms.Add(bom);
            await _context.SaveChangesAsync(ct);
        }

        var policyDefinitions = new[]
        {
            (Channel: "STORE", Platform: 0m, Affiliate: 0m, Payment: 0m, Fixed: 0m, Tax: 0m, Shipping: 0m),
            (Channel: "FACEBOOK", Platform: 0m, Affiliate: 0m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "WEBSITE_AODAI", Platform: 0m, Affiliate: 0m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "ZALO", Platform: 0m, Affiliate: 0m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "SHOPEE", Platform: 0.10m, Affiliate: 0.10m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "TIKTOK", Platform: 0.10m, Affiliate: 0.10m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m)
        };
        foreach (var definition in policyDefinitions)
        {
            if (!await _context.ChannelFeePolicies.AnyAsync(x => x.CompanyId == company.Id && x.Channel == definition.Channel && x.IsActive, ct))
            {
                _context.ChannelFeePolicies.Add(new ChannelFeePolicy
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    Code = $"{definition.Channel}-V1",
                    Channel = definition.Channel,
                    VersionNumber = 1,
                    PlatformFeeRate = definition.Platform,
                    AffiliateFeeRate = definition.Affiliate,
                    PaymentFeeRate = definition.Payment,
                    FixedPaymentFee = definition.Fixed,
                    TaxRate = definition.Tax,
                    DefaultShippingSubsidy = definition.Shipping,
                    EffectiveFrom = DateTime.UtcNow.AddDays(-30),
                    IsActive = true
                });
            }
        }
        await _context.SaveChangesAsync(ct);

        if (!await _context.ProductionOrders.AnyAsync(x => x.CompanyId == company.Id && x.OrderNumber == "SX-SEED-AD-LUA-001", ct))
        {
            var variantM = product.Variants.First(x => x.Sku == "AD-LUA-DO-M");
            var variantL = product.Variants.First(x => x.Sku == "AD-LUA-DO-L");
            var outputQuantityM = 2;
            var outputQuantityL = 1;
            var materialUsages = new (string Code, decimal Quantity)[]
            {
                ("MAT-VAI-LUA", 9.2m), ("MAT-VAI-LOT", 4.6m), ("MAT-REN", 6m),
                ("MAT-CHI", 3m), ("MAT-NUT", 30m), ("MAT-DA", 150m)
            };
            var materialCost = 0m;
            var order = new ProductionOrder
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                OrderNumber = "SX-SEED-AD-LUA-001",
                ProductId = product.Id,
                BomId = bom.Id,
                Status = ProductionOrderStatus.Closed,
                CostStatus = CostStatus.Actual,
                PlannedQuantity = 3,
                GoodQuantity = 3,
                StandardCost = 0,
                ReleasedAt = DateTime.UtcNow.AddDays(-3),
                CompletedAt = DateTime.UtcNow.AddDays(-2),
                ClosedAt = DateTime.UtcNow.AddDays(-2),
                Notes = "Seed batch để kiểm thử actual costing áo dài theo size"
            };
            order.Outputs.Add(new ProductionOrderOutput { ProductionOrderId = order.Id, ProductVariantId = variantM.Id, PlannedQuantity = outputQuantityM, GoodQuantity = outputQuantityM, UnitCost = 1196666.67m });
            order.Outputs.Add(new ProductionOrderOutput { ProductionOrderId = order.Id, ProductVariantId = variantL.Id, PlannedQuantity = outputQuantityL, GoodQuantity = outputQuantityL, UnitCost = 1196666.67m });
            foreach (var usage in materialUsages)
            {
                var material = materials[usage.Code];
                var lot = await _context.MaterialLots.FirstAsync(x => x.MaterialId == material.Id && x.QuantityRemaining >= usage.Quantity, ct);
                lot.QuantityRemaining -= usage.Quantity;
                material.QuantityOnHand -= usage.Quantity;
                materialCost += usage.Quantity * lot.UnitCost;
                order.Materials.Add(new ProductionOrderMaterial { ProductionOrderId = order.Id, MaterialId = material.Id, MaterialLotId = lot.Id, ActualQuantity = usage.Quantity, UnitCost = lot.UnitCost, TotalCost = usage.Quantity * lot.UnitCost });
                _context.MaterialMovements.Add(new MaterialMovement { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, MaterialId = material.Id, MaterialLotId = lot.Id, MovementType = MaterialMovementType.ProductionConsumption, QuantityDelta = -usage.Quantity, UnitCost = lot.UnitCost, ReferenceType = "ProductionOrder", ReferenceId = order.Id, MovementDate = order.CompletedAt.Value, Notes = "Seed xuất NVL cho actual costing" });
            }
            var labor = 3 * (50000m + 200000m + 100000m + 20000m + 20000m);
            var outside = 0m;
            var overhead = 300000m;
            var scrap = 150000m;
            order.ActualMaterialCost = materialCost;
            order.ActualLaborCost = labor;
            order.ActualOutsideProcessingCost = outside;
            order.ActualOverheadCost = overhead;
            order.ActualScrapReworkCost = scrap;
            order.ActualTotalCost = materialCost + labor + outside + overhead + scrap;
            order.ActualUnitCost = decimal.Round(order.ActualTotalCost / 3m, 2);
            order.StandardCost = order.ActualUnitCost;
            foreach (var output in order.Outputs) output.UnitCost = order.ActualUnitCost;

            _context.ProductionOrders.Add(order);
            foreach (var variant in new[] { variantM, variantL })
            {
                variant.CostPrice = order.ActualUnitCost;
                variant.CostStatus = CostStatus.Actual;
                variant.LastActualCostAt = order.ClosedAt;
            }
            _context.InventoryMovements.AddRange(
                new InventoryMovement { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, WarehouseId = fashionWarehouse.Id, ProductVariantId = variantM.Id, MovementType = InventoryMovementType.ProductionOutput, QuantityDelta = outputQuantityM, UnitCost = order.ActualUnitCost, ReferenceType = "ProductionOrder", ReferenceId = order.Id, MovementDate = order.CompletedAt.Value, Notes = "Seed thành phẩm tốt actual cost" },
                new InventoryMovement { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, WarehouseId = fashionWarehouse.Id, ProductVariantId = variantL.Id, MovementType = InventoryMovementType.ProductionOutput, QuantityDelta = outputQuantityL, UnitCost = order.ActualUnitCost, ReferenceType = "ProductionOrder", ReferenceId = order.Id, MovementDate = order.CompletedAt.Value, Notes = "Seed thành phẩm tốt actual cost" });

            foreach (var output in order.Outputs)
            {
                var balance = await _context.InventoryBalances.FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.BusinessUnitId == fashionBu!.Id && x.WarehouseId == fashionWarehouse.Id && x.ProductVariantId == output.ProductVariantId, ct);
                if (balance == null)
                {
                    _context.InventoryBalances.Add(new InventoryBalance { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, WarehouseId = fashionWarehouse.Id, ProductVariantId = output.ProductVariantId, OnHandQuantity = output.GoodQuantity, ReservedQuantity = 0, LastUpdated = DateTime.UtcNow });
                }
                else
                {
                    balance.OnHandQuantity += output.GoodQuantity;
                    balance.LastUpdated = DateTime.UtcNow;
                }
            }
            await _context.SaveChangesAsync(ct);
        }
    }
}
