using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;

namespace InternalManagement.Application.Features.CscaInterview.Services;

public interface IInterviewService
{
    Task<PaginatedResult<InterviewCustomerDto>> GetCustomersAsync(string? search, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<InterviewCustomerDto>> GetCustomerByIdAsync(Guid id, CancellationToken ct);
    Task<Result<InterviewCustomerDto>> CreateCustomerAsync(CreateInterviewCustomerRequest request, CancellationToken ct);
    Task<Result<InterviewCustomerDto>> UpdateCustomerAsync(Guid id, UpdateInterviewCustomerRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteCustomerAsync(Guid id, CancellationToken ct);
    Task<Result<InterviewFinancialSummaryDto>> GetFinancialSummaryAsync(CancellationToken ct);
}
