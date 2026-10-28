using System;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record FinanceWorkspaceReportItem(
        DateTime From,
        DateTime To,
        decimal CurrentCash,
        decimal StudentDebt,
        decimal TeacherPayable,
        decimal ForecastClosingCash,
        IReadOnlyList<FinanceMonthlyRowItem> Months,
        IReadOnlyList<FinanceReceivableItem> Receivables,
        IReadOnlyList<FinanceTeacherCostItem> TeacherCosts,
        IReadOnlyList<FinancePlanItem> PlannedMovements,
        IReadOnlyList<FinanceCashMovementItem>? CashMovements = null)
    {
        public DateTime AsOf { get; init; }
        public IReadOnlyList<string> Warnings { get; init; } = [];
        public bool CurrentCashIsReconciled { get; init; }
    }

    public sealed record FinanceMonthlyRowItem(
        DateTime Month,
        string Kind,
        decimal OpeningCash,
        decimal TotalInflow,
        decimal TotalOutflow,
        decimal StudentCollections,
        decimal TeacherPayments,
        decimal PlannedIncome,
        decimal PlannedExpense,
        decimal ClosingCash)
    {
        public string MonthLabel => Month.ToString("MM/yyyy");
        public string KindLabel => Kind.ToLowerInvariant() switch { "forecast" => "Dự báo", "mixed" => "Thực tế + dự báo", _ => "Thực tế" };
        public decimal ActualInflow { get; init; }
        public decimal ActualOutflow { get; init; }
        public decimal ForecastInflow { get; init; }
        public decimal ForecastOutflow { get; init; }
    }

    public sealed record FinanceReceivableItem(
        Guid Id,
        Guid StudentId,
        Guid ClassId,
        string StudentName,
        string? ClassName,
        decimal PayableAmount,
        decimal PaidAmount,
        decimal RefundedAmount,
        decimal OutstandingAmount,
        DateTime? DueDate,
        string Status,
        string StatusLabel)
    {
        public DateTime? LocalDueDate => DueDate?.AddHours(7);
        public decimal OverpaidAmount => Math.Max(0, PaidAmount - RefundedAmount - PayableAmount);
        public string CollectionAlert => Status == "Cancelled" ? "" : OverpaidAmount > 0
            ? $"Thu thừa {OverpaidAmount:N0} đ"
            : OutstandingAmount > 0 && DueDate.HasValue && DueDate.Value < DateTime.UtcNow ? "Quá hạn" : "";
    }

    public sealed record FinanceTeacherCostItem(
        Guid EmployeeId,
        string TeacherName,
        decimal EarnedAmount,
        decimal ProjectedAmount,
        decimal SettledAmount,
        decimal AdvancesIssued,
        decimal AdvancesReturned,
        decimal RemainingAdvance,
        decimal UnpaidEarned,
        IReadOnlyList<FinanceTeacherAdvanceItem>? Advances = null,
        decimal AdvancesApplied = 0);

    public sealed record FinanceTeacherAdvanceItem(Guid Id, decimal Amount, decimal ReturnedAmount, decimal AppliedAmount, decimal RemainingAmount, DateTime IssuedAt, string Description);

    public sealed record FinancePlanItem(
        Guid Id,
        DateTime PlannedDate,
        string Type,
        string Title,
        decimal Amount,
        string? Recurrence,
        string Status,
        string? Notes,
        Guid? SettledTransactionId = null)
    {
        public DateTime LocalPlannedDate => PlannedDate.AddHours(7);
        public string TypeLabel => string.Equals(Type, "Income", StringComparison.OrdinalIgnoreCase) || Type == "Thu" ? "Thu" : "Chi";
        public string RecurrenceLabel => Recurrence?.ToLowerInvariant() switch { "monthly" => "Hàng tháng", "weekly" => "Hàng tuần", "yearly" => "Hàng năm", _ => "Một lần" };
        public string StatusLabel => Status switch
        {
            "Settled" => "Đã thu/chi",
            "Cancelled" => "Đã hủy",
            _ => "Dự kiến"
        };
    }

    public sealed record FinanceCashMovementItem(
        Guid Id,
        DateTime OccurredAt,
        string Type,
        string Description,
        decimal Inflow,
        decimal Outflow,
        string? ReferenceType,
        Guid? ReferenceId,
        string? Source = null)
    {
        public DateTime LocalOccurredAt => OccurredAt.AddHours(7);
        public string TypeLabel => string.Equals(Type, "Income", StringComparison.OrdinalIgnoreCase) || Type == "Thu" ? "Thu" : "Chi";
        public string ReferenceLabel => ReferenceId.HasValue ? $"{ReferenceType} · {ReferenceId:N}" : (ReferenceType ?? "—");
    }
}
