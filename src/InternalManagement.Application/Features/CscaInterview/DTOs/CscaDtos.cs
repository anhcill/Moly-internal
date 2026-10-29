using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.CscaInterview.DTOs;

// ── CSCA Class DTOs ──

public sealed record CscaClassDto
{
    public Guid Id { get; init; }
    public Guid CourseId { get; init; }
    public string CourseTitle { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Batch { get; init; } = string.Empty;
    public string Schedule { get; init; } = string.Empty;
    public decimal TuitionFee { get; init; }
    public bool IsTrial { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string Status { get; init; } = "Active";
    public int StudentCount { get; init; }
    public int StaffCount { get; init; }
    public decimal ExpectedRevenue { get; init; }
    public decimal TotalDiscountAmount { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal TotalStaffExpense { get; init; }
    public decimal ProjectedStaffExpense { get; init; }
    public string StaffExpenseSource { get; init; } = "CSCA_COURSE";
    public DateTime? StaffExpenseLastSyncedAt { get; init; }
    public decimal NetProfit => TotalRevenue - TotalStaffExpense;
    public decimal DebtAmount => Math.Max(0, ExpectedRevenue - TotalRevenue);
    public DateTime CreatedAt { get; init; }
}

public sealed record CscaClassDetailDto
{
    public Guid Id { get; init; }
    public Guid CourseId { get; init; }
    public string CourseTitle { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Batch { get; init; } = string.Empty;
    public string Schedule { get; init; } = string.Empty;
    public decimal TuitionFee { get; init; }
    public bool IsTrial { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
    public string Status { get; init; } = "Active";
    public DateTime CreatedAt { get; init; }
    public List<CscaStudentDto> Students { get; init; } = [];
    public List<CscaStaffDto> Staff { get; init; } = [];
    public List<CscaScheduleDto> Schedules { get; init; } = [];
    public ClassFinancialSummaryDto FinancialSummary { get; init; } = new();
}

public sealed record CreateCscaClassRequest(
    string Code,
    string Name,
    string Batch,
    string Schedule,
    decimal TuitionFee,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string Status = "Active",
    Guid? CourseId = null,
    bool IsTrial = false);

public sealed record UpdateCscaClassRequest(
    string Name,
    string Batch,
    string Schedule,
    decimal TuitionFee,
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string Status = "Active",
    Guid? CourseId = null,
    bool IsTrial = false);

// ── CSCA Schedule DTOs ──

public sealed record CscaScheduleDto
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public Guid? ClassroomId { get; init; }
    public string? ClassroomName { get; init; }
    public int DayOfWeek { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public string? Room { get; init; }
    public string? MeetingUrl { get; init; }
    public string? Notes { get; init; }
}

public sealed record CreateCscaScheduleRequest(
    int DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string? Room = null,
    string? MeetingUrl = null,
    string? Notes = null,
    Guid? ClassroomId = null);

public sealed record UpdateCscaScheduleRequest(
    int DayOfWeek,
    TimeSpan StartTime,
    TimeSpan EndTime,
    string? Room = null,
    string? MeetingUrl = null,
    string? Notes = null,
    Guid? ClassroomId = null);

// ── CSCA Classroom catalog ──

public sealed record CscaClassroomDto
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int? Capacity { get; init; }
    public string? Location { get; init; }
    public bool IsActive { get; init; }
}

public sealed record CreateCscaClassroomRequest(
    string Code,
    string Name,
    int? Capacity = null,
    string? Location = null,
    bool IsActive = true);

public sealed record UpdateCscaClassroomRequest(
    string Name,
    int? Capacity = null,
    string? Location = null,
    bool IsActive = true);

// ── CSCA dated lessons & attendance ──

public sealed record CscaLessonSessionDto
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public Guid? ScheduleId { get; init; }
    public Guid? ClassroomId { get; init; }
    public string? ClassroomName { get; init; }
    public DateOnly LessonDate { get; init; }
    public TimeSpan StartTime { get; init; }
    public TimeSpan EndTime { get; init; }
    public string Status { get; init; } = "Scheduled";
    public string? MeetingUrl { get; init; }
    public string? Notes { get; init; }
}

public sealed record CreateCscaLessonSessionRequest(
    DateOnly LessonDate,
    TimeSpan StartTime,
    TimeSpan EndTime,
    Guid? ClassroomId = null,
    string? MeetingUrl = null,
    string? Notes = null,
    Guid? ScheduleId = null,
    string Status = "Scheduled");

public sealed record UpdateCscaLessonSessionRequest(
    DateOnly LessonDate,
    TimeSpan StartTime,
    TimeSpan EndTime,
    Guid? ClassroomId = null,
    string? MeetingUrl = null,
    string? Notes = null,
    string Status = "Scheduled",
    string? ChangeReason = null);

public sealed record GenerateCscaLessonSessionsRequest(DateOnly FromDate, DateOnly ToDate);

public sealed record CscaLessonAttendanceDto
{
    public Guid? Id { get; init; }
    public Guid LessonSessionId { get; init; }
    public Guid StudentId { get; init; }
    public string StudentCode { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public string Status { get; init; } = "Unmarked";
    public DateTime? CheckInAt { get; init; }
    public string? Notes { get; init; }
}

public sealed record UpsertCscaLessonAttendanceRequest(
    Guid StudentId,
    string Status,
    DateTime? CheckInAt = null,
    string? Notes = null);

// ── CSCA Student DTOs ──

public sealed record CscaStudentDto
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public string StudentCode { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public int? Age { get; init; }
    public string? Hometown { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public decimal DiscountAmount { get; init; }
    public string? DiscountNote { get; init; }
    public decimal PayableAmount { get; init; }
    public decimal DebtAmount => Math.Max(0, PayableAmount - PaidAmount);
    public decimal PaidAmount { get; init; }
    public PaymentStatus PaymentStatus { get; init; } = PaymentStatus.Pending;
    public DateTime? DebtDueDate { get; init; }
    public DateTime JoinedAt { get; init; }
    public string? Notes { get; init; }
}

public sealed record EnrollStudentRequest(
    string StudentName,
    string? Email,
    string? PhoneNumber,
    decimal PaidAmount = 0,
    PaymentStatus PaymentStatus = PaymentStatus.Pending,
    string? Notes = null,
    int? Age = null,
    string? Hometown = null,
    DateTime? DebtDueDate = null,
    decimal DiscountAmount = 0,
    string? DiscountNote = null,
    string? StudentCode = null);

public sealed record UpdateStudentPaymentRequest(
    decimal PaidAmount,
    PaymentStatus PaymentStatus,
    string? Notes = null,
    string? StudentName = null,
    string? Email = null,
    string? PhoneNumber = null,
    int? Age = null,
    string? Hometown = null,
    DateTime? DebtDueDate = null,
    decimal DiscountAmount = 0,
    string? DiscountNote = null,
    string? StudentCode = null);

public sealed record CscaStudentDirectoryDto
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public string StudentCode { get; init; } = string.Empty;
    public string StudentName { get; init; } = string.Empty;
    public int? Age { get; init; }
    public string? Hometown { get; init; }
    public string? Email { get; init; }
    public string? PhoneNumber { get; init; }
    public string CourseTitle { get; init; } = string.Empty;
    public string ClassCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public decimal TuitionFee { get; init; }
    public bool IsTrial { get; init; }
    public decimal DiscountAmount { get; init; }
    public string? DiscountNote { get; init; }
    public decimal PayableAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal DebtAmount { get; init; }
    public DateTime? DebtDueDate { get; init; }
    public PaymentStatus PaymentStatus { get; init; } = PaymentStatus.Pending;
    public DateTime JoinedAt { get; init; }
    public string? Notes { get; init; }
}

// ── CSCA Staff DTOs ──

public sealed record CscaStaffDto
{
    public Guid Id { get; init; }
    public Guid ClassId { get; init; }
    public Guid EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string? EmployeeCode { get; init; }
    public string RoleInClass { get; init; } = "Teacher"; // Teacher, TeachingAssistant, Mentor
    public decimal CompensationRate { get; init; }
    public string? Notes { get; init; }
}

public sealed record AssignStaffRequest(
    Guid EmployeeId,
    string RoleInClass,
    decimal CompensationRate,
    string? Notes = null);

public sealed record UpdateCscaStaffRequest(
    string RoleInClass,
    decimal CompensationRate,
    string? Notes = null);

// ── Class Financial Summary DTO ──

public sealed record ClassFinancialSummaryDto
{
    public Guid ClassId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public int TotalStudents { get; init; }
    public int PaidStudents { get; init; }
    public decimal ExpectedRevenue { get; init; }
    public decimal ActualRevenue { get; init; }
    public decimal TotalStaffExpense { get; init; }
    public decimal ProjectedStaffExpense { get; init; }
    public string StaffExpenseSource { get; init; } = "CSCA_COURSE";
    public DateTime? StaffExpenseLastSyncedAt { get; init; }
    public decimal NetProfit => ActualRevenue - TotalStaffExpense;
    public decimal DebtAmount => Math.Max(0, ExpectedRevenue - ActualRevenue);
    public double ProfitMarginPercent => ActualRevenue > 0 ? (double)(NetProfit / ActualRevenue) * 100 : 0;
}

/// <summary>Class-level cost and break-even analysis backed by LMS snapshots and the finance ledger.</summary>
public sealed record ClassBreakEvenAnalysisDto
{
    public Guid ClassId { get; init; }
    public string ClassCode { get; init; } = string.Empty;
    public string ClassName { get; init; } = string.Empty;
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public int EnrolledStudents { get; init; }
    public int PaidStudents { get; init; }
    public decimal ActualRevenue { get; init; }
    public decimal PotentialRevenue { get; init; }
    public decimal TeacherAccruedCost { get; init; }
    public decimal TeacherProjectedCost { get; init; }
    public decimal LedgerFixedCost { get; init; }
    public decimal LedgerVariableCost { get; init; }
    public decimal LedgerUnclassifiedExpense { get; init; }
    public decimal ActualVariableCost => LedgerVariableCost;
    public decimal ProjectedVariableCost => LedgerVariableCost;
    public decimal ActualFixedCost => TeacherAccruedCost + LedgerFixedCost + LedgerUnclassifiedExpense;
    public decimal ProjectedFixedCost => TeacherProjectedCost + LedgerFixedCost + LedgerUnclassifiedExpense;
    public decimal ActualProfit => ActualRevenue - ActualFixedCost - ActualVariableCost;
    public decimal ProjectedProfit => PotentialRevenue - ProjectedFixedCost - ProjectedVariableCost;
    public decimal AverageRevenuePerStudent { get; init; }
    public decimal VariableCostPerStudent { get; init; }
    public decimal ContributionMarginPerStudent { get; init; }
    public decimal? BreakEvenStudents { get; init; }
    public decimal? BreakEvenRevenue { get; init; }
    public bool BreakEvenReachable { get; init; }
    public string TeacherCostSource { get; init; } = "CSCA_COURSE";
    public DateTime? TeacherCostLastSyncedAt { get; init; }
    public List<string> Warnings { get; init; } = [];
}
