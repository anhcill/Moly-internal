using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Enums;
using InternalManagement.Domain.Entities.MasterData;

namespace InternalManagement.Infrastructure.Integration.Mappers;

/// <summary>
/// Upsert customer từ API CSCA-Interview theo khóa nguồn.
/// Không tạo customer trùng khi chạy lại cùng một trang hoặc cùng một cursor.
/// </summary>
public sealed partial class InterviewCustomerMapper : IEntityMapper
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<InterviewCustomerMapper> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public InterviewCustomerMapper(
        IApplicationDbContext db,
        ILogger<InterviewCustomerMapper> logger,
        IPartyResolver? partyResolver = null,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null)
    {
        _db = db;
        _logger = logger;
        _partyResolver = partyResolver;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
    }

    public string EntityType => "InterviewCustomers";

    public async Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item,
        string sourceSystem,
        Guid companyId,
        Guid businessUnitId,
        CancellationToken ct)
    {
        ExternalInterviewCustomerDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExternalInterviewCustomerDto>(item.GetRawText(), JsonOptions);
        }
        catch (JsonException ex)
        {
            return new EntityMapResult(MapResultStatus.Failed, null, "JSON_PARSE", ex.Message);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            return new EntityMapResult(MapResultStatus.Failed, null, "MISSING_ID", "Interview customer ID is required.");

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Interview customer full name is required.");

        if (string.IsNullOrWhiteSpace(dto.Email) || !EmailRegex().IsMatch(dto.Email))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", $"Invalid email: '{dto.Email}'.");

        if (dto.SessionCount < 1)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Session count must be at least 1.");

        if (dto.PaidAmount < 0)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Paid amount cannot be negative.");

        if (string.IsNullOrWhiteSpace(dto.PackageName))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Interview package name is required.");

        if (!Enum.TryParse<PaymentStatus>(dto.Status, true, out var status))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", $"Invalid payment status: '{dto.Status}'.");

        var existing = await _db.InterviewCustomers.FirstOrDefaultAsync(
            c => c.CompanyId == companyId
                 && c.SourceSystem == sourceSystem
                 && c.SourceId == dto.Id,
            ct);

        var party = _partyResolver is null
            ? null
            : await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                companyId,
                businessUnitId == Guid.Empty ? null : businessUnitId,
                PartyType.Individual,
                PartyRole.Customer,
                dto.FullName,
                dto.Email,
                dto.PhoneNumber,
                sourceSystem,
                dto.Id), ct);

        if (existing != null)
        {
            var changed = existing.FullName != dto.FullName
                          || existing.Email != dto.Email
                          || existing.Phone != dto.PhoneNumber
                          || existing.PackageName != dto.PackageName
                          || existing.SessionCount != dto.SessionCount
                          || existing.PaidAmount != dto.PaidAmount
                          || existing.Status != status
                          || (party is not null && existing.PartyId != party.Id);

            if (changed)
            {
                existing.FullName = dto.FullName;
                existing.Email = dto.Email;
                existing.Phone = dto.PhoneNumber;
                if (party is not null)
                    existing.PartyId = party.Id;
                existing.PackageName = dto.PackageName;
                existing.SessionCount = dto.SessionCount;
                existing.PaidAmount = dto.PaidAmount;
                existing.Status = status;
                existing.UpdatedAt = dto.UpdatedAt ?? DateTime.UtcNow;
                existing.UpdatedBy = "sync";
            }

            if (_documentRegistry is not null)
            {
                var document = await RegisterBusinessDocumentAsync(existing, party?.Id ?? existing.PartyId, ct);
                changed |= existing.BusinessDocumentId != document.Id;
                existing.BusinessDocumentId = document.Id;
            }

            var financePostingOutcome = await PostFinanceIfRequiredAsync(existing, ct);

            if (!changed && financePostingOutcome == FinancePostingOutcome.Unchanged)
            {
                // Commit any party contact/profile created by the shared resolver.
                await _db.SaveChangesAsync(ct);
                return new EntityMapResult(MapResultStatus.Skipped, dto.Id);
            }

            var existingAllocation = await _db.ProfitAllocations.FirstOrDefaultAsync(
                p => p.ReferenceType == "InterviewCustomer" && p.ReferenceId == existing.Id,
                ct);
            if (existingAllocation != null)
            {
                existingAllocation.IncomeAmount = dto.PaidAmount;
                existingAllocation.AllocatedAt = dto.UpdatedAt ?? DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Interview customer updated: {SourceId} → {Name}", dto.Id, dto.FullName);
            return new EntityMapResult(MapResultStatus.Written, dto.Id);
        }

        var customer = new InterviewCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId == Guid.Empty ? null : businessUnitId,
            PartyId = party?.Id,
            SourceSystem = sourceSystem,
            SourceId = dto.Id,
            FullName = dto.FullName,
            Email = dto.Email,
            Phone = dto.PhoneNumber,
            PackageName = dto.PackageName,
            SessionCount = dto.SessionCount,
            PaidAmount = dto.PaidAmount,
            Status = status,
            CreatedAt = dto.CreatedAt ?? DateTime.UtcNow,
            CreatedBy = "sync"
        };

        if (_documentRegistry is not null)
        {
            var document = await RegisterBusinessDocumentAsync(customer, party?.Id, ct);
            customer.BusinessDocumentId = document.Id;
        }

        _db.InterviewCustomers.Add(customer);
        _db.ProfitAllocations.Add(new ProfitAllocation
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId == Guid.Empty ? null : businessUnitId,
            ReferenceType = "InterviewCustomer",
            ReferenceId = customer.Id,
            IncomeAmount = dto.PaidAmount,
            ExpenseAmount = 0,
            AllocatedAt = dto.UpdatedAt ?? dto.CreatedAt ?? DateTime.UtcNow
        });

        await PostFinanceIfRequiredAsync(customer, ct);

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Interview customer created: {SourceId} → {Name}", dto.Id, dto.FullName);
        return new EntityMapResult(MapResultStatus.Written, dto.Id);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task<BusinessDocument> RegisterBusinessDocumentAsync(
        InterviewCustomer customer,
        Guid? partyId,
        CancellationToken ct) =>
        await _documentRegistry!.RegisterAsync(new BusinessDocumentRegistration(
            customer.CompanyId,
            customer.BusinessUnitId,
            BusinessDocumentType.InterviewService,
            nameof(InterviewCustomer),
            customer.Id,
            $"INTERVIEW-{customer.Id:N}",
            customer.PaidAmount,
            customer.UpdatedAt ?? customer.CreatedAt,
            partyId,
            Status: ToDocumentStatus(customer.Status),
            ExternalSourceSystem: customer.SourceSystem,
            ExternalSourceId: customer.SourceId), ct);

    private static BusinessDocumentStatus ToDocumentStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => BusinessDocumentStatus.Settled,
        PaymentStatus.Failed or PaymentStatus.Refunded or PaymentStatus.Cancelled => BusinessDocumentStatus.Voided,
        _ => BusinessDocumentStatus.Open
    };

    private async Task<FinancePostingOutcome> PostFinanceIfRequiredAsync(InterviewCustomer customer, CancellationToken ct)
    {
        if (_financePostingService is null || customer.PaidAmount <= 0)
        {
            return FinancePostingOutcome.Unchanged;
        }

        var transactionType = customer.Status switch
        {
            PaymentStatus.Paid or PaymentStatus.Partial => TransactionType.Income,
            PaymentStatus.Refunded => TransactionType.Expense,
            _ => (TransactionType?)null
        };
        if (!transactionType.HasValue)
        {
            return FinancePostingOutcome.Unchanged;
        }

        var description = transactionType == TransactionType.Income
            ? $"Thu dịch vụ Mock Interview {customer.FullName} ({customer.PackageName})"
            : $"Hoàn tiền dịch vụ Mock Interview {customer.FullName} ({customer.PackageName})";
        var result = await _financePostingService.PostAsync(new FinancePostingRequest(
            customer.CompanyId,
            customer.BusinessUnitId,
            transactionType.Value,
            customer.PaidAmount,
            customer.UpdatedAt ?? customer.CreatedAt,
            nameof(InterviewCustomer),
            customer.Id,
            description,
            customer.BusinessDocumentId), ct);
        return result.Outcome;
    }
}
