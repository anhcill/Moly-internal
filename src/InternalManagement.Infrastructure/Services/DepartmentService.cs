using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Domain.Entities.Identity;

namespace InternalManagement.Infrastructure.Services;

public sealed class DepartmentService : IDepartmentService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DepartmentService> _logger;

    public DepartmentService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<DepartmentService> logger)
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

    public async Task<Result<List<DepartmentDto>>> GetDepartmentsAsync(CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var departments = await _db.Departments
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .OrderBy(d => d.Code)
            .ToListAsync(ct);

        var employeeCounts = await _db.Employees
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.CompanyId == companyId && e.DepartmentId.HasValue)
            .GroupBy(e => e.DepartmentId!.Value)
            .Select(g => new { DepartmentId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DepartmentId, x => x.Count, ct);

        var dtos = departments.Select(d => new DepartmentDto(
            d.Id,
            d.CompanyId,
            d.Code,
            d.Name,
            employeeCounts.GetValueOrDefault(d.Id, 0),
            d.CreatedAt)).ToList();

        return Result<List<DepartmentDto>>.Success(dtos);
    }

    public async Task<Result<DepartmentDto>> CreateDepartmentAsync(CreateDepartmentRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Result<DepartmentDto>.Failure("Mã phòng ban không được để trống.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<DepartmentDto>.Failure("Tên phòng ban không được để trống.");
        }

        var exists = await _db.Departments.AnyAsync(d =>
            d.CompanyId == companyId && d.Code == request.Code.Trim().ToUpperInvariant(), ct);
        if (exists)
        {
            return Result<DepartmentDto>.Failure($"Mã phòng ban '{request.Code}' đã tồn tại.");
        }

        var dept = new Department
        {
            CompanyId = companyId,
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            CreatedBy = _currentUser.Username ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.Departments.Add(dept);
        await _db.SaveChangesAsync(ct);

        var dto = new DepartmentDto(dept.Id, dept.CompanyId, dept.Code, dept.Name, 0, dept.CreatedAt);
        return Result<DepartmentDto>.Success(dto);
    }

    public async Task<Result<DepartmentDto>> UpdateDepartmentAsync(Guid id, UpdateDepartmentRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var dept = await _db.Departments.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId, ct);
        if (dept == null)
        {
            return Result<DepartmentDto>.Failure("Không tìm thấy phòng ban.");
        }

        dept.Name = request.Name.Trim();
        dept.UpdatedAt = DateTime.UtcNow;
        dept.UpdatedBy = _currentUser.Username ?? "system";

        await _db.SaveChangesAsync(ct);

        var count = await _db.Employees.CountAsync(e => !e.IsDeleted && e.DepartmentId == dept.Id, ct);
        var dto = new DepartmentDto(dept.Id, dept.CompanyId, dept.Code, dept.Name, count, dept.CreatedAt);
        return Result<DepartmentDto>.Success(dto);
    }
}
