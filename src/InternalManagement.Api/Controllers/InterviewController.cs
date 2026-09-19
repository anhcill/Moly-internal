using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class InterviewController : BaseApiController
{
    private readonly IInterviewService _interviewService;

    public InterviewController(IInterviewService interviewService)
    {
        _interviewService = interviewService;
    }

    [HttpGet("customers")]
    [HasPermission(Permissions.InterviewCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<InterviewCustomerDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _interviewService.GetCustomersAsync(search, status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<InterviewCustomerDto>>.Ok(result, "Lấy danh sách khách hàng Interview thành công."));
    }

    [HttpGet("customers/{id:guid}")]
    [HasPermission(Permissions.InterviewCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<InterviewCustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<InterviewCustomerDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerById(Guid id, CancellationToken ct)
    {
        var result = await _interviewService.GetCustomerByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<InterviewCustomerDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy khách hàng."));

        return Ok(ApiResponse<InterviewCustomerDto>.Ok(result.Value!, "Lấy chi tiết khách hàng thành công."));
    }

    [HttpPost("customers")]
    [HasPermission(Permissions.InterviewCustomersManage)]
    [ProducesResponseType(typeof(ApiResponse<InterviewCustomerDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<InterviewCustomerDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateInterviewCustomerRequest request, CancellationToken ct)
    {
        var result = await _interviewService.CreateCustomerAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<InterviewCustomerDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo thông tin khách hàng thất bại."));

        return CreatedAtAction(nameof(GetCustomerById), new { id = result.Value!.Id }, ApiResponse<InterviewCustomerDto>.Ok(result.Value, "Tạo khách hàng Interview thành công."));
    }

    [HttpPut("customers/{id:guid}")]
    [HasPermission(Permissions.InterviewCustomersManage)]
    [ProducesResponseType(typeof(ApiResponse<InterviewCustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<InterviewCustomerDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] UpdateInterviewCustomerRequest request, CancellationToken ct)
    {
        var result = await _interviewService.UpdateCustomerAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<InterviewCustomerDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật khách hàng thất bại."));

        return Ok(ApiResponse<InterviewCustomerDto>.Ok(result.Value!, "Cập nhật khách hàng thành công."));
    }

    [HttpDelete("customers/{id:guid}")]
    [HasPermission(Permissions.InterviewCustomersManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteCustomer(Guid id, CancellationToken ct)
    {
        var result = await _interviewService.DeleteCustomerAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa khách hàng thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Xóa khách hàng thành công."));
    }

    [HttpGet("financial-summary")]
    [HasPermission(Permissions.InterviewCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<InterviewFinancialSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFinancialSummary(CancellationToken ct)
    {
        var result = await _interviewService.GetFinancialSummaryAsync(ct);
        return Ok(ApiResponse<InterviewFinancialSummaryDto>.Ok(result.Value!, "Lấy tổng hợp doanh thu Mock Interview thành công."));
    }
}
