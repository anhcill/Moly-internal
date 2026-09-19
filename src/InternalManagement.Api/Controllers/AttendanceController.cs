using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class AttendanceController : BaseApiController
{
    private readonly IAttendanceService _attendanceService;

    public AttendanceController(IAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [HttpGet]
    [HasPermission(Permissions.EmployeesView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<AttendanceRecordDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAttendance(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? employeeId,
        [FromQuery] Guid? departmentId,
        [FromQuery] string? status,
        [FromQuery] Guid? businessUnitId,
        [FromQuery] string? businessSegment,
        [FromQuery] string? segment,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var result = await _attendanceService.GetAttendanceRecordsAsync(
            fromDate, toDate, employeeId, departmentId, status, pageIndex, pageSize, ct,
            businessUnitId, businessSegment ?? segment);
        return Ok(ApiResponse<PaginatedResult<AttendanceRecordDto>>.Ok(result, "Lấy dữ liệu chấm công thành công."));
    }

    [HttpGet("summary")]
    [HasPermission(Permissions.EmployeesView)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAttendanceSummary(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        [FromQuery] Guid? departmentId,
        [FromQuery] Guid? businessUnitId,
        [FromQuery] string? businessSegment,
        [FromQuery] string? segment,
        CancellationToken ct = default)
    {
        var result = await _attendanceService.GetAttendanceSummaryAsync(
            fromDate, toDate, departmentId, ct, businessUnitId, businessSegment ?? segment);
        return Ok(ApiResponse<AttendanceSummaryDto>.Ok(result.Value!, "Lấy thống kê chấm công thành công."));
    }

    [HttpPost]
    [HasPermission(Permissions.EmployeesManage)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceRecordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceRecordDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RecordAttendance(
        [FromBody] CreateAttendanceRecordRequest request,
        CancellationToken ct = default)
    {
        var result = await _attendanceService.RecordAttendanceAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<AttendanceRecordDto>.Fail(result.Errors.FirstOrDefault() ?? "Ghi nhận chấm công thất bại."));

        return Ok(ApiResponse<AttendanceRecordDto>.Ok(result.Value!, "Ghi nhận chấm công thành công."));
    }

    [HttpPost("import")]
    [HasPermission(Permissions.AttendanceImport)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceImportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AttendanceImportResultDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportAttendance(
        [FromForm] IFormFile file,
        [FromForm] Guid? businessUnitId,
        [FromForm] string? businessSegment,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(ApiResponse<AttendanceImportResultDto>.Fail("Vui lòng chọn file chấm công để tải lên."));
        }

        using var stream = file.OpenReadStream();
        var result = await _attendanceService.ImportAttendanceAsync(stream, file.FileName, ct, businessUnitId, businessSegment);
        if (!result.Succeeded)
        {
            return BadRequest(ApiResponse<AttendanceImportResultDto>.Fail(result.Errors.FirstOrDefault() ?? "Xử lý file chấm công thất bại."));
        }

        return Ok(ApiResponse<AttendanceImportResultDto>.Ok(result.Value!, "Xử lý file chấm công hoàn tất."));
    }

    [HttpGet("template")]
    [HasPermission(Permissions.AttendanceImport)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public async Task<IActionResult> DownloadImportTemplate(CancellationToken ct)
    {
        var file = await _attendanceService.CreateImportTemplateAsync(ct);
        return File(file.Content, file.ContentType, file.FileName);
    }
}
