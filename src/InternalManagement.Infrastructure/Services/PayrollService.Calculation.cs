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
    public async Task<Result<PayrollCalculationResultDto>> CalculatePayrollAsync(Guid periodId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .Include(p => p.Adjustments)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollCalculationResultDto>.Failure("Không tìm thấy kỳ lương cần tính.");
        }

        if (period.Status >= PayrollStatus.Reviewing)
        {
            return Result<PayrollCalculationResultDto>.Failure(
                "Kỳ lương đã gửi duyệt hoặc khóa, không thể tính lại.");
        }

        var employeeQuery = _db.Employees
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.CompanyId == companyId && e.Status == "Active")
            .Include(e => e.Department)
            .AsQueryable();

        if (period.BusinessUnitId.HasValue)
        {
            employeeQuery = employeeQuery.Where(e => e.BusinessUnitId == period.BusinessUnitId.Value);
        }

        var employees = await employeeQuery.ToListAsync(ct);

        var attendanceQuery = _db.AttendanceRecords
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Date >= period.StartDate && a.Date <= period.EndDate)
            .AsQueryable();

        if (period.BusinessUnitId.HasValue)
        {
            attendanceQuery = attendanceQuery.Where(a => a.BusinessUnitId == period.BusinessUnitId.Value);
        }

        var attendances = await attendanceQuery.ToListAsync(ct);

        var missingAttendanceEmployees = employees
            .Where(employee => employee.EmploymentType == EmploymentType.FULL_TIME &&
                               !attendances.Any(attendance => attendance.EmployeeId == employee.Id))
            .OrderBy(employee => employee.EmployeeCode)
            .Select(employee => $"{employee.EmployeeCode} - {employee.FullName}")
            .ToList();
        if (missingAttendanceEmployees.Count > 0)
        {
            var shownEmployees = string.Join(", ", missingAttendanceEmployees.Take(10));
            var remaining = missingAttendanceEmployees.Count - 10;
            return Result<PayrollCalculationResultDto>.Failure(
                $"Chưa thể tính lương: {missingAttendanceEmployees.Count} nhân viên toàn thời gian chưa có dữ liệu chấm công trong kỳ {period.StartDate:dd/MM/yyyy}–{period.EndDate:dd/MM/yyyy}. " +
                $"Hãy nhập/chốt chấm công trước: {shownEmployees}" +
                (remaining > 0 ? $" và {remaining} nhân viên khác." : "."));
        }

        var adjustments = await _db.PayrollAdjustments
            .AsNoTracking()
            .Where(a => a.PayrollPeriodId == period.Id)
            .ToListAsync(ct);
        var workEntries = await _db.PayrollWorkEntries
            .AsNoTracking()
            .Where(w => w.PayrollPeriodId == period.Id && !w.IsVoided)
            .ToListAsync(ct);

        var payslipList = new List<PayslipDto>();

        foreach (var emp in employees)
        {
            var empAttendances = attendances.Where(a => a.EmployeeId == emp.Id).ToList();
            decimal actualWorkDays;
            decimal actualWorkHours;
            decimal actualShifts;

            if (empAttendances.Count > 0)
            {
                // Chỉ tính thời gian có làm việc thực tế. Bản ghi Vắng/Nghỉ phép
                // có thể vẫn mang số giờ do file import hoặc người nhập nhầm,
                // tuyệt đối không được tạo tiền cho nhân sự theo giờ/ca.
                var workedAttendances = empAttendances.Where(IsWorkedShift).ToList();
                actualWorkHours = workedAttendances.Sum(a => Math.Max(0, a.WorkHours));
                actualWorkDays = Math.Round(actualWorkHours / 8.0m, 2);
                actualShifts = workedAttendances.Count;
            }
            else
            {
                // Chỉ part-time có thể chưa có dữ liệu và được tính 0 theo giờ/ca thực tế.
                // Full-time đã được chặn trước vòng lặp để không bao giờ tự mặc định 22 công.
                actualWorkDays = 0;
                actualWorkHours = 0;
                actualShifts = 0;
            }

            var empAdjustments = adjustments.Where(a => a.EmployeeId == emp.Id).ToList();
            var allowances = SumAdjustments(empAdjustments, PayrollComponentCodes.Allowance);
            var kpiBonus = SumAdjustments(empAdjustments, PayrollComponentCodes.KpiBonus);
            var bonuses = SumAdjustments(empAdjustments, PayrollComponentCodes.Bonus);
            var overtime = SumAdjustments(empAdjustments, PayrollComponentCodes.Overtime);
            var healthInsurance = SumAdjustments(empAdjustments, PayrollComponentCodes.HealthInsurance);
            var otherDeductions = SumAdjustments(empAdjustments, PayrollComponentCodes.Deduction);
            var workEarnings = workEntries.Where(w => w.EmployeeId == emp.Id).Sum(w => w.Amount);

            var earnedSalary = emp.EmploymentType == EmploymentType.PART_TIME
                ? CalculatePartTimeSalary(emp, actualWorkHours, actualShifts)
                : Math.Round(emp.BaseSalary * (actualWorkDays / 22.0m), 0);
            var otherIncomeAdjustments = allowances + bonuses + overtime;
            var totalIncome = earnedSalary + otherIncomeAdjustments + kpiBonus + workEarnings;
            var totalDeductions = healthInsurance + otherDeductions;
            var netSalary = Math.Max(0, totalIncome - totalDeductions);

            var existingSlip = period.Payslips.FirstOrDefault(ps => ps.EmployeeId == emp.Id);
            if (existingSlip == null)
            {
                existingSlip = new Payslip
                {
                    PayrollPeriodId = period.Id,
                    EmployeeId = emp.Id,
                    BaseSalary = emp.BaseSalary,
                    StandardWorkDays = 22.0m,
                    ActualWorkDays = actualWorkDays,
                    EmploymentType = emp.EmploymentType,
                    PartTimeCalculationMethod = emp.PartTimeCalculationMethod,
                    PartTimeUnitRate = emp.PartTimeUnitRate,
                    ActualWorkHours = actualWorkHours,
                    ActualShifts = actualShifts,
                    GrossSalary = totalIncome,
                    Allowances = otherIncomeAdjustments,
                    KpiBonus = kpiBonus,
                    WorkEarnings = workEarnings,
                    HealthInsurance = healthInsurance,
                    TotalIncome = totalIncome,
                    Deductions = totalDeductions,
                    TotalDeductions = totalDeductions,
                    NetSalary = netSalary,
                    Status = PayrollStatus.Calculated,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username ?? "system"
                };
                _db.Payslips.Add(existingSlip);
            }
            else
            {
                existingSlip.BaseSalary = emp.BaseSalary;
                existingSlip.ActualWorkDays = actualWorkDays;
                existingSlip.EmploymentType = emp.EmploymentType;
                existingSlip.PartTimeCalculationMethod = emp.PartTimeCalculationMethod;
                existingSlip.PartTimeUnitRate = emp.PartTimeUnitRate;
                existingSlip.ActualWorkHours = actualWorkHours;
                existingSlip.ActualShifts = actualShifts;
                existingSlip.GrossSalary = totalIncome;
                existingSlip.Allowances = otherIncomeAdjustments;
                existingSlip.KpiBonus = kpiBonus;
                existingSlip.WorkEarnings = workEarnings;
                existingSlip.HealthInsurance = healthInsurance;
                existingSlip.TotalIncome = totalIncome;
                existingSlip.Deductions = totalDeductions;
                existingSlip.TotalDeductions = totalDeductions;
                existingSlip.NetSalary = netSalary;
                existingSlip.Status = PayrollStatus.Calculated;
                existingSlip.UpdatedAt = DateTime.UtcNow;
                existingSlip.UpdatedBy = _currentUser.Username ?? "system";
            }

            payslipList.Add(MapPayslip(existingSlip, period.Name, emp));
        }

        period.Status = PayrollStatus.Calculated;
        period.TotalGrossAmount = payslipList.Sum(ps => ps.GrossSalary);
        period.TotalNetAmount = payslipList.Sum(ps => ps.NetSalary);
        period.CalculatedAt = DateTime.UtcNow;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Open, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Calculated payroll for period {PeriodName} ({PeriodId}): {Count} employees, TotalNet={TotalNet:N0} đ",
            period.Name, period.Id, employees.Count, period.TotalNetAmount);

        var result = new PayrollCalculationResultDto(
            period.Id,
            period.Name,
            employees.Count,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            payslipList);

        return Result<PayrollCalculationResultDto>.Success(result);
    }

}
