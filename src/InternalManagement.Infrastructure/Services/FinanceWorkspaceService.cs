using Microsoft.EntityFrameworkCore;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Finance.DTOs;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Common;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

/// <summary>Money facts and their source records commit together in one SaveChanges.</summary>
public sealed partial class FinanceWorkspaceService(
    IFinanceDbContext db,
    ICurrentUserService? currentUser = null,
    ILmsAccessLifecycleService? lmsAccess = null) : IFinanceWorkspaceService
{
    private async Task<Guid?> CompanyAsync(CancellationToken ct)
    {
        if (currentUser?.CompanyId is { } id && id != Guid.Empty) return id;
        var ids = await db.Companies.AsNoTracking().Select(x => x.Id).Take(2).ToListAsync(ct);
        return ids.Count == 1 ? ids[0] : null;
    }

    private IQueryable<T> Scope<T>(IQueryable<T> query, Guid company) where T : class, ITenantScoped
    {
        query = query.Where(x => x.CompanyId == company);
        return currentUser?.BusinessUnitId is { } unit ? query.Where(x => x.BusinessUnitId == unit) : query;
    }

    private async Task<bool> ValidUnit(Guid company, Guid? unit, CancellationToken ct)
    {
        if (currentUser?.BusinessUnitId is { } scopedUnit && unit != scopedUnit) return false;
        return !unit.HasValue || await db.BusinessUnits.AnyAsync(x => x.Id == unit && x.CompanyId == company, ct);
    }

    private static bool Type(string? value, out TransactionType type)
    {
        type = TransactionType.Income;
        if (string.Equals(value, "Income", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Thu", StringComparison.OrdinalIgnoreCase)) return true;
        type = TransactionType.Expense;
        return string.Equals(value, "Expense", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Chi", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Key(string? key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    private static bool ValidKey(string? key) => Key(key) is { Length: > 0 and <= 128 };
    private static DateTime Utc(DateTime? date) => date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc) : DateTime.UtcNow;
    private static decimal NetPaid(StudentReceivable row) => row.PaidAmount - row.RefundedAmount;
    private static string Text(string? text, string fallback) => string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
    private static bool SameCashRequest(decimal storedAmount, decimal amount, DateTime storedDate, DateTime? date, string storedDescription, string? description) =>
        storedAmount == amount && (!date.HasValue || storedDate == Utc(date))
        && (string.IsNullOrWhiteSpace(description) || storedDescription == description.Trim());

    private FinanceTransaction Post(Guid company, Guid? unit, TransactionType type, decimal amount, DateTime at, string source, Guid sourceId, string description)
    {
        var tx = new FinanceTransaction { CompanyId = company, BusinessUnitId = unit, TransactionType = type, Amount = amount, TransactionDate = at, ReferenceType = source, ReferenceId = sourceId, Description = description };
        var document = new BusinessDocument { CompanyId = company, BusinessUnitId = unit, DocumentType = BusinessDocumentType.FinanceTransaction, Status = BusinessDocumentStatus.Settled, SourceEntityType = nameof(FinanceTransaction), SourceEntityId = tx.Id, DocumentNumber = $"FIN-{tx.Id:N}", TotalAmount = amount, IssuedAt = at };
        db.BusinessDocuments.Add(document);
        tx.BusinessDocumentId = document.Id;
        db.FinanceTransactions.Add(tx);
        return tx;
    }

    public async Task<Result<IReadOnlyList<StudentReceivableDto>>> GetReceivablesAsync(CancellationToken ct)
    {
        var c = await CompanyAsync(ct);
        return c is null ? Result<IReadOnlyList<StudentReceivableDto>>.Failure("Không xác định được công ty hiện tại.") : Result<IReadOnlyList<StudentReceivableDto>>.Success(await GetReceivablesForCompany(c.Value, ct));
    }

    private IQueryable<CscaClassStudent> Students(Guid company)
    {
        var query = db.CscaClassStudents.Where(x => x.Class.CompanyId == company && !x.Class.IsDeleted && x.Class.Status != "Cancelled");
        return currentUser?.BusinessUnitId is { } unit ? query.Where(x => x.Class.BusinessUnitId == unit) : query;
    }

    private static StudentReceivable OpeningReceivable(CscaClassStudent student) => new()
    {
        Id = student.Id, CompanyId = student.Class.CompanyId, BusinessUnitId = student.Class.BusinessUnitId,
        StudentId = student.Id, ClassId = student.ClassId,
        DueAmount = Math.Max(0, student.Class.TuitionFee - student.DiscountAmount),
        PaidAmount = student.PaidAmount,
        RefundedAmount = student.PaymentStatus == PaymentStatus.Refunded ? student.PaidAmount : 0,
        DueDate = student.DebtDueDate,
        Status = student.PaymentStatus == PaymentStatus.Cancelled ? "Cancelled" : "Open"
    };

    private static StudentReceivableDto ReceivableDto(StudentReceivable row, CscaClassStudent student)
    {
        var outstanding = row.Status == "Cancelled" ? 0 : Math.Max(0, row.DueAmount - NetPaid(row));
        var status = row.Status == "Cancelled" ? "Cancelled" : outstanding == 0 ? "Paid" : "Open";
        return new(row.Id, row.StudentId, row.ClassId, student.StudentName, student.Class.Name,
            row.DueAmount, NetPaid(row), row.RefundedAmount, outstanding, row.DueDate, status,
            status == "Cancelled" ? "Đã hủy" : outstanding == 0 ? "Đã thu đủ" : "Chưa thu đủ");
    }

    private async Task<List<StudentReceivableDto>> GetReceivablesForCompany(Guid company, CancellationToken ct)
    {
        var students = await Students(company).AsNoTracking().Include(x => x.Class).ToListAsync(ct);
        var rows = await Scope(db.StudentReceivables.AsNoTracking(), company).ToListAsync(ct);
        var byStudent = rows.ToDictionary(x => x.StudentId);
        // Older enrollments remain a read-only projection until the first receipt/refund.
        // Their legacy aggregate finance posting is never re-posted by this service.
        return students.Where(x => x.PaymentStatus != PaymentStatus.Cancelled)
            .Select(s => ReceivableDto(byStudent.GetValueOrDefault(s.Id) ?? OpeningReceivable(s), s))
            .OrderBy(x => x.DueDate).ThenBy(x => x.StudentName).ToList();
    }

    private async Task<(StudentReceivable? Row, CscaClassStudent? Student)> FindReceivable(Guid company, Guid id, CancellationToken ct)
    {
        var row = await Scope(db.StudentReceivables, company).FirstOrDefaultAsync(x => x.Id == id, ct);
        var studentId = row?.StudentId ?? id;
        var student = await Students(company).Include(x => x.Class).ThenInclude(x => x.Course).FirstOrDefaultAsync(x => x.Id == studentId, ct);
        if (student is null || student.PaymentStatus == PaymentStatus.Cancelled) return (null, null);
        row ??= OpeningReceivable(student);
        return (row, student);
    }

    private async Task UpdateStudent(StudentReceivable row, CscaClassStudent student, CancellationToken ct)
    {
        student.PaidAmount = NetPaid(row);
        student.PaymentStatus = student.PaidAmount >= row.DueAmount ? PaymentStatus.Paid : student.PaidAmount > 0 ? PaymentStatus.Partial : row.RefundedAmount > 0 ? PaymentStatus.Refunded : PaymentStatus.Pending;
        row.Status = student.PaymentStatus == PaymentStatus.Paid ? "Paid" : "Open";
        var allocation = await db.ProfitAllocations.FirstOrDefaultAsync(x => x.CompanyId == row.CompanyId && x.ReferenceType == nameof(CscaClass) && x.ReferenceId == row.ClassId, ct);
        if (allocation is not null)
        {
            var otherRevenue = await Students(row.CompanyId).Where(x => x.ClassId == row.ClassId && x.Id != student.Id).SumAsync(x => x.PaidAmount, ct);
            allocation.IncomeAmount = otherRevenue + student.PaidAmount;
            // Expense is owned by the LMS teaching-cost projection.
            allocation.AllocatedAt = DateTime.UtcNow;
        }
        if (lmsAccess is not null)
            await lmsAccess.ReconcileStudentAccessAsync(new LmsAccessEvaluationRequest(row.CompanyId, row.BusinessUnitId, student.Id, student.PartyId, row.ClassId, student.Class.CourseId, student.Class.Course.CourseSourceId, student.StudentName, student.Email, student.PhoneNumber, student.PaidAmount, row.DueAmount, student.PaymentStatus, DateTime.UtcNow, row.Id.ToString("N"), student.StudentCode, student.Class.Code), ct);
    }

    public async Task<Result<StudentReceivableDto>> RecordReceiptAsync(Guid receivableId, RecordStudentReceiptRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0 || !ValidKey(request.IdempotencyKey)) return Result<StudentReceivableDto>.Failure("Cần số tiền thu dương và IdempotencyKey (tối đa 128 ký tự).");
        var c = await CompanyAsync(ct); if (c is null) return Result<StudentReceivableDto>.Failure("Không xác định được công ty hiện tại.");
        var (row, student) = await FindReceivable(c.Value, receivableId, ct);
        if (row is null || student is null || row.Status == "Cancelled") return Result<StudentReceivableDto>.Failure("Không tìm thấy công nợ học viên trong phạm vi được phép.");
        var existing = await Scope(db.StudentReceipts, c.Value).FirstOrDefaultAsync(x => x.IdempotencyKey == Key(request.IdempotencyKey), ct);
        if (existing is not null) return existing.StudentReceivableId == row.Id && SameCashRequest(existing.Amount, request.Amount, existing.ReceivedAt, request.ReceivedAt, existing.Description, request.Description) ? Result<StudentReceivableDto>.Success(ReceivableDto(row, student)) : Result<StudentReceivableDto>.Failure("IdempotencyKey đã dùng cho giao dịch khác.");
        if (request.Amount > row.DueAmount - NetPaid(row)) return Result<StudentReceivableDto>.Failure("Số tiền thu vượt công nợ còn lại.");
        if (!await db.StudentReceivables.AnyAsync(x => x.Id == row.Id, ct)) db.StudentReceivables.Add(row);
        var receipt = new StudentReceipt { CompanyId = c.Value, BusinessUnitId = row.BusinessUnitId, StudentReceivableId = row.Id, Amount = request.Amount, ReceivedAt = Utc(request.ReceivedAt), Description = Text(request.Description, $"Thu học phí {student.StudentName}"), IdempotencyKey = Key(request.IdempotencyKey) };
        receipt.FinanceTransactionId = Post(c.Value, row.BusinessUnitId, TransactionType.Income, receipt.Amount, receipt.ReceivedAt, nameof(StudentReceipt), receipt.Id, receipt.Description).Id;
        db.StudentReceipts.Add(receipt); row.PaidAmount += receipt.Amount;
        await UpdateStudent(row, student, ct);
        await db.SaveChangesAsync(ct);
        return Result<StudentReceivableDto>.Success(ReceivableDto(row, student));
    }

    public async Task<Result<StudentReceivableDto>> RecordRefundAsync(Guid receivableId, RecordStudentRefundRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0 || !ValidKey(request.IdempotencyKey)) return Result<StudentReceivableDto>.Failure("Cần số tiền hoàn dương và IdempotencyKey (tối đa 128 ký tự).");
        var c = await CompanyAsync(ct); if (c is null) return Result<StudentReceivableDto>.Failure("Không xác định được công ty hiện tại.");
        var (row, student) = await FindReceivable(c.Value, receivableId, ct);
        if (row is null || student is null || row.Status == "Cancelled") return Result<StudentReceivableDto>.Failure("Không tìm thấy công nợ học viên trong phạm vi được phép.");
        var existing = await Scope(db.StudentRefunds, c.Value).FirstOrDefaultAsync(x => x.IdempotencyKey == Key(request.IdempotencyKey), ct);
        if (existing is not null) return existing.StudentReceivableId == row.Id && SameCashRequest(existing.Amount, request.Amount, existing.RefundedAt, request.RefundedAt, existing.Description, request.Description) ? Result<StudentReceivableDto>.Success(ReceivableDto(row, student)) : Result<StudentReceivableDto>.Failure("IdempotencyKey đã dùng cho giao dịch khác.");
        if (request.Amount > NetPaid(row)) return Result<StudentReceivableDto>.Failure("Số tiền hoàn vượt số tiền học viên đã nộp.");
        if (!await db.StudentReceivables.AnyAsync(x => x.Id == row.Id, ct)) db.StudentReceivables.Add(row);
        var refund = new StudentRefund { CompanyId = c.Value, BusinessUnitId = row.BusinessUnitId, StudentReceivableId = row.Id, Amount = request.Amount, RefundedAt = Utc(request.RefundedAt), Description = Text(request.Description, $"Hoàn học phí {student.StudentName}"), IdempotencyKey = Key(request.IdempotencyKey) };
        refund.FinanceTransactionId = Post(c.Value, row.BusinessUnitId, TransactionType.Expense, refund.Amount, refund.RefundedAt, nameof(StudentRefund), refund.Id, refund.Description).Id;
        db.StudentRefunds.Add(refund); row.RefundedAmount += refund.Amount;
        await UpdateStudent(row, student, ct);
        await db.SaveChangesAsync(ct);
        return Result<StudentReceivableDto>.Success(ReceivableDto(row, student));
    }

    public async Task<Result<IReadOnlyList<TeacherCostWorkspaceDto>>> GetTeacherCostsAsync(CancellationToken ct)
    {
        var c = await CompanyAsync(ct);
        return c is null ? Result<IReadOnlyList<TeacherCostWorkspaceDto>>.Failure("Không xác định được công ty hiện tại.") : Result<IReadOnlyList<TeacherCostWorkspaceDto>>.Success(await GetTeacherCostsForCompany(c.Value, ct));
    }

    public async Task<Result<StudentReceivableDto>> UpdateReceivableAsync(Guid receivableId, UpdateStudentReceivableRequest request, CancellationToken ct)
    {
        var company = await CompanyAsync(ct);
        if (company is null) return Result<StudentReceivableDto>.Failure("Không xác định được công ty hiện tại.");
        var (row, student) = await FindReceivable(company.Value, receivableId, ct);
        if (row is null || student is null) return Result<StudentReceivableDto>.Failure("Không tìm thấy công nợ học viên.");
        if (row.Status == "Cancelled") return Result<StudentReceivableDto>.Failure("Công nợ đã hủy không thể thay đổi.");
        if (request.Cancelled && NetPaid(row) > 0) return Result<StudentReceivableDto>.Failure("Hãy hoàn hết tiền đã nhận trước khi hủy công nợ.");
        if (!await db.StudentReceivables.AnyAsync(x => x.Id == row.Id, ct)) db.StudentReceivables.Add(row);
        row.DueDate = request.DueDate.HasValue ? Utc(request.DueDate) : null;
        student.DebtDueDate = row.DueDate;
        if (request.Cancelled)
        {
            row.Status = "Cancelled";
            student.PaymentStatus = PaymentStatus.Cancelled;
            if (lmsAccess is not null)
                await lmsAccess.ReconcileStudentAccessAsync(new LmsAccessEvaluationRequest(row.CompanyId, row.BusinessUnitId, student.Id, student.PartyId, row.ClassId, student.Class.CourseId, student.Class.Course.CourseSourceId, student.StudentName, student.Email, student.PhoneNumber, student.PaidAmount, row.DueAmount, student.PaymentStatus, DateTime.UtcNow, row.Id.ToString("N"), student.StudentCode, student.Class.Code), ct);
        }
        await db.SaveChangesAsync(ct);
        return Result<StudentReceivableDto>.Success(ReceivableDto(row, student));
    }

    private async Task<List<TeacherCostWorkspaceDto>> GetTeacherCostsForCompany(Guid company, CancellationToken ct)
    {
        var snapshotQuery = db.CscaTeacherCostSnapshots.AsNoTracking()
            .Where(x => x.CompanyId == company && x.SourceSystem == "CSCA_COURSE" && x.IsCurrent && x.EmployeeId != null);
        if (currentUser?.BusinessUnitId is { } snapshotUnit)
            snapshotQuery = snapshotQuery.Where(x => x.BusinessUnitId == snapshotUnit || x.BusinessUnitId == null);
        var snapshots = (await snapshotQuery.ToListAsync(ct))
            .Where(x => !string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Status, "canceled", StringComparison.OrdinalIgnoreCase)).ToList();
        var advances = await Scope(db.TeacherAdvances.AsNoTracking(), company).ToListAsync(ct);
        var payments = await Scope(db.TeacherPayments.AsNoTracking(), company).ToListAsync(ct);
        var allocations = await Scope(db.TeacherPaymentAllocations.AsNoTracking(), company).ToListAsync(ct);
        var ids = snapshots.Select(x => x.EmployeeId!.Value).Concat(advances.Select(x => x.EmployeeId)).Concat(payments.Select(x => x.EmployeeId)).Distinct().ToList();
        // Snapshot scope is authoritative for imported CSCA teaching costs; a
        // legacy employee may not yet carry the same business-unit assignment.
        var employees = await db.Employees.AsNoTracking()
            .Where(x => x.CompanyId == company && ids.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        return ids.Where(employees.ContainsKey).Select(id =>
        {
            var facts = snapshots.Where(x => x.EmployeeId == id).ToList();
            var issued = advances.Where(x => x.EmployeeId == id && x.Status != "Cancelled").ToList();
            var earned = facts.Sum(x => x.AccruedAmount);
            var settled = facts.Sum(x => Math.Max(IsSourcePaid(x.PaymentStatus, x.PaidAt) ? x.AccruedAmount : 0, allocations.Where(a => a.TeacherCostSnapshotId == x.Id).Sum(a => a.Amount)));
            return new TeacherCostWorkspaceDto(id, employees[id].FullName, earned, facts.Sum(x => x.ProjectedAmount ?? x.AccruedAmount), settled,
                issued.Sum(x => x.Amount), issued.Sum(x => x.ReturnedAmount), issued.Sum(x => x.Amount - x.ReturnedAmount - x.AppliedAmount), Math.Max(0, earned - settled),
                issued.OrderBy(x => x.IssuedAt).Select(x => new TeacherAdvanceDto(x.Id, x.Amount, x.ReturnedAmount, x.AppliedAmount, x.Amount - x.ReturnedAmount - x.AppliedAmount, x.IssuedAt, x.Description)).ToList(), issued.Sum(x => x.AppliedAmount));
        }).OrderBy(x => x.TeacherName).ToList();
    }

    private async Task<TeacherCostWorkspaceDto> Teacher(Guid company, Guid employee, CancellationToken ct) => (await GetTeacherCostsForCompany(company, ct)).First(x => x.EmployeeId == employee);

    private static bool IsSourcePaid(string? status, DateTime? paidAt) => paidAt.HasValue || string.Equals(status, "paid", StringComparison.OrdinalIgnoreCase) || string.Equals(status, "settled", StringComparison.OrdinalIgnoreCase);

    public async Task<Result<TeacherCostWorkspaceDto>> IssueAdvanceAsync(IssueTeacherAdvanceRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0 || !ValidKey(request.IdempotencyKey)) return Result<TeacherCostWorkspaceDto>.Failure("Cần số tiền ứng dương và IdempotencyKey.");
        var company = await CompanyAsync(ct); if (company is null) return Result<TeacherCostWorkspaceDto>.Failure("Không xác định được công ty hiện tại.");
        var employee = await Scope(db.Employees, company.Value).FirstOrDefaultAsync(x => x.Id == request.EmployeeId && !x.IsDeleted, ct);
        var unit = request.BusinessUnitId ?? employee?.BusinessUnitId ?? currentUser?.BusinessUnitId;
        if (employee is null || !await ValidUnit(company.Value, unit, ct)) return Result<TeacherCostWorkspaceDto>.Failure("Giáo viên/đơn vị không thuộc phạm vi được phép.");
        var existing = await Scope(db.TeacherAdvances, company.Value).FirstOrDefaultAsync(x => x.IdempotencyKey == Key(request.IdempotencyKey), ct);
        if (existing is not null) return existing.EmployeeId == request.EmployeeId && existing.BusinessUnitId == unit && SameCashRequest(existing.Amount, request.Amount, existing.IssuedAt, request.IssuedAt, existing.Description, request.Description) ? Result<TeacherCostWorkspaceDto>.Success(await Teacher(company.Value, employee.Id, ct)) : Result<TeacherCostWorkspaceDto>.Failure("IdempotencyKey đã dùng cho giao dịch khác.");
        var advance = new TeacherAdvance { CompanyId = company.Value, BusinessUnitId = unit, EmployeeId = employee.Id, Amount = request.Amount, IssuedAt = Utc(request.IssuedAt), Description = Text(request.Description, $"Ứng tiền giáo viên {employee.FullName}"), IdempotencyKey = Key(request.IdempotencyKey) };
        db.TeacherAdvances.Add(advance);
        Post(company.Value, unit, TransactionType.Expense, advance.Amount, advance.IssuedAt, nameof(TeacherAdvance), advance.Id, advance.Description);
        await db.SaveChangesAsync(ct);
        return Result<TeacherCostWorkspaceDto>.Success(await Teacher(company.Value, employee.Id, ct));
    }

    public async Task<Result<TeacherCostWorkspaceDto>> ReturnAdvanceAsync(Guid advanceId, ReturnTeacherAdvanceRequest request, CancellationToken ct)
    {
        if (request.Amount <= 0 || !ValidKey(request.IdempotencyKey)) return Result<TeacherCostWorkspaceDto>.Failure("Cần số tiền hoàn ứng dương và IdempotencyKey.");
        var company = await CompanyAsync(ct); if (company is null) return Result<TeacherCostWorkspaceDto>.Failure("Không xác định được công ty hiện tại.");
        var advance = await Scope(db.TeacherAdvances, company.Value).FirstOrDefaultAsync(x => x.Id == advanceId && x.Status != "Cancelled", ct);
        if (advance is null) return Result<TeacherCostWorkspaceDto>.Failure("Không tìm thấy khoản ứng trong phạm vi được phép.");
        var existing = await Scope(db.TeacherAdvanceReturns, company.Value).FirstOrDefaultAsync(x => x.IdempotencyKey == Key(request.IdempotencyKey), ct);
        if (existing is not null) return existing.TeacherAdvanceId == advanceId && SameCashRequest(existing.Amount, request.Amount, existing.ReturnedAt, request.ReturnedAt, existing.Description, request.Description) ? Result<TeacherCostWorkspaceDto>.Success(await Teacher(company.Value, advance.EmployeeId, ct)) : Result<TeacherCostWorkspaceDto>.Failure("IdempotencyKey đã dùng cho giao dịch khác.");
        if (request.Amount > advance.Amount - advance.ReturnedAmount - advance.AppliedAmount) return Result<TeacherCostWorkspaceDto>.Failure("Số tiền hoàn vượt phần ứng còn lại; phần đã trừ vào công không thể hoàn lại.");
        var returned = new TeacherAdvanceReturn { CompanyId = company.Value, BusinessUnitId = advance.BusinessUnitId, TeacherAdvanceId = advanceId, Amount = request.Amount, ReturnedAt = Utc(request.ReturnedAt), Description = Text(request.Description, "Hoàn ứng giáo viên"), IdempotencyKey = Key(request.IdempotencyKey) };
        returned.FinanceTransactionId = Post(company.Value, advance.BusinessUnitId, TransactionType.Income, returned.Amount, returned.ReturnedAt, nameof(TeacherAdvanceReturn), returned.Id, returned.Description).Id;
        db.TeacherAdvanceReturns.Add(returned); advance.ReturnedAmount += request.Amount;
        advance.Status = advance.Amount == advance.ReturnedAmount + advance.AppliedAmount ? "Settled" : "Open";
        await db.SaveChangesAsync(ct);
        return Result<TeacherCostWorkspaceDto>.Success(await Teacher(company.Value, advance.EmployeeId, ct));
    }

    public async Task<Result<TeacherCostWorkspaceDto>> PayTeacherAsync(PayTeacherRequest request, CancellationToken ct)
    {
        if (request.Amount < 0 || request.ApplyAdvanceAmount < 0 || request.Amount + request.ApplyAdvanceAmount <= 0 || !ValidKey(request.IdempotencyKey)) return Result<TeacherCostWorkspaceDto>.Failure("Cần số tiền thanh toán/khấu trừ ứng hợp lệ và IdempotencyKey.");
        var company = await CompanyAsync(ct); if (company is null) return Result<TeacherCostWorkspaceDto>.Failure("Không xác định được công ty hiện tại.");
        var employee = await Scope(db.Employees, company.Value).FirstOrDefaultAsync(x => x.Id == request.EmployeeId && !x.IsDeleted, ct);
        var unit = request.BusinessUnitId ?? employee?.BusinessUnitId ?? currentUser?.BusinessUnitId;
        if (employee is null || !await ValidUnit(company.Value, unit, ct)) return Result<TeacherCostWorkspaceDto>.Failure("Giáo viên/đơn vị không thuộc phạm vi được phép.");
        var existing = await Scope(db.TeacherPayments, company.Value).FirstOrDefaultAsync(x => x.IdempotencyKey == Key(request.IdempotencyKey), ct);
        if (existing is not null) return existing.EmployeeId == request.EmployeeId && existing.BusinessUnitId == unit && SameCashRequest(existing.Amount, request.Amount, existing.PaidAt, request.PaidAt, existing.Description, request.Description) && existing.AdvanceAppliedAmount == request.ApplyAdvanceAmount ? Result<TeacherCostWorkspaceDto>.Success(await Teacher(company.Value, employee.Id, ct)) : Result<TeacherCostWorkspaceDto>.Failure("IdempotencyKey đã dùng cho giao dịch khác.");
        var costs = (await GetTeacherCostsForCompany(company.Value, ct)).FirstOrDefault(x => x.EmployeeId == employee.Id);
        if (costs is null || request.Amount + request.ApplyAdvanceAmount > costs.UnpaidEarned) return Result<TeacherCostWorkspaceDto>.Failure("Tổng tiền và phần trừ ứng vượt công giáo viên còn phải trả.");
        var advances = await Scope(db.TeacherAdvances, company.Value).Where(x => x.EmployeeId == employee.Id && x.Status == "Open").OrderBy(x => x.IssuedAt).ThenBy(x => x.Id).ToListAsync(ct);
        if (request.ApplyAdvanceAmount > advances.Sum(x => x.Amount - x.ReturnedAmount - x.AppliedAmount)) return Result<TeacherCostWorkspaceDto>.Failure("Phần trừ ứng vượt số ứng còn lại.");
        var toApply = request.ApplyAdvanceAmount;
        foreach (var advance in advances) { var applied = Math.Min(toApply, advance.Amount - advance.ReturnedAmount - advance.AppliedAmount); advance.AppliedAmount += applied; toApply -= applied; advance.Status = advance.Amount == advance.ReturnedAmount + advance.AppliedAmount ? "Settled" : "Open"; }
        var payment = new TeacherPayment { CompanyId = company.Value, BusinessUnitId = unit, EmployeeId = employee.Id, Amount = request.Amount, AdvanceAppliedAmount = request.ApplyAdvanceAmount, PaidAt = Utc(request.PaidAt), Description = Text(request.Description, $"Thanh toán công giáo viên {employee.FullName}"), IdempotencyKey = Key(request.IdempotencyKey) };
        var facts = (await Scope(db.CscaTeacherCostSnapshots, company.Value).Where(x => x.SourceSystem == "CSCA_COURSE" && x.IsCurrent && x.EmployeeId == employee.Id).OrderBy(x => x.StartTime).ThenBy(x => x.Id).ToListAsync(ct))
            .Where(x => !string.Equals(x.Status, "cancelled", StringComparison.OrdinalIgnoreCase) && !string.Equals(x.Status, "canceled", StringComparison.OrdinalIgnoreCase)).ToList();
        var oldAllocations = await Scope(db.TeacherPaymentAllocations, company.Value).ToListAsync(ct);
        var toAllocate = request.Amount + request.ApplyAdvanceAmount;
        foreach (var fact in facts)
        {
            var alreadySettled = Math.Max(IsSourcePaid(fact.PaymentStatus, fact.PaidAt) ? fact.AccruedAmount : 0, oldAllocations.Where(x => x.TeacherCostSnapshotId == fact.Id).Sum(x => x.Amount));
            var allocated = Math.Min(toAllocate, Math.Max(0, fact.AccruedAmount - alreadySettled));
            if (allocated == 0) continue;
            db.TeacherPaymentAllocations.Add(new TeacherPaymentAllocation { CompanyId = company.Value, BusinessUnitId = fact.BusinessUnitId, TeacherPaymentId = payment.Id, TeacherCostSnapshotId = fact.Id, Amount = allocated });
            fact.LocalSettlementVersion++;
            toAllocate -= allocated;
        }
        if (toAllocate > 0) throw new InvalidOperationException("Teaching cost changed during settlement; reload and retry.");
        if (payment.Amount > 0) payment.FinanceTransactionId = Post(company.Value, unit, TransactionType.Expense, payment.Amount, payment.PaidAt, nameof(TeacherPayment), payment.Id, payment.Description).Id;
        db.TeacherPayments.Add(payment);
        await db.SaveChangesAsync(ct);
        return Result<TeacherCostWorkspaceDto>.Success(await Teacher(company.Value, employee.Id, ct));
    }

    public async Task<Result<IReadOnlyList<PlannedCashMovementDto>>> GetPlannedMovementsAsync(CancellationToken ct)
    {
        var company = await CompanyAsync(ct);
        return company is null ? Result<IReadOnlyList<PlannedCashMovementDto>>.Failure("Không xác định được công ty hiện tại.") : Result<IReadOnlyList<PlannedCashMovementDto>>.Success((await Scope(db.PlannedCashMovements.AsNoTracking(), company.Value).OrderBy(x => x.PlannedDate).ToListAsync(ct)).Select(ToDto).ToList());
    }

    public async Task<Result<PlannedCashMovementDto>> CreatePlannedMovementAsync(CreatePlannedCashMovementRequest request, CancellationToken ct)
    {
        if (!Type(request.TransactionType, out var type) || request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 300 || !ValidKey(request.IdempotencyKey)) return Result<PlannedCashMovementDto>.Failure("Cần loại Thu/Chi, số tiền dương, nội dung và IdempotencyKey hợp lệ.");
        if (request.Recurrence is not ("None" or "Monthly") || request.RecurrenceCount < 1 || request.RecurrenceCount > 24 || (request.Recurrence == "None" && request.RecurrenceCount != 1)) return Result<PlannedCashMovementDto>.Failure("Lặp lại chỉ hỗ trợ None hoặc Monthly, từ 1 đến 24 lần.");
        var company = await CompanyAsync(ct); if (company is null) return Result<PlannedCashMovementDto>.Failure("Không xác định được công ty hiện tại.");
        var unit = request.BusinessUnitId ?? currentUser?.BusinessUnitId;
        if (!await ValidUnit(company.Value, unit, ct)) return Result<PlannedCashMovementDto>.Failure("Đơn vị không thuộc phạm vi được phép.");
        var key = Key(request.IdempotencyKey)!;
        var existing = await Scope(db.PlannedCashMovements, company.Value).FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            var count = await Scope(db.PlannedCashMovements, company.Value).CountAsync(x => x.IdempotencyKey == key || (x.IdempotencyKey != null && x.IdempotencyKey.StartsWith(key + ":")), ct);
            return existing.Amount == request.Amount && existing.Title == request.Title.Trim() && existing.PlannedDate == Utc(request.PlannedDate) && existing.TransactionType == type && existing.Recurrence == request.Recurrence && count == request.RecurrenceCount ? Result<PlannedCashMovementDto>.Success(ToDto(existing)) : Result<PlannedCashMovementDto>.Failure("IdempotencyKey đã dùng cho kế hoạch khác.");
        }
        PlannedCashMovement? first = null;
        for (var i = 0; i < request.RecurrenceCount; i++)
        {
            var row = new PlannedCashMovement { CompanyId = company.Value, BusinessUnitId = unit, PlannedDate = Utc(request.PlannedDate).AddMonths(i), TransactionType = type, Amount = request.Amount, Title = request.Title.Trim(), Notes = request.Notes, Recurrence = request.Recurrence, IdempotencyKey = i == 0 ? key : $"{key}:{i}" };
            db.PlannedCashMovements.Add(row); first ??= row;
        }
        await db.SaveChangesAsync(ct);
        return Result<PlannedCashMovementDto>.Success(ToDto(first!));
    }

    public Task<Result<PlannedCashMovementDto>> SettlePlannedMovementAsync(Guid id, CancellationToken ct) => SetPlan(id, "Settled", ct);
    public Task<Result<PlannedCashMovementDto>> CancelPlannedMovementAsync(Guid id, CancellationToken ct) => SetPlan(id, "Cancelled", ct);

    private async Task<Result<PlannedCashMovementDto>> SetPlan(Guid id, string status, CancellationToken ct)
    {
        var company = await CompanyAsync(ct); if (company is null) return Result<PlannedCashMovementDto>.Failure("Không xác định được công ty hiện tại.");
        var plan = await Scope(db.PlannedCashMovements, company.Value).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan is null) return Result<PlannedCashMovementDto>.Failure("Không tìm thấy kế hoạch trong phạm vi được phép.");
        if (plan.Status == status) return Result<PlannedCashMovementDto>.Success(ToDto(plan));
        if (plan.Status != "Planned") return Result<PlannedCashMovementDto>.Failure("Kế hoạch đã quyết toán/hủy không thể đổi trạng thái.");
        if (status == "Settled")
        {
            plan.SettledAt = DateTime.UtcNow;
            plan.SettledTransactionId = Post(company.Value, plan.BusinessUnitId, plan.TransactionType, plan.Amount, plan.SettledAt.Value, nameof(PlannedCashMovement), plan.Id, plan.Title).Id;
        }
        plan.Status = status;
        await db.SaveChangesAsync(ct);
        return Result<PlannedCashMovementDto>.Success(ToDto(plan));
    }

    private static PlannedCashMovementDto ToDto(PlannedCashMovement row) => new(row.Id, row.PlannedDate, row.TransactionType == TransactionType.Income ? "Income" : "Expense", row.Amount, row.Title, row.Notes, row.Recurrence, row.Status, row.SettledTransactionId);
}
