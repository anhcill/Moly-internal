using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed class ManufacturingService : IManufacturingService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<ManufacturingService> _logger;

    public ManufacturingService(IApplicationDbContext db, ICurrentUserService currentUser, ILogger<ManufacturingService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetTenantAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            companyId = (await _db.Companies.FirstOrDefaultAsync(x => x.Code == "MOLI", ct))?.Id ?? Guid.Empty;
        }

        var businessUnitId = (await _db.BusinessUnits.FirstOrDefaultAsync(
            x => x.CompanyId == companyId && x.Code == "FASHION" && x.IsActive && !x.IsDeleted, ct))?.Id
            ?? _currentUser.BusinessUnitId;

        return (companyId.Value, businessUnitId);
    }

    public async Task<PaginatedResult<MaterialDto>> GetMaterialsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var query = _db.Materials.AsNoTracking().Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x => x.Code.Contains(search) || x.Name.Contains(search));
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Code)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MaterialDto(x.Id, x.CompanyId, x.Code, x.Name, x.Category, x.Unit, x.QuantityOnHand, x.IsActive, x.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedResult<MaterialDto>(items, total, pageIndex, pageSize);
    }

    public async Task<PaginatedResult<MaterialLotDto>> GetMaterialLotsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.MaterialLots.AsNoTracking()
            .Include(x => x.Material)
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.LotNumber.Contains(search) || x.Material.Code.Contains(search) || x.Material.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.ReceivedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new MaterialLotDto(x.Id, x.MaterialId, x.Material.Code, x.LotNumber, x.QuantityReceived, x.QuantityRemaining, x.UnitCost, x.Currency, x.ReceivedAt))
            .ToListAsync(ct);
        return new PaginatedResult<MaterialLotDto>(items, total, pageIndex, pageSize);
    }

    public async Task<Result<MaterialDto>> CreateMaterialAsync(CreateMaterialRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Unit))
        {
            return Result<MaterialDto>.Failure("Mã, tên và đơn vị nguyên vật liệu là bắt buộc.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _db.Materials.AnyAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.Code == code, ct))
        {
            return Result<MaterialDto>.Failure($"Nguyên vật liệu '{code}' đã tồn tại.");
        }

        var material = new Material
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = code,
            Name = request.Name.Trim(),
            Category = request.Category,
            Unit = request.Unit.Trim(),
            IsActive = true
        };

        _db.Materials.Add(material);
        await _db.SaveChangesAsync(ct);
        return Result<MaterialDto>.Success(ToDto(material));
    }

    public async Task<Result<MaterialLotDto>> ReceiveMaterialAsync(ReceiveMaterialRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Quantity <= 0 || request.UnitCost < 0)
        {
            return Result<MaterialLotDto>.Failure("Số lượng phải lớn hơn 0 và đơn giá không được âm.");
        }

        var material = await _db.Materials.FirstOrDefaultAsync(x => x.Id == request.MaterialId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.IsActive, ct);
        if (material == null)
        {
            return Result<MaterialLotDto>.Failure("Nguyên vật liệu không tồn tại trong phạm vi công ty.");
        }

        if (request.SupplierId.HasValue && !await _db.Suppliers.AnyAsync(x => x.Id == request.SupplierId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct))
        {
            return Result<MaterialLotDto>.Failure("Nhà cung cấp nguyên vật liệu không tồn tại.");
        }

        var lotNumber = string.IsNullOrWhiteSpace(request.LotNumber)
            ? $"MAT-{DateTime.UtcNow:yyyyMMddHHmmssfff}"
            : request.LotNumber.Trim();
        if (await _db.MaterialLots.AnyAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.LotNumber == lotNumber, ct))
        {
            return Result<MaterialLotDto>.Failure($"Lô nguyên vật liệu '{lotNumber}' đã tồn tại.");
        }

        var lot = new MaterialLot
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            MaterialId = material.Id,
            SupplierId = request.SupplierId,
            LotNumber = lotNumber,
            QuantityReceived = request.Quantity,
            QuantityRemaining = request.Quantity,
            UnitCost = request.UnitCost,
            Currency = string.IsNullOrWhiteSpace(request.Currency) ? "VND" : request.Currency.ToUpperInvariant(),
            ReceivedAt = DateTime.UtcNow
        };
        material.QuantityOnHand += request.Quantity;

        _db.MaterialLots.Add(lot);
        _db.MaterialMovements.Add(new MaterialMovement
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            MaterialId = material.Id,
            MaterialLotId = lot.Id,
            MovementType = MaterialMovementType.PurchaseReceipt,
            QuantityDelta = request.Quantity,
            UnitCost = request.UnitCost,
            ReferenceType = "MaterialReceipt",
            ReferenceId = lot.Id,
            MovementDate = DateTime.UtcNow,
            Notes = request.Notes
        });

        await _db.SaveChangesAsync(ct);
        return Result<MaterialLotDto>.Success(ToDto(lot, material));
    }

    public async Task<Result<BomDto>> CreateBomAsync(CreateBomRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Items == null || request.Items.Count == 0)
        {
            return Result<BomDto>.Failure("BOM phải có ít nhất một nguyên vật liệu.");
        }

        var product = await _db.Products.FirstOrDefaultAsync(x => x.Id == request.ProductId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && !x.IsDeleted, ct);
        if (product == null)
        {
            return Result<BomDto>.Failure("Sản phẩm không tồn tại trong phạm vi công ty.");
        }

        var materialIds = request.Items.Select(x => x.MaterialId).Distinct().ToList();
        var materials = await _db.Materials.Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && materialIds.Contains(x.Id) && x.IsActive).ToDictionaryAsync(x => x.Id, ct);
        if (materials.Count != materialIds.Count)
        {
            return Result<BomDto>.Failure("BOM có nguyên vật liệu không tồn tại hoặc không còn hoạt động.");
        }

        var version = (await _db.Boms.Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.ProductId == product.Id).Select(x => (int?)x.VersionNumber).MaxAsync(ct) ?? 0) + 1;
        var bom = new Bom
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            ProductId = product.Id,
            Code = string.IsNullOrWhiteSpace(request.Code) ? $"BOM-{product.Code}" : request.Code.Trim().ToUpperInvariant(),
            VersionNumber = version,
            Status = "Approved",
            EffectiveFrom = request.EffectiveFrom == default ? DateTime.UtcNow : request.EffectiveFrom,
            IsActive = true
        };

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0 || item.WastePercent < 0 || item.WastePercent >= 100)
            {
                return Result<BomDto>.Failure("Định mức phải lớn hơn 0 và tỷ lệ hao hụt phải từ 0 đến dưới 100%.");
            }

            bom.Items.Add(new BomItem
            {
                BomId = bom.Id,
                MaterialId = item.MaterialId,
                Size = string.IsNullOrWhiteSpace(item.Size) ? null : item.Size.Trim().ToUpperInvariant(),
                Quantity = item.Quantity,
                WastePercent = item.WastePercent,
                Unit = item.Unit,
                Sequence = item.Sequence,
                Notes = item.Notes
            });
        }

        _db.Boms.Add(bom);
        await _db.SaveChangesAsync(ct);
        return Result<BomDto>.Success(ToDto(bom, product, materials));
    }

    public async Task<BomDto?> GetBomAsync(Guid id, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var bom = await _db.Boms.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Items).ThenInclude(x => x.Material)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        return bom == null ? null : ToDto(bom, bom.Product, bom.Items.GroupBy(x => x.MaterialId).ToDictionary(x => x.Key, x => x.First().Material));
    }

    public async Task<PaginatedResult<BomDto>> GetBomsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.Boms.AsNoTracking()
            .Include(x => x.Product)
            .Include(x => x.Items).ThenInclude(x => x.Material)
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Code.Contains(search) || x.Product.Name.Contains(search));

        var total = await query.CountAsync(ct);
        var boms = await query.OrderByDescending(x => x.EffectiveFrom)
            .ThenByDescending(x => x.VersionNumber)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = boms.Select(x => ToDto(x, x.Product, x.Items.GroupBy(i => i.MaterialId).ToDictionary(g => g.Key, g => g.First().Material))).ToList();
        return new PaginatedResult<BomDto>(items, total, pageIndex, pageSize);
    }

    public async Task<Result<ProductionOrderDto>> CreateProductionOrderAsync(CreateProductionOrderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        if (request.Outputs == null || request.Outputs.Count == 0)
        {
            return Result<ProductionOrderDto>.Failure("Lệnh sản xuất phải có ít nhất một đầu ra theo SKU/cỡ.");
        }

        var bom = await _db.Boms.Include(x => x.Items).ThenInclude(x => x.Material)
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == request.BomId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.ProductId == request.ProductId && x.IsActive && x.Status == "Approved", ct);
        if (bom == null)
        {
            return Result<ProductionOrderDto>.Failure("BOM không tồn tại, không thuộc sản phẩm hoặc chưa được duyệt.");
        }

        var variantIds = request.Outputs.Select(x => x.ProductVariantId).Distinct().ToList();
        var variants = await _db.ProductVariants.Include(x => x.Product)
            .Where(x => variantIds.Contains(x.Id) && x.ProductId == request.ProductId && x.Product.CompanyId == companyId && x.Product.BusinessUnitId == businessUnitId && x.IsActive)
            .ToDictionaryAsync(x => x.Id, ct);
        if (variants.Count != variantIds.Count)
        {
            return Result<ProductionOrderDto>.Failure("Đầu ra có SKU không thuộc sản phẩm của BOM.");
        }

        if (request.Outputs.Any(x => x.PlannedQuantity <= 0))
        {
            return Result<ProductionOrderDto>.Failure("Số lượng kế hoạch của từng SKU phải lớn hơn 0.");
        }

        var plannedQuantity = request.Outputs.Sum(x => x.PlannedQuantity);
        var plannedMaterialRows = new Dictionary<(Guid MaterialId, string? Size), (decimal Quantity, decimal Cost)>();
        foreach (var output in request.Outputs)
        {
            var size = variants[output.ProductVariantId].Size?.Trim().ToUpperInvariant();
            var items = bom.Items.Where(x => x.Size == null || x.Size == size).ToList();
            foreach (var item in items)
            {
                var quantity = item.Quantity * (1m + item.WastePercent / 100m) * output.PlannedQuantity;
                var cost = quantity * await GetAverageMaterialCostAsync(item.MaterialId, companyId, businessUnitId, ct);
                var key = (item.MaterialId, item.Size);
                plannedMaterialRows[key] = plannedMaterialRows.TryGetValue(key, out var current)
                    ? (current.Quantity + quantity, current.Cost + cost)
                    : (quantity, cost);
            }
        }

        if (plannedMaterialRows.Any(x => x.Value.Cost <= 0))
        {
            return Result<ProductionOrderDto>.Failure("Không thể tính standard cost vì có nguyên vật liệu chưa có giá theo lô.");
        }

        var date = DateTime.UtcNow.Date;
        var count = await _db.ProductionOrders.CountAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.CreatedAt >= date, ct);
        var order = new ProductionOrder
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            OrderNumber = $"SX-{DateTime.UtcNow:yyyyMMdd}-{count + 1:D3}",
            ProductId = request.ProductId,
            BomId = request.BomId,
            Status = ProductionOrderStatus.Released,
            CostStatus = CostStatus.Standard,
            PlannedQuantity = plannedQuantity,
            StandardCost = plannedMaterialRows.Sum(x => x.Value.Cost) / plannedQuantity,
            ReleasedAt = DateTime.UtcNow,
            Notes = request.Notes
        };
        // The BOM query already loads the product. Keep it attached so the
        // create response can show the same human-readable name as the list.
        order.Product = bom.Product;

        foreach (var output in request.Outputs)
        {
            order.Outputs.Add(new ProductionOrderOutput
            {
                ProductionOrderId = order.Id,
                ProductVariantId = output.ProductVariantId,
                PlannedQuantity = output.PlannedQuantity
            });
        }

        foreach (var row in plannedMaterialRows)
        {
            var unitCost = await GetAverageMaterialCostAsync(row.Key.MaterialId, companyId, businessUnitId, ct);
            order.Materials.Add(new ProductionOrderMaterial
            {
                ProductionOrderId = order.Id,
                MaterialId = row.Key.MaterialId,
                Size = row.Key.Size,
                PlannedQuantity = row.Value.Quantity,
                UnitCost = unitCost,
                TotalCost = row.Value.Cost
            });
        }

        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync(ct);
        return Result<ProductionOrderDto>.Success(ToDto(order));
    }

    public async Task<ProductionOrderDto?> GetProductionOrderAsync(Guid id, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var order = await _db.ProductionOrders.AsNoTracking()
            .Include(x => x.Product)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        return order == null ? null : ToDto(order);
    }

    public async Task<PaginatedResult<ProductionOrderDto>> GetProductionOrdersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var query = _db.ProductionOrders.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.OrderNumber.Contains(search));

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ProductionOrderDto(x.Id, x.CompanyId, x.OrderNumber, x.ProductId, x.BomId, x.Status, x.CostStatus, x.PlannedQuantity, x.GoodQuantity, x.DefectiveQuantity, x.ReworkQuantity, x.StandardCost, x.ActualMaterialCost, x.ActualLaborCost, x.ActualOutsideProcessingCost, x.ActualOverheadCost, x.ActualScrapReworkCost, x.ActualTotalCost, x.ActualUnitCost, x.CompletedAt, x.ClosedAt, x.Product.Name))
            .ToListAsync(ct);
        return new PaginatedResult<ProductionOrderDto>(items, total, pageIndex, pageSize);
    }

    public async Task<Result<ProductionOrderDto>> CompleteProductionOrderAsync(Guid id, CompleteProductionOrderRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetTenantAsync(ct);
        var order = await _db.ProductionOrders
            .Include(x => x.Product)
            .Include(x => x.Outputs)
            .Include(x => x.Materials)
            .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId, ct);
        if (order == null)
        {
            return Result<ProductionOrderDto>.Failure("Không tìm thấy lệnh sản xuất.");
        }
        if (order.Status is ProductionOrderStatus.Closed or ProductionOrderStatus.Cancelled)
        {
            return Result<ProductionOrderDto>.Failure("Lệnh sản xuất đã đóng hoặc đã hủy, không thể ghi đè actual cost.");
        }
        if (request.Materials == null || request.Operations == null || request.Outputs == null)
        {
            return Result<ProductionOrderDto>.Failure("Số liệu sản xuất thực tế phải có nguyên vật liệu, công đoạn và đầu ra.");
        }

        if (request.Outputs.GroupBy(x => x.ProductVariantId).Any(x => x.Count() > 1))
        {
            return Result<ProductionOrderDto>.Failure("Mỗi SKU chỉ được xuất hiện một lần trong đầu ra thực tế.");
        }
        var requestedOutputs = request.Outputs.ToDictionary(x => x.ProductVariantId);
        if (requestedOutputs.Count != order.Outputs.Count || order.Outputs.Any(x => !requestedOutputs.ContainsKey(x.ProductVariantId)))
        {
            return Result<ProductionOrderDto>.Failure("Đầu ra thực tế phải khai báo đủ các SKU trong lệnh.");
        }

        var goodQuantity = 0;
        var defectiveQuantity = 0;
        var reworkQuantity = 0;
        foreach (var output in order.Outputs)
        {
            var actual = requestedOutputs[output.ProductVariantId];
            if (actual.GoodQuantity < 0 || actual.DefectiveQuantity < 0 || actual.ReworkQuantity < 0 || actual.GoodQuantity + actual.DefectiveQuantity + actual.ReworkQuantity > output.PlannedQuantity)
            {
                return Result<ProductionOrderDto>.Failure("Số lượng thực tế của SKU không hợp lệ so với kế hoạch.");
            }

            output.GoodQuantity = actual.GoodQuantity;
            output.DefectiveQuantity = actual.DefectiveQuantity;
            output.ReworkQuantity = actual.ReworkQuantity;
            goodQuantity += actual.GoodQuantity;
            defectiveQuantity += actual.DefectiveQuantity;
            reworkQuantity += actual.ReworkQuantity;
        }

        if (goodQuantity <= 0)
        {
            return Result<ProductionOrderDto>.Failure("Lệnh phải có ít nhất một thành phẩm tốt để chốt actual unit cost.");
        }
        if (request.OverheadCost < 0 || request.ScrapReworkCost < 0)
        {
            return Result<ProductionOrderDto>.Failure("Chi phí chung xưởng và chi phí hao hụt/sửa hàng lỗi không được âm.");
        }

        var allocations = new List<MaterialAllocation>();
        var simulatedMaterialRemaining = new Dictionary<Guid, decimal>();
        var simulatedLotRemaining = new Dictionary<Guid, decimal>();
        foreach (var consumption in request.Materials)
        {
            if (consumption.Quantity <= 0)
            {
                return Result<ProductionOrderDto>.Failure("Lượng NVL thực tế phải lớn hơn 0.");
            }

            var material = await _db.Materials.FirstOrDefaultAsync(x => x.Id == consumption.MaterialId && x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.IsActive, ct);
            if (material == null)
            {
                return Result<ProductionOrderDto>.Failure($"Tồn kho NVL không đủ cho {consumption.MaterialId}.");
            }
            var materialRemaining = simulatedMaterialRemaining.TryGetValue(material.Id, out var remainingMaterial)
                ? remainingMaterial
                : material.QuantityOnHand;
            if (materialRemaining < consumption.Quantity)
            {
                return Result<ProductionOrderDto>.Failure($"Tồn kho NVL không đủ cho {material.Code}.");
            }

            var lotsQuery = _db.MaterialLots.Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.MaterialId == consumption.MaterialId && x.QuantityRemaining > 0);
            if (consumption.MaterialLotId.HasValue)
            {
                lotsQuery = lotsQuery.Where(x => x.Id == consumption.MaterialLotId.Value);
            }
            var lots = await lotsQuery.OrderBy(x => x.ReceivedAt).ToListAsync(ct);
            var remaining = consumption.Quantity;
            foreach (var lot in lots)
            {
                if (remaining <= 0) break;
                var lotRemaining = simulatedLotRemaining.TryGetValue(lot.Id, out var remainingLot)
                    ? remainingLot
                    : lot.QuantityRemaining;
                var quantity = Math.Min(remaining, lotRemaining);
                if (quantity <= 0) continue;
                allocations.Add(new MaterialAllocation(material, lot, quantity, lot.UnitCost, consumption.Size));
                remaining -= quantity;
                simulatedLotRemaining[lot.Id] = lotRemaining - quantity;
            }
            if (remaining > 0)
            {
                return Result<ProductionOrderDto>.Failure($"Lô NVL không đủ số lượng cho {material.Code}.");
            }
            simulatedMaterialRemaining[material.Id] = materialRemaining - consumption.Quantity;
        }

        var laborCost = 0m;
        var outsideCost = 0m;
        foreach (var operation in request.Operations)
        {
            if (operation.Rate < 0 || operation.ActualUnits < 0)
            {
            return Result<ProductionOrderDto>.Failure("Đơn giá và khối lượng công đoạn không được âm.");
            }

            var total = decimal.Round(operation.Rate * operation.ActualUnits, 2);
            if (operation.IsOutsideProcessing) outsideCost += total; else laborCost += total;
            _db.ProductionOperations.Add(new ProductionOperation
            {
                ProductionOrderId = order.Id,
                Sequence = operation.Sequence,
                Name = operation.Name,
                IsOutsideProcessing = operation.IsOutsideProcessing,
                RateType = operation.RateType,
                Rate = operation.Rate,
                ActualUnits = operation.ActualUnits,
                TotalCost = total,
                Notes = operation.Notes
            });
        }

        var materialCost = allocations.Sum(x => x.Quantity * x.UnitCost);
        foreach (var allocation in allocations)
        {
            allocation.Material.QuantityOnHand -= allocation.Quantity;
            allocation.Lot.QuantityRemaining -= allocation.Quantity;
            _db.ProductionOrderMaterials.Add(new ProductionOrderMaterial
            {
                ProductionOrderId = order.Id,
                MaterialId = allocation.Material.Id,
                MaterialLotId = allocation.Lot.Id,
                Size = allocation.Size,
                PlannedQuantity = order.Materials.FirstOrDefault(x => x.MaterialId == allocation.Material.Id && x.Size == allocation.Size)?.PlannedQuantity ?? 0,
                ActualQuantity = allocation.Quantity,
                UnitCost = allocation.UnitCost,
                TotalCost = decimal.Round(allocation.Quantity * allocation.UnitCost, 2)
            });
            _db.MaterialMovements.Add(new MaterialMovement
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId,
                MaterialId = allocation.Material.Id,
                MaterialLotId = allocation.Lot.Id,
                MovementType = MaterialMovementType.ProductionConsumption,
                QuantityDelta = -allocation.Quantity,
                UnitCost = allocation.UnitCost,
                ReferenceType = "ProductionOrder",
                ReferenceId = order.Id,
                MovementDate = DateTime.UtcNow,
                Notes = $"Xuất NVL cho {order.OrderNumber}"
            });
        }

        var totalCost = decimal.Round(materialCost + laborCost + outsideCost + request.OverheadCost + request.ScrapReworkCost, 2);
        var unitCost = decimal.Round(totalCost / goodQuantity, 2);
        order.Status = ProductionOrderStatus.Closed;
        order.CostStatus = CostStatus.Actual;
        order.GoodQuantity = goodQuantity;
        order.DefectiveQuantity = defectiveQuantity;
        order.ReworkQuantity = reworkQuantity;
        order.ActualMaterialCost = decimal.Round(materialCost, 2);
        order.ActualLaborCost = decimal.Round(laborCost, 2);
        order.ActualOutsideProcessingCost = decimal.Round(outsideCost, 2);
        order.ActualOverheadCost = decimal.Round(request.OverheadCost, 2);
        order.ActualScrapReworkCost = decimal.Round(request.ScrapReworkCost, 2);
        order.ActualTotalCost = totalCost;
        order.ActualUnitCost = unitCost;
        order.CompletedAt = DateTime.UtcNow;
        order.ClosedAt = DateTime.UtcNow;
        order.Notes = request.Notes ?? order.Notes;

        var outputVariants = await _db.ProductVariants
            .Include(x => x.Product)
            .Where(x => order.Outputs.Select(o => o.ProductVariantId).Contains(x.Id) &&
                        x.Product.CompanyId == companyId && x.Product.BusinessUnitId == businessUnitId)
            .ToDictionaryAsync(x => x.Id, ct);
        if (outputVariants.Count != order.Outputs.Count)
        {
            return Result<ProductionOrderDto>.Failure("Không tìm thấy đầy đủ biến thể đầu ra trong đơn vị Thời trang.");
        }

        // Cost theo size: NVL theo size đi đúng nhóm size; NVL dùng chung, nhân công,
        // gia công ngoài, overhead và scrap/rework phân bổ theo sản lượng tốt.
        var goodBySize = order.Outputs
            .GroupBy(x => NormalizeSize(outputVariants[x.ProductVariantId].Size))
            .ToDictionary(x => x.Key, x => x.Sum(o => requestedOutputs[o.ProductVariantId].GoodQuantity));
        var specificMaterialCostBySize = allocations
            .Where(x => !string.IsNullOrWhiteSpace(x.Size))
            .GroupBy(x => NormalizeSize(x.Size))
            .ToDictionary(x => x.Key, x => x.Sum(a => a.Quantity * a.UnitCost));
        var sharedMaterialCost = allocations
            .Where(x =>
            {
                if (string.IsNullOrWhiteSpace(x.Size))
                    return true;

                var normalizedSize = NormalizeSize(x.Size);
                return !goodBySize.TryGetValue(normalizedSize, out var goodQuantity) || goodQuantity <= 0;
            })
            .Sum(x => x.Quantity * x.UnitCost);
        foreach (var size in specificMaterialCostBySize.Keys.ToList())
        {
            if (!goodBySize.TryGetValue(size, out var goodQuantityForSize) || goodQuantityForSize <= 0)
            {
                sharedMaterialCost += specificMaterialCostBySize[size];
                specificMaterialCostBySize.Remove(size);
            }
        }
        var sharedCost = sharedMaterialCost + laborCost + outsideCost + request.OverheadCost + request.ScrapReworkCost;

        var warehouse = await GetOrCreateWarehouseAsync(companyId, businessUnitId, ct);
        foreach (var output in order.Outputs)
        {
            var actual = requestedOutputs[output.ProductVariantId];
            var variant = outputVariants[output.ProductVariantId];
            var size = NormalizeSize(variant.Size);
            var sizeGood = goodBySize[size];
            var sizeUnitCost = sizeGood > 0 && specificMaterialCostBySize.TryGetValue(size, out var sizeCost)
                ? sizeCost / sizeGood
                : 0m;
            output.UnitCost = actual.GoodQuantity > 0
                ? decimal.Round(sizeUnitCost + (sharedCost / goodQuantity), 2)
                : 0m;
            if (actual.GoodQuantity > 0)
            {
                variant.CostPrice = output.UnitCost;
                variant.CostStatus = CostStatus.Actual;
                variant.LastActualCostAt = DateTime.UtcNow;
            }
        }

        foreach (var output in order.Outputs)
        {
            var actual = requestedOutputs[output.ProductVariantId];
            var variant = await _db.ProductVariants.FirstAsync(x => x.Id == output.ProductVariantId, ct);
            if (actual.GoodQuantity > 0)
            {
                _db.InventoryMovements.Add(new InventoryMovement
                {
                    CompanyId = companyId,
                    BusinessUnitId = businessUnitId,
                    WarehouseId = warehouse.Id,
                    Warehouse = warehouse,
                    ProductVariantId = variant.Id,
                    MovementType = InventoryMovementType.ProductionOutput,
                    QuantityDelta = actual.GoodQuantity,
                    UnitCost = output.UnitCost,
                    ReferenceType = "ProductionOrder",
                    ReferenceId = order.Id,
                    MovementDate = DateTime.UtcNow,
                    Notes = $"Nhập thành phẩm tốt từ {order.OrderNumber}"
                });
                var balance = await _db.InventoryBalances.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.WarehouseId == warehouse.Id && x.ProductVariantId == variant.Id, ct);
                if (balance == null)
                {
                    _db.InventoryBalances.Add(new InventoryBalance
                    {
                        CompanyId = companyId,
                        BusinessUnitId = businessUnitId,
                        WarehouseId = warehouse.Id,
                        Warehouse = warehouse,
                        ProductVariantId = variant.Id,
                        OnHandQuantity = actual.GoodQuantity,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    });
                }
                else
                {
                    balance.OnHandQuantity += actual.GoodQuantity;
                    balance.LastUpdated = DateTime.UtcNow;
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Production order {OrderNumber} closed with actual unit cost {UnitCost}.", order.OrderNumber, unitCost);
        return Result<ProductionOrderDto>.Success(ToDto(order));
    }

    private async Task<decimal> GetAverageMaterialCostAsync(Guid materialId, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        var lots = await _db.MaterialLots.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.QuantityRemaining > 0 && x.MaterialId == materialId)
            .ToListAsync(ct);
        var quantity = lots.Sum(x => x.QuantityRemaining);
        return quantity <= 0 ? 0 : lots.Sum(x => x.QuantityRemaining * x.UnitCost) / quantity;
    }

    private static MaterialDto ToDto(Material x) => new(x.Id, x.CompanyId, x.Code, x.Name, x.Category, x.Unit, x.QuantityOnHand, x.IsActive, x.CreatedAt);

    private static MaterialLotDto ToDto(MaterialLot x, Material material) => new(x.Id, x.MaterialId, material.Code, x.LotNumber, x.QuantityReceived, x.QuantityRemaining, x.UnitCost, x.Currency, x.ReceivedAt);

    private static BomDto ToDto(Bom bom, Product product, Dictionary<Guid, Material> materials)
        => new(bom.Id, bom.CompanyId, bom.ProductId, product.Name, bom.Code, bom.VersionNumber, bom.Status, bom.EffectiveFrom, bom.EffectiveTo,
            bom.Items.OrderBy(x => x.Sequence).Select(x => new BomItemDto(x.Id, x.MaterialId, materials[x.MaterialId].Code, materials[x.MaterialId].Name, x.Size, x.Quantity, x.WastePercent, x.Unit, x.Sequence, x.Notes)).ToList());

    private static ProductionOrderDto ToDto(ProductionOrder x) => new(x.Id, x.CompanyId, x.OrderNumber, x.ProductId, x.BomId, x.Status, x.CostStatus, x.PlannedQuantity, x.GoodQuantity, x.DefectiveQuantity, x.ReworkQuantity, x.StandardCost, x.ActualMaterialCost, x.ActualLaborCost, x.ActualOutsideProcessingCost, x.ActualOverheadCost, x.ActualScrapReworkCost, x.ActualTotalCost, x.ActualUnitCost, x.CompletedAt, x.ClosedAt, x.Product?.Name ?? string.Empty);

    private static string NormalizeSize(string? size) => string.IsNullOrWhiteSpace(size) ? string.Empty : size.Trim().ToUpperInvariant();

    private async Task<Warehouse> GetOrCreateWarehouseAsync(Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        var warehouse = await _db.Warehouses
            .Where(x => x.CompanyId == companyId && x.BusinessUnitId == businessUnitId && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Code)
            .FirstOrDefaultAsync(ct);
        if (warehouse != null)
            return warehouse;

        warehouse = new Warehouse
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = businessUnitId.HasValue ? "FASHION-MAIN" : "MAIN",
            Name = businessUnitId.HasValue ? "Kho Thời trang chính" : "Kho chính",
            IsActive = true,
            IsDefault = true
        };
        _db.Warehouses.Add(warehouse);
        return warehouse;
    }

    private sealed record MaterialAllocation(Material Material, MaterialLot Lot, decimal Quantity, decimal UnitCost, string? Size);
}
