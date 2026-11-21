using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService
{
    public async Task<Result<RecordingCurriculumReferenceDto>> UpdateRecordingCurriculumReferenceAsync(
        Guid id,
        UpdateRecordingCurriculumReferenceRequest request,
        CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(id, ct))
            return Result<RecordingCurriculumReferenceDto>.Failure("Không tìm thấy lớp Record cần liên kết nội dung.");

        var recordingClass = await _db.CscaClasses
            .FirstOrDefaultAsync(cls => cls.Id == id && !cls.IsDeleted, ct);
        if (recordingClass is null)
            return Result<RecordingCurriculumReferenceDto>.Failure("Không tìm thấy lớp Record cần liên kết nội dung.");
        if (!IsRecordingClass(recordingClass) ||
            !string.Equals(recordingClass.RecordingContentMode, "INDEPENDENT", StringComparison.OrdinalIgnoreCase) ||
            recordingClass.SourceClassId.HasValue)
        {
            return Result<RecordingCurriculumReferenceDto>.Failure(
                "Chỉ khóa video INDEPENDENT, không kế thừa lớp Live, mới được dùng lại bộ nội dung.");
        }
        if (request.ContentSourceClassId == id)
            return Result<RecordingCurriculumReferenceDto>.Failure("Khóa video không thể dùng chính nó làm nguồn nội dung.");

        var sourceClass = await _db.CscaClasses
            .Include(cls => cls.LessonSessions)
            .FirstOrDefaultAsync(cls => cls.Id == request.ContentSourceClassId &&
                cls.CompanyId == recordingClass.CompanyId && !cls.IsDeleted, ct);
        if (sourceClass is null)
            return Result<RecordingCurriculumReferenceDto>.Failure("Không tìm thấy lớp nguồn nội dung trong cùng công ty.");
        if (IsRecordingClass(sourceClass))
            return Result<RecordingCurriculumReferenceDto>.Failure("Nguồn nội dung phải là một lớp Live.");
        if (string.Equals(sourceClass.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return Result<RecordingCurriculumReferenceDto>.Failure("Không thể dùng nội dung của lớp Live đã hủy.");

        var excludedIds = (request.ExcludedSessionSourceIds ?? [])
            .Distinct()
            .OrderBy(sessionId => sessionId)
            .ToArray();
        var sourceSessionIds = sourceClass.LessonSessions.Select(session => session.Id).ToHashSet();
        var invalidExcludedIds = excludedIds.Where(sessionId => !sourceSessionIds.Contains(sessionId)).ToArray();
        if (invalidExcludedIds.Length > 0)
        {
            return Result<RecordingCurriculumReferenceDto>.Failure(
                "Buổi học cần loại phải thuộc đúng lớp Live nguồn; không chấp nhận ID suy đoán hoặc ID của lớp khác.");
        }

        var excludedJson = LmsRecordingCurriculumSync.SerializeExcludedSessionIds(excludedIds);
        var unchanged = recordingClass.CurriculumSourceClassId == sourceClass.Id &&
            string.Equals(recordingClass.CurriculumExcludedSessionIdsJson, excludedJson, StringComparison.Ordinal);
        if (!unchanged)
        {
            var now = DateTime.UtcNow;
            recordingClass.CurriculumSourceClassId = sourceClass.Id;
            recordingClass.CurriculumExcludedSessionIdsJson = excludedJson;
            recordingClass.CurriculumReferenceUpdatedAt = now;
            recordingClass.UpdatedAt = now;
            recordingClass.UpdatedBy = _currentUser.Username ?? "System";

            if (_integrationDb is not null)
            {
                var link = await _integrationDb.LmsCourseLinks.FirstOrDefaultAsync(item =>
                    item.CompanyId == recordingClass.CompanyId &&
                    item.CourseId == recordingClass.CourseId &&
                    item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                    item.Status != IntegrationStatus.Pending, ct);
                if (link is not null)
                {
                    LmsRecordingCurriculumSync.QueueEvent(
                        _integrationDb, _currentUser, link, recordingClass, now, Guid.NewGuid().ToString("N"));
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        return Result<RecordingCurriculumReferenceDto>.Success(ToCurriculumReferenceDto(
            recordingClass, sourceClass, excludedIds));
    }

    private static RecordingCurriculumReferenceDto ToCurriculumReferenceDto(
        CscaClass recordingClass,
        CscaClass sourceClass,
        IReadOnlyList<Guid> excludedIds) => new()
    {
        RecordingClassId = recordingClass.Id,
        ContentSourceClassId = sourceClass.Id,
        ContentSourceClassCode = sourceClass.Code,
        ContentSourceClassName = sourceClass.Name,
        IncludeVideos = false,
        ExcludedSessionSourceIds = excludedIds,
        SourceUpdatedAt = recordingClass.CurriculumReferenceUpdatedAt ?? recordingClass.UpdatedAt ?? recordingClass.CreatedAt
    };
}
