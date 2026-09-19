using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface IEmployeeService
{
    Task<PaginatedResult<EmployeeDto>> GetEmployeesAsync(
        string? search, Guid? departmentId, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null);

    Task<Result<EmployeeDetailDto>> GetEmployeeByIdAsync(Guid id, CancellationToken ct);

    Task<Result<EmployeeDto>> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken ct);

    Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, CancellationToken ct);

    Task<Result<bool>> DeleteEmployeeAsync(Guid id, CancellationToken ct);
}
