using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.MasterData.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/system/sync")]
public class SystemSyncController : BaseApiController
{
    private readonly ISyncEngine _syncEngine;
    private readonly IDeadLetterService _deadLetterService;
    private readonly IMasterDataBackfillService _masterDataBackfillService;

    public SystemSyncController(
        ISyncEngine syncEngine,
        IDeadLetterService deadLetterService,
        IMasterDataBackfillService masterDataBackfillService)
    {
        _syncEngine = syncEngine;
        _deadLetterService = deadLetterService;
        _masterDataBackfillService = masterDataBackfillService;
    }

    /// <summary>Lấy danh sách các đợt đồng bộ (IntegrationRun) phân trang.</summary>
    [HttpGet("runs")]
    [HasPermission(Permissions.SystemSyncView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<SyncRunDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRuns(
        [FromQuery] string? sourceSystem,
        [FromQuery] string? entityType,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _syncEngine.GetRunsAsync(sourceSystem, entityType, status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<SyncRunDto>>.Ok(result, "Lấy danh sách đợt đồng bộ thành công."));
    }

    /// <summary>Kích hoạt đồng bộ thủ công cho một connector/entity type.</summary>
    [HttpPost("trigger")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    [ProducesResponseType(typeof(ApiResponse<SyncTriggerResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SyncTriggerResponse>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> TriggerSync([FromBody] TriggerSyncRequest request, CancellationToken ct)
    {
        try
        {
            var result = await _syncEngine.ExecutePullAsync(
                request.SourceSystem, request.EntityType, request.ForceFullSync, ct);

            var response = new SyncTriggerResponse
            {
                RunId = result.RunId,
                SourceSystem = result.SourceSystem,
                EntityType = result.EntityType,
                Status = result.Succeeded ? "Success" : "Failed",
                RecordsRead = result.RecordsRead,
                RecordsWritten = result.RecordsWritten,
                RecordsSkipped = result.RecordsSkipped,
                RecordsFailed = result.RecordsFailed,
                ErrorMessage = result.ErrorMessage
            };

            if (!result.Succeeded)
            {
                return BadRequest(ApiResponse<SyncTriggerResponse>.Fail(
                    result.ErrorMessage ?? "Đồng bộ thất bại."));
            }

            return Ok(ApiResponse<SyncTriggerResponse>.Ok(response, "Đồng bộ thành công."));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<SyncTriggerResponse>.Fail(ex.Message));
        }
    }

    /// <summary>
    /// Liên kết dữ liệu lịch sử vào hồ sơ Party và sổ BusinessDocument mới.
    /// Thao tác có thể chạy lại an toàn: chỉ xử lý bản ghi còn thiếu liên kết.
    /// </summary>
    [HttpPost("master-data/backfill")]
    [HasPermission(Permissions.SystemSyncTrigger)]
    [ProducesResponseType(typeof(ApiResponse<MasterDataBackfillResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BackfillMasterData(
        [FromQuery] int batchSize = 100,
        CancellationToken ct = default)
    {
        var result = await _masterDataBackfillService.BackfillAsync(batchSize, ct);
        return Ok(ApiResponse<MasterDataBackfillResult>.Ok(
            result,
            "Đã liên kết dữ liệu lịch sử vào hồ sơ dùng chung và registry chứng từ."));
    }

    /// <summary>Lấy danh sách dead-letter phân trang.</summary>
    [HttpGet("dead-letters")]
    [HasPermission(Permissions.DeadLettersManage)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<DeadLetterDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDeadLetters(
        [FromQuery] string? sourceSystem,
        [FromQuery] bool? resolved,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _deadLetterService.GetListAsync(sourceSystem, resolved, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<DeadLetterDto>>.Ok(result, "Lấy danh sách dead-letter thành công."));
    }

    /// <summary>Retry một bản ghi dead-letter cụ thể.</summary>
    [HttpPost("dead-letters/{id:guid}/retry")]
    [HasPermission(Permissions.DeadLettersManage)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RetryDeadLetter(Guid id, CancellationToken ct)
    {
        var success = await _deadLetterService.RetryAsync(id, ct);

        if (!success)
        {
            return NotFound(ApiResponse<string>.Fail("Dead letter không tồn tại."));
        }

        return Ok(ApiResponse<string>.Ok("Retry thành công.", "Retry dead letter thành công."));
    }
}
