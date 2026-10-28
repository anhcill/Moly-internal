using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Finance.DTOs;

public sealed record PlannedCashMovementDto(Guid Id, DateTime PlannedDate, string Type, decimal Amount, string Title, string? Notes, string Recurrence, string Status, Guid? SettledTransactionId);
public sealed record CreatePlannedCashMovementRequest(DateTime PlannedDate, string TransactionType, decimal Amount, string Title, string? Notes = null, string Recurrence = "None", string? IdempotencyKey = null, Guid? BusinessUnitId = null, int RecurrenceCount = 1);

public sealed record StudentReceivableDto(Guid Id, Guid StudentId, Guid ClassId, string StudentName, string ClassName, decimal PayableAmount, decimal PaidAmount, decimal RefundedAmount, decimal OutstandingAmount, DateTime? DueDate, string Status, string StatusLabel);
public sealed record RecordStudentReceiptRequest(decimal Amount, DateTime? ReceivedAt = null, string? Description = null, string? IdempotencyKey = null);
public sealed record RecordStudentRefundRequest(decimal Amount, DateTime? RefundedAt = null, string? Description = null, string? IdempotencyKey = null);
public sealed record UpdateStudentReceivableRequest(DateTime? DueDate, bool Cancelled = false);

public sealed record TeacherAdvanceDto(Guid Id, decimal Amount, decimal ReturnedAmount, decimal AppliedAmount, decimal RemainingAmount, DateTime IssuedAt, string Description);
public sealed record TeacherCostWorkspaceDto(Guid EmployeeId, string TeacherName, decimal EarnedAmount, decimal ProjectedAmount, decimal SettledAmount, decimal AdvancesIssued, decimal AdvancesReturned, decimal RemainingAdvance, decimal UnpaidEarned, IReadOnlyList<TeacherAdvanceDto>? Advances = null, decimal AdvancesApplied = 0);
public sealed record IssueTeacherAdvanceRequest(Guid EmployeeId, decimal Amount, DateTime? IssuedAt = null, string? Description = null, string? IdempotencyKey = null, Guid? BusinessUnitId = null);
public sealed record ReturnTeacherAdvanceRequest(decimal Amount, DateTime? ReturnedAt = null, string? Description = null, string? IdempotencyKey = null);
public sealed record PayTeacherRequest(Guid EmployeeId, decimal Amount, DateTime? PaidAt = null, string? Description = null, string? IdempotencyKey = null, Guid? BusinessUnitId = null, decimal ApplyAdvanceAmount = 0);

public sealed record FinanceWorkspaceMonthDto(DateTime Month, string Kind, decimal OpeningCash, decimal TotalInflow, decimal TotalOutflow, decimal StudentCollections, decimal TeacherPayments, decimal PlannedIncome, decimal PlannedExpense, decimal ClosingCash)
{
    public decimal ActualInflow { get; init; }
    public decimal ActualOutflow { get; init; }
    public decimal ForecastInflow { get; init; }
    public decimal ForecastOutflow { get; init; }
}
public sealed record FinanceWorkspaceCashMovementDto(Guid Id, DateTime OccurredAt, string Type, decimal Inflow, decimal Outflow, string Description, string? ReferenceType, Guid? ReferenceId);
public sealed record FinanceWorkspaceReportDto(DateTime From, DateTime To, decimal CurrentCash, decimal StudentDebt, decimal TeacherPayable, decimal ForecastClosingCash, IReadOnlyList<FinanceWorkspaceMonthDto> Months, IReadOnlyList<StudentReceivableDto> Receivables, IReadOnlyList<TeacherCostWorkspaceDto> TeacherCosts, IReadOnlyList<PlannedCashMovementDto> PlannedMovements, IReadOnlyList<FinanceWorkspaceCashMovementDto>? CashMovements = null)
{
    public DateTime AsOf { get; init; }
    public bool CurrentCashIsReconciled { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
