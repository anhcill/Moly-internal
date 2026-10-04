using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class PayrollService
{
    public async Task<Result<List<PayrollWorkEntryDto>>> GetWorkEntriesAsync(Guid periodId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period is null)
            return Result<List<PayrollWorkEntryDto>>.Failure("Không tìm thấy kỳ lương.");

        var entries = await _db.PayrollWorkEntries.IgnoreQueryFilters().AsNoTracking()
            .Include(w => w.Employee)
            .Where(w => w.PayrollPeriodId == periodId && w.CompanyId == companyId)
            .OrderBy(w => w.WorkDate)
            .ThenBy(w => w.ReferenceCode)
            .ToListAsync(ct);
        return Result<List<PayrollWorkEntryDto>>.Success(entries.Select(MapWorkEntry).ToList());
    }

    public async Task<Result<PayrollWorkEntryDto>> AddWorkEntryAsync(
        Guid periodId, CreatePayrollWorkEntryRequest request, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .FirstOrDefaultAsync(p => p.Id == periodId && p.CompanyId == companyId, ct);
        if (period is null)
            return Result<PayrollWorkEntryDto>.Failure("Không tìm thấy kỳ lương.");
        if (period.Status >= PayrollStatus.Reviewing)
            return Result<PayrollWorkEntryDto>.Failure("Kỳ lương đã gửi duyệt hoặc khóa, không thể thêm đầu việc.");

        var workType = request.WorkType?.Trim().ToUpperInvariant();
        var referenceCode = request.ReferenceCode?.Trim().ToUpperInvariant();
        var title = request.Title?.Trim();
        if (workType is null || !PayrollWorkTypes.All.Contains(workType))
            return Result<PayrollWorkEntryDto>.Failure("Loại đầu việc không hợp lệ.");
        if (string.IsNullOrWhiteSpace(referenceCode) || referenceCode.Length > 100 ||
            string.IsNullOrWhiteSpace(title) || title.Length > 300)
            return Result<PayrollWorkEntryDto>.Failure("Cần nhập mã tham chiếu và tên đầu việc hợp lệ.");
        if (request.Quantity <= 0 || request.Quantity > 1_000_000 ||
            decimal.Round(request.Quantity, 2) != request.Quantity ||
            request.UnitRate <= 0 || request.UnitRate > 1_000_000_000_000m ||
            decimal.Round(request.UnitRate, 2) != request.UnitRate ||
            request.WorkDate > period.EndDate)
            return Result<PayrollWorkEntryDto>.Failure("Số lượng, đơn giá hoặc ngày hoàn thành không hợp lệ.");
        if (request.EvidenceUrl?.Length > 1000)
            return Result<PayrollWorkEntryDto>.Failure("Đường dẫn chứng từ quá dài.");

        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.CompanyId == companyId &&
                !e.IsDeleted && e.Status == "Active", ct);
        if (employee is null)
            return Result<PayrollWorkEntryDto>.Failure("Nhân sự không tồn tại hoặc không còn làm việc.");
        if (period.BusinessUnitId.HasValue && employee.BusinessUnitId != period.BusinessUnitId)
            return Result<PayrollWorkEntryDto>.Failure("Nhân sự không thuộc đơn vị kinh doanh của kỳ lương.");

        var duplicate = await _db.PayrollWorkEntries.AnyAsync(w =>
            w.CompanyId == companyId && w.EmployeeId == employee.Id &&
            w.WorkType == workType && w.ReferenceCode == referenceCode && !w.IsVoided, ct);
        if (duplicate)
            return Result<PayrollWorkEntryDto>.Failure("Đầu việc này đã được ghi nhận trả công cho nhân sự.");

        var amount = Math.Round(request.Quantity * request.UnitRate, 2, MidpointRounding.AwayFromZero);
        if (amount > 9_999_999_999_999_999.99m)
            return Result<PayrollWorkEntryDto>.Failure("Thành tiền vượt giới hạn cho phép.");
        var entry = new PayrollWorkEntry
        {
            CompanyId = companyId,
            BusinessUnitId = period.BusinessUnitId,
            PayrollPeriodId = period.Id,
            EmployeeId = employee.Id,
            WorkType = workType,
            ReferenceCode = referenceCode,
            Title = title,
            EvidenceUrl = string.IsNullOrWhiteSpace(request.EvidenceUrl) ? null : request.EvidenceUrl.Trim(),
            WorkDate = request.WorkDate,
            Quantity = request.Quantity,
            UnitRate = request.UnitRate,
            Amount = amount,
            CreatedBy = _currentUser.Username ?? "system"
        };

        if (period.Status == PayrollStatus.Calculated)
        {
            var slip = await _db.Payslips.FirstOrDefaultAsync(p =>
                p.PayrollPeriodId == period.Id && p.EmployeeId == employee.Id, ct);
            if (slip is null)
                return Result<PayrollWorkEntryDto>.Failure("Nhân sự chưa có phiếu lương trong kỳ; hãy tính lại kỳ lương trước khi thêm đầu việc.");
            ApplyWorkDelta(period, slip, amount);
            await SyncPayrollDocumentAsync(period, BusinessDocumentStatus.Open, ct);
        }

        _db.PayrollWorkEntries.Add(entry);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<PayrollWorkEntryDto>.Failure("Kỳ lương vừa thay đổi; vui lòng tải lại và nhập lại đầu việc.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return Result<PayrollWorkEntryDto>.Failure("Mã đầu việc này đã được dùng để trả công cho nhân sự.");
        }

        return Result<PayrollWorkEntryDto>.Success(MapWorkEntry(entry, employee.EmployeeCode, employee.FullName));
    }

    public async Task<Result<bool>> VoidWorkEntryAsync(Guid entryId, CancellationToken ct)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var entry = await _db.PayrollWorkEntries.IgnoreQueryFilters()
            .Include(w => w.PayrollPeriod)
            .FirstOrDefaultAsync(w => w.Id == entryId && w.CompanyId == companyId, ct);
        if (entry is null)
            return Result<bool>.Failure("Không tìm thấy đầu việc.");
        if (entry.PayrollPeriod.Status >= PayrollStatus.Reviewing)
            return Result<bool>.Failure("Kỳ lương đã gửi duyệt hoặc khóa, không thể hủy đầu việc.");
        if (entry.IsVoided)
            return Result<bool>.Success(true);

        if (entry.PayrollPeriod.Status == PayrollStatus.Calculated)
        {
            var slip = await _db.Payslips.FirstOrDefaultAsync(p =>
                p.PayrollPeriodId == entry.PayrollPeriodId && p.EmployeeId == entry.EmployeeId, ct);
            if (slip is null || slip.WorkEarnings < entry.Amount)
                return Result<bool>.Failure("Phiếu lương không khớp khoản công việc; hãy tính lại kỳ lương.");
            ApplyWorkDelta(entry.PayrollPeriod, slip, -entry.Amount);
            await SyncPayrollDocumentAsync(entry.PayrollPeriod, BusinessDocumentStatus.Open, ct);
        }

        entry.IsVoided = true;
        entry.VoidedAt = DateTime.UtcNow;
        entry.VoidedBy = _currentUser.Username ?? "system";
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<bool>.Failure("Kỳ lương vừa thay đổi; vui lòng tải lại trước khi hủy đầu việc.");
        }
        return Result<bool>.Success(true);
    }

    private static void ApplyWorkDelta(PayrollPeriod period, Payslip slip, decimal delta)
    {
        var previousNet = slip.NetSalary;
        slip.WorkEarnings += delta;
        slip.GrossSalary += delta;
        slip.TotalIncome += delta;
        slip.NetSalary = Math.Max(0, slip.TotalIncome - slip.TotalDeductions);
        slip.UpdatedAt = DateTime.UtcNow;
        period.TotalGrossAmount += delta;
        period.TotalNetAmount += slip.NetSalary - previousNet;
        period.UpdatedAt = DateTime.UtcNow;
    }

    private static PayrollWorkEntryDto MapWorkEntry(PayrollWorkEntry entry) =>
        MapWorkEntry(entry, entry.Employee.EmployeeCode, entry.Employee.FullName);

    private static PayrollWorkEntryDto MapWorkEntry(PayrollWorkEntry entry, string employeeCode, string employeeName) =>
        new(entry.Id, entry.PayrollPeriodId, entry.EmployeeId, employeeCode, employeeName,
            entry.WorkType, entry.ReferenceCode, entry.Title, entry.EvidenceUrl, entry.WorkDate,
            entry.Quantity, entry.UnitRate, entry.Amount, entry.IsVoided, entry.CreatedAt);
}
