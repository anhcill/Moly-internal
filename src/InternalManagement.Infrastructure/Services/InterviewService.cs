using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Mail;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed class InterviewService : IInterviewService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<InterviewService> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public InterviewService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<InterviewService> logger,
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

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetContextAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        var bu = await _db.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Code == "INTERVIEW", ct);
        var buId = bu?.Id ?? _currentUser.BusinessUnitId;

        return (companyId.Value, buId);
    }

    public async Task<PaginatedResult<InterviewCustomerDto>> GetCustomersAsync(
        string? search, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, _) = await GetContextAsync(ct);
        var query = _db.InterviewCustomers.AsNoTracking()
            .Where(c => c.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.FullName.ToLower().Contains(s) ||
                                     c.Email.ToLower().Contains(s) ||
                                     (c.Phone != null && c.Phone.Contains(s)) ||
                                     c.PackageName.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new InterviewCustomerDto
            {
                Id = c.Id,
                SourceSystem = c.SourceSystem,
                SourceId = c.SourceId,
                FullName = c.FullName,
                Email = c.Email,
                Phone = c.Phone,
                PackageName = c.PackageName,
                SessionCount = c.SessionCount,
                PaidAmount = c.PaidAmount,
                Status = c.Status,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(ct);

        return new PaginatedResult<InterviewCustomerDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<InterviewCustomerDto>> GetCustomerByIdAsync(Guid id, CancellationToken ct)
    {
        var (companyId, _) = await GetContextAsync(ct);
        var c = await _db.InterviewCustomers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id &&
            x.CompanyId == companyId, ct);
        if (c == null)
        {
            return Result<InterviewCustomerDto>.Failure("Không tìm thấy thông tin khách hàng Interview.");
        }

        var dto = new InterviewCustomerDto
        {
            Id = c.Id,
            SourceSystem = c.SourceSystem,
            SourceId = c.SourceId,
            FullName = c.FullName,
            Email = c.Email,
            Phone = c.Phone,
            PackageName = c.PackageName,
            SessionCount = c.SessionCount,
            PaidAmount = c.PaidAmount,
            Status = c.Status,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        };

        return Result<InterviewCustomerDto>.Success(dto);
    }

    public async Task<Result<InterviewCustomerDto>> CreateCustomerAsync(CreateInterviewCustomerRequest request, CancellationToken ct)
    {
        var (companyId, buId) = await GetContextAsync(ct);

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email))
        {
            return Result<InterviewCustomerDto>.Failure("Họ tên và email khách hàng là bắt buộc.");
        }
        try { _ = new MailAddress(request.Email.Trim()); }
        catch (FormatException) { return Result<InterviewCustomerDto>.Failure("Email khách hàng không hợp lệ."); }
        if (request.PaidAmount < 0)
            return Result<InterviewCustomerDto>.Failure("Số tiền đã thanh toán không được âm.");

        var sourceId = string.IsNullOrWhiteSpace(request.SourceId)
            ? $"INT_{DateTime.UtcNow:yyyyMMddHHmmss}_{Random.Shared.Next(100, 999)}"
            : request.SourceId.Trim();

        var duplicateSource = await _db.InterviewCustomers.AnyAsync(c => c.CompanyId == companyId &&
            c.SourceSystem == "WEBSITE_INTERVIEW" && c.SourceId == sourceId, ct);
        if (duplicateSource)
            return Result<InterviewCustomerDto>.Failure($"Mã khách hàng từ website '{sourceId}' đã tồn tại.");

        var customer = new InterviewCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            SourceSystem = "WEBSITE_INTERVIEW",
            SourceId = sourceId,
            FullName = request.FullName.Trim(),
            Email = request.Email.Trim(),
            Phone = request.Phone?.Trim(),
            PackageName = request.PackageName.Trim(),
            SessionCount = request.SessionCount > 0 ? request.SessionCount : 1,
            PaidAmount = request.PaidAmount,
            Status = request.Status,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System"
        };

        _db.InterviewCustomers.Add(customer);

        // Record profit allocation
        var allocation = new ProfitAllocation
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            ReferenceType = "InterviewCustomer",
            ReferenceId = customer.Id,
            IncomeAmount = customer.PaidAmount,
            ExpenseAmount = 0,
            AllocatedAt = DateTime.UtcNow
        };
        _db.ProfitAllocations.Add(allocation);

        await SyncCustomerDataLinksAsync(customer, ct);

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tạo khách hàng Interview thành công: {FullName} ({Package})", customer.FullName, customer.PackageName);

        var dto = new InterviewCustomerDto
        {
            Id = customer.Id,
            SourceSystem = customer.SourceSystem,
            SourceId = customer.SourceId,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            PackageName = customer.PackageName,
            SessionCount = customer.SessionCount,
            PaidAmount = customer.PaidAmount,
            Status = customer.Status,
            CreatedAt = customer.CreatedAt
        };

        return Result<InterviewCustomerDto>.Success(dto);
    }

    public async Task<Result<InterviewCustomerDto>> UpdateCustomerAsync(Guid id, UpdateInterviewCustomerRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var customer = await _db.InterviewCustomers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId &&
            (!businessUnitId.HasValue || x.BusinessUnitId == businessUnitId), ct);
        if (customer == null)
        {
            return Result<InterviewCustomerDto>.Failure("Không tìm thấy khách hàng Interview cần cập nhật.");
        }

        if (string.IsNullOrWhiteSpace(request.FullName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.PackageName))
            return Result<InterviewCustomerDto>.Failure("Họ tên, email và gói dịch vụ là bắt buộc.");
        try { _ = new MailAddress(request.Email.Trim()); }
        catch (FormatException) { return Result<InterviewCustomerDto>.Failure("Email khách hàng không hợp lệ."); }
        if (request.PaidAmount < 0)
            return Result<InterviewCustomerDto>.Failure("Số tiền đã thanh toán không được âm.");
        customer.FullName = request.FullName.Trim();
        customer.Email = request.Email.Trim();
        customer.Phone = request.Phone?.Trim();
        customer.PackageName = request.PackageName.Trim();
        customer.SessionCount = request.SessionCount;
        customer.PaidAmount = request.PaidAmount;
        customer.Status = request.Status;
        customer.UpdatedAt = DateTime.UtcNow;
        customer.UpdatedBy = _currentUser.Username ?? "System";

        // Sync profit allocation
        var allocation = await _db.ProfitAllocations
            .FirstOrDefaultAsync(p => p.ReferenceType == "InterviewCustomer" && p.ReferenceId == customer.Id, ct);

        if (allocation != null)
        {
            allocation.IncomeAmount = customer.PaidAmount;
            allocation.AllocatedAt = DateTime.UtcNow;
        }

        await SyncCustomerDataLinksAsync(customer, ct);

        await _db.SaveChangesAsync(ct);

        var dto = new InterviewCustomerDto
        {
            Id = customer.Id,
            SourceSystem = customer.SourceSystem,
            SourceId = customer.SourceId,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            PackageName = customer.PackageName,
            SessionCount = customer.SessionCount,
            PaidAmount = customer.PaidAmount,
            Status = customer.Status,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt
        };

        return Result<InterviewCustomerDto>.Success(dto);
    }

    public async Task<Result<bool>> DeleteCustomerAsync(Guid id, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var customer = await _db.InterviewCustomers.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId &&
            (!businessUnitId.HasValue || x.BusinessUnitId == businessUnitId), ct);
        if (customer == null)
        {
            return Result<bool>.Failure("Không tìm thấy khách hàng Interview.");
        }

        var allocation = await _db.ProfitAllocations
            .FirstOrDefaultAsync(p => p.ReferenceType == "InterviewCustomer" && p.ReferenceId == customer.Id, ct);

        if (allocation != null)
        {
            _db.ProfitAllocations.Remove(allocation);
        }

        _db.InterviewCustomers.Remove(customer);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    private async Task SyncCustomerDataLinksAsync(InterviewCustomer customer, CancellationToken ct)
    {
        if (_partyResolver is not null)
        {
            var party = await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                customer.CompanyId,
                customer.BusinessUnitId,
                PartyType.Individual,
                PartyRole.Customer,
                customer.FullName,
                customer.Email,
                customer.Phone,
                customer.SourceSystem,
                customer.SourceId), ct);
            customer.PartyId = party.Id;
        }

        if (_documentRegistry is not null)
        {
            var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                customer.CompanyId,
                customer.BusinessUnitId,
                BusinessDocumentType.InterviewService,
                nameof(InterviewCustomer),
                customer.Id,
                $"INTERVIEW-{customer.Id:N}",
                customer.PaidAmount,
                customer.UpdatedAt ?? customer.CreatedAt,
                customer.PartyId,
                Status: ToDocumentStatus(customer.Status),
                ExternalSourceSystem: customer.SourceSystem,
                ExternalSourceId: customer.SourceId), ct);
            customer.BusinessDocumentId = document.Id;
        }

        if (_financePostingService is null || customer.PaidAmount <= 0)
        {
            return;
        }

        var transactionType = customer.Status switch
        {
            PaymentStatus.Paid or PaymentStatus.Partial => TransactionType.Income,
            PaymentStatus.Refunded => TransactionType.Expense,
            _ => (TransactionType?)null
        };
        if (!transactionType.HasValue)
        {
            return;
        }

        var description = transactionType == TransactionType.Income
            ? $"Thu dịch vụ Mock Interview {customer.FullName} ({customer.PackageName})"
            : $"Hoàn tiền dịch vụ Mock Interview {customer.FullName} ({customer.PackageName})";
        await _financePostingService.PostAsync(new FinancePostingRequest(
            customer.CompanyId,
            customer.BusinessUnitId,
            transactionType.Value,
            customer.PaidAmount,
            customer.UpdatedAt ?? customer.CreatedAt,
            nameof(InterviewCustomer),
            customer.Id,
            description,
            customer.BusinessDocumentId), ct);
    }

    private static BusinessDocumentStatus ToDocumentStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => BusinessDocumentStatus.Settled,
        PaymentStatus.Failed or PaymentStatus.Refunded or PaymentStatus.Cancelled => BusinessDocumentStatus.Voided,
        _ => BusinessDocumentStatus.Open
    };

    public async Task<Result<InterviewFinancialSummaryDto>> GetFinancialSummaryAsync(CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var customers = await _db.InterviewCustomers.AsNoTracking()
            .Where(c => c.CompanyId == companyId && (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId))
            .ToListAsync(ct);

        var totalCustomers = customers.Count;
        var totalSessions = customers.Sum(c => c.SessionCount);
        var totalRevenue = customers.Where(c => c.Status == PaymentStatus.Paid).Sum(c => c.PaidAmount);
        var pendingRevenue = customers.Where(c => c.Status == PaymentStatus.Pending).Sum(c => c.PaidAmount);

        var summary = new InterviewFinancialSummaryDto
        {
            TotalCustomers = totalCustomers,
            TotalSessions = totalSessions,
            TotalRevenue = totalRevenue,
            PendingRevenue = pendingRevenue
        };

        return Result<InterviewFinancialSummaryDto>.Success(summary);
    }
}
