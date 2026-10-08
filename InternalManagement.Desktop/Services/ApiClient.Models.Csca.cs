using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    // ── CSCA & Interview Client Models ──

    public sealed record CscaOnlineMaterialItem(
        int Id,
        string Title,
        string? Description,
        string? Subject,
        string? Topic,
        string? FileType,
        string? FileUrl,
        string? ThumbnailUrl,
        int ViewCount,
        int DownloadCount,
        bool IsPremium,
        string? VipTier,
        DateTime? UpdatedAt,
        int ClassCount = 0);

    public sealed record CscaOnlineVocabularyItem(
        int Id,
        string WordChinese,
        string? Pinyin,
        string? WordVietnamese,
        string? WordEnglish,
        string? Subject,
        string? Topic,
        string? ExampleChinese,
        string? ExampleVietnamese,
        bool IsPremium,
        string? VipTier);

    public sealed record CscaOnlinePostItem(
        int Id,
        string Content,
        string? ImageUrl,
        string? PostType,
        bool IsOfficial,
        string? ModerationStatus,
        string? AuthorName,
        string? AuthorRole,
        int LikeCount,
        int CommentCount,
        DateTime? CreatedAt);

    public sealed record CscaClassItem(
        Guid Id,
        string Code,
        string Name,
        string Batch,
        string Schedule,
        decimal TuitionFee,
        DateTime? StartDate,
        DateTime? EndDate,
        string Status,
        int StudentCount,
        int StaffCount,
        decimal TotalRevenue,
        decimal TotalStaffExpense,
        decimal NetProfit,
        DateTime CreatedAt,
        Guid CourseId,
        string CourseTitle,
        decimal ExpectedRevenue = 0,
        decimal TotalDiscountAmount = 0)
    {
        public decimal DebtAmount => Math.Max(0, ExpectedRevenue - TotalRevenue);
    }

    public sealed record CscaClassDetailItem(
        Guid Id,
        string Code,
        string Name,
        string Batch,
        string Schedule,
        decimal TuitionFee,
        DateTime? StartDate,
        DateTime? EndDate,
        string Status,
        DateTime CreatedAt,
        IReadOnlyList<CscaStudentItem> Students,
        IReadOnlyList<CscaStaffItem> Staff,
        IReadOnlyList<CscaScheduleItem> Schedules,
        ClassFinancialItem FinancialSummary,
        Guid CourseId,
        string CourseTitle);

    public sealed record CscaStudentItem(
        Guid Id,
        Guid ClassId,
        string StudentName,
        int? Age,
        string? Hometown,
        string? Email,
        string? PhoneNumber,
        decimal PaidAmount,
        int PaymentStatus,
        DateTime JoinedAt,
        string? Notes,
        DateTime? DebtDueDate,
        decimal DiscountAmount = 0,
        string? DiscountNote = null,
        decimal PayableAmount = 0);

    public sealed record CscaStudentDirectoryItem(
        Guid Id,
        Guid ClassId,
        string StudentName,
        string? Email,
        string? PhoneNumber,
        string CourseTitle,
        string ClassCode,
        string ClassName,
        decimal TuitionFee,
        decimal PaidAmount,
        decimal DebtAmount,
        DateTime? DebtDueDate,
        int PaymentStatus,
        DateTime JoinedAt,
        string? Notes,
        decimal DiscountAmount = 0,
        string? DiscountNote = null,
        decimal PayableAmount = 0,
        int? Age = null,
        string? Hometown = null);

    public sealed record CscaStaffItem(
        Guid Id,
        Guid ClassId,
        Guid EmployeeId,
        string EmployeeName,
        string? EmployeeCode,
        string RoleInClass,
        decimal CompensationRate,
        string? Notes);

    public sealed record CscaScheduleItem(
        Guid Id,
        Guid ClassId,
        int DayOfWeek,
        TimeSpan StartTime,
        TimeSpan EndTime,
        string? Room,
        string? MeetingUrl,
        string? Notes,
        Guid? ClassroomId,
        string? ClassroomName);

    public sealed record CscaClassroomItem(
        Guid Id,
        string Code,
        string Name,
        int? Capacity,
        string? Location,
        bool IsActive);

    public sealed record CscaLessonSessionItem(
        Guid Id,
        Guid ClassId,
        Guid? ScheduleId,
        Guid? ClassroomId,
        string? ClassroomName,
        DateOnly LessonDate,
        TimeSpan StartTime,
        TimeSpan EndTime,
        string Status,
        string? MeetingUrl,
        string? Notes);

    public sealed record CscaLessonAttendanceItem(
        Guid? Id,
        Guid LessonSessionId,
        Guid StudentId,
        string StudentName,
        string Status,
        DateTime? CheckInAt,
        string? Notes);

    public sealed record CscaClassAttendanceReportItem(
        Guid ClassId,
        int TotalStudents,
        int TotalSessions,
        int TotalRecords,
        int PresentCount,
        int LateCount,
        int AbsentCount,
        int ExcusedCount,
        decimal AttendanceRatePercent,
        DateTime? LastSyncedAt,
        IReadOnlyList<CscaAttendanceSessionSummaryItem> Sessions,
        IReadOnlyList<CscaStudentAttendanceSummaryItem> Students);

    public sealed record CscaAttendanceSessionSummaryItem(
        Guid SessionId,
        DateOnly LessonDate,
        TimeSpan StartTime,
        TimeSpan EndTime,
        int TotalRecords,
        int PresentCount,
        int LateCount,
        int AbsentCount,
        int ExcusedCount,
        DateTime? LastSyncedAt);

    public sealed record CscaStudentAttendanceSummaryItem(
        Guid StudentId,
        string StudentName,
        int TotalRecords,
        int PresentCount,
        int LateCount,
        int AbsentCount,
        int ExcusedCount,
        decimal AttendanceRatePercent);

    public sealed record ClassFinancialItem(
        Guid ClassId,
        string ClassCode,
        string ClassName,
        int TotalStudents,
        int PaidStudents,
        decimal ExpectedRevenue,
        decimal ActualRevenue,
        decimal TotalStaffExpense,
        decimal NetProfit,
        double ProfitMarginPercent)
    {
        public decimal DebtAmount => Math.Max(0, ExpectedRevenue - ActualRevenue);
    }

    public sealed record InterviewCustomerItem(
        Guid Id,
        string SourceSystem,
        string SourceId,
        string FullName,
        string Email,
        string? Phone,
        string PackageName,
        int SessionCount,
        decimal PaidAmount,
        int Status,
        DateTime CreatedAt);

    public sealed record InterviewFinancialSummaryItem(
        int TotalCustomers,
        int TotalSessions,
        decimal TotalRevenue,
        decimal PendingRevenue);

}
