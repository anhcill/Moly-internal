using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;

namespace InternalManagement.Application.Features.Integration.Interfaces;

/// <summary>
/// The Management control-plane for all governed applications. It owns the
/// central records; website connectors only project these decisions outward.
/// </summary>
public interface IApplicationOrchestrationService
{
    Task<Result<ApplicationOrchestrationOverviewDto>> GetOverviewAsync(CancellationToken ct);
    Task<Result<PaginatedResult<ManagedApplicationDto>>> GetApplicationsAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ManagedApplicationDto>> CreateApplicationAsync(UpsertManagedApplicationRequest request, CancellationToken ct);
    Task<Result<ManagedApplicationDto>> UpdateApplicationAsync(Guid applicationId, UpsertManagedApplicationRequest request, CancellationToken ct);

    Task<Result<PaginatedResult<ApplicationMembershipDto>>> GetMembershipsAsync(Guid applicationId, string? search, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ApplicationMembershipDto>> UpsertMembershipAsync(Guid applicationId, UpsertApplicationMembershipRequest request, CancellationToken ct);
    Task<Result<ApplicationRoleAssignmentDto>> UpsertRoleAssignmentAsync(Guid membershipId, UpsertApplicationRoleAssignmentRequest request, CancellationToken ct);

    Task<Result<PaginatedResult<ApplicationCourseMapDto>>> GetCourseMapsAsync(Guid applicationId, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ApplicationCourseMapDto>> UpsertCourseMapAsync(Guid applicationId, Guid courseId, UpsertApplicationCourseMapRequest request, CancellationToken ct);
    Task<Result<PaginatedResult<ApplicationClassMapDto>>> GetClassMapsAsync(Guid applicationId, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ApplicationClassMapDto>> UpsertClassMapAsync(Guid applicationId, Guid cscaClassId, UpsertApplicationClassMapRequest request, CancellationToken ct);

    Task<Result<PaginatedResult<ApplicationEntitlementDto>>> GetEntitlementsAsync(Guid applicationId, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ApplicationEntitlementDto>> UpsertEntitlementAsync(Guid applicationId, Guid membershipId, UpsertApplicationEntitlementRequest request, CancellationToken ct);

    Task<Result<PaginatedResult<ApplicationDeliveryItemDto>>> GetDeliveryQueueAsync(string? applicationCode, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<ApplicationDeliveryItemDto>> RetryDeliveryAsync(Guid outboxId, CancellationToken ct);
}
