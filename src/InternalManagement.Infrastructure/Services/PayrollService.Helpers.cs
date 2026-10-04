using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class PayrollService
{
    private async Task SyncPayrollDocumentAsync(PayrollPeriod period, BusinessDocumentStatus status, CancellationToken ct)
    {
        if (_documentRegistry is null)
        {
            return;
        }

        var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
            period.CompanyId,
            period.BusinessUnitId,
            BusinessDocumentType.PayrollPeriod,
            nameof(PayrollPeriod),
            period.Id,
            $"PAYROLL-{period.Id:N}",
            period.TotalNetAmount,
            period.StartDate.ToDateTime(TimeOnly.MinValue),
            Status: status), ct);
        period.BusinessDocumentId = document.Id;
    }

    private static PayslipDto MapPayslip(Payslip payslip, string periodName, Employee employee)
    {
        var totalIncome = payslip.TotalIncome != 0 ? payslip.TotalIncome : payslip.GrossSalary;
        var totalDeductions = payslip.TotalDeductions != 0 ? payslip.TotalDeductions : payslip.Deductions;

        return new PayslipDto(
            payslip.Id,
            payslip.PayrollPeriodId,
            periodName,
            payslip.EmployeeId,
            employee.EmployeeCode,
            employee.FullName,
            employee.Department?.Name,
            employee.Position,
            payslip.BaseSalary,
            payslip.StandardWorkDays,
            payslip.ActualWorkDays,
            payslip.GrossSalary,
            payslip.Allowances,
            payslip.Deductions,
            payslip.NetSalary,
            payslip.Status,
            payslip.PublishedAt,
            payslip.CreatedAt,
            payslip.EmploymentType,
            payslip.EmploymentType == EmploymentType.PART_TIME ? "Bán thời gian" : "Toàn thời gian",
            payslip.PartTimeCalculationMethod,
            payslip.PartTimeCalculationMethod == PartTimeCalculationMethod.HOURLY ? "Theo giờ" :
                payslip.PartTimeCalculationMethod == PartTimeCalculationMethod.SHIFT ? "Theo ca" : null,
            payslip.PartTimeUnitRate,
            payslip.ActualWorkHours,
            payslip.ActualShifts,
            payslip.KpiBonus,
            payslip.HealthInsurance,
            totalIncome,
            totalDeductions,
            payslip.WorkEarnings);
    }

    private static PayrollAdjustmentDto MapAdjustment(PayrollAdjustment adjustment) =>
        MapAdjustment(adjustment, adjustment.Employee.EmployeeCode, adjustment.Employee.FullName);

    private static PayrollAdjustmentDto MapAdjustment(
        PayrollAdjustment adjustment,
        string employeeCode,
        string employeeName)
    {
        var code = NormalizeComponentCode(adjustment.Type);
        var (name, category) = GetComponentMetadata(code);
        return new PayrollAdjustmentDto(
            adjustment.Id,
            adjustment.PayrollPeriodId,
            adjustment.EmployeeId,
            employeeCode,
            employeeName,
            code,
            adjustment.Amount,
            adjustment.Reason,
            adjustment.CreatedAt,
            name,
            category);
    }

    private static decimal CalculatePartTimeSalary(Employee employee, decimal actualWorkHours, decimal actualShifts)
    {
        if (employee.PartTimeCalculationMethod == PartTimeCalculationMethod.OUTPUT)
            return 0;
        var unitRate = employee.PartTimeUnitRate.GetValueOrDefault();
        var units = employee.PartTimeCalculationMethod == PartTimeCalculationMethod.SHIFT
            ? actualShifts
            : actualWorkHours;
        return Math.Round(unitRate * units, 0);
    }

    private static bool IsWorkedShift(AttendanceRecord attendance) =>
        attendance.WorkHours > 0 &&
        !attendance.Status.Equals("Absent", StringComparison.OrdinalIgnoreCase) &&
        !attendance.Status.Equals("Leave", StringComparison.OrdinalIgnoreCase);

    private static decimal SumAdjustments(IEnumerable<PayrollAdjustment> adjustments, string componentCode) =>
        adjustments
            .Where(a => NormalizeComponentCode(a.Type) == componentCode)
            .Sum(a => a.Amount);

    private static string NormalizeComponentCode(string type)
    {
        var normalized = type.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
        return normalized switch
        {
            "ALLOWANCE" or "TRO_CAP" => PayrollComponentCodes.Allowance,
            "KPI" or "KPI_BONUS" or "THUONG_KPI" => PayrollComponentCodes.KpiBonus,
            "HEALTH_INSURANCE" or "BHYT" => PayrollComponentCodes.HealthInsurance,
            "DEDUCTION" or "KHAU_TRU" => PayrollComponentCodes.Deduction,
            "BONUS" or "THUONG" => PayrollComponentCodes.Bonus,
            "OVERTIME" or "OT" or "LAM_THEM_GIO" => PayrollComponentCodes.Overtime,
            _ => type.Trim()
        };
    }

    private static (string Name, string Category) GetComponentMetadata(string code) => code switch
    {
        PayrollComponentCodes.Allowance => ("Trợ cấp", "INCOME"),
        PayrollComponentCodes.KpiBonus => ("Thưởng KPI", "INCOME"),
        PayrollComponentCodes.Bonus => ("Thưởng khác", "INCOME"),
        PayrollComponentCodes.Overtime => ("Tiền làm thêm giờ", "INCOME"),
        PayrollComponentCodes.HealthInsurance => ("Bảo hiểm y tế (BHYT)", "DEDUCTION"),
        PayrollComponentCodes.Deduction => ("Khấu trừ khác", "DEDUCTION"),
        _ => ("Khoản điều chỉnh", "OTHER")
    };

    private async Task<List<Guid>?> ResolveSegmentBusinessUnitIdsAsync(
        Guid companyId,
        Guid? businessUnitId,
        string? businessSegment,
        CancellationToken ct)
    {
        if (businessUnitId.HasValue && businessUnitId.Value != Guid.Empty)
        {
            return [businessUnitId.Value];
        }

        if (string.IsNullOrWhiteSpace(businessSegment))
        {
            return null;
        }

        var normalizedSegment = businessSegment.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
        var units = _db.BusinessUnits.AsNoTracking().Where(b =>
            b.CompanyId == companyId && !b.IsDeleted && b.IsActive);

        if (normalizedSegment == "FASHION" || normalizedSegment == "THOI_TRANG")
        {
            units = units.Where(b => b.Code.ToUpper() == "FASHION");
        }
        else if (normalizedSegment is "TECH_EDUCATION" or "TECHNOLOGY_EDUCATION" or "CONG_NGHE_GIAO_DUC" or "MOLY")
        {
            units = units.Where(b => b.Code.ToUpper() == "EDTECH" ||
                                     b.Code.ToUpper() == "CSCA" ||
                                     b.Code.ToUpper() == "INTERVIEW");
        }
        else
        {
            units = units.Where(b => b.Code.ToUpper() == normalizedSegment);
        }

        return await units.Select(b => b.Id).ToListAsync(ct);
    }
}
