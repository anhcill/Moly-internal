using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record PayrollPeriodItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        int Status,
        decimal TotalGrossAmount,
        decimal TotalNetAmount,
        int PayslipCount,
        DateTime? CalculatedAt,
        DateTime? ApprovedAt,
        DateTime CreatedAt);

    public sealed record DownloadedFile(string FileName, byte[] Content);

    public sealed record CreatePayrollPeriodModel(
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        Guid? BusinessUnitId = null);

    public sealed record PayrollPeriodDetailItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string Name,
        DateOnly StartDate,
        DateOnly EndDate,
        int Status,
        decimal TotalGrossAmount,
        decimal TotalNetAmount,
        DateTime? CalculatedAt,
        DateTime? ApprovedAt,
        DateTime CreatedAt,
        IReadOnlyList<PayslipItem> Payslips,
        IReadOnlyList<PayrollAdjustmentItem> Adjustments);

    public sealed record PayslipItem(
        Guid Id,
        Guid PayrollPeriodId,
        string PayrollPeriodName,
        Guid EmployeeId,
        string EmployeeCode,
        string EmployeeName,
        string? DepartmentName,
        string? Position,
        decimal BaseSalary,
        decimal StandardWorkDays,
        decimal ActualWorkDays,
        decimal GrossSalary,
        decimal Allowances,
        decimal Deductions,
        decimal NetSalary,
        int Status,
        DateTime? PublishedAt,
        DateTime CreatedAt,
        int EmploymentType = 0,
        string EmploymentTypeNameVi = "Toàn thời gian",
        int? PartTimeCalculationMethod = null,
        string? PartTimeCalculationMethodNameVi = null,
        decimal? PartTimeUnitRate = null,
        decimal ActualWorkHours = 0,
        decimal ActualShifts = 0,
        decimal KpiBonus = 0,
        decimal HealthInsurance = 0,
        decimal TotalIncome = 0,
        decimal TotalDeductions = 0,
        decimal WorkEarnings = 0)
    {
        public string WorkQuantityDisplay => EmploymentType == 1
            ? PartTimeCalculationMethod switch
            {
                1 => $"{ActualShifts:0.##} ca",
                2 => "Theo đầu việc",
                _ => $"{ActualWorkHours:0.##} giờ"
            }
            : $"{ActualWorkDays:0.##}/{StandardWorkDays:0.##} ngày";

        public string PayRateDisplay => EmploymentType == 1
            ? PartTimeCalculationMethod == 2
                ? $"Đầu việc: {WorkEarnings:N0} đ"
                : $"{PartTimeUnitRate.GetValueOrDefault():N0} đ/{(PartTimeCalculationMethod == 1 ? "ca" : "giờ")}"
            : $"{BaseSalary:N0} đ/tháng";

        public string StatusNameVi => Status switch
        {
            0 => "Bản nháp",
            1 => "Đã tính",
            2 => "Chờ duyệt",
            3 => "Đã duyệt",
            4 => "Đã chi",
            5 => "Đã phát hành",
            _ => "Chưa xác định"
        };
    }

}
