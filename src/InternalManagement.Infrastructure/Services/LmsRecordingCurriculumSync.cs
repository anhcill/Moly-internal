using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Integration;

namespace InternalManagement.Infrastructure.Services;

/// <summary>
/// Emits a curriculum-only reference for an independent recording class.
/// The LMS resolves the referenced live-class units in place; Internal never
/// copies lessons and explicitly prevents reuse of source videos.
/// </summary>
internal static class LmsRecordingCurriculumSync
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void QueueEvent(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        CscaClass recordingClass,
        DateTime sourceUpdatedAt,
        string correlationId)
    {
        if (!recordingClass.CurriculumSourceClassId.HasValue) return;

        LmsManagementOutboxQueue.Queue(db, currentUser, link, "class.curriculum.linked", new
        {
            ClassSourceId = recordingClass.Id.ToString("N"),
            ContentSourceClassSourceId = recordingClass.CurriculumSourceClassId.Value.ToString("N"),
            IncludeVideos = false,
            ExcludedSessionSourceIds = ParseExcludedSessionIds(recordingClass.CurriculumExcludedSessionIdsJson)
                .Select(id => id.ToString("N"))
                .ToArray(),
            SourceUpdatedAt = sourceUpdatedAt
        }, sourceUpdatedAt, correlationId);
    }

    public static Guid[] ParseExcludedSessionIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return (JsonSerializer.Deserialize<Guid[]>(json, JsonOptions) ?? [])
                .Distinct()
                .OrderBy(id => id)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializeExcludedSessionIds(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.Distinct().OrderBy(id => id).ToArray(), JsonOptions);
}
