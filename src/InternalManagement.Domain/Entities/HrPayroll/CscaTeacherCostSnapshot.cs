using InternalManagement.Domain.Common;

namespace InternalManagement.Domain.Entities.HrPayroll;

/// <summary>
/// Read-only payroll projection imported from CSCA Course. The LMS remains the
/// owner of session duration, hourly rates and settlement adjustments.
/// </summary>
public sealed class CscaTeacherCostSnapshot : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public string SourceSystem { get; set; } = "CSCA_COURSE";
    public string SourceKey { get; set; } = string.Empty;
    public string? TeacherSourceId { get; set; }
    public long TeacherLmsId { get; set; }
    public string TeacherName { get; set; } = string.Empty;
    public string? TeacherUsername { get; set; }
    public string? CourseSourceId { get; set; }
    public string? CourseTitle { get; set; }
    public string ClassSourceId { get; set; } = string.Empty;
    public long ClassLmsId { get; set; }
    public string ClassTitle { get; set; } = string.Empty;
    public string TeachingRole { get; set; } = "lead";
    public string SessionSourceId { get; set; } = string.Empty;
    public long SessionLmsId { get; set; }
    public string? SessionTitle { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public decimal DurationHours { get; set; }
    public decimal EffectiveHourlyRate { get; set; }
    public decimal AccruedAmount { get; set; }
    public decimal? ProjectedAmount { get; set; }
    public decimal BonusAmount { get; set; }
    public decimal DeductionAmount { get; set; }
    public string PaymentStatus { get; set; } = "pending";
    public DateTime? PaidAt { get; set; }
    public DateTime? SourceUpdatedAt { get; set; }
    public DateTime SourceCalculatedAt { get; set; }
    public DateTime SyncedAt { get; set; }
    public bool IsCurrent { get; set; } = true;
    // Local settlement writes use this token without changing LMS-owned cost facts.
    public int LocalSettlementVersion { get; set; }
}
