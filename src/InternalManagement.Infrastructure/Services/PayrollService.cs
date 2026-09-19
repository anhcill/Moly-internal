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

public sealed class PayrollService : IPayrollService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PayrollService> _logger;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public PayrollService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<PayrollService> logger,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
    }

    public IReadOnlyList<PayrollComponentTypeDto> GetPayrollComponentTypes() =>
    [
        new(PayrollComponentCodes.Allowance, "Trợ cấp", "INCOME", "Khoản trợ cấp được cộng vào tổng thu nhập."),
        new(PayrollComponentCodes.KpiBonus, "Thưởng KPI", "INCOME", "Thưởng theo kết quả đánh giá KPI."),
        new(PayrollComponentCodes.Bonus, "Thưởng khác", "INCOME", "Khoản thưởng khác ngoài KPI."),
        new(PayrollComponentCodes.Overtime, "Tiền làm thêm giờ", "INCOME", "Thu nhập do làm thêm giờ."),
        new(PayrollComponentCodes.HealthInsurance, "Bảo hiểm y tế (BHYT)", "DEDUCTION", "Khoản BHYT khấu trừ vào lương."),
        new(PayrollComponentCodes.Deduction, "Khấu trừ khác", "DEDUCTION", "Khoản khấu trừ khác ngoài BHYT.")
    ];

    private async Task<Guid> GetCompanyIdAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        return companyId.Value;
    }

    public async Task<PaginatedResult<PayrollPeriodDto>> GetPayrollPeriodsAsync(
        int? year, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var scopedBusinessUnitIds = await ResolveSegmentBusinessUnitIdsAsync(
            companyId, businessUnitId, businessSegment, ct);
        var query = _db.PayrollPeriods
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId)
            .Include(p => p.Payslips)
            .AsQueryable();

        if (scopedBusinessUnitIds != null)
        {
            query = query.Where(p => p.BusinessUnitId.HasValue &&
                                     scopedBusinessUnitIds.Contains(p.BusinessUnitId.Value));
        }

        if (year.HasValue)
        {
            query = query.Where(p => p.StartDate.Year == year.Value || p.EndDate.Year == year.Value);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PayrollStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(p => p.Status == parsedStatus);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.StartDate)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PayrollPeriodDto(
                p.Id,
                p.CompanyId,
                p.BusinessUnitId,
                p.Name,
                p.StartDate,
                p.EndDate,
                p.Status,
                p.TotalGrossAmount,
                p.TotalNetAmount,
                p.Payslips.Count,
                p.CalculatedAt,
                p.ApprovedAt,
                p.CreatedAt))
            .ToListAsync(ct);

        return new PaginatedResult<PayrollPeriodDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<PayrollPeriodDetailDto>> GetPayrollPeriodByIdAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .AsNoTracking()
            .Include(p => p.Payslips)
                .ThenInclude(ps => ps.Employee)
                    .ThenInclude(e => e.Department)
            .Include(p => p.Adjustments)
                .ThenInclude(a => a.Employee)
            .FirstOrDefaultAsync(p => p.Id == id && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDetailDto>.Failure("Không tìm thấy kỳ lương.");
        }

        var payslips = period.Payslips
            .Where(ps => !period.BusinessUnitId.HasValue || ps.Employee.BusinessUnitId == period.BusinessUnitId)
            .Select(ps => MapPayslip(ps, period.Name, ps.Employee))
            .ToList();

        var adjustments = period.Adjustments
            .Where(a => !period.BusinessUnitId.HasValue || a.Employee.BusinessUnitId == period.BusinessUnitId)
            .Select(MapAdjustment)
            .ToList();

        var detail = new PayrollPeriodDetailDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt,
            payslips,
            adjustments);

        return Result<PayrollPeriodDetailDto>.Success(detail);
    }

    public async Task<Result<PayrollPeriodDto>> CreatePayrollPeriodAsync(CreatePayrollPeriodRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<PayrollPeriodDto>.Failure("Tên kỳ lương không được để trống.");
        }

        if (request.EndDate < request.StartDate)
        {
            return Result<PayrollPeriodDto>.Failure("Ngày kết thúc kỳ lương phải sau ngày bắt đầu.");
        }

        if (request.BusinessUnitId.HasValue)
        {
            var validBusinessUnit = await _db.BusinessUnits.AsNoTracking().AnyAsync(b =>
                b.Id == request.BusinessUnitId.Value && b.CompanyId == companyId && !b.IsDeleted && b.IsActive, ct);
            if (!validBusinessUnit)
            {
                return Result<PayrollPeriodDto>.Failure("Đơn vị kinh doanh của kỳ lương không hợp lệ.");
            }
        }

        var overlapsExistingPeriod = await _db.PayrollPeriods.AsNoTracking().AnyAsync(p =>
            p.CompanyId == companyId &&
            p.Status != PayrollStatus.Cancelled &&
            p.StartDate <= request.EndDate &&
            p.EndDate >= request.StartDate &&
            (request.BusinessUnitId.HasValue
                ? p.BusinessUnitId == request.BusinessUnitId.Value
                : p.BusinessUnitId == null), ct);
        if (overlapsExistingPeriod)
        {
            return Result<PayrollPeriodDto>.Failure(
                "Khoảng ngày của kỳ lương bị chồng lấn với một kỳ lương chưa hủy trong cùng mảng. Hãy dùng kỳ hiện có hoặc chọn khoảng ngày khác.");
        }

        var period = new PayrollPeriod
        {
            CompanyId = companyId,
            BusinessUnitId = request.BusinessUnitId,
            Name = request.Name.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = PayrollStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "system"
        };

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Draft, ct);
        _db.PayrollPeriods.Add(period);
        await _db.SaveChangesAsync(ct);

        var dto = new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            0,
            0,
            0,
            null,
            null,
            period.CreatedAt);

        return Result<PayrollPeriodDto>.Success(dto);
    }

    public async Task<Result<PayrollPeriodDto>> CancelPayrollPeriodAsync(
        Guid periodId,
        CancelPayrollPeriodRequest request,
        CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương cần hủy.");
        }

        if (period.Status != PayrollStatus.Draft)
        {
            return Result<PayrollPeriodDto>.Failure(
                "Chỉ được hủy kỳ lương đang ở Bản nháp. Kỳ đã tính hoặc đã gửi duyệt phải giữ nguyên để bảo toàn dấu vết nghiệp vụ.");
        }

        var now = DateTime.UtcNow;
        period.Status = PayrollStatus.Cancelled;
        period.UpdatedAt = now;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var actorId = _currentUser.UserId;
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId,
            CompanyId = period.CompanyId,
            BusinessUnitId = period.BusinessUnitId,
            Action = "Cancel",
            EntityName = nameof(PayrollPeriod),
            EntityId = period.Id.ToString(),
            OldValues = "{\"Status\":\"Draft\"}",
            NewValues = $"{{\"Status\":\"Cancelled\",\"Reason\":{System.Text.Json.JsonSerializer.Serialize(request.Comments?.Trim() ?? "Hủy kỳ lương nháp")}}}",
            CreatedAt = now
        });

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Voided, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) was cancelled by {User}",
            period.Name, period.Id, _currentUser.Username);

        return Result<PayrollPeriodDto>.Success(new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.Payslips.Count,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt));
    }

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

            var earnedSalary = emp.EmploymentType == EmploymentType.PART_TIME
                ? CalculatePartTimeSalary(emp, actualWorkHours, actualShifts)
                : Math.Round(emp.BaseSalary * (actualWorkDays / 22.0m), 0);
            var otherIncomeAdjustments = allowances + bonuses + overtime;
            var totalIncome = earnedSalary + otherIncomeAdjustments + kpiBonus;
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

    public async Task<Result<List<PayrollAdjustmentDto>>> GetAdjustmentsAsync(Guid periodId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<List<PayrollAdjustmentDto>>.Failure("Không tìm thấy kỳ lương.");
        }

        var adjustmentEntities = await _db.PayrollAdjustments
            .AsNoTracking()
            .Where(a => a.PayrollPeriodId == periodId &&
                        (!period.BusinessUnitId.HasValue || a.Employee.BusinessUnitId == period.BusinessUnitId))
            .Include(a => a.Employee)
            .OrderBy(a => a.Employee.EmployeeCode)
            .ToListAsync(ct);
        var adjustments = adjustmentEntities.Select(MapAdjustment).ToList();

        return Result<List<PayrollAdjustmentDto>>.Success(adjustments);
    }

    public async Task<Result<PayrollAdjustmentDto>> AddAdjustmentAsync(Guid periodId, CreatePayrollAdjustmentRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<PayrollAdjustmentDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status >= PayrollStatus.Reviewing)
        {
            return Result<PayrollAdjustmentDto>.Failure(
                "Kỳ lương đã gửi duyệt hoặc khóa, không thể chỉnh sửa khoản lương.");
        }

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.CompanyId == companyId, ct);
        if (emp == null)
        {
            return Result<PayrollAdjustmentDto>.Failure("Không tìm thấy nhân viên.");
        }

        if (period.BusinessUnitId.HasValue && emp.BusinessUnitId != period.BusinessUnitId)
        {
            return Result<PayrollAdjustmentDto>.Failure(
                "Không thể thêm khoản lương: nhân viên không thuộc đơn vị kinh doanh của kỳ lương.");
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return Result<PayrollAdjustmentDto>.Failure("Loại khoản lương không được để trống.");
        }

        if (request.Amount < 0)
        {
            return Result<PayrollAdjustmentDto>.Failure("Số tiền khoản lương không được âm.");
        }

        var adj = new PayrollAdjustment
        {
            PayrollPeriodId = periodId,
            EmployeeId = request.EmployeeId,
            Type = NormalizeComponentCode(request.Type),
            Amount = request.Amount,
            Reason = request.Reason.Trim(),
            CreatedBy = _currentUser.Username ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.PayrollAdjustments.Add(adj);
        await _db.SaveChangesAsync(ct);

        var dto = MapAdjustment(adj, emp.EmployeeCode, emp.FullName);

        return Result<PayrollAdjustmentDto>.Success(dto);
    }

    public async Task<Result<bool>> DeleteAdjustmentAsync(Guid adjustmentId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var adj = await _db.PayrollAdjustments
            .Include(a => a.PayrollPeriod)
            .FirstOrDefaultAsync(
            a => a.Id == adjustmentId && a.PayrollPeriod.CompanyId == companyId, ct);
        if (adj == null)
        {
            return Result<bool>.Failure("Không tìm thấy khoản điều chỉnh.");
        }

        if (adj.PayrollPeriod.Status >= PayrollStatus.Reviewing)
        {
            return Result<bool>.Failure(
                "Kỳ lương đã gửi duyệt hoặc khóa, không thể chỉnh sửa khoản lương.");
        }

        _db.PayrollAdjustments.Remove(adj);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<PaginatedResult<PayslipDto>> GetPayslipsAsync(
        Guid periodId, Guid? departmentId, string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var query = _db.Payslips
            .AsNoTracking()
            .Where(ps => ps.PayrollPeriodId == periodId &&
                         ps.PayrollPeriod.CompanyId == companyId &&
                         (!ps.PayrollPeriod.BusinessUnitId.HasValue ||
                          ps.Employee.BusinessUnitId == ps.PayrollPeriod.BusinessUnitId))
            .Include(ps => ps.PayrollPeriod)
            .Include(ps => ps.Employee)
                .ThenInclude(e => e.Department)
            .AsQueryable();

        if (departmentId.HasValue && departmentId.Value != Guid.Empty)
        {
            query = query.Where(ps => ps.Employee.DepartmentId == departmentId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(ps => ps.Employee.EmployeeCode.ToLower().Contains(s) ||
                                      ps.Employee.FullName.ToLower().Contains(s));
        }

        var count = await query.CountAsync(ct);
        var payslipEntities = await query
            .OrderBy(ps => ps.Employee.EmployeeCode)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = payslipEntities
            .Select(ps => MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee))
            .ToList();

        return new PaginatedResult<PayslipDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<PayslipDto>> GetPayslipByIdAsync(Guid id, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var ps = await _db.Payslips
            .AsNoTracking()
            .Include(p => p.PayrollPeriod)
            .Include(p => p.Employee)
                .ThenInclude(e => e.Department)
            .FirstOrDefaultAsync(p => p.Id == id &&
                                      p.PayrollPeriod.CompanyId == companyId &&
                                      (!p.PayrollPeriod.BusinessUnitId.HasValue ||
                                       p.Employee.BusinessUnitId == p.PayrollPeriod.BusinessUnitId), ct);

        if (ps == null)
        {
            return Result<PayslipDto>.Failure("Không tìm thấy phiếu lương.");
        }

        var dto = MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee);

        return Result<PayslipDto>.Success(dto);
    }

    public async Task<Result<PayrollPeriodDto>> SubmitForReviewAsync(Guid periodId, SubmitPayrollForReviewRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Calculated)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể gửi duyệt kỳ lương đang ở trạng thái {period.Status}. Kỳ lương phải được Tính Lương (Calculated) trước.");
        }

        period.Status = PayrollStatus.Reviewing;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Reviewing,
                Comments = request.Comments ?? "Gửi duyệt kỳ lương",
                ApprovedAt = DateTime.UtcNow
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Open, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) submitted for review by {User}",
            period.Name, period.Id, _currentUser.Username);

        return Result<PayrollPeriodDto>.Success(new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.Payslips.Count,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt));
    }

    public async Task<Result<PayrollPeriodDto>> ApprovePayrollAsync(Guid periodId, ApprovePayrollRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Reviewing && period.Status != PayrollStatus.Calculated)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể phê duyệt kỳ lương đang ở trạng thái {period.Status}.");
        }

        period.Status = PayrollStatus.Approved;
        period.ApprovedAt = DateTime.UtcNow;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Approved,
                Comments = request.Comments ?? "Phê duyệt kỳ lương",
                ApprovedAt = DateTime.UtcNow
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Open, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) approved by {User}",
            period.Name, period.Id, _currentUser.Username);

        return Result<PayrollPeriodDto>.Success(new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.Payslips.Count,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt));
    }

    public async Task<Result<PayrollPeriodDto>> MarkAsPaidAsync(Guid periodId, MarkPayrollPaidRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Approved)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể đánh dấu đã chi cho kỳ lương chưa được phê duyệt (Trạng thái hiện tại: {period.Status}).");
        }

        period.Status = PayrollStatus.Paid;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Paid,
                Comments = request.Comments ?? "Xác nhận đã giải ngân chi lương",
                ApprovedAt = DateTime.UtcNow
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Settled, ct);
        if (_financePostingService is not null && period.TotalNetAmount > 0)
        {
            await _financePostingService.PostAsync(new FinancePostingRequest(
                period.CompanyId,
                period.BusinessUnitId,
                TransactionType.Expense,
                period.TotalNetAmount,
                DateTime.UtcNow,
                nameof(PayrollPeriod),
                period.Id,
                $"Chi lương kỳ {period.Name}",
                period.BusinessDocumentId), ct);
        }
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) marked as PAID by {User}",
            period.Name, period.Id, _currentUser.Username);

        return Result<PayrollPeriodDto>.Success(new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.Payslips.Count,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt));
    }

    public async Task<Result<PayrollPeriodDto>> PublishPayrollAsync(Guid periodId, PublishPayrollRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .Include(p => p.Payslips)
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);

        if (period == null)
        {
            return Result<PayrollPeriodDto>.Failure("Không tìm thấy kỳ lương.");
        }

        if (period.Status != PayrollStatus.Paid && period.Status != PayrollStatus.Approved)
        {
            return Result<PayrollPeriodDto>.Failure($"Không thể phát hành kỳ lương đang ở trạng thái {period.Status}. Kỳ lương phải được Approved hoặc Paid trước khi phát hành.");
        }

        period.Status = PayrollStatus.Published;
        period.UpdatedAt = DateTime.UtcNow;
        period.UpdatedBy = _currentUser.Username ?? "system";

        var publishedTime = DateTime.UtcNow;
        foreach (var ps in period.Payslips)
        {
            ps.Status = PayrollStatus.Published;
            ps.PublishedAt = publishedTime;
            ps.UpdatedAt = publishedTime;
            ps.UpdatedBy = _currentUser.Username ?? "system";
        }

        var approverId = _currentUser.UserId ?? Guid.Empty;
        if (approverId != Guid.Empty)
        {
            _db.PayrollApprovals.Add(new PayrollApproval
            {
                PayrollPeriodId = period.Id,
                ApproverId = approverId,
                Status = PayrollStatus.Published,
                Comments = request.Comments ?? "Phát hành phiếu lương cho toàn bộ nhân sự",
                ApprovedAt = publishedTime
            });
        }

        await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Settled, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Payroll period {PeriodName} ({PeriodId}) PUBLISHED by {User}, {Count} payslips released",
            period.Name, period.Id, _currentUser.Username, period.Payslips.Count);

        return Result<PayrollPeriodDto>.Success(new PayrollPeriodDto(
            period.Id,
            period.CompanyId,
            period.BusinessUnitId,
            period.Name,
            period.StartDate,
            period.EndDate,
            period.Status,
            period.TotalGrossAmount,
            period.TotalNetAmount,
            period.Payslips.Count,
            period.CalculatedAt,
            period.ApprovedAt,
            period.CreatedAt));
    }

    public async Task<Result<List<PayrollApprovalDto>>> GetApprovalsAsync(Guid periodId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period == null)
        {
            return Result<List<PayrollApprovalDto>>.Failure("Không tìm thấy kỳ lương.");
        }

        var approvals = await _db.PayrollApprovals
            .AsNoTracking()
            .Where(a => a.PayrollPeriodId == periodId)
            .Include(a => a.Approver)
            .OrderByDescending(a => a.ApprovedAt)
            .Select(a => new PayrollApprovalDto(
                a.Id,
                a.PayrollPeriodId,
                a.ApproverId,
                a.Approver != null ? a.Approver.FullName : "System",
                a.Status,
                a.Comments,
                a.ApprovedAt))
            .ToListAsync(ct);

        return Result<List<PayrollApprovalDto>>.Success(approvals);
    }

    public async Task<PaginatedResult<PayslipDto>> GetMyPayslipsAsync(int pageIndex, int pageSize, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return new PaginatedResult<PayslipDto>(Array.Empty<PayslipDto>(), 0, pageIndex, pageSize);
        }

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId.Value, ct);
        if (emp == null)
        {
            return new PaginatedResult<PayslipDto>(Array.Empty<PayslipDto>(), 0, pageIndex, pageSize);
        }

        var query = _db.Payslips
            .AsNoTracking()
            .Where(ps => ps.EmployeeId == emp.Id && ps.Status == PayrollStatus.Published && ps.PayrollPeriod.Status == PayrollStatus.Published)
            .Include(ps => ps.PayrollPeriod)
            .Include(ps => ps.Employee)
                .ThenInclude(e => e.Department)
            .OrderByDescending(ps => ps.PayrollPeriod.StartDate);

        var count = await query.CountAsync(ct);
        var payslipEntities = await query
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        var items = payslipEntities
            .Select(ps => MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee))
            .ToList();

        return new PaginatedResult<PayslipDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<PayslipDto>> GetPersonalPayslipAsync(Guid periodId, CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue || userId.Value == Guid.Empty)
        {
            return Result<PayslipDto>.Failure("Chưa xác thực người dùng.");
        }

        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.UserId == userId.Value, ct);
        if (emp == null)
        {
            return Result<PayslipDto>.Failure("Tài khoản của bạn chưa liên kết với hồ sơ nhân viên.");
        }

        var ps = await _db.Payslips
            .AsNoTracking()
            .Include(p => p.PayrollPeriod)
            .Include(p => p.Employee)
                .ThenInclude(e => e.Department)
            .FirstOrDefaultAsync(p => p.PayrollPeriodId == periodId && p.EmployeeId == emp.Id && p.Status == PayrollStatus.Published, ct);

        if (ps == null)
        {
            return Result<PayslipDto>.Failure("Không tìm thấy phiếu lương cá nhân của bạn hoặc phiếu lương chưa được phát hành.");
        }

        var dto = MapPayslip(ps, ps.PayrollPeriod.Name, ps.Employee);

        return Result<PayslipDto>.Success(dto);
    }

    public async Task<Result<PayrollPolicyVersionDto>> GetActivePolicyAsync(CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var policy = await _db.PayrollPolicyVersions
            .AsNoTracking()
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .OrderByDescending(p => p.EffectiveDate)
            .FirstOrDefaultAsync(ct);

        if (policy == null)
        {
            return Result<PayrollPolicyVersionDto>.Failure("Không tìm thấy chính sách lương hiệu lực.");
        }

        var dto = new PayrollPolicyVersionDto(
            policy.Id,
            policy.CompanyId,
            policy.VersionNumber,
            policy.EffectiveDate,
            policy.ConfigJson,
            policy.IsActive,
            policy.CreatedAt);

        return Result<PayrollPolicyVersionDto>.Success(dto);
    }

    public async Task<Result<PayrollPolicyVersionDto>> CreatePolicyVersionAsync(CreatePayrollPolicyVersionRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);

        var policy = new PayrollPolicyVersion
        {
            CompanyId = companyId,
            VersionNumber = request.VersionNumber,
            EffectiveDate = request.EffectiveDate,
            ConfigJson = request.ConfigJson,
            IsActive = true,
            CreatedBy = _currentUser.Username ?? "system",
            CreatedAt = DateTime.UtcNow
        };

        _db.PayrollPolicyVersions.Add(policy);
        await _db.SaveChangesAsync(ct);

        var dto = new PayrollPolicyVersionDto(
            policy.Id,
            policy.CompanyId,
            policy.VersionNumber,
            policy.EffectiveDate,
            policy.ConfigJson,
            policy.IsActive,
            policy.CreatedAt);

        return Result<PayrollPolicyVersionDto>.Success(dto);
    }

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
            totalDeductions);
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
