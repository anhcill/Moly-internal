using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration.Mappers;

public sealed class PaymentMapper : IEntityMapper
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<PaymentMapper> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public PaymentMapper(
        IApplicationDbContext db,
        ILogger<PaymentMapper> logger,
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

    public string EntityType => "Payments";

    public async Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item, string sourceSystem, Guid companyId, Guid businessUnitId, CancellationToken ct)
    {
        ExternalPaymentDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExternalPaymentDto>(item.GetRawText(), JsonOpts);
        }
        catch (JsonException ex)
        {
            return new EntityMapResult(MapResultStatus.Failed, null, "JSON_PARSE", ex.Message);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            return new EntityMapResult(MapResultStatus.Failed, null, "MISSING_ID", "Payment ID is required.");

        if (dto.Amount <= 0)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", $"Payment amount must be positive, got {dto.Amount}.");

        if (!Enum.TryParse<PaymentStatus>(dto.Status, true, out var paymentStatus))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", $"Invalid payment status: '{dto.Status}'.");

        // Resolve customer FK qua SourceId
        var customer = await _db.EdTechCustomers
            .FirstOrDefaultAsync(c => c.SourceSystem == sourceSystem && c.SourceId == dto.CustomerId && c.CompanyId == companyId, ct);

        if (customer == null)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "FK_NOT_FOUND", $"Customer '{dto.CustomerId}' not found for payment.");

        var party = _partyResolver is null
            ? null
            : await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                companyId,
                businessUnitId == Guid.Empty ? null : businessUnitId,
                PartyType.Individual,
                PartyRole.Customer,
                customer.FullName,
                customer.Email,
                customer.PhoneNumber,
                customer.SourceSystem,
                customer.SourceId), ct);
        if (party is not null)
            customer.PartyId = party.Id;

        // Idempotent by connector key. A repeated event may still complete a
        // missing Party/document/finance link from an earlier partial rollout.
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.SourceSystem == sourceSystem && p.SourcePaymentId == dto.Id && p.CompanyId == companyId, ct);
        var isNew = payment is null;
        var paymentChanged = isNew;
        if (payment is null)
        {
            payment = new Payment
            {
                CompanyId = companyId,
                BusinessUnitId = businessUnitId == Guid.Empty ? null : businessUnitId,
                CustomerId = customer.Id,
                SourceSystem = sourceSystem,
                SourcePaymentId = dto.Id,
                Amount = dto.Amount,
                Currency = dto.Currency,
                Status = paymentStatus,
                PaidAt = dto.PaidAt,
                PaymentMethod = dto.PaymentMethod,
                TransactionReference = dto.TransactionReference,
                CreatedBy = "sync"
            };
            _db.Payments.Add(payment);
        }
        else
        {
            paymentChanged = payment.BusinessUnitId != (businessUnitId == Guid.Empty ? null : businessUnitId)
                             || payment.CustomerId != customer.Id
                             || payment.Amount != dto.Amount
                             || payment.Currency != dto.Currency
                             || payment.Status != paymentStatus
                             || payment.PaidAt != dto.PaidAt
                             || payment.PaymentMethod != dto.PaymentMethod
                             || payment.TransactionReference != dto.TransactionReference;
            payment.BusinessUnitId = businessUnitId == Guid.Empty ? null : businessUnitId;
            payment.CustomerId = customer.Id;
            payment.Amount = dto.Amount;
            payment.Currency = dto.Currency;
            payment.Status = paymentStatus;
            payment.PaidAt = dto.PaidAt;
            payment.PaymentMethod = dto.PaymentMethod;
            payment.TransactionReference = dto.TransactionReference;
            payment.UpdatedBy = "sync";
        }

        if (_documentRegistry is not null)
        {
            var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                companyId,
                businessUnitId == Guid.Empty ? null : businessUnitId,
                BusinessDocumentType.EdTechPayment,
                nameof(Payment),
                payment.Id,
                $"PAY-{payment.Id:N}",
                payment.Amount,
                payment.PaidAt,
                party?.Id ?? customer.PartyId,
                payment.Currency,
                ToDocumentStatus(paymentStatus),
                sourceSystem,
                dto.Id), ct);
            paymentChanged |= payment.BusinessDocumentId != document.Id;
            payment.BusinessDocumentId = document.Id;
        }

        var financePostingOutcome = await PostFinanceIfRequiredAsync(payment, customer, ct);
        await _db.SaveChangesAsync(ct);

        if (isNew)
        {
            _logger.LogInformation("Payment created: {SourceId} → {Amount} {Currency}", dto.Id, dto.Amount, dto.Currency);
            return new EntityMapResult(MapResultStatus.Written, dto.Id);
        }

        return !paymentChanged && financePostingOutcome == FinancePostingOutcome.Unchanged
            ? new EntityMapResult(MapResultStatus.Skipped, dto.Id)
            : new EntityMapResult(MapResultStatus.Written, dto.Id);
    }

    private async Task<FinancePostingOutcome> PostFinanceIfRequiredAsync(
        Payment payment,
        EdTechCustomer customer,
        CancellationToken ct)
    {
        if (_financePostingService is null)
        {
            return FinancePostingOutcome.Unchanged;
        }

        var transactionType = payment.Status switch
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
            ? $"Thu thanh toán học viên {customer.FullName} ({payment.SourcePaymentId})"
            : $"Hoàn tiền học viên {customer.FullName} ({payment.SourcePaymentId})";
        var result = await _financePostingService.PostAsync(new FinancePostingRequest(
            payment.CompanyId,
            payment.BusinessUnitId,
            transactionType.Value,
            payment.Amount,
            payment.PaidAt,
            nameof(Payment),
            payment.Id,
            description,
            payment.BusinessDocumentId), ct);
        return result.Outcome;
    }

    private static BusinessDocumentStatus ToDocumentStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => BusinessDocumentStatus.Settled,
        PaymentStatus.Failed or PaymentStatus.Refunded or PaymentStatus.Cancelled => BusinessDocumentStatus.Voided,
        _ => BusinessDocumentStatus.Open
    };

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
