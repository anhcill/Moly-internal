using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class FinanceLedgerService
{
    public async Task<Result<FinanceReferenceDto>> ResolveReferenceAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referenceType) || referenceId == Guid.Empty)
            return Result<FinanceReferenceDto>.Failure("ReferenceType và ReferenceId là bắt buộc.");

        var companyId = await ResolveCompanyIdAsync(ct);
        if (!companyId.HasValue)
            return Result<FinanceReferenceDto>.Failure("Chưa xác định được công ty hiện tại.");

        var result = await ResolveReferenceInternalAsync(
            NormalizeReferenceType(referenceType), referenceId, companyId.Value, ct);
        return Result<FinanceReferenceDto>.Success(result);
    }

}
