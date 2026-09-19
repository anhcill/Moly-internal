namespace InternalManagement.Application.Features.HrPayroll.DTOs;

public record DepartmentDto(
    Guid Id,
    Guid CompanyId,
    string Code,
    string Name,
    int EmployeeCount,
    DateTime CreatedAt);

public record CreateDepartmentRequest(
    string Code,
    string Name);

public record UpdateDepartmentRequest(
    string Name);
