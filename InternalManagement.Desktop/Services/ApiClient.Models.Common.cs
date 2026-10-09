using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── Records & Data Models ──

    public sealed record LoginRequest(string Username, string Password);
    public sealed record ApiEnvelope<T>(bool Success, T? Data, string? Message);
    public sealed record PaginatedData<T>(IReadOnlyList<T> Items, int TotalCount, int PageIndex, int PageSize);

    public sealed record LoginResponse(
        string AccessToken,
        string RefreshToken,
        DateTime ExpiresAt,
        UserInfo User);

    public sealed record UserInfo(
        Guid Id,
        string Username,
        string Email,
        string FullName,
        IReadOnlyList<string> Roles,
        IReadOnlyList<string> Permissions,
        Guid? DefaultBusinessUnitId = null,
        string? DefaultBusinessUnitCode = null,
        IReadOnlyList<BusinessUnitItem>? AccessibleBusinessUnits = null);

    public sealed record BusinessUnitItem(Guid Id, string Code, string Name);

    public sealed record CourseItem(
        Guid Id,
        string CourseSourceId,
        string Title,
        string? Slug,
        string? Description,
        decimal Price,
        string Status,
        int Version,
        int ModuleCount,
        DateTime CreatedAt,
        int ClassCount = 0);

    public sealed record QuestionItem(
        Guid Id,
        string BankName,
        string? SubjectName,
        string? TopicName,
        string DifficultyLevel,
        Guid? CurrentVersionId,
        int CurrentVersionNumber,
        string Status,
        string ContentHtml,
        string? ExplanationHtml,
        IReadOnlyList<QuestionChoiceItem> Choices,
        IReadOnlyList<string> Tags);

    public sealed record QuestionChoiceItem(
        Guid Id,
        string Label,
        string ContentHtml,
        bool IsCorrect,
        int OrderIndex);

    public sealed record CustomerItem(
        Guid Id,
        string SourceId,
        string FullName,
        string Email,
        string? PhoneNumber,
        int SubscriptionCount,
        decimal TotalPaidAmount,
        DateTime CreatedAt);

    public sealed record SyncTriggerResult(
        Guid RunId,
        string SourceSystem,
        string EntityType,
        string Status,
        int RecordsRead,
        int RecordsWritten,
        int RecordsSkipped,
        int RecordsFailed,
        string? ErrorMessage);

    public sealed record SyncRunItem(
        Guid Id,
        string SourceSystem,
        string EntityType,
        DateTime StartedAt,
        DateTime? CompletedAt,
        string Status,
        int RecordsRead,
        int RecordsWritten,
        int RecordsSkipped,
        int RecordsFailed,
        string? ErrorMessage)
    {
        public string StatusLabel => Status switch
        {
            "Success" => "Thành công",
            "Failed" => "Thất bại",
            "Running" => "Đang chạy",
            "Partial" => "Một phần",
            _ => Status
        };
    }

    public sealed record DeadLetterItem(
        Guid Id,
        string SourceSystem,
        string EntityType,
        string SourceId,
        string? ErrorCode,
        string ErrorMessage,
        int RetryCount,
        bool Resolved,
        DateTime CreatedAt,
        DateTime? ResolvedAt,
        string? ResolvedBy)
    {
        public string ResolvedLabel => Resolved ? "Đã xử lý" : "Chờ xử lý";
    }

    public sealed record LmsIntegrationOverviewItem(
        int TotalCourses,
        int MappedCourses,
        int ReadyCourseMappings,
        int ActiveAccounts,
        int PendingAccounts,
        int ActiveGrants,
        int PendingOutbox,
        int FailedOutbox,
        int DeadLetterOutbox,
        DateTime? LastSuccessfulDispatchAt);

    public sealed record LmsCourseMappingItem(
        Guid CourseId,
        string CourseSourceId,
        string CourseTitle,
        string? CourseSlug,
        Guid? MappingId,
        string? ExternalCourseId,
        long? LmsCourseId,
        string? LmsCourseSlug,
        string? Status,
        DateTime? LastSyncedAt,
        string? LastSyncError,
        Guid? BusinessUnitId,
        string ClassSummary = "",
        int ClassCount = 0)
    {
        public string StatusLabel => Status switch
        {
            "Success" => "Sẵn sàng cấp quyền",
            "Pending" => "Chưa bật đồng bộ",
            "Processing" => "Đang gửi sang Web",
            "Failed" => "Lỗi đồng bộ",
            "DeadLetter" => "Cần rà soát",
            _ => "Chưa liên kết Web"
        };

        public string DisplaySlug => !string.IsNullOrWhiteSpace(LmsCourseSlug)
            ? LmsCourseSlug
            : (!string.IsNullOrWhiteSpace(CourseSlug) ? CourseSlug : "—");

        public string DisplayLmsId => LmsCourseId.HasValue ? $"#{LmsCourseId.Value}" : "—";
    }

    public sealed record LmsCourseMappingRequest(
        string? ExternalCourseId,
        long? LmsCourseId,
        string? LmsCourseSlug,
        bool EnableAccess,
        Guid? BusinessUnitId = null);

    public sealed record LmsOutboxItem(
        Guid Id,
        string EventType,
        string AggregateType,
        string AggregateId,
        string Status,
        int AttemptCount,
        DateTime CreatedAt,
        DateTime? NextAttemptAt,
        DateTime? PublishedAt,
        string? CorrelationId,
        string? LastError)
    {
        public string EventDisplayName => EventType switch
        {
            "lms.student.provision.requested" or "student.provisioned" => "Cấp tài khoản học viên",
            "course.upserted" => "Đồng bộ khóa học",
            "class.upserted" => "Đồng bộ lớp học",
            "class.schedule.upserted" => "Cập nhật lịch cố định",
            "class.schedule.archived" => "Ngừng lịch cố định",
            "class.session.upserted" => "Cập nhật buổi học",
            "class.session.cancelled" => "Hủy buổi học",
            "class.membership.changed" => "Ghi danh học viên vào lớp",
            "class.teacher.assigned" => "Phân công giảng viên",
            "teacher.upserted" => "Đồng bộ giảng viên",
            "entitlement.changed" => "Cập nhật quyền học",
            "payment.refunded" => "Hoàn tiền / Khóa quyền",
            _ => EventType
        };

        public string StatusLabel => Status switch
        {
            "Pending" => "Chờ gửi",
            "Processing" => "Đang gửi...",
            "Success" => "Thành công",
            "Failed" => "Thử lại sau",
            "DeadLetter" => "Cần rà soát",
            "Skipped" => "Đã được thay thế",
            _ => Status
        };

        public string DisplayAttempt => $"{AttemptCount} lần";

        public string ResultSummary => !string.IsNullOrWhiteSpace(LastError)
            ? LastError
            : (string.Equals(Status, "Success", StringComparison.OrdinalIgnoreCase)
                ? "Đã đồng bộ sang Web Course"
                : (string.Equals(Status, "Pending", StringComparison.OrdinalIgnoreCase)
                    ? NextAttemptAt is { } nextAttempt && nextAttempt > DateTime.UtcNow
                        ? $"Tự gửi lại lúc {nextAttempt.ToLocalTime():HH:mm}"
                        : "Đã xếp hàng, hệ thống sẽ tự gửi"
                    : "Đang xử lý"));
    }

    public sealed record LmsOutboxDispatchResultItem(int Processed, int Succeeded, int Retrying, int DeadLettered);

    public sealed record HealthResult(bool IsHealthy, string RawBody);

}
