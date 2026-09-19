using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class PayrollController : BaseApiController
{
    private readonly IPayrollService _payrollService;

    public PayrollController(IPayrollService payrollService)
    {
        _payrollService = payrollService;
    }

    [HttpGet("periods")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<PayrollPeriodDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayrollPeriods(
        [FromQuery] int? year,
        [FromQuery] string? status,
        [FromQuery] Guid? businessUnitId,
        [FromQuery] string? businessSegment,
        [FromQuery] string? segment,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _payrollService.GetPayrollPeriodsAsync(
            year, status, pageIndex, pageSize, ct, businessUnitId, businessSegment ?? segment);
        return Ok(ApiResponse<PaginatedResult<PayrollPeriodDto>>.Ok(result, "Lấy danh sách kỳ lương thành công."));
    }

    [HttpGet("periods/{id:guid}")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayrollPeriodById(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollService.GetPayrollPeriodByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<PayrollPeriodDetailDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy kỳ lương."));

        return Ok(ApiResponse<PayrollPeriodDetailDto>.Ok(result.Value!, "Lấy thông tin chi tiết kỳ lương thành công."));
    }

    [HttpGet("component-types")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PayrollComponentTypeDto>>), StatusCodes.Status200OK)]
    public IActionResult GetPayrollComponentTypes()
    {
        var result = _payrollService.GetPayrollComponentTypes();
        return Ok(ApiResponse<IReadOnlyList<PayrollComponentTypeDto>>.Ok(
            result, "Lấy danh mục khoản lương thành công."));
    }

    [HttpPost("periods")]
    [HasPermission(Permissions.PayrollCalculate)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePayrollPeriod([FromBody] CreatePayrollPeriodRequest request, CancellationToken ct = default)
    {
        var result = await _payrollService.CreatePayrollPeriodAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollPeriodDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo kỳ lương thất bại."));

        return CreatedAtAction(nameof(GetPayrollPeriodById), new { id = result.Value!.Id }, ApiResponse<PayrollPeriodDto>.Ok(result.Value, "Tạo kỳ lương thành công."));
    }

    [HttpPost("periods/{id:guid}/cancel")]
    [HasPermission(Permissions.PayrollCalculate)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CancelPayrollPeriod(
        Guid id,
        [FromBody] CancelPayrollPeriodRequest request,
        CancellationToken ct = default)
    {
        var result = await _payrollService.CancelPayrollPeriodAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollPeriodDto>.Fail(result.Errors.FirstOrDefault() ?? "Hủy kỳ lương thất bại."));

        return Ok(ApiResponse<PayrollPeriodDto>.Ok(result.Value!, "Đã hủy kỳ lương nháp và lưu nhật ký nghiệp vụ."));
    }

    [HttpPost("periods/{id:guid}/calculate")]
    [HasPermission(Permissions.PayrollCalculate)]
    [ProducesResponseType(typeof(ApiResponse<PayrollCalculationResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollCalculationResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CalculatePayroll(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollService.CalculatePayrollAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollCalculationResultDto>.Fail(result.Errors.FirstOrDefault() ?? "Tính lương thất bại."));

        return Ok(ApiResponse<PayrollCalculationResultDto>.Ok(result.Value!, "Tính lương cho kỳ lương thành công."));
    }

    [HttpGet("periods/{id:guid}/payslips")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<PayslipDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayslips(
        Guid id,
        [FromQuery] Guid? departmentId,
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _payrollService.GetPayslipsAsync(id, departmentId, search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<PayslipDto>>.Ok(result, "Lấy danh sách phiếu lương thành công."));
    }

    [HttpGet("payslips/{id:guid}")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<PayslipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayslipDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayslipById(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollService.GetPayslipByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<PayslipDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy phiếu lương."));

        return Ok(ApiResponse<PayslipDto>.Ok(result.Value!, "Lấy thông tin phiếu lương thành công."));
    }

    [HttpGet("periods/{id:guid}/adjustments")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<List<PayrollAdjustmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAdjustments(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollService.GetAdjustmentsAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<List<PayrollAdjustmentDto>>.Fail(result.Errors.FirstOrDefault() ?? "Lấy danh sách điều chỉnh thất bại."));

        return Ok(ApiResponse<List<PayrollAdjustmentDto>>.Ok(result.Value!, "Lấy danh sách điều chỉnh thành công."));
    }

    [HttpPost("periods/{id:guid}/adjustments")]
    [HasPermission(Permissions.PayrollCalculate)]
    [ProducesResponseType(typeof(ApiResponse<PayrollAdjustmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<PayrollAdjustmentDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddAdjustment(Guid id, [FromBody] CreatePayrollAdjustmentRequest request, CancellationToken ct = default)
    {
        var result = await _payrollService.AddAdjustmentAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollAdjustmentDto>.Fail(result.Errors.FirstOrDefault() ?? "Thêm điều chỉnh lương thất bại."));

        return Ok(ApiResponse<PayrollAdjustmentDto>.Ok(result.Value!, "Thêm điều chỉnh lương thành công."));
    }

    [HttpDelete("adjustments/{id:guid}")]
    [HasPermission(Permissions.PayrollCalculate)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteAdjustment(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollService.DeleteAdjustmentAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa điều chỉnh lương thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Xóa điều chỉnh lương thành công."));
    }

    [HttpGet("policies/active")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPolicyVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPolicyVersionDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetActivePolicy(CancellationToken ct = default)
    {
        var result = await _payrollService.GetActivePolicyAsync(ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<PayrollPolicyVersionDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy chính sách lương hiệu lực."));

        return Ok(ApiResponse<PayrollPolicyVersionDto>.Ok(result.Value!, "Lấy chính sách lương hiệu lực thành công."));
    }

    [HttpPost("periods/{id:guid}/submit-review")]
    [HasPermission(Permissions.PayrollCalculate)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitForReview(Guid id, [FromBody] SubmitPayrollForReviewRequest request, CancellationToken ct = default)
    {
        var result = await _payrollService.SubmitForReviewAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollPeriodDto>.Fail(result.Errors.FirstOrDefault() ?? "Gửi duyệt kỳ lương thất bại."));

        return Ok(ApiResponse<PayrollPeriodDto>.Ok(result.Value!, "Gửi duyệt kỳ lương thành công."));
    }

    [HttpPost("periods/{id:guid}/approve")]
    [HasPermission(Permissions.PayrollApprove)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ApprovePayroll(Guid id, [FromBody] ApprovePayrollRequest request, CancellationToken ct = default)
    {
        var result = await _payrollService.ApprovePayrollAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollPeriodDto>.Fail(result.Errors.FirstOrDefault() ?? "Phê duyệt kỳ lương thất bại."));

        return Ok(ApiResponse<PayrollPeriodDto>.Ok(result.Value!, "Phê duyệt kỳ lương thành công."));
    }

    [HttpPost("periods/{id:guid}/mark-paid")]
    [HasPermission(Permissions.PayrollApprove)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MarkAsPaid(Guid id, [FromBody] MarkPayrollPaidRequest request, CancellationToken ct = default)
    {
        var result = await _payrollService.MarkAsPaidAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollPeriodDto>.Fail(result.Errors.FirstOrDefault() ?? "Đánh dấu đã chi lương thất bại."));

        return Ok(ApiResponse<PayrollPeriodDto>.Ok(result.Value!, "Xác nhận đã chi tiền lương thành công."));
    }

    [HttpPost("periods/{id:guid}/publish")]
    [HasPermission(Permissions.PayrollPublish)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayrollPeriodDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PublishPayroll(Guid id, [FromBody] PublishPayrollRequest request, CancellationToken ct = default)
    {
        var result = await _payrollService.PublishPayrollAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<PayrollPeriodDto>.Fail(result.Errors.FirstOrDefault() ?? "Phát hành phiếu lương thất bại."));

        return Ok(ApiResponse<PayrollPeriodDto>.Ok(result.Value!, "Phát hành phiếu lương cho nhân sự thành công."));
    }

    [HttpGet("periods/{id:guid}/approvals")]
    [HasPermission(Permissions.PayrollViewAll)]
    [ProducesResponseType(typeof(ApiResponse<List<PayrollApprovalDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetApprovals(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollService.GetApprovalsAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<List<PayrollApprovalDto>>.Fail(result.Errors.FirstOrDefault() ?? "Lấy lịch sử phê duyệt thất bại."));

        return Ok(ApiResponse<List<PayrollApprovalDto>>.Ok(result.Value!, "Lấy lịch sử phê duyệt thành công."));
    }

    [HttpGet("my-payslips")]
    [HasPermission(Permissions.PayrollViewPersonal)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<PayslipDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMyPayslips(
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _payrollService.GetMyPayslipsAsync(pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<PayslipDto>>.Ok(result, "Lấy danh sách phiếu lương cá nhân thành công."));
    }

    [HttpGet("my-payslips/{periodId:guid}")]
    [HasPermission(Permissions.PayrollViewPersonal)]
    [ProducesResponseType(typeof(ApiResponse<PayslipDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PayslipDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMyPayslipByPeriod(Guid periodId, CancellationToken ct = default)
    {
        var result = await _payrollService.GetPersonalPayslipAsync(periodId, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<PayslipDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy phiếu lương cá nhân."));

        return Ok(ApiResponse<PayslipDto>.Ok(result.Value!, "Lấy phiếu lương cá nhân thành công."));
    }
}
