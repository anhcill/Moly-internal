using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class PayrollService
{
    public async Task<Result<PayrollPolicyVersionDto>> GetActivePolicyAsync(CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var policy = await _db.PayrollPolicyVersions
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .OrderByDescending(p => p.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (policy == null)
        {
            return Result<PayrollPolicyVersionDto>.Failure("Không tìm thấy chính sách lương hiệu lực.");
        }

        var dto = new PayrollPolicyVersionDto(
            policy.Id,
            policy.CompanyId,
            policy.VersionNumber,
            policy.EffectiveDate,
            policy.ConfigJson,
            policy.IsActive,
            policy.CreatedAt);

        return Result<PayrollPolicyVersionDto>.Success(dto);
    }

    public async Task<Result<PayrollPolicyVersionDto>> CreatePolicyVersionAsync(CreatePayrollPolicyVersionRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);

        var policy = new PayrollPolicyVersion
        {
            CompanyId = companyId,
            VersionNumber = request.VersionNumber,
            EffectiveDate = request.EffectiveDate,
            ConfigJson = request.ConfigJson,
            IsActive = true,
            CreatedBy = _currentUser.Username ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.PayrollPolicyVersions.Add(policy);
        await _db.SaveChangesAsync(ct);

        var dto = new PayrollPolicyVersionDto(
            policy.Id,
            policy.CompanyId,
            policy.VersionNumber,
            policy.EffectiveDate,
            policy.ConfigJson,
            policy.IsActive,
            policy.CreatedAt);

        return Result<PayrollPolicyVersionDto>.Success(dto);
    }

}
