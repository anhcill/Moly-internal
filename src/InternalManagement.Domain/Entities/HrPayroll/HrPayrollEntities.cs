using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Domain.Entities.HrPayroll;

public class Employee : BaseEntity, IAuditableEntity, ITenantScoped, ISoftDeletable
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public Guid? BranchId { get; set; }
    public Branch? Branch { get; set; }

    public Guid? UserId { get; set; }
    public User? User { get; set; }

    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Position { get; set; }
    public decimal BaseSalary { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FULL_TIME;
    public PartTimeCalculationMethod? PartTimeCalculationMethod { get; set; }
    public decimal? PartTimeUnitRate { get; set; }

    // Hồ sơ nghề nghiệp/CV. Nullable để dữ liệu nhân sự cũ tiếp tục hợp lệ.
    public string? CvUrlOrPath { get; set; }
    public string? ProfessionalSummary { get; set; }
    public string? Skills { get; set; }
    public string? Experience { get; set; }
    public DateTime? JoinedDate { get; set; }
    public string Status { get; set; } = "Active"; // Active, Probation, Resigned
    public DateTime? StatusChangedAt { get; set; }
    public string? StatusReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }

    public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
}

public class AttendanceRecord : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public DateOnly Date { get; set; }
    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public decimal WorkHours { get; set; } = 8;
    public string Status { get; set; } = "Present"; // Present, Late, Absent, Leave
    public string? ImportBatchId { get; set; }
}

public class PayrollPeriod : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public string Name { get; set; } = string.Empty; // e.g. "Kỳ lương Tháng 08/2026"
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;

    public decimal TotalGrossAmount { get; set; }
    public decimal TotalNetAmount { get; set; }
    public DateTime? CalculatedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();
    public ICollection<PayrollAdjustment> Adjustments { get; set; } = new List<PayrollAdjustment>();
    public ICollection<PayrollApproval> Approvals { get; set; } = new List<PayrollApproval>();
}

public class PayrollPolicyVersion : BaseEntity, IAuditableEntity
{
    public Guid CompanyId { get; set; }
    public int VersionNumber { get; set; } = 1;
    public DateOnly EffectiveDate { get; set; }
    public string ConfigJson { get; set; } = "{}"; // JSON storing allowances, tax rate rules
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class PayrollAdjustment : BaseEntity
{
    public Guid PayrollPeriodId { get; set; }
    public PayrollPeriod PayrollPeriod { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public string Type { get; set; } = "Bonus"; // Bonus, Deduction, Overtime, Allowance
    public decimal Amount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
}

public class Payslip : BaseEntity, IAuditableEntity
{
    public Guid PayrollPeriodId { get; set; }
    public PayrollPeriod PayrollPeriod { get; set; } = null!;

    public Guid EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;

    public decimal BaseSalary { get; set; }
    public decimal StandardWorkDays { get; set; } = 22;
    public decimal ActualWorkDays { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.FULL_TIME;
    public PartTimeCalculationMethod? PartTimeCalculationMethod { get; set; }
    public decimal? PartTimeUnitRate { get; set; }
    public decimal ActualWorkHours { get; set; }
    public decimal ActualShifts { get; set; }
    public decimal GrossSalary { get; set; }
    public decimal Allowances { get; set; }
    public decimal KpiBonus { get; set; }
    public decimal HealthInsurance { get; set; }
    public decimal TotalIncome { get; set; }
    public decimal Deductions { get; set; }
    public decimal TotalDeductions { get; set; }
    public decimal NetSalary { get; set; }
    public PayrollStatus Status { get; set; } = PayrollStatus.Draft;
    public DateTime? PublishedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class PayrollApproval : BaseEntity
{
    public Guid PayrollPeriodId { get; set; }
    public PayrollPeriod PayrollPeriod { get; set; } = null!;

    public Guid ApproverId { get; set; }
    public User Approver { get; set; } = null!;

    public PayrollStatus Status { get; set; } = PayrollStatus.Approved;
    public string? Comments { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
}
