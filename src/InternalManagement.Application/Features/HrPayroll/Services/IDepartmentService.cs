using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface IDepartmentService
{
    Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct);

    Task<Result<DepartmentDto>> CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken ct);

    Task<Result<DepartmentDto>> UpdateDepartmentAsync(Guid id, UpdateDepartmentRequest request, CancellationToken ct);
}
