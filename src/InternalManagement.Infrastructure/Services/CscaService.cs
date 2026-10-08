using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService : ICscaService
{
    private readonly ICscaDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CscaService> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;
    private readonly ILmsAccessLifecycleService? _lmsAccessLifecycleService;
    private readonly IApplicationDbContext? _integrationDb;

    public CscaService(
        ICscaDbContext db,
        ICurrentUserService currentUser,
        ILogger<CscaService> logger,
        IPartyResolver? partyResolver = null,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null,
        ILmsAccessLifecycleService? lmsAccessLifecycleService = null,
        IApplicationDbContext? integrationDb = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _partyResolver = partyResolver;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
        _lmsAccessLifecycleService = lmsAccessLifecycleService;
        _integrationDb = integrationDb;
    }

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetContextAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        var bu = await _db.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Code == "CSCA", ct);
        var buId = bu?.Id ?? _currentUser.BusinessUnitId;

        return (companyId.Value, buId);
    }

}
