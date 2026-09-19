namespace InternalManagement.Application.Features.Finance.DTOs;

public sealed record BusinessUnitProfitSummaryDto
{
    public Guid? BusinessUnitId { get; init; }
    public string BusinessUnitCode { get; init; } = string.Empty;
    public string BusinessUnitName { get; init; } = string.Empty;
    public decimal TotalIncome { get; init; }
    public decimal TotalExpense { get; init; }
    public decimal NetProfit => TotalIncome - TotalExpense;
    public double ProfitMarginPercent => TotalIncome > 0 ? (double)(NetProfit / TotalIncome) * 100 : 0;
    public List<ProfitAllocationItemDto> Allocations { get; init; } = [];
}

public sealed record ProfitAllocationItemDto
{
    public Guid Id { get; init; }
    public string ReferenceType { get; init; } = string.Empty;
    public Guid ReferenceId { get; init; }
    public string ReferenceTitle { get; init; } = string.Empty;
    public decimal IncomeAmount { get; init; }
    public decimal ExpenseAmount { get; init; }
    public decimal NetAmount => IncomeAmount - ExpenseAmount;
    public DateTime AllocatedAt { get; init; }
}

public sealed record RecordProfitAllocationRequest(
    string BusinessUnitCode,
    string ReferenceType,
    Guid ReferenceId,
    decimal IncomeAmount,
    decimal ExpenseAmount);
