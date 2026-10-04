using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Fashion.DTOs;
using InternalManagement.Application.Features.Fashion.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class OrderCostingService : IOrderCostingService
{
    private readonly IOrderDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<OrderCostingService> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public OrderCostingService(
        IOrderDbContext db,
        ICurrentUserService currentUser,
        ILogger<OrderCostingService> logger,
        IPartyResolver? partyResolver = null,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _partyResolver = partyResolver;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
    }

}
