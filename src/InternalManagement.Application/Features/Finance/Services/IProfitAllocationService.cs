using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;

namespace InternalManagement.Application.Features.Finance.Services;

public interface IProfitAllocationService
{
    Task<Result<BusinessUnitProfitSummaryDto>> GetProfitSummaryByBusinessUnitAsync(string businessUnitCode, CancellationToken ct);
    Task<Result<List<BusinessUnitProfitSummaryDto>>> GetAllBusinessUnitsProfitSummaryAsync(CancellationToken ct);
    Task<Result<bool>> RecordAllocationAsync(RecordProfitAllocationRequest request, CancellationToken ct);
}
