using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed class EmployeeService : IEmployeeService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<EmployeeService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    private async Task<Guid> GetCompanyIdAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        return companyId.Value;
    }

    public async Task<PaginatedResult<EmployeeDto>> GetEmployeesAsync(
        string? search, Guid? departmentId, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var scopedBusinessUnitIds = await ResolveBusinessUnitIdsAsync(companyId, businessUnitId, businessSegment, ct);
        var query = _db.Employees
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.CompanyId == companyId)
            .Include(e => e.Department)
            .Include(e => e.User)
            .AsQueryable();

        if (scopedBusinessUnitIds != null)
        {
            query = query.Where(e => e.BusinessUnitId.HasValue && scopedBusinessUnitIds.Contains(e.BusinessUnitId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(e => e.EmployeeCode.ToLower().Contains(s) ||
                                     e.FullName.ToLower().Contains(s) ||
                                     e.Email.ToLower().Contains(s) ||
                                     (e.Phone != null && e.Phone.Contains(s)) ||
                                     (e.Position != null && e.Position.ToLower().Contains(s)));
        }

        if (departmentId.HasValue && departmentId.Value != Guid.Empty)
        {
            query = query.Where(e => e.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToUpperInvariant();
            query = normalizedStatus switch
            {
                "WORKING" => query.Where(e => e.Status == "Active" || e.Status == "Probation" || e.Status == "OnLeave"),
                "RESIGNED" => query.Where(e => e.Status == "Resigned" || e.Status == "Inactive"),
                "BLACKLISTED" => query.Where(e => e.Status == "Blacklisted"),
                _ => query.Where(e => e.Status.ToUpper() == normalizedStatus)
            };
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderBy(e => e.EmployeeCode)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new EmployeeDto(
                e.Id,
                e.CompanyId,
                e.BusinessUnitId,
                null,
                e.DepartmentId,
                e.Department != null ? e.Department.Name : null,
                e.UserId,
                e.User != null ? e.User.Username : null,
                e.EmployeeCode,
                e.FullName,
                e.Email,
                e.Phone,
                e.Position,
                e.BaseSalary,
                e.JoinedDate,
                e.Status,
                e.CreatedAt,
                e.EmploymentType,
                e.EmploymentType == EmploymentType.PART_TIME ? "Bán thời gian" : "Toàn thời gian",
                e.PartTimeCalculationMethod,
                e.PartTimeCalculationMethod == PartTimeCalculationMethod.HOURLY ? "Theo giờ" :
                    e.PartTimeCalculationMethod == PartTimeCalculationMethod.SHIFT ? "Theo ca" : null,
                e.PartTimeUnitRate,
                e.CvUrlOrPath,
                e.ProfessionalSummary,
                e.Skills,
                e.Experience,
                e.StatusChangedAt,
                e.StatusReason))
            .ToListAsync(ct);

        var pageBusinessUnitIds = items
            .Where(e => e.BusinessUnitId.HasValue)
            .Select(e => e.BusinessUnitId!.Value)
            .Distinct()
            .ToList();
        if (pageBusinessUnitIds.Count > 0)
        {
            var businessUnitNames = await _db.BusinessUnits
                .AsNoTracking()
                .Where(b => b.CompanyId == companyId && pageBusinessUnitIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            items = items
                .Select(e => e.BusinessUnitId.HasValue && businessUnitNames.TryGetValue(e.BusinessUnitId.Value, out var name)
                    ? e with { BusinessUnitName = name }
                    : e)
                .ToList();
        }

        return new PaginatedResult<EmployeeDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<EmployeeDetailDto>> GetEmployeeByIdAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var emp = await _db.Employees
            .AsNoTracking()
            .Include(e => e.Department)
            .Include(e => e.User)
            .Include(e => e.AttendanceRecords)
            .Include(e => e.Payslips)
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted && e.CompanyId == companyId, ct);

        if (emp == null)
        {
            return Result<EmployeeDetailDto>.Failure("Không tìm thấy nhân viên.");
        }

        var businessUnitName = emp.BusinessUnitId.HasValue
            ? await _db.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == emp.BusinessUnitId.Value && b.CompanyId == companyId)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var detail = new EmployeeDetailDto(
            emp.Id,
            emp.CompanyId,
            emp.BusinessUnitId,
            businessUnitName,
            emp.DepartmentId,
            emp.Department?.Name,
            emp.UserId,
            emp.User?.Username,
            emp.EmployeeCode,
            emp.FullName,
            emp.Email,
            emp.Phone,
            emp.Position,
            emp.BaseSalary,
            emp.JoinedDate,
            emp.Status,
            emp.CreatedAt,
            emp.AttendanceRecords.Count,
            emp.Payslips.Count,
            emp.EmploymentType,
            GetEmploymentTypeNameVi(emp.EmploymentType),
            emp.PartTimeCalculationMethod,
            GetPartTimeMethodNameVi(emp.PartTimeCalculationMethod),
            emp.PartTimeUnitRate,
            emp.CvUrlOrPath,
            emp.ProfessionalSummary,
            emp.Skills,
            emp.Experience,
            emp.StatusChangedAt,
            emp.StatusReason);

        return Result<EmployeeDetailDto>.Success(detail);
    }

    public async Task<Result<EmployeeDto>> CreateEmployeeAsync(CreateEmployeeRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);

        if (string.IsNullOrWhiteSpace(request.EmployeeCode))
        {
            return Result<EmployeeDto>.Failure("Mã nhân viên không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return Result<EmployeeDto>.Failure("Họ và tên nhân viên không được để trống.");
        }

        var compensationValidation = ValidateCompensation(
            request.EmploymentType,
            request.BaseSalary,
            request.PartTimeCalculationMethod,
            request.PartTimeUnitRate);
        if (compensationValidation != null)
        {
            return Result<EmployeeDto>.Failure(compensationValidation);
        }

        if (request.BusinessUnitId.HasValue && !await IsValidBusinessUnitAsync(companyId, request.BusinessUnitId.Value, ct))
        {
            return Result<EmployeeDto>.Failure("Đơn vị kinh doanh của nhân viên không hợp lệ.");
        }

        var linkedUsername = await ValidateEmployeeUserLinkAsync(companyId, request.UserId, null, ct);
        if (linkedUsername is null && request.UserId.HasValue)
        {
            return Result<EmployeeDto>.Failure("Tài khoản liên kết không hợp lệ, đã bị khóa hoặc đã liên kết với nhân viên khác.");
        }

        var codeExists = await _db.Employees.AnyAsync(e =>
            !e.IsDeleted && e.CompanyId == companyId && e.EmployeeCode == request.EmployeeCode.Trim(), ct);
        if (codeExists)
        {
            return Result<EmployeeDto>.Failure($"Mã nhân viên '{request.EmployeeCode}' đã tồn tại trong hệ thống.");
        }

        var employeeStatus = NormalizeEmployeeStatus(request.Status);
        var emp = new Employee
        {
            CompanyId = companyId,
            BusinessUnitId = request.BusinessUnitId,
            DepartmentId = request.DepartmentId,
            UserId = request.UserId,
            EmployeeCode = request.EmployeeCode.Trim().ToUpperInvariant(),
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone?.Trim(),
            Position = request.Position?.Trim(),
            BaseSalary = request.BaseSalary,
            EmploymentType = request.EmploymentType,
            PartTimeCalculationMethod = request.EmploymentType == EmploymentType.PART_TIME
                ? request.PartTimeCalculationMethod
                : null,
            PartTimeUnitRate = request.EmploymentType == EmploymentType.PART_TIME
                ? request.PartTimeUnitRate
                : null,
            CvUrlOrPath = NormalizeOptional(request.CvUrlOrPath),
            ProfessionalSummary = NormalizeOptional(request.ProfessionalSummary),
            Skills = NormalizeOptional(request.Skills),
            Experience = NormalizeOptional(request.Experience),
            JoinedDate = request.JoinedDate ?? DateTime.UtcNow,
            Status = employeeStatus,
            StatusChangedAt = request.StatusChangedAt ?? (employeeStatus == "Active" ? null : DateTime.UtcNow),
            StatusReason = NormalizeOptional(request.StatusReason),
            CreatedBy = _currentUser.Username ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.Employees.Add(emp);
        await _db.SaveChangesAsync(ct);

        var deptName = request.DepartmentId.HasValue
            ? (await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == request.DepartmentId.Value, ct))?.Name
            : null;
        var businessUnitName = emp.BusinessUnitId.HasValue
            ? await _db.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == emp.BusinessUnitId.Value && b.CompanyId == companyId)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var dto = new EmployeeDto(
            emp.Id,
            emp.CompanyId,
            emp.BusinessUnitId,
            businessUnitName,
            emp.DepartmentId,
            deptName,
            emp.UserId,
            linkedUsername,
            emp.EmployeeCode,
            emp.FullName,
            emp.Email,
            emp.Phone,
            emp.Position,
            emp.BaseSalary,
            emp.JoinedDate,
            emp.Status,
            emp.CreatedAt,
            emp.EmploymentType,
            GetEmploymentTypeNameVi(emp.EmploymentType),
            emp.PartTimeCalculationMethod,
            GetPartTimeMethodNameVi(emp.PartTimeCalculationMethod),
            emp.PartTimeUnitRate,
            emp.CvUrlOrPath,
            emp.ProfessionalSummary,
            emp.Skills,
            emp.Experience,
            emp.StatusChangedAt,
            emp.StatusReason);

        return Result<EmployeeDto>.Success(dto);
    }

    public async Task<Result<EmployeeDto>> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted && e.CompanyId == companyId, ct);
        if (emp == null)
        {
            return Result<EmployeeDto>.Failure("Không tìm thấy nhân viên cần cập nhật.");
        }


        var employmentType = request.EmploymentType ?? emp.EmploymentType;
        var partTimeMethod = request.PartTimeCalculationMethod ?? emp.PartTimeCalculationMethod;
        var partTimeUnitRate = request.PartTimeUnitRate ?? emp.PartTimeUnitRate;
        var compensationValidation = ValidateCompensation(
            employmentType,
            request.BaseSalary,
            partTimeMethod,
            partTimeUnitRate);
        if (compensationValidation != null)
        {
            return Result<EmployeeDto>.Failure(compensationValidation);
        }

        if (request.BusinessUnitId.HasValue && !await IsValidBusinessUnitAsync(companyId, request.BusinessUnitId.Value, ct))
        {
            return Result<EmployeeDto>.Failure("Đơn vị kinh doanh của nhân viên không hợp lệ.");
        }

        var linkedUsername = emp.UserId.HasValue
            ? await _db.Users.AsNoTracking()
                .Where(user => user.Id == emp.UserId.Value && user.CompanyId == companyId && !user.IsDeleted)
                .Select(user => user.Username)
                .FirstOrDefaultAsync(ct)
            : null;
        if (request.UserId.HasValue)
        {
            linkedUsername = await ValidateEmployeeUserLinkAsync(companyId, request.UserId, emp.Id, ct);
            if (linkedUsername is null)
            {
                return Result<EmployeeDto>.Failure("Tài khoản liên kết không hợp lệ, đã bị khóa hoặc đã liên kết với nhân viên khác.");
            }

            emp.UserId = request.UserId;
        }

        emp.FullName = request.FullName.Trim();
        emp.Email = request.Email.Trim();
        emp.Phone = request.Phone?.Trim();
        emp.Position = request.Position?.Trim();
        emp.BaseSalary = request.BaseSalary;
        emp.EmploymentType = employmentType;
        emp.PartTimeCalculationMethod = employmentType == EmploymentType.PART_TIME ? partTimeMethod : null;
        emp.PartTimeUnitRate = employmentType == EmploymentType.PART_TIME ? partTimeUnitRate : null;
        if (request.CvUrlOrPath != null) emp.CvUrlOrPath = NormalizeOptional(request.CvUrlOrPath);
        if (request.ProfessionalSummary != null) emp.ProfessionalSummary = NormalizeOptional(request.ProfessionalSummary);
        if (request.Skills != null) emp.Skills = NormalizeOptional(request.Skills);
        if (request.Experience != null) emp.Experience = NormalizeOptional(request.Experience);
        emp.DepartmentId = request.DepartmentId;
        emp.BusinessUnitId = request.BusinessUnitId;
        if (request.JoinedDate.HasValue) emp.JoinedDate = request.JoinedDate.Value;
        var requestedStatus = NormalizeEmployeeStatus(request.Status);
        if (!string.Equals(emp.Status, requestedStatus, StringComparison.OrdinalIgnoreCase))
        {
            emp.Status = requestedStatus;
            emp.StatusChangedAt = request.StatusChangedAt ?? DateTime.UtcNow;
            emp.StatusReason = NormalizeOptional(request.StatusReason);
        }
        else
        {
            if (request.StatusChangedAt.HasValue) emp.StatusChangedAt = request.StatusChangedAt;
            if (request.StatusReason != null) emp.StatusReason = NormalizeOptional(request.StatusReason);
        }
        emp.UpdatedAt = DateTime.UtcNow;
        emp.UpdatedBy = _currentUser.Username ?? "system";

        await _db.SaveChangesAsync(ct);

        var deptName = emp.DepartmentId.HasValue
            ? (await _db.Departments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == emp.DepartmentId.Value, ct))?.Name
            : null;
        var businessUnitName = emp.BusinessUnitId.HasValue
            ? await _db.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == emp.BusinessUnitId.Value && b.CompanyId == companyId)
                .Select(b => b.Name)
                .FirstOrDefaultAsync(ct)
            : null;

        var dto = new EmployeeDto(
            emp.Id,
            emp.CompanyId,
            emp.BusinessUnitId,
            businessUnitName,
            emp.DepartmentId,
            deptName,
            emp.UserId,
            linkedUsername,
            emp.EmployeeCode,
            emp.FullName,
            emp.Email,
            emp.Phone,
            emp.Position,
            emp.BaseSalary,
            emp.JoinedDate,
            emp.Status,
            emp.CreatedAt,
            emp.EmploymentType,
            GetEmploymentTypeNameVi(emp.EmploymentType),
            emp.PartTimeCalculationMethod,
            GetPartTimeMethodNameVi(emp.PartTimeCalculationMethod),
            emp.PartTimeUnitRate,
            emp.CvUrlOrPath,
            emp.ProfessionalSummary,
            emp.Skills,
            emp.Experience,
            emp.StatusChangedAt,
            emp.StatusReason);

        return Result<EmployeeDto>.Success(dto);
    }

    public async Task<Result<bool>> DeleteEmployeeAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted && e.CompanyId == companyId, ct);
        if (emp == null)
        {
            return Result<bool>.Failure("Không tìm thấy nhân viên cần xóa.");
        }

        emp.IsDeleted = true;
        emp.DeletedAt = DateTime.UtcNow;
        emp.DeletedBy = _currentUser.Username ?? "system";

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private static string? ValidateCompensation(
        EmploymentType employmentType,
        decimal baseSalary,
        PartTimeCalculationMethod? partTimeMethod,
        decimal? partTimeUnitRate)
    {
        if (baseSalary < 0)
        {
            return "Lương cơ bản không được âm.";
        }

        if (employmentType == EmploymentType.PART_TIME && !partTimeMethod.HasValue)
        {
            return "Nhân sự bán thời gian phải chọn cách tính lương theo giờ hoặc theo ca.";
        }

        if (employmentType == EmploymentType.PART_TIME && (!partTimeUnitRate.HasValue || partTimeUnitRate <= 0))
        {
            return "Nhân sự bán thời gian phải có đơn giá lớn hơn 0.";
        }

        return null;
    }

    private static string GetEmploymentTypeNameVi(EmploymentType employmentType) =>
        employmentType == EmploymentType.PART_TIME ? "Bán thời gian" : "Toàn thời gian";

    private static string? GetPartTimeMethodNameVi(PartTimeCalculationMethod? method) => method switch
    {
        PartTimeCalculationMethod.HOURLY => "Theo giờ",
        PartTimeCalculationMethod.SHIFT => "Theo ca",
        _ => null
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeEmployeeStatus(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "PROBATION" => "Probation",
        "ONLEAVE" or "ON_LEAVE" => "OnLeave",
        "RESIGNED" or "INACTIVE" => "Resigned",
        "BLACKLISTED" or "BLACKLIST" => "Blacklisted",
        _ => "Active"
    };

    private async Task<List<Guid>?> ResolveBusinessUnitIdsAsync(
        Guid companyId,
        Guid? businessUnitId,
        string? segment,
        CancellationToken ct)
    {
        if (businessUnitId.HasValue && businessUnitId.Value != Guid.Empty)
        {
            return [businessUnitId.Value];
        }

        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        var normalizedSegment = NormalizeSegment(segment);
        var units = _db.BusinessUnits.AsNoTracking().Where(b =>
            b.CompanyId == companyId && !b.IsDeleted && b.IsActive);

        if (normalizedSegment == "FASHION" || normalizedSegment == "THOI_TRANG")
        {
            units = units.Where(b => b.Code.ToUpper() == "FASHION");
        }
        else if (normalizedSegment is "TECH_EDUCATION" or "TECHNOLOGY_EDUCATION" or "CONG_NGHE_GIAO_DUC" or "MOLY")
        {
            units = units.Where(b => b.Code.ToUpper() == "EDTECH" ||
                                     b.Code.ToUpper() == "CSCA" ||
                                     b.Code.ToUpper() == "INTERVIEW");
        }
        else
        {
            units = units.Where(b => b.Code.ToUpper() == normalizedSegment);
        }

        return await units.Select(b => b.Id).ToListAsync(ct);
    }

    private static string NormalizeSegment(string segment) => segment
        .Trim()
        .ToUpperInvariant()
        .Replace('-', '_')
        .Replace(' ', '_');

    private Task<bool> IsValidBusinessUnitAsync(Guid companyId, Guid businessUnitId, CancellationToken ct) =>
        _db.BusinessUnits.AsNoTracking().AnyAsync(b =>
            b.Id == businessUnitId && b.CompanyId == companyId && !b.IsDeleted && b.IsActive, ct);

    private async Task<string?> ValidateEmployeeUserLinkAsync(
        Guid companyId,
        Guid? userId,
        Guid? excludingEmployeeId,
        CancellationToken ct)
    {
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return null;
        }

        var username = await _db.Users.AsNoTracking()
            .Where(user => user.Id == userId.Value && user.CompanyId == companyId && !user.IsDeleted && user.IsActive)
            .Select(user => user.Username)
            .FirstOrDefaultAsync(ct);
        if (username is null)
        {
            return null;
        }

        var alreadyLinked = await _db.Employees.AsNoTracking().AnyAsync(employee =>
            !employee.IsDeleted &&
            employee.UserId == userId.Value &&
            (!excludingEmployeeId.HasValue || employee.Id != excludingEmployeeId.Value), ct);

        return alreadyLinked ? null : username;
    }
}
