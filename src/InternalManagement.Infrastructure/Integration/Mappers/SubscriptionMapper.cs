using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration.Mappers;

public sealed class SubscriptionMapper : IEntityMapper
{
    private readonly IApplicationDbContext _db;
    private readonly ILogger<SubscriptionMapper> _logger;

    public SubscriptionMapper(IApplicationDbContext db, ILogger<SubscriptionMapper> logger)
    {
        _db = db;
        _logger = logger;
    }

    public string EntityType => "Subscriptions";

    public async Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item, string sourceSystem, Guid companyId, Guid businessUnitId, CancellationToken ct)
    {
        ExternalSubscriptionDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<ExternalSubscriptionDto>(item.GetRawText(), JsonOpts);
        }
        catch (JsonException ex)
        {
            return new EntityMapResult(MapResultStatus.Failed, null, "JSON_PARSE", ex.Message);
        }

        if (dto == null || string.IsNullOrWhiteSpace(dto.Id))
            return new EntityMapResult(MapResultStatus.Failed, null, "MISSING_ID", "Subscription ID is required.");

        if (string.IsNullOrWhiteSpace(dto.PackageName))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "Package name is required.");

        if (dto.ExpiresAt <= dto.StartsAt)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", "ExpiresAt must be after StartsAt.");

        if (!Enum.TryParse<SubscriptionStatus>(dto.Status, true, out var subStatus))
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "VALIDATION", $"Invalid subscription status: '{dto.Status}'.");

        // Resolve customer FK
        var customer = await _db.EdTechCustomers
            .FirstOrDefaultAsync(c => c.SourceSystem == sourceSystem && c.SourceId == dto.CustomerId && c.CompanyId == companyId, ct);

        if (customer == null)
            return new EntityMapResult(MapResultStatus.Failed, dto.Id, "FK_NOT_FOUND", $"Customer '{dto.CustomerId}' not found for subscription.");

        // Idempotent: check nếu subscription giống hệt đã tồn tại
        var existing = await _db.Subscriptions
            .FirstOrDefaultAsync(s => s.CustomerId == customer.Id
                && s.PackageName == dto.PackageName
                && s.StartsAt == dto.StartsAt
                && s.CompanyId == companyId, ct);

        if (existing != null)
        {
            if (existing.Status == subStatus && existing.ExpiresAt == dto.ExpiresAt)
                return new EntityMapResult(MapResultStatus.Skipped, dto.Id);

            existing.Status = subStatus;
            existing.ExpiresAt = dto.ExpiresAt;
            await _db.SaveChangesAsync(ct);
            return new EntityMapResult(MapResultStatus.Written, dto.Id);
        }

        var subscription = new Subscription
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            CustomerId = customer.Id,
            PackageName = dto.PackageName,
            StartsAt = dto.StartsAt,
            ExpiresAt = dto.ExpiresAt,
            Status = subStatus
        };

        _db.Subscriptions.Add(subscription);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Subscription created: {SourceId} → {Package}", dto.Id, dto.PackageName);
        return new EntityMapResult(MapResultStatus.Written, dto.Id);
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
