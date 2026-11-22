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
        var primaryEmployeeIds = employees.Select(e => e.Id).ToHashSet();

        var attendanceQuery = _db.AttendanceRecords
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Date >= period.StartDate && a.Date <= period.EndDate)
            .AsQueryable();

        if (period.BusinessUnitId.HasValue)
        {
            attendanceQuery = attendanceQuery.Where(a => a.BusinessUnitId == period.BusinessUnitId.Value);
        }

        var attendances = await attendanceQuery.ToListAsync(ct);

        // 1) Load completed LMS teacher cost snapshots for this period (Single Source of Truth for LMS-linked classes).
        var periodStartUtc = period.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var periodEndExclusiveUtc = period.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        string? periodBuCode = null;
        if (period.BusinessUnitId.HasValue)
        {
            periodBuCode = await _db.BusinessUnits.AsNoTracking()
                .Where(b => b.Id == period.BusinessUnitId.Value && b.CompanyId == companyId)
                .Select(b => b.Code)
                .FirstOrDefaultAsync(ct);
        }

        var allowNullBuSnapshots = !period.BusinessUnitId.HasValue ||
            string.Equals(periodBuCode, "CSCA", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(periodBuCode, "EDTECH", StringComparison.OrdinalIgnoreCase);

        var snapshotQuery = _db.CscaTeacherCostSnapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                x.SourceSystem == "CSCA_COURSE" &&
                x.IsCurrent &&
                x.IsCompleted &&
                x.EmployeeId != null &&
                x.StartTime >= periodStartUtc &&
                x.StartTime < periodEndExclusiveUtc);
        if (period.BusinessUnitId.HasValue)
        {
            var buId = period.BusinessUnitId.Value;
            snapshotQuery = allowNullBuSnapshots
                ? snapshotQuery.Where(x => x.BusinessUnitId == buId || x.BusinessUnitId == null)
                : snapshotQuery.Where(x => x.BusinessUnitId == buId);
        }

        var lmsSnapshots = (await snapshotQuery.ToListAsync(ct))
            .Where(x => !IsCancelledStatus(x.Status))
            .ToList();

        var lmsBackedClassIds = new HashSet<Guid>();
        if (lmsSnapshots.Count > 0)
        {
            var snapshotClassSources = lmsSnapshots
                .Select(x => x.ClassSourceId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var src in snapshotClassSources)
            {
                if (Guid.TryParse(src, out var parsedClassId))
                    lmsBackedClassIds.Add(parsedClassId);
            }

            if (snapshotClassSources.Count > 0)
            {
                var mappedClassIds = await _db.ApplicationClassMaps.AsNoTracking()
                    .Where(m => m.CompanyId == companyId && snapshotClassSources.Contains(m.ExternalClassId))
                    .Select(m => m.CscaClassId)
                    .ToListAsync(ct);
                foreach (var cid in mappedClassIds)
                    lmsBackedClassIds.Add(cid);
            }
        }

        var snapshotIds = lmsSnapshots.Select(x => x.Id).ToList();
        var allocatedBySnapshotId = snapshotIds.Count == 0
            ? new Dictionary<Guid, decimal>()
            : await _db.TeacherPaymentAllocations.AsNoTracking()
                .Where(a => a.CompanyId == companyId && snapshotIds.Contains(a.TeacherCostSnapshotId))
                .GroupBy(a => a.TeacherCostSnapshotId)
                .Select(g => new { SnapshotId = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.SnapshotId, x => x.Amount, ct);

        // 2) Load completed local lesson sessions (excluding LMS-backed classes to avoid double-counting).
        // Supports Teacher, TeachingAssistant, and Mentor; strictly requires Status == "Completed".
        var rawLessonAssignments = await _db.CscaLessonSessions.AsNoTracking()
            .Where(s => s.LessonDate >= period.StartDate && s.LessonDate <= period.EndDate &&
                s.Class.CompanyId == companyId && !s.Class.IsDeleted &&
                s.Class.Status != "Cancelled" && s.Status == "Completed" && s.IsPayable &&
                (!period.BusinessUnitId.HasValue || s.Class.BusinessUnitId == period.BusinessUnitId))
            .SelectMany(s => s.Class.Staff.Where(st =>
                    st.RoleInClass.ToLower() == "teacher" ||
                    st.RoleInClass.ToLower() == "teachingassistant" ||
                    st.RoleInClass.ToLower() == "mentor"),
                (s, st) => new
                {
                    s.ClassId,
                    SessionId = s.Id,
                    st.EmployeeId,
                    st.CompensationRate,
                    s.LessonDate,
                    s.StartTime,
                    s.EndTime
                })
            .ToListAsync(ct);

        var localLessonAssignments = rawLessonAssignments
            .Where(x => !lmsBackedClassIds.Contains(x.ClassId))
            .GroupBy(x => new { x.SessionId, x.EmployeeId })
            .Select(g =>
            {
                var best = g.OrderByDescending(x => x.CompensationRate).First();
                var hours = Math.Max(0m, (decimal)(best.EndTime - best.StartTime).TotalHours);
                return new LocalSessionAssignment(
                    best.SessionId,
                    best.EmployeeId,
                    best.LessonDate,
                    hours,
                    best.CompensationRate);
            })
            .ToList();

        // 3) Include cross-BU active employees who taught in this BU during this period.
        if (period.BusinessUnitId.HasValue)
        {
            var teachingEmployeeIds = lmsSnapshots
                .Where(x => x.EmployeeId.HasValue)
                .Select(x => x.EmployeeId!.Value)
                .Concat(localLessonAssignments.Select(x => x.EmployeeId))
                .Where(id => !primaryEmployeeIds.Contains(id))
                .Distinct()
                .ToList();

            if (teachingEmployeeIds.Count > 0)
            {
                var extraTeachers = await _db.Employees.AsNoTracking()
                    .Where(e => !e.IsDeleted && e.CompanyId == companyId && e.Status == "Active" && teachingEmployeeIds.Contains(e.Id))
                    .Include(e => e.Department)
                    .ToListAsync(ct);
                employees.AddRange(extraTeachers);
            }
        }

        var attendanceLookup = attendances.ToLookup(a => a.EmployeeId);
        var missingAttendanceEmployees = employees
            .Where(employee => primaryEmployeeIds.Contains(employee.Id) &&
                               employee.EmploymentType == EmploymentType.FULL_TIME &&
                               !attendanceLookup[employee.Id].Any())
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

        var openAdvanceQuery = _db.TeacherAdvances.AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.Status != "Cancelled");
        if (period.BusinessUnitId.HasValue)
        {
            var buId = period.BusinessUnitId.Value;
            openAdvanceQuery = allowNullBuSnapshots
                ? openAdvanceQuery.Where(a => a.BusinessUnitId == buId || a.BusinessUnitId == null)
                : openAdvanceQuery.Where(a => a.BusinessUnitId == buId);
        }
        var openAdvancesByEmployee = (await openAdvanceQuery.ToListAsync(ct))
            .Where(a => a.Amount > a.ReturnedAmount + a.AppliedAmount)
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.Amount - a.ReturnedAmount - a.AppliedAmount));

        var adjustmentsLookup = adjustments.ToLookup(a => a.EmployeeId);
        var workEntriesLookup = workEntries.ToLookup(w => w.EmployeeId);
        var lmsSnapshotsByEmployee = lmsSnapshots.ToLookup(x => x.EmployeeId!.Value);
        var localSessionsByEmployee = localLessonAssignments.ToLookup(x => x.EmployeeId);

        var payslipList = new List<PayslipDto>();

        foreach (var emp in employees)
        {
            var empAttendances = attendanceLookup[emp.Id].ToList();
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

            decimal teachingWorkEarnings = 0m;
            decimal lmsTeachingHours = 0m;
            int lmsTeachingSessions = 0;
            var hasTeachingCompensation = false;

            // A) Completed local lesson sessions (not backed by LMS snapshots)
            var empLocalSessions = localSessionsByEmployee[emp.Id].ToList();
            if (empLocalSessions.Count > 0)
            {
                var standardHourlySessions = emp.EmploymentType == EmploymentType.PART_TIME &&
                    emp.PartTimeCalculationMethod == PartTimeCalculationMethod.HOURLY
                    ? empLocalSessions.Where(s => s.CompensationRate <= 0m).ToList()
                    : [];
                var customOrShiftSessions = empLocalSessions.Except(standardHourlySessions).ToList();

                if (standardHourlySessions.Count > 0)
                {
                    hasTeachingCompensation = true;
                    var dailyLessonHours = standardHourlySessions
                        .GroupBy(s => s.LessonDate)
                        .ToDictionary(g => g.Key, g => g.Sum(s => s.DurationHours));
                    var attendanceByDate = empAttendances
                        .GroupBy(a => a.Date)
                        .ToDictionary(g => g.Key, g => g.Sum(a => IsWorkedShift(a) ? Math.Max(0, a.WorkHours) : 0));
                    actualWorkHours = dailyLessonHours.Sum(day =>
                        attendanceByDate.TryGetValue(day.Key, out var recordedHours) ? recordedHours : day.Value);
                    actualWorkHours += attendanceByDate
                        .Where(day => !dailyLessonHours.ContainsKey(day.Key)).Sum(day => day.Value);
                    actualWorkDays = Math.Round(actualWorkHours / 8.0m, 2);
                    actualShifts = dailyLessonHours.Keys.Union(attendanceByDate.Keys).Count();
                }

                if (customOrShiftSessions.Count > 0)
                {
                    foreach (var session in customOrShiftSessions)
                    {
                        if (session.CompensationRate > 0m)
                        {
                            hasTeachingCompensation = true;
                            teachingWorkEarnings += Math.Round(session.DurationHours * session.CompensationRate, 0);
                            lmsTeachingHours += session.DurationHours;
                            lmsTeachingSessions++;
                        }
                        else if (emp.EmploymentType == EmploymentType.PART_TIME &&
                                 emp.PartTimeCalculationMethod == PartTimeCalculationMethod.SHIFT)
                        {
                            hasTeachingCompensation = true;
                            actualShifts += 1m;
                            actualWorkHours += session.DurationHours;
                        }
                        else if (emp.EmploymentType == EmploymentType.FULL_TIME &&
                                 emp.PartTimeUnitRate.HasValue && emp.PartTimeUnitRate.Value > 0m)
                        {
                            hasTeachingCompensation = true;
                            teachingWorkEarnings += Math.Round(session.DurationHours * emp.PartTimeUnitRate.Value, 0);
                            lmsTeachingHours += session.DurationHours;
                            lmsTeachingSessions++;
                        }
                    }
                }
            }

            // B) Completed LMS teacher cost snapshots (authoritative for LMS-linked classes, anti-double-pay)
            var empSnapshots = lmsSnapshotsByEmployee[emp.Id].ToList();
            if (empSnapshots.Count > 0)
            {
                decimal unpaidSnapshotAccrued = 0m;
                foreach (var snap in empSnapshots)
                {
                    var alreadySettled = Math.Max(
                        IsSnapshotPaid(snap) ? snap.AccruedAmount : 0m,
                        allocatedBySnapshotId.GetValueOrDefault(snap.Id));
                    var unpaid = Math.Max(0m, snap.AccruedAmount - alreadySettled);
                    unpaidSnapshotAccrued += unpaid;
                    lmsTeachingHours += snap.DurationHours;
                    lmsTeachingSessions++;
                }

                var latestSnapshot = empSnapshots
                    .OrderByDescending(x => x.SourceUpdatedAt ?? x.SourceCalculatedAt)
                    .First();
                var unpaidSettlementBonus = IsSnapshotPaid(latestSnapshot) ? 0m : latestSnapshot.BonusAmount;
                var unpaidSettlementDeduction = IsSnapshotPaid(latestSnapshot) ? 0m : latestSnapshot.DeductionAmount;
                var lmsNetUnpaid = Math.Max(0m, unpaidSnapshotAccrued + unpaidSettlementBonus - unpaidSettlementDeduction);
                if (lmsNetUnpaid > 0m)
                {
                    hasTeachingCompensation = true;
                    teachingWorkEarnings += lmsNetUnpaid;
                }
            }

            var empAdjustments = adjustmentsLookup[emp.Id].ToList();
            var allowances = SumAdjustments(empAdjustments, PayrollComponentCodes.Allowance);
            var kpiBonus = SumAdjustments(empAdjustments, PayrollComponentCodes.KpiBonus);
            var bonuses = SumAdjustments(empAdjustments, PayrollComponentCodes.Bonus);
            var overtime = SumAdjustments(empAdjustments, PayrollComponentCodes.Overtime);
            var healthInsurance = SumAdjustments(empAdjustments, PayrollComponentCodes.HealthInsurance);
            var otherDeductions = SumAdjustments(empAdjustments, PayrollComponentCodes.Deduction);
            var workEarnings = workEntriesLookup[emp.Id].Sum(w => w.Amount) + teachingWorkEarnings;

            // Compute base/hourly salary BEFORE adding LMS display hours so PART_TIME hourly isn't double-multiplied.
            var earnedSalary = emp.EmploymentType == EmploymentType.PART_TIME
                ? CalculatePartTimeSalary(emp, actualWorkHours, actualShifts)
                : Math.Round(emp.BaseSalary * (actualWorkDays / 22.0m), 0);

            var displayWorkHours = actualWorkHours + lmsTeachingHours;
            var displayShifts = actualShifts + lmsTeachingSessions;
            var displayWorkDays = emp.EmploymentType == EmploymentType.PART_TIME
                ? Math.Round(displayWorkHours / 8.0m, 2)
                : actualWorkDays;

            var otherIncomeAdjustments = allowances + bonuses + overtime;
            var totalIncome = earnedSalary + otherIncomeAdjustments + kpiBonus + workEarnings;
            var totalDeductions = healthInsurance + otherDeductions;

            // C) Automatically offset open TeacherAdvances against teaching earnings so net cash payable is accurate.
            if (hasTeachingCompensation &&
                openAdvancesByEmployee.TryGetValue(emp.Id, out var openAdvance) &&
                openAdvance > 0m)
            {
                var advanceOffset = Math.Min(openAdvance, Math.Max(0m, totalIncome - totalDeductions));
                totalDeductions += advanceOffset;
            }

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
                    ActualWorkDays = displayWorkDays,
                    EmploymentType = emp.EmploymentType,
                    PartTimeCalculationMethod = emp.PartTimeCalculationMethod,
                    PartTimeUnitRate = emp.PartTimeUnitRate,
                    ActualWorkHours = displayWorkHours,
                    ActualShifts = displayShifts,
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
                existingSlip.ActualWorkDays = displayWorkDays;
                existingSlip.EmploymentType = emp.EmploymentType;
                existingSlip.PartTimeCalculationMethod = emp.PartTimeCalculationMethod;
                existingSlip.PartTimeUnitRate = emp.PartTimeUnitRate;
                existingSlip.ActualWorkHours = displayWorkHours;
                existingSlip.ActualShifts = displayShifts;
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

    private sealed record LocalSessionAssignment(
        Guid SessionId,
        Guid EmployeeId,
        DateOnly LessonDate,
        decimal DurationHours,
        decimal CompensationRate);

    private static bool IsCancelledStatus(string? status) =>
        string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase);

    private static bool IsSnapshotPaid(CscaTeacherCostSnapshot snapshot) =>
        snapshot.PaidAt.HasValue ||
        string.Equals(snapshot.PaymentStatus, "paid", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(snapshot.PaymentStatus, "settled", StringComparison.OrdinalIgnoreCase);
}
