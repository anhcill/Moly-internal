using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;

namespace InternalManagement.Application.Features.Finance.Services;

public interface IFinanceWorkspaceService
{
    Task<Result<FinanceWorkspaceReportDto>> GetWorkspaceAsync(DateTime? from, int horizonMonths, decimal collectionPercent, decimal costFactor, decimal openingCash, CancellationToken ct);
    Task<Result<IReadOnlyList<StudentReceivableDto>>> GetReceivablesAsync(CancellationToken ct);
    Task<Result<StudentReceivableDto>> RecordReceiptAsync(Guid receivableId, RecordStudentReceiptRequest request, CancellationToken ct);
    Task<Result<StudentReceivableDto>> RecordRefundAsync(Guid receivableId, RecordStudentRefundRequest request, CancellationToken ct);
    Task<Result<StudentReceivableDto>> UpdateReceivableAsync(Guid receivableId, UpdateStudentReceivableRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<TeacherCostWorkspaceDto>>> GetTeacherCostsAsync(CancellationToken ct);
    Task<Result<TeacherCostWorkspaceDto>> IssueAdvanceAsync(IssueTeacherAdvanceRequest request, CancellationToken ct);
    Task<Result<TeacherCostWorkspaceDto>> ReturnAdvanceAsync(Guid advanceId, ReturnTeacherAdvanceRequest request, CancellationToken ct);
    Task<Result<TeacherCostWorkspaceDto>> PayTeacherAsync(PayTeacherRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<PlannedCashMovementDto>>> GetPlannedMovementsAsync(CancellationToken ct);
    Task<Result<PlannedCashMovementDto>> CreatePlannedMovementAsync(CreatePlannedCashMovementRequest request, CancellationToken ct);
    Task<Result<PlannedCashMovementDto>> SettlePlannedMovementAsync(Guid id, CancellationToken ct);
    Task<Result<PlannedCashMovementDto>> CancelPlannedMovementAsync(Guid id, CancellationToken ct);
}
