using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.InternalData.DTOs;
using InternalManagement.Domain.Entities.InternalData;

namespace InternalManagement.Application.Features.InternalData.Services;

public interface IInternalDataService
{
    Task<Result<PaginatedResult<InternalCustomerDto>>> GetCustomersAsync(
        BusinessSegment segment, string? search, string? source, string? status,
        int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<InternalCustomerDto>> GetCustomerByIdAsync(BusinessSegment segment, Guid id, CancellationToken ct);
    Task<Result<InternalCustomerDto>> CreateCustomerAsync(BusinessSegment segment, CreateInternalCustomerRequest request, CancellationToken ct);
    Task<Result<InternalCustomerDto>> UpdateCustomerAsync(BusinessSegment segment, Guid id, UpdateInternalCustomerRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteCustomerAsync(BusinessSegment segment, Guid id, CancellationToken ct);

    Task<Result<PaginatedResult<InternalResourceDto>>> GetResourcesAsync(
        BusinessSegment segment, string? search, string? resourceType, string? status, string? tag,
        int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<InternalResourceDto>> GetResourceByIdAsync(BusinessSegment segment, Guid id, CancellationToken ct);
    Task<Result<InternalResourceDto>> CreateResourceAsync(BusinessSegment segment, CreateInternalResourceRequest request, CancellationToken ct);
    Task<Result<InternalResourceDto>> UpdateResourceAsync(BusinessSegment segment, Guid id, UpdateInternalResourceRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteResourceAsync(BusinessSegment segment, Guid id, CancellationToken ct);
}
