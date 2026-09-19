using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.InternalData.DTOs;
using InternalManagement.Application.Features.InternalData.Services;
using InternalManagement.Domain.Entities.InternalData;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/noi-bo/{segment}/tai-lieu")]
public sealed class InternalResourcesController : BaseApiController
{
    private readonly IInternalDataService _service;

    public InternalResourcesController(IInternalDataService service) => _service = service;

    [HttpGet]
    [HasPermission(Permissions.InternalResourcesView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InternalResourceDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InternalResourceDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        string segment,
        [FromQuery] string? search,
        [FromQuery] string? resourceType,
        [FromQuery] string? status,
        [FromQuery] string? tag,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.GetResourcesAsync(parsedSegment, search, resourceType, status, tag, pageIndex, pageSize, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<PaginatedResult<InternalResourceDto>>.Fail(FirstError(result)));
        return Ok(ApiResponse<PaginatedResult<InternalResourceDto>>.Ok(result.Value!, "Lấy kho thông tin nội bộ thành công."));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InternalResourcesView)]
    public async Task<IActionResult> GetById(string segment, Guid id, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.GetResourceByIdAsync(parsedSegment, id, ct);
        if (!result.Succeeded) return NotFound(ApiResponse<InternalResourceDto>.Fail(FirstError(result)));
        return Ok(ApiResponse<InternalResourceDto>.Ok(result.Value!, "Lấy thông tin tài liệu thành công."));
    }

    [HttpPost]
    [HasPermission(Permissions.InternalResourcesManage)]
    public async Task<IActionResult> Create(string segment, [FromBody] CreateInternalResourceRequest request, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.CreateResourceAsync(parsedSegment, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<InternalResourceDto>.Fail(FirstError(result), ToErrors(result.Errors)));
        return CreatedAtAction(nameof(GetById), new { segment, id = result.Value!.Id },
            ApiResponse<InternalResourceDto>.Ok(result.Value, "Tạo metadata tài liệu thành công; tệp được lưu bên ngoài cơ sở dữ liệu."));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.InternalResourcesManage)]
    public async Task<IActionResult> Update(string segment, Guid id, [FromBody] UpdateInternalResourceRequest request, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.UpdateResourceAsync(parsedSegment, id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<InternalResourceDto>.Fail(FirstError(result), ToErrors(result.Errors)));
        return Ok(ApiResponse<InternalResourceDto>.Ok(result.Value!, "Cập nhật metadata tài liệu thành công."));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.InternalResourcesManage)]
    public async Task<IActionResult> Delete(string segment, Guid id, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.DeleteResourceAsync(parsedSegment, id, ct);
        if (!result.Succeeded) return NotFound(ApiResponse<bool>.Fail(FirstError(result)));
        return Ok(ApiResponse<bool>.Ok(true, "Xóa metadata tài liệu thành công; tệp ngoài DB không bị xóa."));
    }

    private bool TrySegment(string value, out BusinessSegment segment, out IActionResult error)
    {
        if (InternalDataContract.TryParseSegment(value, out segment))
        {
            error = null!;
            return true;
        }

        error = BadRequest(ApiResponse<object>.Fail(
            "Mảng dữ liệu không hợp lệ. Dùng 'cong-nghe-giao-duc' hoặc 'thoi-trang'."));
        return false;
    }

    private static string FirstError(Result result) => result.Errors.FirstOrDefault() ?? "Yêu cầu không hợp lệ.";
    private static Dictionary<string, string[]> ToErrors(string[] errors) => new() { ["request"] = errors };
}
