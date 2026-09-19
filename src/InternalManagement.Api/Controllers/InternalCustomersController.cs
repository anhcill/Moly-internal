using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.InternalData.DTOs;
using InternalManagement.Application.Features.InternalData.Services;
using InternalManagement.Domain.Entities.InternalData;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/noi-bo/{segment}/khach-hang")]
public sealed class InternalCustomersController : BaseApiController
{
    private readonly IInternalDataService _service;

    public InternalCustomersController(IInternalDataService service) => _service = service;

    [HttpGet]
    [HasPermission(Permissions.InternalCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InternalCustomerDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InternalCustomerDto>>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAll(
        string segment,
        [FromQuery] string? search,
        [FromQuery] string? source,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.GetCustomersAsync(parsedSegment, search, source, status, pageIndex, pageSize, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<PaginatedResult<InternalCustomerDto>>.Fail(FirstError(result)));
        return Ok(ApiResponse<PaginatedResult<InternalCustomerDto>>.Ok(result.Value!, "Lấy danh mục khách hàng thành công."));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.InternalCustomersView)]
    public async Task<IActionResult> GetById(string segment, Guid id, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.GetCustomerByIdAsync(parsedSegment, id, ct);
        if (!result.Succeeded) return NotFound(ApiResponse<InternalCustomerDto>.Fail(FirstError(result)));
        return Ok(ApiResponse<InternalCustomerDto>.Ok(result.Value!, "Lấy thông tin khách hàng thành công."));
    }

    [HttpPost]
    [HasPermission(Permissions.InternalCustomersManage)]
    public async Task<IActionResult> Create(string segment, [FromBody] CreateInternalCustomerRequest request, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.CreateCustomerAsync(parsedSegment, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<InternalCustomerDto>.Fail(FirstError(result), ToErrors(result.Errors)));
        return CreatedAtAction(nameof(GetById), new { segment, id = result.Value!.Id },
            ApiResponse<InternalCustomerDto>.Ok(result.Value, "Tạo khách hàng thành công."));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.InternalCustomersManage)]
    public async Task<IActionResult> Update(string segment, Guid id, [FromBody] UpdateInternalCustomerRequest request, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.UpdateCustomerAsync(parsedSegment, id, request, ct);
        if (!result.Succeeded) return BadRequest(ApiResponse<InternalCustomerDto>.Fail(FirstError(result), ToErrors(result.Errors)));
        return Ok(ApiResponse<InternalCustomerDto>.Ok(result.Value!, "Cập nhật khách hàng thành công."));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.InternalCustomersManage)]
    public async Task<IActionResult> Delete(string segment, Guid id, CancellationToken ct)
    {
        if (!TrySegment(segment, out var parsedSegment, out var segmentError)) return segmentError;
        var result = await _service.DeleteCustomerAsync(parsedSegment, id, ct);
        if (!result.Succeeded) return NotFound(ApiResponse<bool>.Fail(FirstError(result)));
        return Ok(ApiResponse<bool>.Ok(true, "Xóa khách hàng thành công."));
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
