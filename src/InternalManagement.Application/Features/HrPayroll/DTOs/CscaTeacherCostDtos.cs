namespace InternalManagement.Application.Features.HrPayroll.DTOs;

public sealed record SyncCscaTeacherCostsRequest(
    DateTime? From = null,
    DateTime? To = null,
    string? CourseSourceId = null,
    string? ClassSourceId = null,
    string? TeacherSourceId = null,
    bool IncludeProjected = true);

public sealed record CscaTeacherCostSnapshotDto(
    Guid Id,
    Guid? EmployeeId,
    string? TeacherSourceId,
    long TeacherLmsId,
    string TeacherName,
    string? CourseSourceId,
    string? CourseTitle,
    string ClassSourceId,
    string ClassTitle,
    string SessionSourceId,
    string? SessionTitle,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    bool IsCompleted,
    decimal DurationHours,
    decimal EffectiveHourlyRate,
    decimal AccruedAmount,
    decimal? ProjectedAmount,
    string PaymentStatus,
    DateTime? PaidAt,
    DateTime? SourceUpdatedAt,
    DateTime SyncedAt);

public sealed record CscaTeacherCostSummaryDto(
    int ItemCount,
    int TeacherCount,
    decimal AccruedAmount,
    decimal ProjectedAmount,
    decimal BonusAmount,
    decimal DeductionAmount,
    decimal NetAccruedAmount,
    DateTime? LastSyncedAt,
    IReadOnlyList<CscaTeacherCostSnapshotDto> Items,
    int ClassCount = 0,
    decimal ClassCostAmount = 0,
    decimal ProjectedClassCostAmount = 0,
    string SettlementAdjustmentScope = "teacher",
    string NetAccruedAmountScope = "teacher",
    IReadOnlyList<CscaTeacherCostClassSummaryDto>? ClassSummaries = null);

public sealed record CscaTeacherCostClassSummaryDto(
    string ClassSourceId,
    string ClassTitle,
    string? CourseSourceId,
    string? CourseTitle,
    string? TeacherSourceId,
    long TeacherLmsId,
    string TeacherName,
    int SessionCount,
    int CompletedSessionCount,
    decimal TotalHours,
    decimal CompletedHours,
    decimal AccruedAmount,
    decimal ProjectedAmount,
    bool SettlementAdjustmentApplied = false);

public sealed record CscaTeacherCostSyncResultDto(
    int Received,
    int Inserted,
    int Updated,
    DateTime CalculatedAt,
    DateTime SyncedAt);
