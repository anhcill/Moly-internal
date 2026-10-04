using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record FinancialAreaSummaryItem(
        string AreaCode,
        string AreaName,
        IReadOnlyList<string> BusinessUnitCodes,
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetCashFlow,
        decimal OperatingProfit,
        decimal ConfirmedProfit,
        decimal ProvisionalProfit,
        bool HasProvisionalData,
        string ProfitDataStatus,
        string ProfitDataStatusName,
        string ProfitSourceName,
        int CashTransactionCount,
        int ProfitSnapshotCount);

    public sealed record UnclassifiedCashFlowItem(
        decimal TotalIncome,
        decimal TotalExpense,
        decimal NetCashFlow,
        int TransactionCount);

    public sealed record CompanyFinancialOverviewItem(
        DateTime From,
        DateTime To,
        IReadOnlyList<FinancialAreaSummaryItem> Areas,
        FinancialAreaSummaryItem CompanyTotal,
        UnclassifiedCashFlowItem UnclassifiedCashFlow,
        bool HasUnclassifiedTransactions,
        string CalculationNote);

}
