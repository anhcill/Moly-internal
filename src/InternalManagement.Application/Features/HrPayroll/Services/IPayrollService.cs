using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface IPayrollService
{
    IReadOnlyList<PayrollComponentTypeDto> GetPayrollComponentTypes();

    Task<PaginatedResult<PayrollPeriodDto>> GetPayrollPeriodsAsync(
        int? year, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null);

    Task<Result<PayrollPeriodDetailDto>> GetPayrollPeriodByIdAsync(Guid id, CancellationToken ct);

    Task<Result<PayrollPeriodDto>> CreatePayrollPeriodAsync(CreatePayrollPeriodRequest request, CancellationToken ct);

    Task<Result<PayrollPeriodDto>> CancelPayrollPeriodAsync(Guid periodId, CancelPayrollPeriodRequest request, CancellationToken ct);

    Task<Result<PayrollCalculationResultDto>> CalculatePayrollAsync(Guid periodId, CancellationToken ct);

    Task<Result<List<PayrollAdjustmentDto>>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct);

    Task<Result<PayrollAdjustmentDto>> AddAdjustmentAsync(Guid periodId, CreatePayrollAdjustmentRequest request, CancellationToken ct);

    Task<Result<bool>> DeleteAdjustmentAsync(Guid adjustmentId, CancellationToken ct);

    Task<PaginatedResult<PayslipDto>> GetPayslipsAsync(
        Guid periodId, Guid? departmentId, string? search, int pageIndex, int pageSize, CancellationToken ct);

    Task<Result<PayslipDto>> GetPayslipByIdAsync(Guid id, CancellationToken ct);

    Task<Result<PayrollPeriodDto>> SubmitForReviewAsync(Guid periodId, SubmitPayrollForReviewRequest request, CancellationToken ct);

    Task<Result<PayrollPeriodDto>> ApprovePayrollAsync(Guid periodId, ApprovePayrollRequest request, CancellationToken ct);

    Task<Result<PayrollPeriodDto>> MarkAsPaidAsync(Guid periodId, MarkPayrollPaidRequest request, CancellationToken ct);

    Task<Result<PayrollPeriodDto>> PublishPayrollAsync(Guid periodId, PublishPayrollRequest request, CancellationToken ct);

    Task<Result<List<PayrollApprovalDto>>> GetApprovalsAsync(Guid periodId, CancellationToken ct);

    Task<PaginatedResult<PayslipDto>> GetMyPayslipsAsync(int pageIndex, int pageSize, CancellationToken ct);

    Task<Result<PayslipDto>> GetPersonalPayslipAsync(Guid periodId, CancellationToken ct);

    Task<Result<PayrollPolicyVersionDto>> GetActivePolicyAsync(CancellationToken ct);

    Task<Result<PayrollPolicyVersionDto>> CreatePolicyVersionAsync(CreatePayrollPolicyVersionRequest request, CancellationToken ct);
}
