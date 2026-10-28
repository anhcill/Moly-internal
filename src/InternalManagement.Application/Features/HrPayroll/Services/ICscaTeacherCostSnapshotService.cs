using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface ICscaTeacherCostSnapshotService
{
    Task<Result<CscaTeacherCostSyncResultDto>> SyncAsync(SyncCscaTeacherCostsRequest request, CancellationToken ct);

    Task<Result<CscaTeacherCostSummaryDto>> GetAsync(
        DateTime? from,
        DateTime? to,
        string? courseSourceId,
        string? classSourceId,
        string? teacherSourceId,
        bool includeProjected,
        CancellationToken ct);
}
