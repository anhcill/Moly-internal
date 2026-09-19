using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace InternalManagement.Api.Controllers;

/// <summary>
/// Control-plane API for every website governed by InternalManagement. This
/// surface exposes only operator-safe metadata; connector secrets and outbox
/// payloads never leave the server through these endpoints.
/// </summary>
[Route("api/v1/application-orchestration")]
public sealed class ApplicationOrchestrationController : BaseApiController
{
    private readonly IApplicationOrchestrationService _service;

    public ApplicationOrchestrationController(IApplicationOrchestrationService service) => _service = service;

    [HttpGet("overview")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var result = await _service.GetOverviewAsync(ct);
        return ToActionResult(result, "Lấy dashboard điều phối ứng dụng thành công.");
    }

    [HttpGet("applications")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetApplications([FromQuery] string? search, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetApplicationsAsync(search, pageIndex, pageSize, ct);
        return ToActionResult(result, "Lấy registry ứng dụng thành công.");
    }

    [HttpPost("applications")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> CreateApplication([FromBody] UpsertManagedApplicationRequest request, CancellationToken ct)
    {
        var result = await _service.CreateApplicationAsync(request, ct);
        return ToActionResult(result, "Đã tạo ứng dụng quản lý.");
    }

    [HttpPut("applications/{applicationId:guid}")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> UpdateApplication(Guid applicationId, [FromBody] UpsertManagedApplicationRequest request, CancellationToken ct)
    {
        var result = await _service.UpdateApplicationAsync(applicationId, request, ct);
        return ToActionResult(result, "Đã cập nhật ứng dụng quản lý.");
    }

    [HttpGet("applications/{applicationId:guid}/memberships")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetMemberships(Guid applicationId, [FromQuery] string? search, [FromQuery] string? status, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetMembershipsAsync(applicationId, search, status, pageIndex, pageSize, ct);
        return ToActionResult(result, "Lấy membership ứng dụng thành công.");
    }

    [HttpPut("applications/{applicationId:guid}/memberships")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> UpsertMembership(Guid applicationId, [FromBody] UpsertApplicationMembershipRequest request, CancellationToken ct)
    {
        var result = await _service.UpsertMembershipAsync(applicationId, request, ct);
        return ToActionResult(result, "Đã lưu membership ứng dụng.");
    }

    [HttpPut("memberships/{membershipId:guid}/roles")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> UpsertRole(Guid membershipId, [FromBody] UpsertApplicationRoleAssignmentRequest request, CancellationToken ct)
    {
        var result = await _service.UpsertRoleAssignmentAsync(membershipId, request, ct);
        return ToActionResult(result, "Đã lưu role theo scope.");
    }

    [HttpGet("applications/{applicationId:guid}/course-maps")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetCourseMaps(Guid applicationId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetCourseMapsAsync(applicationId, pageIndex, pageSize, ct);
        return ToActionResult(result, "Lấy course map thành công.");
    }

    [HttpPut("applications/{applicationId:guid}/course-maps/{courseId:guid}")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> UpsertCourseMap(Guid applicationId, Guid courseId, [FromBody] UpsertApplicationCourseMapRequest request, CancellationToken ct)
    {
        var result = await _service.UpsertCourseMapAsync(applicationId, courseId, request, ct);
        return ToActionResult(result, "Đã lưu course map.");
    }

    [HttpGet("applications/{applicationId:guid}/class-maps")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetClassMaps(Guid applicationId, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetClassMapsAsync(applicationId, pageIndex, pageSize, ct);
        return ToActionResult(result, "Lấy class map thành công.");
    }

    [HttpPut("applications/{applicationId:guid}/class-maps/{cscaClassId:guid}")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> UpsertClassMap(Guid applicationId, Guid cscaClassId, [FromBody] UpsertApplicationClassMapRequest request, CancellationToken ct)
    {
        var result = await _service.UpsertClassMapAsync(applicationId, cscaClassId, request, ct);
        return ToActionResult(result, "Đã lưu class map.");
    }

    [HttpGet("applications/{applicationId:guid}/entitlements")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetEntitlements(Guid applicationId, [FromQuery] string? status, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetEntitlementsAsync(applicationId, status, pageIndex, pageSize, ct);
        return ToActionResult(result, "Lấy entitlement thành công.");
    }

    [HttpPut("applications/{applicationId:guid}/memberships/{membershipId:guid}/entitlements")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> UpsertEntitlement(Guid applicationId, Guid membershipId, [FromBody] UpsertApplicationEntitlementRequest request, CancellationToken ct)
    {
        var result = await _service.UpsertEntitlementAsync(applicationId, membershipId, request, ct);
        return ToActionResult(result, "Đã lưu entitlement trung tâm.");
    }

    [HttpGet("delivery-queue")]
    [HasPermission(Permissions.SystemSyncView)]
    public async Task<IActionResult> GetDeliveryQueue([FromQuery] string? applicationCode, [FromQuery] string? status, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var result = await _service.GetDeliveryQueueAsync(applicationCode, status, pageIndex, pageSize, ct);
        return ToActionResult(result, "Lấy hàng chờ đồng bộ thành công.");
    }

    [HttpPost("delivery-queue/{outboxId:guid}/retry")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    public async Task<IActionResult> RetryDelivery(Guid outboxId, CancellationToken ct)
    {
        var result = await _service.RetryDeliveryAsync(outboxId, ct);
        return ToActionResult(result, "Đã đưa delivery trở lại hàng chờ.");
    }

    private static IActionResult ToActionResult<T>(Result<T> result, string message) =>
        result.Succeeded
            ? new OkObjectResult(ApiResponse<T>.Ok(result.Value!, message))
            : new BadRequestObjectResult(ApiResponse<T>.Fail(result.Errors.FirstOrDefault() ?? "Thao tác điều phối ứng dụng thất bại."));
}
