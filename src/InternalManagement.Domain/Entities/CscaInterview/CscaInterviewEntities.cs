using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Domain.Entities.CscaInterview;

public class CscaClass : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid CourseId { get; set; }
    public Course Course { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string Schedule { get; set; } = string.Empty;
    public decimal TuitionFee { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "Active"; // Active, Completed, Cancelled

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<CscaClassStudent> Students { get; set; } = new List<CscaClassStudent>();
    public ICollection<CscaClassStaff> Staff { get; set; } = new List<CscaClassStaff>();
    public ICollection<CscaClassSchedule> Schedules { get; set; } = new List<CscaClassSchedule>();
    public ICollection<CscaLessonSession> LessonSessions { get; set; } = new List<CscaLessonSession>();
}

/// <summary>Danh mục phòng học dùng chung trong phạm vi công ty/đơn vị.</summary>
public class CscaClassroom : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? Capacity { get; set; }
    public string? Location { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<CscaClassSchedule> Schedules { get; set; } = new List<CscaClassSchedule>();
    public ICollection<CscaLessonSession> LessonSessions { get; set; } = new List<CscaLessonSession>();
}

/// <summary>Normalized recurring/specific lesson slot for a CSCA class.</summary>
public class CscaClassSchedule : BaseEntity, IAuditableEntity
{
    public Guid ClassId { get; set; }
    public CscaClass Class { get; set; } = null!;
    public Guid? ClassroomId { get; set; }
    public CscaClassroom? Classroom { get; set; }

    /// <summary>0 = Sunday through 6 = Saturday, matching System.DayOfWeek.</summary>
    public int DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string? Room { get; set; }
    public string? MeetingUrl { get; set; }
    public string? Notes { get; set; }

    // A schedule imported from the LMS is a read-model projection. It must
    // never be edited back into the LMS by Management CRUD screens.
    public string? ExternalSource { get; set; }
    public string? ExternalScheduleId { get; set; }
    public string? Title { get; set; }
    public string Timezone { get; set; } = "Asia/Ho_Chi_Minh";
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = "Active"; // Active, Archived
    public int ExternalVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<CscaLessonSession> LessonSessions { get; set; } = new List<CscaLessonSession>();
}

/// <summary>Một buổi học theo ngày cụ thể; có thể được tạo từ lịch lặp hằng tuần hoặc tạo thủ công.</summary>
public class CscaLessonSession : BaseEntity, IAuditableEntity
{
    public Guid ClassId { get; set; }
    public CscaClass Class { get; set; } = null!;
    public Guid? ScheduleId { get; set; }
    public CscaClassSchedule? Schedule { get; set; }
    public Guid? ClassroomId { get; set; }
    public CscaClassroom? Classroom { get; set; }
    public DateOnly LessonDate { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public string Status { get; set; } = "Scheduled"; // Scheduled, Rescheduled, Cancelled, Completed
    public string? MeetingUrl { get; set; }
    public string? Notes { get; set; }

    // Immutable source reference when a session is created or linked by an
    // external teaching system. This keeps repeated attendance updates tied to
    // one Management lesson even if its time is later edited in the LMS.
    public string? ExternalSource { get; set; }
    public string? ExternalSessionId { get; set; }
    public int ExternalVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<CscaLessonAttendance> Attendances { get; set; } = new List<CscaLessonAttendance>();
}

/// <summary>Điểm danh của một học viên trong một buổi học CSCA cụ thể.</summary>
public class CscaLessonAttendance : BaseEntity, IAuditableEntity
{
    public Guid LessonSessionId { get; set; }
    public CscaLessonSession LessonSession { get; set; } = null!;
    public Guid StudentId { get; set; }
    public CscaClassStudent Student { get; set; } = null!;
    public string Status { get; set; } = "Present"; // Present, Late, Absent, Excused
    public DateTime? CheckInAt { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class CscaClassStudent : BaseEntity, IAuditableEntity
{
    public Guid ClassId { get; set; }
    public CscaClass Class { get; set; } = null!;
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public string StudentName { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string? Hometown { get; set; }
    public string? Email { get; set; }
    public string? PhoneNumber { get; set; }
    public decimal PaidAmount { get; set; }
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;
    public DateTime? DebtDueDate { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<CscaLessonAttendance> LessonAttendances { get; set; } = new List<CscaLessonAttendance>();
}

public class CscaClassStaff : BaseEntity, IAuditableEntity
{
    public Guid ClassId { get; set; }
    public CscaClass Class { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public string RoleInClass { get; set; } = "Teacher"; // Teacher, TeachingAssistant, Mentor
    public decimal CompensationRate { get; set; }
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class InterviewCustomer : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? PartyId { get; set; }
    public Party? Party { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public string SourceSystem { get; set; } = "WEBSITE_INTERVIEW";
    public string SourceId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PackageName { get; set; } = string.Empty;
    public int SessionCount { get; set; } = 1;
    public decimal PaidAmount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Paid;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class ProfitAllocation : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string ReferenceType { get; set; } = string.Empty; // CscaClass, InterviewPackage, Course, Order
    public Guid ReferenceId { get; set; }
    public decimal IncomeAmount { get; set; }
    public decimal ExpenseAmount { get; set; }
    public decimal NetAmount => IncomeAmount - ExpenseAmount;
    public DateTime AllocatedAt { get; set; } = DateTime.UtcNow;
}
