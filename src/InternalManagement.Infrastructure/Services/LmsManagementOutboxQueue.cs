using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;

namespace InternalManagement.Infrastructure.Services;

internal static class LmsManagementOutboxQueue
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Queue(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        string eventType,
        object payload,
        DateTime now,
        string correlationId)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var sequence = db.IntegrationOutboxes.Local.Count(item => item.CorrelationId == correlationId);
        var command = new LmsManagementEvent(eventId, eventType, now, "internal-management", payload);
        db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = link.CompanyId,
            BusinessUnitId = link.BusinessUnitId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = eventId,
            EventType = LmsOutboxEventTypes.ManagementEventRequested,
            AggregateType = nameof(LmsCourseLink),
            AggregateId = link.Id.ToString("N"),
            IdempotencyKey = $"management-event:{eventId}",
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(command, JsonOptions),
            Status = IntegrationStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now.AddMilliseconds(sequence),
            CreatedBy = currentUser.Username ?? "System"
        });

        if (link.Status != IntegrationStatus.Success)
            link.Status = IntegrationStatus.Processing;
        link.LastSyncError = null;
        link.LastSyncedAt = null;
        link.UpdatedAt = now;
        link.UpdatedBy = currentUser.Username ?? "System";
    }
}
