using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using Microsoft.AspNetCore.Mvc;

namespace InternalManagement.Api.Controllers;

/// <summary>
/// Administration surface for Management-owned LMS access policy and delivery.
/// The actual learning content and credentials remain owned by CSCA Course LMS.
/// </summary>
[Route("api/v1/lms-integration")]
public sealed class LmsIntegrationController : BaseApiController
{
    private readonly ILmsIntegrationOperationsService _operations;
    private readonly ILmsOutboxDispatcher _outboxDispatcher;

    public LmsIntegrationController(
        ILmsIntegrationOperationsService operations,
        ILmsOutboxDispatcher outboxDispatcher)
    {
        _operations = operations;
        _outboxDispatcher = outboxDispatcher;
    }

    [HttpGet("overview")]
    [HasPermission(Permissions.SystemSyncView)]
    [ProducesResponseType(typeof(ApiResponse<LmsIntegrationOverviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LmsIntegrationOverviewDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOverview(CancellationToken ct)
    {
        var result = await _operations.GetOverviewAsync(ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<LmsIntegrationOverviewDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy trạng thái LMS."));

        return Ok(ApiResponse<LmsIntegrationOverviewDto>.Ok(result.Value!, "Lấy trạng thái LMS thành công."));
    }

    [HttpGet("course-mappings")]
    [HasPermission(Permissions.SystemSyncView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<LmsCourseMappingDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<LmsCourseMappingDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCourseMappings(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _operations.GetCourseMappingsAsync(search, status, pageIndex, pageSize, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PaginatedResult<LmsCourseMappingDto>>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy mapping LMS."));

        return Ok(ApiResponse<PaginatedResult<LmsCourseMappingDto>>.Ok(result.Value!, "Lấy mapping khóa học LMS thành công."));
    }

    [HttpPut("course-mappings/{courseId:guid}")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    [ProducesResponseType(typeof(ApiResponse<LmsCourseMappingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LmsCourseMappingDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpsertCourseMapping(
        Guid courseId,
        [FromBody] UpsertLmsCourseMappingRequest request,
        CancellationToken ct)
    {
        var result = await _operations.UpsertCourseMappingAsync(courseId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<LmsCourseMappingDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lưu mapping LMS."));

        return Ok(ApiResponse<LmsCourseMappingDto>.Ok(result.Value!, "Đã lưu mapping khóa học LMS."));
    }

    [HttpGet("outbox")]
    [HasPermission(Permissions.SystemSyncView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<LmsOutboxItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<LmsOutboxItemDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetOutbox(
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _operations.GetOutboxAsync(status, pageIndex, pageSize, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PaginatedResult<LmsOutboxItemDto>>.Fail(result.Errors.FirstOrDefault() ?? "Không thể lấy LMS outbox."));

        return Ok(ApiResponse<PaginatedResult<LmsOutboxItemDto>>.Ok(result.Value!, "Lấy LMS outbox thành công."));
    }

    [HttpPost("outbox/{outboxId:guid}/retry")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    [ProducesResponseType(typeof(ApiResponse<LmsOutboxItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<LmsOutboxItemDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RetryOutbox(Guid outboxId, CancellationToken ct)
    {
        var result = await _operations.RetryOutboxAsync(outboxId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<LmsOutboxItemDto>.Fail(result.Errors.FirstOrDefault() ?? "Không thể retry LMS outbox."));

        return Ok(ApiResponse<LmsOutboxItemDto>.Ok(result.Value!, "Đã đưa LMS outbox trở lại hàng chờ."));
    }

    /// <summary>
    /// Dispatches at most 100 committed commands. It is an explicit operational
    /// action; normal production delivery should be performed by LmsOutboxWorker.
    /// </summary>
    [HttpPost("outbox/dispatch")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    [ProducesResponseType(typeof(ApiResponse<LmsOutboxDispatchResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DispatchOutbox([FromQuery] int batchSize = 20, CancellationToken ct = default)
    {
        var result = await _outboxDispatcher.DispatchPendingAsync(batchSize, ct);
        return Ok(ApiResponse<LmsOutboxDispatchResult>.Ok(result, "Đã xử lý hàng chờ LMS."));
    }
}
