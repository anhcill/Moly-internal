using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Domain.Entities.Finance;

namespace InternalManagement.Infrastructure.Services;

public sealed class ProfitAllocationService : IProfitAllocationService
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<ProfitAllocationService> _logger;
    private readonly ICurrentUserService? _currentUser;

    public ProfitAllocationService(
        IApplicationDbContext db,
        ILogger<ProfitAllocationService> logger,
        ICurrentUserService? currentUser = null)
    {
        _db = db;
        _logger = logger;
        _currentUser = currentUser;
    }

    public async Task<Result<BusinessUnitProfitSummaryDto>> GetProfitSummaryByBusinessUnitAsync(
        string businessUnitCode, CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<BusinessUnitProfitSummaryDto>.Failure(
                "Chưa xác định được công ty hiện tại; không thể đọc phân bổ lợi nhuận an toàn giữa các tenant.");

        var query = _db.BusinessUnits
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId.Value
                && b.Code == businessUnitCode.ToUpperInvariant());

        var bu = await query.FirstOrDefaultAsync(ct);

        if (bu == null)
        {
            return Result<BusinessUnitProfitSummaryDto>.Failure($"Không tìm thấy Business Unit '{businessUnitCode}'.");
        }

        var allocations = await _db.ProfitAllocations
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId.Value && p.BusinessUnitId == bu.Id)
            .OrderByDescending(p => p.AllocatedAt)
            .ToListAsync(ct);

        var totalIncome = allocations.Sum(p => p.IncomeAmount);
        var totalExpense = allocations.Sum(p => p.ExpenseAmount);

        // Fetch reference titles
        var classIds = allocations.Where(a => a.ReferenceType == "CscaClass").Select(a => a.ReferenceId).ToList();
        var classes = await _db.CscaClasses
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId.Value && classIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => $"{c.Code} - {c.Name}", ct);

        var customerIds = allocations.Where(a => a.ReferenceType == "InterviewCustomer").Select(a => a.ReferenceId).ToList();
        var customers = await _db.InterviewCustomers
            .AsNoTracking()
            .Where(c => c.CompanyId == companyId.Value && customerIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => $"{c.FullName} ({c.PackageName})", ct);

        var items = allocations.Select(a =>
        {
            var title = a.ReferenceType switch
            {
                "CscaClass" => classes.GetValueOrDefault(a.ReferenceId, $"Lớp CSCA {a.ReferenceId}"),
                "InterviewCustomer" => customers.GetValueOrDefault(a.ReferenceId, $"Khách hàng Interview {a.ReferenceId}"),
                _ => $"{a.ReferenceType} - {a.ReferenceId}"
            };

            return new ProfitAllocationItemDto
            {
                Id = a.Id,
                ReferenceType = a.ReferenceType,
                ReferenceId = a.ReferenceId,
                ReferenceTitle = title,
                IncomeAmount = a.IncomeAmount,
                ExpenseAmount = a.ExpenseAmount,
                AllocatedAt = a.AllocatedAt
            };
        }).ToList();

        var summary = new BusinessUnitProfitSummaryDto
        {
            BusinessUnitId = bu.Id,
            BusinessUnitCode = bu.Code,
            BusinessUnitName = bu.Name,
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            Allocations = items
        };

        return Result<BusinessUnitProfitSummaryDto>.Success(summary);
    }

    public async Task<Result<List<BusinessUnitProfitSummaryDto>>> GetAllBusinessUnitsProfitSummaryAsync(CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<List<BusinessUnitProfitSummaryDto>>.Failure(
                "Chưa xác định được công ty hiện tại; không thể đọc phân bổ lợi nhuận an toàn giữa các tenant.");

        var query = _db.BusinessUnits
            .AsNoTracking()
            .Where(b => b.CompanyId == companyId.Value && b.IsActive);

        var bus = await query.ToListAsync(ct);
        var resultList = new List<BusinessUnitProfitSummaryDto>();

        foreach (var bu in bus)
        {
            var summaryResult = await GetProfitSummaryByBusinessUnitAsync(bu.Code, ct);
            if (summaryResult.Succeeded && summaryResult.Value != null)
            {
                resultList.Add(summaryResult.Value);
            }
        }

        return Result<List<BusinessUnitProfitSummaryDto>>.Success(resultList);
    }

    public async Task<Result<bool>> RecordAllocationAsync(RecordProfitAllocationRequest request, CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<bool>.Failure(
                "Chưa xác định được công ty hiện tại; không thể ghi phân bổ lợi nhuận an toàn giữa các tenant.");

        var query = _db.BusinessUnits.Where(b => b.CompanyId == companyId.Value
            && b.Code == request.BusinessUnitCode.ToUpperInvariant());

        var bu = await query.FirstOrDefaultAsync(ct);
        if (bu == null)
        {
            return Result<bool>.Failure($"Không tìm thấy Business Unit '{request.BusinessUnitCode}'.");
        }

        var allocation = await _db.ProfitAllocations
            .FirstOrDefaultAsync(p => p.CompanyId == companyId.Value
                && p.BusinessUnitId == bu.Id
                && p.ReferenceType == request.ReferenceType
                && p.ReferenceId == request.ReferenceId, ct);

        if (allocation == null)
        {
            allocation = new Domain.Entities.CscaInterview.ProfitAllocation
            {
                CompanyId = bu.CompanyId,
                BusinessUnitId = bu.Id,
                ReferenceType = request.ReferenceType,
                ReferenceId = request.ReferenceId,
                IncomeAmount = request.IncomeAmount,
                ExpenseAmount = request.ExpenseAmount,
                AllocatedAt = DateTime.UtcNow
            };
            _db.ProfitAllocations.Add(allocation);
        }
        else
        {
            allocation.IncomeAmount = request.IncomeAmount;
            allocation.ExpenseAmount = request.ExpenseAmount;
            allocation.AllocatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private async Task<Guid?> ResolveCompanyIdAsync(CancellationToken ct)
    {
        if (_currentUser?.CompanyId is { } companyId && companyId != Guid.Empty)
            return companyId;

        var companyIds = await _db.Companies
            .AsNoTracking()
            .Select(x => x.Id)
            .Take(2)
            .ToListAsync(ct);
        return companyIds.Count == 1 ? companyIds[0] : null;
    }
}
