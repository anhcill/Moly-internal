using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Application.Features.Integration.Interfaces;

/// <summary>
/// Applies the paid-access policy and writes account/grant/outbox rows using
/// the caller's current database transaction. It never calls the LMS directly.
/// </summary>
public interface ILmsAccessLifecycleService
{
    Task ReconcileStudentAccessAsync(LmsAccessEvaluationRequest request, CancellationToken ct);
}

/// <summary>Executes committed CSCA Course LMS outbox commands with retry and dead-letter handling.</summary>
public interface ILmsOutboxDispatcher
{
    Task<LmsOutboxDispatchResult> DispatchPendingAsync(int batchSize, CancellationToken ct);
}

/// <summary>HTTP boundary for the CSCA Course LMS integration contract.</summary>
public interface ICscaCourseLmsClient
{
    Task<LmsProvisionResult> ProvisionStudentAsync(
        LmsProvisionCommand command,
        LmsOutboundRequestContext context,
        CancellationToken ct);

    Task UpdateStudentAccessAsync(
        string externalStudentId,
        LmsAccessCommand command,
        LmsOutboundRequestContext context,
        CancellationToken ct);
}

/// <summary>
/// Tenant-scoped administration operations. This is deliberately separate from
/// the lifecycle service so operational staff never need direct LMS database access.
/// </summary>
public interface ILmsIntegrationOperationsService
{
    Task<Result<LmsIntegrationOverviewDto>> GetOverviewAsync(CancellationToken ct);

    Task<Result<PaginatedResult<LmsCourseMappingDto>>> GetCourseMappingsAsync(
        string? search,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct);

    Task<Result<LmsCourseMappingDto>> UpsertCourseMappingAsync(
        Guid courseId,
        UpsertLmsCourseMappingRequest request,
        CancellationToken ct);

    Task<Result<PaginatedResult<LmsOutboxItemDto>>> GetOutboxAsync(
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct);

    Task<Result<LmsOutboxItemDto>> RetryOutboxAsync(Guid outboxId, CancellationToken ct);
}
