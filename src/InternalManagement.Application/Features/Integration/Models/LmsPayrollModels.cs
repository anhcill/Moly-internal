namespace InternalManagement.Application.Features.Integration.Models;

public sealed record LmsTeacherCostQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? CourseSourceId = null,
    string? ClassSourceId = null,
    string? TeacherSourceId = null,
    bool IncludeProjected = true);

public sealed record LmsTeacherCostItem(
    string? TeacherSourceId,
    long TeacherLmsId,
    string TeacherName,
    string? TeacherUsername,
    string? CourseSourceId,
    string? CourseTitle,
    string ClassSourceId,
    long ClassLmsId,
    string ClassTitle,
    string TeachingRole,
    string SessionSourceId,
    long SessionLmsId,
    string? SessionTitle,
    DateTime StartTime,
    DateTime EndTime,
    string Status,
    bool IsCompleted,
    decimal DurationHours,
    decimal EffectiveHourlyRate,
    decimal AccruedAmount,
    decimal? ProjectedAmount,
    decimal BonusAmount,
    decimal DeductionAmount,
    string PaymentStatus,
    DateTime? PaidAt,
    DateTime? SourceUpdatedAt);

public sealed record LmsTeacherCostReport(
    string SourceSystem,
    DateTime CalculatedAt,
    bool IncludeProjected,
    IReadOnlyList<LmsTeacherCostItem> Items);
