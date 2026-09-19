using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class DepartmentsController : BaseApiController
{
    private readonly IDepartmentService _departmentService;

    public DepartmentsController(IDepartmentService departmentService)
    {
        _departmentService = departmentService;
    }

    [HttpGet]
    [HasPermission(Permissions.EmployeesView)]
    [ProducesResponseType(typeof(ApiResponse<List<DepartmentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDepartments(CancellationToken ct = default)
    {
        var result = await _departmentService.GetDepartmentsAsync(ct);
        return Ok(ApiResponse<List<DepartmentDto>>.Ok(result.Value ?? new List<DepartmentDto>(), "Lấy danh sách phòng ban thành công."));
    }

    [HttpPost]
    [HasPermission(Permissions.EmployeesManage)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateDepartment([FromBody] CreateDepartmentRequest request, CancellationToken ct = default)
    {
        var result = await _departmentService.CreateDepartmentAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<DepartmentDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo phòng ban thất bại."));

        return Ok(ApiResponse<DepartmentDto>.Ok(result.Value!, "Tạo phòng ban thành công."));
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.EmployeesManage)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<DepartmentDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateDepartment(Guid id, [FromBody] UpdateDepartmentRequest request, CancellationToken ct = default)
    {
        var result = await _departmentService.UpdateDepartmentAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<DepartmentDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật phòng ban thất bại."));

        return Ok(ApiResponse<DepartmentDto>.Ok(result.Value!, "Cập nhật phòng ban thành công."));
    }
}
