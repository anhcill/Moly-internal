using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.MasterData;

namespace InternalManagement.Infrastructure.Integration.Mappers;

public sealed partial class CustomerMapper : IEntityMapper
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<CustomerMapper> _logger;
    private readonly IPartyResolver? _partyResolver;

    public CustomerMapper(IApplicationDbContext db, ILogger<CustomerMapper> logger, IPartyResolver? partyResolver = null)
    {
        _db = db;
        _logger = logger;
        _partyResolver = partyResolver;
    }

    public string EntityType => "Customers";

    public async Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item, string sourceSystem, Guid companyId, Guid businessUnitId, CancellationToken ct)
    {
        ExternalCustomerDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExternalCustomerDto>(item.GetRawText(), JsonOpts);
        }
        catch (JsonException ex)
        {
            return new EntityMapResult(MapResultStatus.Failed, null, "JSON_PARSE", ex.Message);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            return new EntityMapResult(MapResultStatus.Failed, null, "MISSING_ID", "Customer ID is required.");

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Customer full name is required.");

        if (string.IsNullOrWhiteSpace(dto.Email) || !EmailRegex().IsMatch(dto.Email))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", $"Invalid email: '{dto.Email}'.");

        // Idempotent: tìm theo (SourceSystem, SourceId)
        var existing = await _db.EdTechCustomers
            .FirstOrDefaultAsync(c => c.SourceSystem == sourceSystem && c.SourceId == dto.Id && c.CompanyId == companyId, ct);

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
            // Update nếu có thay đổi
            if (existing.FullName == dto.FullName && existing.Email == dto.Email && existing.PhoneNumber == dto.PhoneNumber && (party is null || existing.PartyId == party.Id))
            {
                // Resolver may have supplied a missing contact/profile even when
                // the source-customer projection itself did not change.
                await _db.SaveChangesAsync(ct);
                return new EntityMapResult(MapResultStatus.Skipped, dto.Id);
            }

            existing.FullName = dto.FullName;
            existing.Email = dto.Email;
            existing.PhoneNumber = dto.PhoneNumber;
            if (party is not null)
                existing.PartyId = party.Id;
            existing.UpdatedBy = "sync";
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Customer updated: {SourceId} → {Name}", dto.Id, dto.FullName);
            return new EntityMapResult(MapResultStatus.Written, dto.Id);
        }

        // Insert mới
        var customer = new EdTechCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            PartyId = party?.Id,
            SourceSystem = sourceSystem,
            SourceId = dto.Id,
            FullName = dto.FullName,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            CreatedBy = "sync"
        };

        _db.EdTechCustomers.Add(customer);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Customer created: {SourceId} → {Name}", dto.Id, dto.FullName);
        return new EntityMapResult(MapResultStatus.Written, dto.Id);
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
