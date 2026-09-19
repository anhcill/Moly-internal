using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;

namespace InternalManagement.Application.Features.Fashion.Services;

public interface IManufacturingService
{
    Task<PaginatedResult<MaterialDto>> GetMaterialsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<PaginatedResult<MaterialLotDto>> GetMaterialLotsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<MaterialDto>> CreateMaterialAsync(CreateMaterialRequest request, CancellationToken ct);
    Task<Result<MaterialLotDto>> ReceiveMaterialAsync(ReceiveMaterialRequest request, CancellationToken ct);
    Task<Result<BomDto>> CreateBomAsync(CreateBomRequest request, CancellationToken ct);
    Task<BomDto?> GetBomAsync(Guid id, CancellationToken ct);
    Task<PaginatedResult<BomDto>> GetBomsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ProductionOrderDto>> CreateProductionOrderAsync(CreateProductionOrderRequest request, CancellationToken ct);
    Task<ProductionOrderDto?> GetProductionOrderAsync(Guid id, CancellationToken ct);
    Task<PaginatedResult<ProductionOrderDto>> GetProductionOrdersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ProductionOrderDto>> CompleteProductionOrderAsync(Guid id, CompleteProductionOrderRequest request, CancellationToken ct);
}
