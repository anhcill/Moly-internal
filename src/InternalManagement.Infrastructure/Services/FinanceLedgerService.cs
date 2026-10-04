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

public sealed partial class FinanceLedgerService : IFinanceLedgerService
{
    private const string TechnologyEducationAreaCode = "TECHNOLOGY_EDUCATION";
    private const string FashionAreaCode = "FASHION";
    private static readonly string[] TechnologyEducationBusinessUnitCodes = ["EDTECH", "CSCA", "INTERVIEW"];
    private static readonly string[] FashionBusinessUnitCodes = ["FASHION"];

    private readonly IFinanceDbContext _db;
    private readonly ILogger<FinanceLedgerService> _logger;
    private readonly ICurrentUserService? _currentUser;
    private readonly IBusinessDocumentRegistry? _documentRegistry;

    public FinanceLedgerService(
        IFinanceDbContext db,
        ILogger<FinanceLedgerService> logger,
        ICurrentUserService? currentUser = null,
        IBusinessDocumentRegistry? documentRegistry = null)
    {
        _db = db;
        _logger = logger;
        _currentUser = currentUser;
        _documentRegistry = documentRegistry;
    }

}
