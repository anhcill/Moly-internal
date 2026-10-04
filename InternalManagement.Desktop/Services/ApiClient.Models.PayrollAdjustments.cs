using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record PayrollAdjustmentItem(
        Guid Id,
        Guid PayrollPeriodId,
        Guid EmployeeId,
        string EmployeeCode,
        string EmployeeName,
        string Type,
        decimal Amount,
        string Reason,
        DateTime CreatedAt)
    {
        public bool IsDeduction => Type.Trim().ToUpperInvariant() is "HEALTH_INSURANCE" or "BHYT" or "DEDUCTION" or "KHAU_TRU";

        public string TypeNameVi => Type.Trim().ToUpperInvariant() switch
        {
            "ALLOWANCE" or "TRO_CAP" => "Trợ cấp",
            "KPI" or "KPI_BONUS" or "THUONG_KPI" => "Thưởng KPI",
            "BONUS" or "THUONG" => "Thưởng khác",
            "OVERTIME" or "OT" or "LAM_THEM_GIO" => "Tiền làm thêm giờ",
            "HEALTH_INSURANCE" or "BHYT" => "BHYT khấu trừ",
            "DEDUCTION" or "KHAU_TRU" => "Khấu trừ khác",
            _ => Type
        };

        public string AmountEffectDisplay => $"{(IsDeduction ? "−" : "+")} {Amount:N0} đ";
    }

    public sealed record CreatePayrollAdjustmentModel(
        Guid EmployeeId,
        string Type,
        decimal Amount,
        string Reason);

    public sealed record PayrollCalculationResultItem(
        Guid PayrollPeriodId,
        string PayrollPeriodName,
        int TotalEmployeesProcessed,
        decimal TotalGrossAmount,
        decimal TotalNetAmount,
        IReadOnlyList<PayslipItem> Payslips);

}
