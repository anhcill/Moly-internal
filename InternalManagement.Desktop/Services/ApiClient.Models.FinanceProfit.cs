using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record BusinessUnitProfitItem(
        Guid? BusinessUnitId,
        string BusinessUnitCode,
        string BusinessUnitName,
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetProfit,
        double ProfitMarginPercent,
        IReadOnlyList<ProfitAllocationRecordItem> Allocations);

    public sealed record ProfitAllocationRecordItem(
        Guid Id,
        string ReferenceType,
        Guid ReferenceId,
        string ReferenceTitle,
        decimal IncomeAmount,
        decimal ExpenseAmount,
        decimal NetAmount,
        DateTime AllocatedAt);

}
