using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class CscaService
{
    public async Task<Result<CscaStudentDto>> EnrollStudentAsync(Guid classId, EnrollStudentRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStudentDto>.Failure("Không tìm thấy lớp học CSCA.");
        var cls = await _db.CscaClasses
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .Include(c => c.Course)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted, ct);

        if (cls == null)
        {
            return Result<CscaStudentDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        if (string.IsNullOrWhiteSpace(request.StudentName))
        {
            return Result<CscaStudentDto>.Failure("Tên học viên không được để trống.");
        }
        if (request.PaidAmount < 0)
        {
            return Result<CscaStudentDto>.Failure("Số tiền đã thanh toán không được âm.");
        }
        if (request.DiscountAmount < 0 || request.DiscountAmount > cls.TuitionFee)
        {
            return Result<CscaStudentDto>.Failure($"Mức giảm giá phải từ 0 đến {cls.TuitionFee:N0} đ.");
        }

        var email = CleanStudentContact(request.Email);
        var phoneNumber = CleanStudentContact(request.PhoneNumber);
        var duplicateError = ValidateUniqueStudentContact(cls.Students, null, email, phoneNumber);
        if (duplicateError is not null)
        {
            return Result<CscaStudentDto>.Failure(duplicateError);
        }

        var student = new CscaClassStudent
        {
            ClassId = classId,
            StudentName = request.StudentName.Trim(),
            Age = request.Age,
            Hometown = request.Hometown?.Trim(),
            Email = email,
            PhoneNumber = phoneNumber,
            DiscountAmount = request.DiscountAmount,
            DiscountNote = request.DiscountNote?.Trim(),
            PaidAmount = request.PaidAmount,
            PaymentStatus = request.PaymentStatus,
            DebtDueDate = request.DebtDueDate,
            JoinedAt = DateTime.UtcNow,
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System"
        };

        // Keep the loaded aggregate in sync before recalculating its allocation.
        // Otherwise the newly added student is not present in cls.Students yet.
        cls.Students.Add(student);
        _db.CscaClassStudents.Add(student);

        await SyncStudentDataLinksAsync(student, cls, ct);
        await QueueLmsAccessLifecycleAsync(student, cls, ct);

        // Sync ProfitAllocation for this class
        await SyncClassProfitAllocationAsync(cls, ct);

        await _db.SaveChangesAsync(ct);

        var dto = new CscaStudentDto
        {
            Id = student.Id,
            ClassId = student.ClassId,
            StudentName = student.StudentName,
            Age = student.Age,
            Hometown = student.Hometown,
            Email = student.Email,
            PhoneNumber = student.PhoneNumber,
            DiscountAmount = student.DiscountAmount,
            DiscountNote = student.DiscountNote,
            PayableAmount = GetPayableTuition(cls.TuitionFee, student.DiscountAmount),
            PaidAmount = student.PaidAmount,
            PaymentStatus = student.PaymentStatus,
            DebtDueDate = student.DebtDueDate,
            JoinedAt = student.JoinedAt,
            Notes = student.Notes
        };

        return Result<CscaStudentDto>.Success(dto);
    }

    public async Task<Result<CscaStudentDto>> UpdateStudentPaymentAsync(
        Guid classId, Guid studentId, UpdateStudentPaymentRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStudentDto>.Failure("Không tìm thấy lớp học CSCA.");
        var student = await _db.CscaClassStudents
            .Include(s => s.Class)
                .ThenInclude(c => c.Students)
            .Include(s => s.Class)
                .ThenInclude(c => c.Staff)
            .Include(s => s.Class)
                .ThenInclude(c => c.Course)
            .FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId, ct);

        if (student == null)
        {
            return Result<CscaStudentDto>.Failure("Không tìm thấy thông tin học viên trong lớp.");
        }
        if (request.PaidAmount < 0)
        {
            return Result<CscaStudentDto>.Failure("Số tiền đã thanh toán không được âm.");
        }
        if (request.DiscountAmount < 0 || request.DiscountAmount > student.Class.TuitionFee)
        {
            return Result<CscaStudentDto>.Failure($"Mức giảm giá phải từ 0 đến {student.Class.TuitionFee:N0} đ.");
        }

        var updatedEmail = request.Email is null ? student.Email : CleanStudentContact(request.Email);
        var updatedPhoneNumber = request.PhoneNumber is null ? student.PhoneNumber : CleanStudentContact(request.PhoneNumber);
        var duplicateError = ValidateUniqueStudentContact(
            student.Class.Students, student.Id, updatedEmail, updatedPhoneNumber);
        if (duplicateError is not null)
        {
            return Result<CscaStudentDto>.Failure(duplicateError);
        }

        student.PaidAmount = request.PaidAmount;
        student.PaymentStatus = request.PaymentStatus;
        student.DiscountAmount = request.DiscountAmount;
        student.DiscountNote = request.DiscountNote?.Trim();
        student.DebtDueDate = request.DebtDueDate;
        if (request.StudentName != null)
        {
            if (string.IsNullOrWhiteSpace(request.StudentName))
                return Result<CscaStudentDto>.Failure("Tên học viên không được để trống.");
            student.StudentName = request.StudentName.Trim();
        }
        if (request.Email != null)
            student.Email = updatedEmail;
        if (request.PhoneNumber != null)
            student.PhoneNumber = updatedPhoneNumber;
        if (request.Age.HasValue)
            student.Age = request.Age;
        if (request.Hometown != null)
            student.Hometown = request.Hometown.Trim();
        if (request.Notes != null)
        {
            student.Notes = request.Notes.Trim();
        }
        student.UpdatedAt = DateTime.UtcNow;
        student.UpdatedBy = _currentUser.Username ?? "System";

        if (student.Class != null)
        {
            await SyncStudentDataLinksAsync(student, student.Class, ct);
            await QueueLmsAccessLifecycleAsync(student, student.Class, ct);
        }

        if (student.Class != null)
        {
            await SyncClassProfitAllocationAsync(student.Class, ct);
        }

        await _db.SaveChangesAsync(ct);

        var dto = new CscaStudentDto
        {
            Id = student.Id,
            ClassId = student.ClassId,
            StudentName = student.StudentName,
            Age = student.Age,
            Hometown = student.Hometown,
            Email = student.Email,
            PhoneNumber = student.PhoneNumber,
            DiscountAmount = student.DiscountAmount,
            DiscountNote = student.DiscountNote,
            PayableAmount = GetPayableTuition(student.Class!.TuitionFee, student.DiscountAmount),
            PaidAmount = student.PaidAmount,
            PaymentStatus = student.PaymentStatus,
            DebtDueDate = student.DebtDueDate,
            JoinedAt = student.JoinedAt,
            Notes = student.Notes
        };

        return Result<CscaStudentDto>.Success(dto);
    }

    public async Task<Result<bool>> RemoveStudentAsync(Guid classId, Guid studentId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA.");
        var student = await _db.CscaClassStudents
            .Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == classId, ct);

        if (student == null)
        {
            return Result<bool>.Failure("Không tìm thấy học viên trong lớp.");
        }

        var hasLmsAccessHistory = await _db.LmsAccessGrants
            .AnyAsync(grant => grant.CscaClassStudentId == student.Id, ct);
        if (hasLmsAccessHistory)
        {
            return Result<bool>.Failure(
                "Học viên đã có quyền LMS/audit. Hãy cập nhật trạng thái thanh toán thành Cancelled hoặc Refunded để thu hồi quyền thay vì xóa.");
        }

        var cls = student.Class;
        _db.CscaClassStudents.Remove(student);

        if (cls != null)
        {
            cls.Students.Remove(student);
            await SyncClassProfitAllocationAsync(cls, ct);
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    private async Task SyncStudentDataLinksAsync(CscaClassStudent student, CscaClass cls, CancellationToken ct)
    {
        if (_partyResolver is not null)
        {
            var party = await _partyResolver.ResolveAsync(new PartyResolutionRequest(
                cls.CompanyId,
                cls.BusinessUnitId,
                PartyType.Individual,
                PartyRole.Student,
                student.StudentName,
                student.Email,
                student.PhoneNumber,
                "CSCA_CLASS_STUDENT",
                student.Id.ToString("N")), ct);
            student.PartyId = party.Id;
        }

        if (_documentRegistry is not null)
        {
            var document = await _documentRegistry.RegisterAsync(new BusinessDocumentRegistration(
                cls.CompanyId,
                cls.BusinessUnitId,
                BusinessDocumentType.CscaEnrollment,
                nameof(CscaClassStudent),
                student.Id,
                $"CSCA-ENROLL-{student.Id:N}",
                student.PaidAmount,
                student.UpdatedAt ?? student.JoinedAt,
                student.PartyId,
                Status: ToDocumentStatus(student.PaymentStatus),
                ExternalSourceSystem: "CSCA_CLASS_STUDENT",
                ExternalSourceId: student.Id.ToString("N")), ct);
            student.BusinessDocumentId = document.Id;
        }

        if (_financePostingService is null || student.PaidAmount <= 0)
        {
            return;
        }

        var transactionType = student.PaymentStatus switch
        {
            PaymentStatus.Paid or PaymentStatus.Partial => TransactionType.Income,
            PaymentStatus.Refunded => TransactionType.Expense,
            _ => (TransactionType?)null
        };
        if (!transactionType.HasValue)
        {
            return;
        }

        var description = transactionType == TransactionType.Income
            ? $"Thu học phí CSCA {student.StudentName} ({cls.Code})"
            : $"Hoàn học phí CSCA {student.StudentName} ({cls.Code})";
        await _financePostingService.PostAsync(new FinancePostingRequest(
            cls.CompanyId,
            cls.BusinessUnitId,
            transactionType.Value,
            student.PaidAmount,
            student.UpdatedAt ?? student.JoinedAt,
            nameof(CscaClassStudent),
            student.Id,
            description,
            student.BusinessDocumentId), ct);
    }

    private Task QueueLmsAccessLifecycleAsync(CscaClassStudent student, CscaClass cls, CancellationToken ct)
    {
        if (_lmsAccessLifecycleService is null || cls.Course is null)
            return Task.CompletedTask;

        return _lmsAccessLifecycleService.ReconcileStudentAccessAsync(new LmsAccessEvaluationRequest(
            cls.CompanyId,
            cls.BusinessUnitId,
            student.Id,
            student.PartyId,
            cls.Id,
            cls.CourseId,
            cls.Course.CourseSourceId,
            student.StudentName,
            student.Email,
            student.PhoneNumber,
            student.PaidAmount,
            GetPayableTuition(cls.TuitionFee, student.DiscountAmount),
            student.PaymentStatus,
            student.UpdatedAt ?? student.JoinedAt,
            student.BusinessDocumentId?.ToString("N")), ct);
    }

    private static decimal GetPayableTuition(decimal tuitionFee, decimal discountAmount) =>
        Math.Max(0, tuitionFee - discountAmount);

    private static string? CleanStudentContact(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeStudentEmail(string? email) =>
        CleanStudentContact(email)?.ToUpperInvariant();

    private static string? NormalizeStudentPhone(string? phoneNumber)
    {
        var cleaned = CleanStudentContact(phoneNumber);
        if (cleaned is null)
            return null;
        var digits = new string(cleaned.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? cleaned.ToUpperInvariant() : digits;
    }

    private static string? ValidateUniqueStudentContact(
        IEnumerable<CscaClassStudent> students,
        Guid? excludedStudentId,
        string? email,
        string? phoneNumber)
    {
        var normalizedEmail = NormalizeStudentEmail(email);
        if (normalizedEmail is not null)
        {
            var duplicate = students.FirstOrDefault(student =>
                student.Id != excludedStudentId && NormalizeStudentEmail(student.Email) == normalizedEmail);
            if (duplicate is not null)
                return $"Email '{email}' đã được dùng bởi học viên '{duplicate.StudentName}' trong lớp này.";
        }

        var normalizedPhone = NormalizeStudentPhone(phoneNumber);
        if (normalizedPhone is not null)
        {
            var duplicate = students.FirstOrDefault(student =>
                student.Id != excludedStudentId && NormalizeStudentPhone(student.PhoneNumber) == normalizedPhone);
            if (duplicate is not null)
                return $"Số điện thoại '{phoneNumber}' đã được dùng bởi học viên '{duplicate.StudentName}' trong lớp này.";
        }

        return null;
    }

    private static BusinessDocumentStatus ToDocumentStatus(PaymentStatus status) => status switch
    {
        PaymentStatus.Paid => BusinessDocumentStatus.Settled,
        PaymentStatus.Failed or PaymentStatus.Refunded or PaymentStatus.Cancelled => BusinessDocumentStatus.Voided,
        _ => BusinessDocumentStatus.Open
    };

    public async Task<IReadOnlyList<CscaStudentDirectoryDto>> GetStudentDirectoryAsync(string? search, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var query = _db.CscaClassStudents
            .AsNoTracking()
            .Include(s => s.Class)
                .ThenInclude(c => c.Course)
            .Where(s => !s.Class.IsDeleted && s.Class.CompanyId == companyId &&
                (!businessUnitId.HasValue || s.Class.BusinessUnitId == businessUnitId));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(s => s.StudentName.ToLower().Contains(term) ||
                (s.Email != null && s.Email.ToLower().Contains(term)) ||
                (s.PhoneNumber != null && s.PhoneNumber.Contains(term)) ||
                s.Class.Code.ToLower().Contains(term) ||
                s.Class.Name.ToLower().Contains(term) ||
                s.Class.Course.Title.ToLower().Contains(term));
        }

        return await query
            .OrderBy(s => s.StudentName)
            .Select(s => new CscaStudentDirectoryDto
            {
                Id = s.Id,
                ClassId = s.ClassId,
                StudentName = s.StudentName,
                Age = s.Age,
                Hometown = s.Hometown,
                Email = s.Email,
                PhoneNumber = s.PhoneNumber,
                CourseTitle = s.Class.Course.Title,
                ClassCode = s.Class.Code,
                ClassName = s.Class.Name,
                TuitionFee = s.Class.TuitionFee,
                DiscountAmount = s.DiscountAmount,
                DiscountNote = s.DiscountNote,
                PayableAmount = s.Class.TuitionFee - s.DiscountAmount,
                PaidAmount = s.PaidAmount,
                DebtAmount = Math.Max(0, s.Class.TuitionFee - s.DiscountAmount - s.PaidAmount),
                DebtDueDate = s.DebtDueDate,
                PaymentStatus = s.PaymentStatus,
                JoinedAt = s.JoinedAt,
                Notes = s.Notes
            })
            .ToListAsync(ct);
    }

}
