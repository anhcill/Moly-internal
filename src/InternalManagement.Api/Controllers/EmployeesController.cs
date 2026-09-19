using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class EmployeesController : BaseApiController
{
    private readonly IEmployeeService _employeeService;

    public EmployeesController(IEmployeeService employeeService)
    {
        _employeeService = employeeService;
    }

    [HttpGet]
    [HasPermission(Permissions.EmployeesView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<EmployeeDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEmployees(
        [FromQuery] string? search,
        [FromQuery] Guid? departmentId,
        [FromQuery] string? status,
        [FromQuery] Guid? businessUnitId,
        [FromQuery] string? businessSegment,
        [FromQuery] string? segment,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _employeeService.GetEmployeesAsync(
            search, departmentId, status, pageIndex, pageSize, ct, businessUnitId, businessSegment ?? segment);
        return Ok(ApiResponse<PaginatedResult<EmployeeDto>>.Ok(result, "Lấy danh sách nhân viên thành công."));
    }

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.EmployeesView)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEmployeeById(Guid id, CancellationToken ct)
    {
        var result = await _employeeService.GetEmployeeByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<EmployeeDetailDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy nhân viên."));

        return Ok(ApiResponse<EmployeeDetailDto>.Ok(result.Value!, "Lấy thông tin nhân viên thành công."));
    }

    [HttpPost]
    [HasPermission(Permissions.EmployeesManage)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateEmployee([FromBody] CreateEmployeeRequest request, CancellationToken ct)
    {
        var result = await _employeeService.CreateEmployeeAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<EmployeeDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo nhân viên thất bại."));

        return CreatedAtAction(nameof(GetEmployeeById), new { id = result.Value!.Id }, ApiResponse<EmployeeDto>.Ok(result.Value, "Tạo nhân viên thành công."));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.EmployeesManage)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<EmployeeDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateEmployee(Guid id, [FromBody] UpdateEmployeeRequest request, CancellationToken ct)
    {
        var result = await _employeeService.UpdateEmployeeAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<EmployeeDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật nhân viên thất bại."));

        return Ok(ApiResponse<EmployeeDto>.Ok(result.Value!, "Cập nhật nhân viên thành công."));
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.EmployeesManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteEmployee(Guid id, CancellationToken ct)
    {
        var result = await _employeeService.DeleteEmployeeAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa nhân viên thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Xóa nhân viên thành công."));
    }
}
