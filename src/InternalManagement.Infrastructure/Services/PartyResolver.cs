using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.MasterData;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

/// <summary>
/// Resolves the stable identity behind source-specific customers, students and
/// suppliers.  A connector identity is authoritative; normalized email/phone
/// are used only as a safe fallback when they point to exactly one party.
/// </summary>
public sealed class PartyResolver(IApplicationDbContext db) : IPartyResolver
{
    public async Task<Party> ResolveAsync(PartyResolutionRequest request, CancellationToken ct)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required to resolve a party.", nameof(request));
        }

        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("DisplayName is required to resolve a party.", nameof(request));
        }

        var sourceSystem = NormalizeSourceValue(request.SourceSystem);
        var sourceId = NormalizeSourceValue(request.SourceId);
        Party? party = null;

        if (sourceSystem is not null && sourceId is not null)
        {
            party = await db.PartyExternalIdentities
                .Include(x => x.Party)
                .Where(x => x.CompanyId == request.CompanyId && x.SourceSystem == sourceSystem && x.SourceId == sourceId)
                .Select(x => x.Party)
                .SingleOrDefaultAsync(ct);
        }

        if (party is null)
        {
            party = await FindUniqueContactMatchAsync(request.CompanyId, request.Email, request.Phone, ct);
        }

        if (party is null)
        {
            party = new Party
            {
                CompanyId = request.CompanyId,
                BusinessUnitId = request.BusinessUnitId,
                Type = request.Type,
                DisplayName = displayName
            };
            db.Parties.Add(party);
        }
        else if (!string.Equals(party.DisplayName, displayName, StringComparison.Ordinal))
        {
            party.DisplayName = displayName;
        }

        await EnsureBusinessProfileAsync(party, request, ct);
        await EnsureContactAsync(party, PartyContactType.Email, request.Email, ct);
        await EnsureContactAsync(party, PartyContactType.Phone, request.Phone, ct);

        if (sourceSystem is not null && sourceId is not null)
        {
            var hasIdentity = party.ExternalIdentities.Any(x =>
                x.CompanyId == request.CompanyId && x.SourceSystem == sourceSystem && x.SourceId == sourceId)
                || await db.PartyExternalIdentities.AnyAsync(x =>
                    x.CompanyId == request.CompanyId && x.SourceSystem == sourceSystem && x.SourceId == sourceId, ct);

            if (!hasIdentity)
            {
                party.ExternalIdentities.Add(new PartyExternalIdentity
                {
                    CompanyId = request.CompanyId,
                    BusinessUnitId = request.BusinessUnitId,
                    SourceSystem = sourceSystem,
                    SourceId = sourceId
                });
            }
        }

        return party;
    }

    private async Task<Party?> FindUniqueContactMatchAsync(Guid companyId, string? email, string? phone, CancellationToken ct)
    {
        var candidateIds = new HashSet<Guid>();
        var normalizedEmail = NormalizeEmail(email);
        var normalizedPhone = NormalizePhone(phone);

        if (normalizedEmail is not null)
        {
            var ids = await db.PartyContacts
                .Where(x => x.Type == PartyContactType.Email && x.NormalizedValue == normalizedEmail && x.Party.CompanyId == companyId)
                .Select(x => x.PartyId)
                .ToListAsync(ct);
            candidateIds.UnionWith(ids);
        }

        if (normalizedPhone is not null)
        {
            var ids = await db.PartyContacts
                .Where(x => x.Type == PartyContactType.Phone && x.NormalizedValue == normalizedPhone && x.Party.CompanyId == companyId)
                .Select(x => x.PartyId)
                .ToListAsync(ct);
            candidateIds.UnionWith(ids);
        }

        return candidateIds.Count == 1
            ? await db.Parties.SingleOrDefaultAsync(x => x.Id == candidateIds.Single(), ct)
            : null;
    }

    private async Task EnsureBusinessProfileAsync(Party party, PartyResolutionRequest request, CancellationToken ct)
    {
        var existsInMemory = party.BusinessProfiles.Any(x => x.Role == request.Role && x.BusinessUnitId == request.BusinessUnitId);
        var existsInStore = party.Id != Guid.Empty && await db.PartyBusinessProfiles.AnyAsync(
            x => x.PartyId == party.Id && x.Role == request.Role && x.BusinessUnitId == request.BusinessUnitId, ct);

        if (!existsInMemory && !existsInStore)
        {
            party.BusinessProfiles.Add(new PartyBusinessProfile
            {
                CompanyId = request.CompanyId,
                BusinessUnitId = request.BusinessUnitId,
                Role = request.Role
            });
        }
    }

    private async Task EnsureContactAsync(Party party, PartyContactType type, string? rawValue, CancellationToken ct)
    {
        var normalizedValue = type == PartyContactType.Email ? NormalizeEmail(rawValue) : NormalizePhone(rawValue);
        if (normalizedValue is null)
        {
            return;
        }

        var existsInMemory = party.Contacts.Any(x => x.Type == type && x.NormalizedValue == normalizedValue);
        var existsInStore = party.Id != Guid.Empty && await db.PartyContacts.AnyAsync(
            x => x.PartyId == party.Id && x.Type == type && x.NormalizedValue == normalizedValue, ct);

        if (!existsInMemory && !existsInStore)
        {
            var hasContactOfSameType = party.Contacts.Any(x => x.Type == type)
                || (party.Id != Guid.Empty && await db.PartyContacts.AnyAsync(
                    x => x.PartyId == party.Id && x.Type == type, ct));
            party.Contacts.Add(new PartyContact
            {
                Type = type,
                Value = rawValue!.Trim(),
                NormalizedValue = normalizedValue,
                IsPrimary = !hasContactOfSameType
            });
        }
    }

    private static string? NormalizeSourceValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrWhiteSpace(digits) ? null : digits;
    }
}

/// <summary>
/// Creates one idempotent document-registry record per operational record.
/// Services can call it before their single SaveChanges operation, preserving
/// transactional consistency without relying on fragile text references.
/// </summary>
public sealed class BusinessDocumentRegistry(IApplicationDbContext db) : IBusinessDocumentRegistry
{
    public async Task<BusinessDocument> RegisterAsync(BusinessDocumentRegistration registration, CancellationToken ct)
    {
        if (registration.CompanyId == Guid.Empty || registration.SourceEntityId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId and SourceEntityId are required to register a document.", nameof(registration));
        }

        if (string.IsNullOrWhiteSpace(registration.SourceEntityType) || string.IsNullOrWhiteSpace(registration.DocumentNumber))
        {
            throw new ArgumentException("SourceEntityType and DocumentNumber are required to register a document.", nameof(registration));
        }

        if (registration.TotalAmount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(registration), "Document total cannot be negative.");
        }

        var existing = await db.BusinessDocuments.SingleOrDefaultAsync(x =>
            x.CompanyId == registration.CompanyId
            && x.DocumentType == registration.DocumentType
            && x.SourceEntityType == registration.SourceEntityType
            && x.SourceEntityId == registration.SourceEntityId, ct);

        if (existing is not null)
        {
            existing.BusinessUnitId = registration.BusinessUnitId;
            existing.PartyId = registration.PartyId;
            existing.DocumentNumber = registration.DocumentNumber.Trim();
            existing.TotalAmount = registration.TotalAmount;
            existing.Currency = registration.Currency.Trim().ToUpperInvariant();
            existing.IssuedAt = NormalizeUtc(registration.IssuedAt);
            existing.Status = registration.Status;
            existing.ExternalSourceSystem = NormalizeOptional(registration.ExternalSourceSystem);
            existing.ExternalSourceId = NormalizeOptional(registration.ExternalSourceId);
            return existing;
        }

        var document = new BusinessDocument
        {
            CompanyId = registration.CompanyId,
            BusinessUnitId = registration.BusinessUnitId,
            PartyId = registration.PartyId,
            DocumentType = registration.DocumentType,
            Status = registration.Status,
            DocumentNumber = registration.DocumentNumber.Trim(),
            SourceEntityType = registration.SourceEntityType.Trim(),
            SourceEntityId = registration.SourceEntityId,
            ExternalSourceSystem = NormalizeOptional(registration.ExternalSourceSystem),
            ExternalSourceId = NormalizeOptional(registration.ExternalSourceId),
            TotalAmount = registration.TotalAmount,
            Currency = registration.Currency.Trim().ToUpperInvariant(),
            IssuedAt = NormalizeUtc(registration.IssuedAt)
        };
        db.BusinessDocuments.Add(document);
        return document;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime NormalizeUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
