using InternalManagement.Domain.Common;
using InternalManagement.Domain.Enums;
using InternalManagement.Domain.Entities.Documents;

namespace InternalManagement.Domain.Entities.Finance;

public class FinanceCategory : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TransactionType Type { get; set; } = TransactionType.Expense;
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class FinanceTransaction : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid? BusinessDocumentId { get; set; }
    public BusinessDocument? BusinessDocument { get; set; }

    public Guid? CategoryId { get; set; }
    public FinanceCategory? Category { get; set; }

    public TransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; } = DateTime.UtcNow;

    public string? ReferenceType { get; set; } // Payment, SalesOrder, Payroll, Manual
    public Guid? ReferenceId { get; set; }
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class CashAccount : BaseEntity, IAuditableEntity
{
    public Guid CompanyId { get; set; }

    public string Code { get; set; } = string.Empty; // CASH_VND, VCB_MAIN, MB_MOLI
    public string Name { get; set; } = string.Empty;
    public string? AccountNumber { get; set; }
    public string? BankName { get; set; }
    public decimal CurrentBalance { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class MonthlyClosing : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }

    public int Year { get; set; }
    public int Month { get; set; }

    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal NetProfit => TotalIncome - TotalExpense;

    public bool IsLocked { get; set; } = false;
    public string? ClosedBy { get; set; }
    public DateTime? ClosedAt { get; set; }
}

/// <summary>Future cash movement used by the finance workspace forecast.</summary>
public class PlannedCashMovement : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public DateTime PlannedDate { get; set; }
    public TransactionType TransactionType { get; set; }
    public decimal Amount { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string Recurrence { get; set; } = "None";
    public string Status { get; set; } = "Planned"; // Planned, Settled, Cancelled
    public string? IdempotencyKey { get; set; }
    public Guid? SettledTransactionId { get; set; }
    public DateTime? SettledAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>One advance issued to a teacher; returns are separate immutable rows.</summary>
public class TeacherAdvance : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public decimal ReturnedAmount { get; set; }
    public decimal AppliedAmount { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public string Status { get; set; } = "Open"; // Open, Settled, Cancelled
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class TeacherAdvanceReturn : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid TeacherAdvanceId { get; set; }
    public decimal Amount { get; set; }
    public DateTime ReturnedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public Guid? FinanceTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class TeacherPayment : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid EmployeeId { get; set; }
    public decimal Amount { get; set; }
    public decimal AdvanceAppliedAmount { get; set; }
    public DateTime PaidAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public Guid? FinanceTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Enrollment balance with immutable receipt/refund history.</summary>
public class StudentReceivable : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid StudentId { get; set; }
    public Guid ClassId { get; set; }
    public decimal DueAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTime? DueDate { get; set; }
    public string Status { get; set; } = "Open"; // Open, Paid, Refunded, Cancelled
    public string? IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Allocation keeps local settlements and later LMS paid flags from counting twice.</summary>
public class TeacherPaymentAllocation : BaseEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid TeacherPaymentId { get; set; }
    public Guid TeacherCostSnapshotId { get; set; }
    public decimal Amount { get; set; }
}

public class StudentReceipt : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid StudentReceivableId { get; set; }
    public decimal Amount { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public Guid? FinanceTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class StudentRefund : BaseEntity, IAuditableEntity, ITenantScoped
{
    public Guid CompanyId { get; set; }
    public Guid? BusinessUnitId { get; set; }
    public Guid StudentReceivableId { get; set; }
    public decimal Amount { get; set; }
    public DateTime RefundedAt { get; set; } = DateTime.UtcNow;
    public string Description { get; set; } = string.Empty;
    public string? IdempotencyKey { get; set; }
    public Guid? FinanceTransactionId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
