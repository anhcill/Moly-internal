using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.HrPayroll.DTOs;

public record PayrollPeriodDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    PayrollStatus Status,
    decimal TotalGrossAmount,
    decimal TotalNetAmount,
    int PayslipCount,
    DateTime? CalculatedAt,
    DateTime? ApprovedAt,
    DateTime CreatedAt);

public record PayrollPeriodDetailDto(
    Guid Id,
    Guid CompanyId,
    Guid? BusinessUnitId,
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    PayrollStatus Status,
    decimal TotalGrossAmount,
    decimal TotalNetAmount,
    DateTime? CalculatedAt,
    DateTime? ApprovedAt,
    DateTime CreatedAt,
    IReadOnlyList<PayslipDto> Payslips,
    IReadOnlyList<PayrollAdjustmentDto> Adjustments);

public record CreatePayrollPeriodRequest(
    string Name,
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? BusinessUnitId = null);

public record PayrollAdjustmentDto(
    Guid Id,
    Guid PayrollPeriodId,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string Type, // Bonus, Deduction, Overtime, Allowance
    decimal Amount,
    string Reason,
    DateTime CreatedAt,
    string VietnameseName = "Khoản điều chỉnh",
    string Category = "OTHER");

public record CreatePayrollAdjustmentRequest(
    Guid EmployeeId,
    string Type,
    decimal Amount,
    string Reason);

public record PayslipDto(
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
    PayrollStatus Status,
    DateTime? PublishedAt,
    DateTime CreatedAt,
    EmploymentType EmploymentType = EmploymentType.FULL_TIME,
    string EmploymentTypeNameVi = "Toàn thời gian",
    PartTimeCalculationMethod? PartTimeCalculationMethod = null,
    string? PartTimeCalculationMethodNameVi = null,
    decimal? PartTimeUnitRate = null,
    decimal ActualWorkHours = 0,
    decimal ActualShifts = 0,
    decimal KpiBonus = 0,
    decimal HealthInsurance = 0,
    decimal TotalIncome = 0,
    decimal TotalDeductions = 0);

/// <summary>
/// Danh mục mã khoản lương ổn định cho UI. API cũ vẫn có thể gửi Bonus,
/// Deduction, Overtime và Allowance qua trường Type.
/// </summary>
public static class PayrollComponentCodes
{
    public const string Allowance = "ALLOWANCE";
    public const string KpiBonus = "KPI_BONUS";
    public const string HealthInsurance = "HEALTH_INSURANCE";
    public const string Deduction = "DEDUCTION";
    public const string Bonus = "BONUS";
    public const string Overtime = "OVERTIME";
}

public record PayrollComponentTypeDto(
    string Code,
    string VietnameseName,
    string Category,
    string Description);

public record PayrollPolicyVersionDto(
    Guid Id,
    Guid CompanyId,
    int VersionNumber,
    DateOnly EffectiveDate,
    string ConfigJson,
    bool IsActive,
    DateTime CreatedAt);

public record CreatePayrollPolicyVersionRequest(
    int VersionNumber,
    DateOnly EffectiveDate,
    string ConfigJson);

public record PayrollCalculationResultDto(
    Guid PayrollPeriodId,
    string PayrollPeriodName,
    int TotalEmployeesProcessed,
    decimal TotalGrossAmount,
    decimal TotalNetAmount,
    IReadOnlyList<PayslipDto> Payslips);

public record PayrollApprovalDto(
    Guid Id,
    Guid PayrollPeriodId,
    Guid ApproverId,
    string ApproverName,
    PayrollStatus Status,
    string? Comments,
    DateTime ApprovedAt);

public record SubmitPayrollForReviewRequest(string? Comments);
public record ApprovePayrollRequest(string? Comments);
public record MarkPayrollPaidRequest(string? Comments);
public record PublishPayrollRequest(string? Comments);
public record CancelPayrollPeriodRequest(string? Comments);
