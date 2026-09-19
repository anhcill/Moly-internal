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
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed class CscaService : ICscaService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<CscaService> _logger;
    private readonly IPartyResolver? _partyResolver;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;
    private readonly ILmsAccessLifecycleService? _lmsAccessLifecycleService;

    public CscaService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<CscaService> logger,
        IPartyResolver? partyResolver = null,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null,
        ILmsAccessLifecycleService? lmsAccessLifecycleService = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _partyResolver = partyResolver;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
        _lmsAccessLifecycleService = lmsAccessLifecycleService;
    }

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetContextAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        var bu = await _db.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Code == "CSCA", ct);
        var buId = bu?.Id ?? _currentUser.BusinessUnitId;

        return (companyId.Value, buId);
    }

    public async Task<PaginatedResult<CscaClassDto>> GetClassesAsync(
        string? search, string? batch, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var (companyId, _) = await GetContextAsync(ct);
        var query = _db.CscaClasses
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.CompanyId == companyId)
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.Code.ToLower().Contains(s) || c.Name.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(batch))
        {
            query = query.Where(c => c.Batch == batch);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CscaClassDto
            {
                Id = c.Id,
                CourseId = c.CourseId,
                CourseTitle = c.Course.Title,
                Code = c.Code,
                Name = c.Name,
                Batch = c.Batch,
                Schedule = c.Schedule,
                TuitionFee = c.TuitionFee,
                StartDate = c.StartDate,
                EndDate = c.EndDate,
                Status = c.Status,
                StudentCount = c.Students.Count,
                StaffCount = c.Staff.Count,
                TotalRevenue = c.Students.Sum(s => s.PaidAmount),
                TotalStaffExpense = c.Staff.Sum(st => st.CompensationRate),
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);

        return new PaginatedResult<CscaClassDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<CscaClassDetailDto>> GetClassByIdAsync(Guid id, CancellationToken ct)
    {
        var (companyId, _) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Course)
            .Include(c => c.Students)
            .Include(c => c.Staff)
                .ThenInclude(st => st.Employee)
            .Include(c => c.Schedules)
                .ThenInclude(schedule => schedule.Classroom)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted && c.CompanyId == companyId, ct);

        if (cls == null)
        {
            return Result<CscaClassDetailDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        var totalStudents = cls.Students.Count;
        var paidStudents = cls.Students.Count(s => s.PaymentStatus == PaymentStatus.Paid);
        var actualRevenue = cls.Students.Sum(s => s.PaidAmount);
        var expectedRevenue = totalStudents * cls.TuitionFee;
        var totalStaffExpense = cls.Staff.Sum(st => st.CompensationRate);

        var detail = new CscaClassDetailDto
        {
            Id = cls.Id,
            CourseId = cls.CourseId,
            CourseTitle = cls.Course?.Title ?? string.Empty,
            Code = cls.Code,
            Name = cls.Name,
            Batch = cls.Batch,
            Schedule = cls.Schedule,
            TuitionFee = cls.TuitionFee,
            StartDate = cls.StartDate,
            EndDate = cls.EndDate,
            Status = cls.Status,
            CreatedAt = cls.CreatedAt,
            Students = cls.Students.Select(s => new CscaStudentDto
            {
                Id = s.Id,
                ClassId = s.ClassId,
                StudentName = s.StudentName,
                Age = s.Age,
                Hometown = s.Hometown,
                Email = s.Email,
                PhoneNumber = s.PhoneNumber,
                PaidAmount = s.PaidAmount,
                PaymentStatus = s.PaymentStatus,
                DebtDueDate = s.DebtDueDate,
                JoinedAt = s.JoinedAt,
                Notes = s.Notes
            }).OrderBy(s => s.JoinedAt).ToList(),
            Staff = cls.Staff.Select(st => new CscaStaffDto
            {
                Id = st.Id,
                ClassId = st.ClassId,
                EmployeeId = st.EmployeeId,
                EmployeeName = st.Employee != null ? st.Employee.FullName : string.Empty,
                EmployeeCode = st.Employee?.EmployeeCode,
                RoleInClass = st.RoleInClass,
                CompensationRate = st.CompensationRate,
                Notes = st.Notes
            }).ToList(),
            Schedules = cls.Schedules.OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
                .Select(ToScheduleDto).ToList(),
            FinancialSummary = new ClassFinancialSummaryDto
            {
                ClassId = cls.Id,
                ClassCode = cls.Code,
                ClassName = cls.Name,
                TotalStudents = totalStudents,
                PaidStudents = paidStudents,
                ExpectedRevenue = expectedRevenue,
                ActualRevenue = actualRevenue,
                TotalStaffExpense = totalStaffExpense
            }
        };

        return Result<CscaClassDetailDto>.Success(detail);
    }

    public async Task<Result<CscaClassDto>> CreateClassAsync(CreateCscaClassRequest request, CancellationToken ct)
    {
        var (companyId, buId) = await GetContextAsync(ct);

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Result<CscaClassDto>.Failure("Mã lớp và tên lớp là bắt buộc.");
        }

        var codeUpper = request.Code.Trim().ToUpper();
        var exists = await _db.CscaClasses.AnyAsync(c => c.CompanyId == companyId && c.Code == codeUpper && !c.IsDeleted, ct);
        if (exists)
        {
            return Result<CscaClassDto>.Failure($"Mã lớp '{codeUpper}' đã tồn tại trong hệ thống.");
        }

        var courseResult = await ResolveCourseAsync(request.CourseId, companyId, buId, ct);
        if (!courseResult.Succeeded)
            return Result<CscaClassDto>.Failure(courseResult.Errors.FirstOrDefault() ?? "Mỗi lớp học phải thuộc một khóa học.");

        var cls = new CscaClass
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            CourseId = courseResult.Value!.Id,
            Code = codeUpper,
            Name = request.Name.Trim(),
            Batch = request.Batch?.Trim() ?? string.Empty,
            Schedule = request.Schedule?.Trim() ?? string.Empty,
            TuitionFee = request.TuitionFee,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System"
        };

        _db.CscaClasses.Add(cls);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tạo lớp CSCA thành công: {ClassCode} ({ClassName})", cls.Code, cls.Name);

        var dto = new CscaClassDto
        {
            Id = cls.Id,
            CourseId = cls.CourseId,
            CourseTitle = courseResult.Value.Title,
            Code = cls.Code,
            Name = cls.Name,
            Batch = cls.Batch,
            Schedule = cls.Schedule,
            TuitionFee = cls.TuitionFee,
            StartDate = cls.StartDate,
            EndDate = cls.EndDate,
            Status = cls.Status,
            StudentCount = 0,
            StaffCount = 0,
            TotalRevenue = 0,
            TotalStaffExpense = 0,
            CreatedAt = cls.CreatedAt
        };

        return Result<CscaClassDto>.Success(dto);
    }

    public async Task<Result<CscaClassDto>> UpdateClassAsync(Guid id, UpdateCscaClassRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(id, ct))
            return Result<CscaClassDto>.Failure("Không tìm thấy lớp học CSCA cần cập nhật.");
        var cls = await _db.CscaClasses
            .AsSplitQuery()
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);

        if (cls == null)
        {
            return Result<CscaClassDto>.Failure("Không tìm thấy lớp học CSCA cần cập nhật.");
        }

        if (request.CourseId.HasValue && request.CourseId.Value != cls.CourseId)
        {
            var courseResult = await ResolveCourseAsync(request.CourseId, cls.CompanyId, cls.BusinessUnitId, ct);
            if (!courseResult.Succeeded)
                return Result<CscaClassDto>.Failure(courseResult.Errors.FirstOrDefault() ?? "Khóa học không hợp lệ.");
            cls.CourseId = courseResult.Value!.Id;
        }

        cls.Name = request.Name.Trim();
        cls.Batch = request.Batch?.Trim() ?? string.Empty;
        // The structured weekly schedule is the source of truth as soon as the class has lesson slots.
        // Keep the legacy text only for existing classes that have not yet been migrated to detailed slots.
        cls.Schedule = cls.Schedules.Count == 0
            ? request.Schedule?.Trim() ?? string.Empty
            : FormatScheduleSummary(cls.Schedules);
        cls.TuitionFee = request.TuitionFee;
        cls.StartDate = request.StartDate;
        cls.EndDate = request.EndDate;
        cls.Status = request.Status;
        cls.UpdatedAt = DateTime.UtcNow;
        cls.UpdatedBy = _currentUser.Username ?? "System";

        await _db.SaveChangesAsync(ct);

        var dto = new CscaClassDto
        {
            Id = cls.Id,
            CourseId = cls.CourseId,
            CourseTitle = await _db.Courses.Where(c => c.Id == cls.CourseId).Select(c => c.Title).FirstOrDefaultAsync(ct) ?? string.Empty,
            Code = cls.Code,
            Name = cls.Name,
            Batch = cls.Batch,
            Schedule = cls.Schedule,
            TuitionFee = cls.TuitionFee,
            StartDate = cls.StartDate,
            EndDate = cls.EndDate,
            Status = cls.Status,
            StudentCount = cls.Students.Count,
            StaffCount = cls.Staff.Count,
            TotalRevenue = cls.Students.Sum(s => s.PaidAmount),
            TotalStaffExpense = cls.Staff.Sum(st => st.CompensationRate),
            CreatedAt = cls.CreatedAt
        };

        return Result<CscaClassDto>.Success(dto);
    }

    public async Task<Result<bool>> DeleteClassAsync(Guid id, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(id, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA cần xóa.");
        var cls = await _db.CscaClasses.FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, ct);
        if (cls == null)
        {
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA cần xóa.");
        }

        cls.IsDeleted = true;
        cls.DeletedAt = DateTime.UtcNow;
        cls.DeletedBy = _currentUser.Username ?? "System";

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<CscaStudentDto>> EnrollStudentAsync(Guid classId, EnrollStudentRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStudentDto>.Failure("Không tìm thấy lớp học CSCA.");
        var cls = await _db.CscaClasses
            .Include(c => c.Students)
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

        var student = new CscaClassStudent
        {
            ClassId = classId,
            StudentName = request.StudentName.Trim(),
            Age = request.Age,
            Hometown = request.Hometown?.Trim(),
            Email = request.Email?.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
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

        student.PaidAmount = request.PaidAmount;
        student.PaymentStatus = request.PaymentStatus;
        student.DebtDueDate = request.DebtDueDate;
        if (request.StudentName != null)
        {
            if (string.IsNullOrWhiteSpace(request.StudentName))
                return Result<CscaStudentDto>.Failure("Tên học viên không được để trống.");
            student.StudentName = request.StudentName.Trim();
        }
        if (request.Email != null)
            student.Email = request.Email.Trim();
        if (request.PhoneNumber != null)
            student.PhoneNumber = request.PhoneNumber.Trim();
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
            cls.TuitionFee,
            student.PaymentStatus,
            student.UpdatedAt ?? student.JoinedAt,
            student.BusinessDocumentId?.ToString("N")), ct);
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
                Email = s.Email,
                PhoneNumber = s.PhoneNumber,
                CourseTitle = s.Class.Course.Title,
                ClassCode = s.Class.Code,
                ClassName = s.Class.Name,
                TuitionFee = s.Class.TuitionFee,
                PaidAmount = s.PaidAmount,
                DebtAmount = Math.Max(0, s.Class.TuitionFee - s.PaidAmount),
                DebtDueDate = s.DebtDueDate,
                PaymentStatus = s.PaymentStatus,
                JoinedAt = s.JoinedAt,
                Notes = s.Notes
            })
            .ToListAsync(ct);
    }

    public async Task<Result<CscaStaffDto>> AssignStaffAsync(Guid classId, AssignStaffRequest request, CancellationToken ct)
    {
        var validation = ValidateStaffAssignment(request.RoleInClass, request.CompensationRate);
        if (validation != null)
            return Result<CscaStaffDto>.Failure(validation);

        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStaffDto>.Failure("Không tìm thấy lớp học CSCA.");
        var cls = await _db.CscaClasses
            .Include(c => c.Staff)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted, ct);

        if (cls == null)
        {
            return Result<CscaStaffDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId && !e.IsDeleted, ct);
        if (employee == null)
        {
            return Result<CscaStaffDto>.Failure("Không tìm thấy nhân viên/giảng viên được chọn.");
        }

        var existingStaff = cls.Staff.FirstOrDefault(s => s.EmployeeId == request.EmployeeId);
        if (existingStaff != null)
        {
            existingStaff.RoleInClass = request.RoleInClass.Trim();
            existingStaff.CompensationRate = request.CompensationRate;
            existingStaff.Notes = request.Notes?.Trim();
            existingStaff.UpdatedAt = DateTime.UtcNow;
            existingStaff.UpdatedBy = _currentUser.Username ?? "System";
        }
        else
        {
            existingStaff = new CscaClassStaff
            {
                ClassId = classId,
                EmployeeId = request.EmployeeId,
                RoleInClass = request.RoleInClass.Trim(),
                CompensationRate = request.CompensationRate,
                Notes = request.Notes?.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username ?? "System"
            };
            cls.Staff.Add(existingStaff);
            _db.CscaClassStaffs.Add(existingStaff);
        }

        await SyncClassProfitAllocationAsync(cls, ct);
        await _db.SaveChangesAsync(ct);

        var dto = new CscaStaffDto
        {
            Id = existingStaff.Id,
            ClassId = classId,
            EmployeeId = employee.Id,
            EmployeeName = employee.FullName,
            EmployeeCode = employee.EmployeeCode,
            RoleInClass = existingStaff.RoleInClass,
            CompensationRate = existingStaff.CompensationRate,
            Notes = existingStaff.Notes
        };

        return Result<CscaStaffDto>.Success(dto);
    }

    public async Task<Result<CscaStaffDto>> UpdateStaffAsync(
        Guid classId, Guid staffId, UpdateCscaStaffRequest request, CancellationToken ct)
    {
        var validation = ValidateStaffAssignment(request.RoleInClass, request.CompensationRate);
        if (validation != null)
            return Result<CscaStaffDto>.Failure(validation);

        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaStaffDto>.Failure("Không tìm thấy lớp học CSCA.");

        var staff = await _db.CscaClassStaffs
            .Include(item => item.Class)
                .ThenInclude(cls => cls.Staff)
            .Include(item => item.Employee)
            .FirstOrDefaultAsync(item => item.Id == staffId && item.ClassId == classId, ct);
        if (staff is null || staff.Employee is null)
            return Result<CscaStaffDto>.Failure("Không tìm thấy phân công nhân sự trong lớp.");

        staff.RoleInClass = request.RoleInClass.Trim();
        staff.CompensationRate = request.CompensationRate;
        staff.Notes = request.Notes?.Trim();
        staff.UpdatedAt = DateTime.UtcNow;
        staff.UpdatedBy = _currentUser.Username ?? "System";

        if (staff.Class is not null)
            await SyncClassProfitAllocationAsync(staff.Class, ct);

        await _db.SaveChangesAsync(ct);
        return Result<CscaStaffDto>.Success(new CscaStaffDto
        {
            Id = staff.Id,
            ClassId = staff.ClassId,
            EmployeeId = staff.EmployeeId,
            EmployeeName = staff.Employee.FullName,
            EmployeeCode = staff.Employee.EmployeeCode,
            RoleInClass = staff.RoleInClass,
            CompensationRate = staff.CompensationRate,
            Notes = staff.Notes
        });
    }

    public async Task<Result<bool>> RemoveStaffAsync(Guid classId, Guid staffId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA.");
        var staff = await _db.CscaClassStaffs
            .Include(s => s.Class)
                .ThenInclude(c => c.Staff)
            .FirstOrDefaultAsync(s => s.Id == staffId && s.ClassId == classId, ct);

        if (staff == null)
        {
            return Result<bool>.Failure("Không tìm thấy phân công nhân sự trong lớp.");
        }

        var cls = staff.Class;
        _db.CscaClassStaffs.Remove(staff);

        if (cls != null)
        {
            cls.Staff.Remove(staff);
            await SyncClassProfitAllocationAsync(cls, ct);
        }

        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<ClassFinancialSummaryDto>> GetClassFinancialSummaryAsync(Guid classId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses
            .AsNoTracking()
            .Include(c => c.Students)
            .Include(c => c.Staff)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted && c.CompanyId == companyId &&
                (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);

        if (cls == null)
        {
            return Result<ClassFinancialSummaryDto>.Failure("Không tìm thấy lớp học CSCA.");
        }

        var totalStudents = cls.Students.Count;
        var paidStudents = cls.Students.Count(s => s.PaymentStatus == PaymentStatus.Paid);
        var actualRevenue = cls.Students.Sum(s => s.PaidAmount);
        var expectedRevenue = totalStudents * cls.TuitionFee;
        var totalStaffExpense = cls.Staff.Sum(st => st.CompensationRate);

        var summary = new ClassFinancialSummaryDto
        {
            ClassId = cls.Id,
            ClassCode = cls.Code,
            ClassName = cls.Name,
            TotalStudents = totalStudents,
            PaidStudents = paidStudents,
            ExpectedRevenue = expectedRevenue,
            ActualRevenue = actualRevenue,
            TotalStaffExpense = totalStaffExpense
        };

        return Result<ClassFinancialSummaryDto>.Success(summary);
    }

    public async Task<IReadOnlyList<CscaScheduleDto>> GetSchedulesAsync(Guid classId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var exists = await _db.CscaClasses.AsNoTracking()
            .AnyAsync(c => c.Id == classId && !c.IsDeleted && c.CompanyId == companyId &&
                (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);
        if (!exists)
            return Array.Empty<CscaScheduleDto>();

        return await _db.CscaClassSchedules.AsNoTracking()
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.DayOfWeek).ThenBy(s => s.StartTime)
            .Select(s => new CscaScheduleDto
            {
                Id = s.Id,
                ClassId = s.ClassId,
                ClassroomId = s.ClassroomId,
                ClassroomName = s.Classroom != null ? s.Classroom.Name : null,
                DayOfWeek = s.DayOfWeek,
                StartTime = s.StartTime,
                EndTime = s.EndTime,
                Room = s.Room,
                MeetingUrl = s.MeetingUrl,
                Notes = s.Notes
            }).ToListAsync(ct);
    }

    public async Task<Result<CscaScheduleDto>> AddScheduleAsync(Guid classId, CreateCscaScheduleRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses.FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted && c.CompanyId == companyId &&
            (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);
        if (cls == null)
            return Result<CscaScheduleDto>.Failure("Không tìm thấy lớp học CSCA.");
        var validation = ValidateSchedule(request.DayOfWeek, request.StartTime, request.EndTime)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaScheduleDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, cls.CompanyId, cls.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaScheduleDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        var overlapsExistingSchedule = await _db.CscaClassSchedules.AnyAsync(s => s.ClassId == classId &&
            s.DayOfWeek == request.DayOfWeek && request.StartTime < s.EndTime && request.EndTime > s.StartTime, ct);
        if (overlapsExistingSchedule)
            return Result<CscaScheduleDto>.Failure("Khung giờ này bị chồng với một buổi khác cùng ngày trong lớp. Hãy chọn giờ không giao nhau.");
        var classroomConflict = await ValidateRecurringClassroomAvailabilityAsync(
            request.ClassroomId, classId, request.DayOfWeek, request.StartTime, request.EndTime, null, ct);
        if (classroomConflict != null)
            return Result<CscaScheduleDto>.Failure(classroomConflict);

        var schedule = new CscaClassSchedule
        {
            ClassId = classId, DayOfWeek = request.DayOfWeek, StartTime = request.StartTime,
            EndTime = request.EndTime, ClassroomId = request.ClassroomId,
            Room = classroomResult.Value?.Name ?? request.Room?.Trim(), MeetingUrl = request.MeetingUrl?.Trim(), Notes = request.Notes?.Trim()
        };
        _db.CscaClassSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct);
        await RefreshClassScheduleStringAsync(classId, ct);
        return Result<CscaScheduleDto>.Success(ToScheduleDto(schedule));
    }

    public async Task<Result<CscaScheduleDto>> UpdateScheduleAsync(Guid classId, Guid scheduleId, UpdateCscaScheduleRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var schedule = await _db.CscaClassSchedules.Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.ClassId == classId && !s.Class.IsDeleted &&
                s.Class.CompanyId == companyId && (!businessUnitId.HasValue || s.Class.BusinessUnitId == businessUnitId), ct);
        if (schedule == null)
            return Result<CscaScheduleDto>.Failure("Không tìm thấy lịch học.");
        var validation = ValidateSchedule(request.DayOfWeek, request.StartTime, request.EndTime)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaScheduleDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, schedule.Class.CompanyId, schedule.Class.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaScheduleDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        var overlapsExistingSchedule = await _db.CscaClassSchedules.AnyAsync(s => s.Id != scheduleId && s.ClassId == classId &&
            s.DayOfWeek == request.DayOfWeek && request.StartTime < s.EndTime && request.EndTime > s.StartTime, ct);
        if (overlapsExistingSchedule)
            return Result<CscaScheduleDto>.Failure("Khung giờ này bị chồng với một buổi khác cùng ngày trong lớp. Hãy chọn giờ không giao nhau.");
        var classroomConflict = await ValidateRecurringClassroomAvailabilityAsync(
            request.ClassroomId, classId, request.DayOfWeek, request.StartTime, request.EndTime, scheduleId, ct);
        if (classroomConflict != null)
            return Result<CscaScheduleDto>.Failure(classroomConflict);

        schedule.DayOfWeek = request.DayOfWeek;
        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;
        schedule.ClassroomId = request.ClassroomId;
        schedule.Room = classroomResult.Value?.Name ?? request.Room?.Trim();
        schedule.MeetingUrl = request.MeetingUrl?.Trim();
        schedule.Notes = request.Notes?.Trim();
        await _db.SaveChangesAsync(ct);
        await RefreshClassScheduleStringAsync(classId, ct);
        return Result<CscaScheduleDto>.Success(ToScheduleDto(schedule));
    }

    public async Task<Result<bool>> RemoveScheduleAsync(Guid classId, Guid scheduleId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var schedule = await _db.CscaClassSchedules.Include(s => s.Class)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.ClassId == classId && !s.Class.IsDeleted &&
                s.Class.CompanyId == companyId && (!businessUnitId.HasValue || s.Class.BusinessUnitId == businessUnitId), ct);
        if (schedule == null)
            return Result<bool>.Failure("Không tìm thấy lịch học.");
        _db.CscaClassSchedules.Remove(schedule);
        await _db.SaveChangesAsync(ct);
        await RefreshClassScheduleStringAsync(classId, ct);
        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<CscaClassroomDto>> GetClassroomsAsync(bool includeInactive, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var query = _db.CscaClassrooms.AsNoTracking().Where(room => room.CompanyId == companyId &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId));
        if (!includeInactive)
            query = query.Where(room => room.IsActive);

        return await query
            .OrderBy(room => room.Code)
            .Select(room => new CscaClassroomDto
            {
                Id = room.Id,
                Code = room.Code,
                Name = room.Name,
                Capacity = room.Capacity,
                Location = room.Location,
                IsActive = room.IsActive
            })
            .ToListAsync(ct);
    }

    public async Task<Result<CscaClassroomDto>> CreateClassroomAsync(CreateCscaClassroomRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var validation = ValidateClassroom(request.Code, request.Name, request.Capacity);
        if (validation != null)
            return Result<CscaClassroomDto>.Failure(validation);

        var code = request.Code.Trim().ToUpperInvariant();
        var exists = await _db.CscaClassrooms.AnyAsync(room => room.CompanyId == companyId && room.Code == code, ct);
        if (exists)
            return Result<CscaClassroomDto>.Failure($"Mã phòng '{code}' đã tồn tại.");

        var classroom = new CscaClassroom
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            Code = code,
            Name = request.Name.Trim(),
            Capacity = request.Capacity,
            Location = request.Location?.Trim(),
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System"
        };
        _db.CscaClassrooms.Add(classroom);
        await _db.SaveChangesAsync(ct);
        return Result<CscaClassroomDto>.Success(ToClassroomDto(classroom));
    }

    public async Task<Result<CscaClassroomDto>> UpdateClassroomAsync(Guid classroomId, UpdateCscaClassroomRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var validation = ValidateClassroom("ROOM", request.Name, request.Capacity);
        if (validation != null)
            return Result<CscaClassroomDto>.Failure(validation.Replace("Mã phòng", "Tên phòng"));

        var classroom = await _db.CscaClassrooms.FirstOrDefaultAsync(room => room.Id == classroomId && room.CompanyId == companyId &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId), ct);
        if (classroom == null)
            return Result<CscaClassroomDto>.Failure("Không tìm thấy phòng học.");

        classroom.Name = request.Name.Trim();
        classroom.Capacity = request.Capacity;
        classroom.Location = request.Location?.Trim();
        classroom.IsActive = request.IsActive;
        classroom.UpdatedAt = DateTime.UtcNow;
        classroom.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<CscaClassroomDto>.Success(ToClassroomDto(classroom));
    }

    public async Task<Result<bool>> RemoveClassroomAsync(Guid classroomId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var classroom = await _db.CscaClassrooms.FirstOrDefaultAsync(room => room.Id == classroomId && room.CompanyId == companyId &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId), ct);
        if (classroom == null)
            return Result<bool>.Failure("Không tìm thấy phòng học.");

        var isInUse = await _db.CscaClassSchedules.AnyAsync(schedule => schedule.ClassroomId == classroomId, ct)
            || await _db.CscaLessonSessions.AnyAsync(session => session.ClassroomId == classroomId, ct);
        if (isInUse)
            return Result<bool>.Failure("Phòng đang được dùng trong lịch học hoặc buổi học. Hãy chuyển sang Không hoạt động thay vì xóa.");

        classroom.IsDeleted = true;
        classroom.DeletedAt = DateTime.UtcNow;
        classroom.DeletedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<IReadOnlyList<CscaLessonSessionDto>> GetLessonSessionsAsync(
        Guid classId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Array.Empty<CscaLessonSessionDto>();

        var query = _db.CscaLessonSessions.AsNoTracking()
            .Include(session => session.Classroom)
            .Where(session => session.ClassId == classId);
        if (fromDate.HasValue) query = query.Where(session => session.LessonDate >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(session => session.LessonDate <= toDate.Value);

        return await query.OrderBy(session => session.LessonDate).ThenBy(session => session.StartTime)
            .Select(session => new CscaLessonSessionDto
            {
                Id = session.Id,
                ClassId = session.ClassId,
                ScheduleId = session.ScheduleId,
                ClassroomId = session.ClassroomId,
                ClassroomName = session.Classroom != null ? session.Classroom.Name : null,
                LessonDate = session.LessonDate,
                StartTime = session.StartTime,
                EndTime = session.EndTime,
                Status = session.Status,
                MeetingUrl = session.MeetingUrl,
                Notes = session.Notes
            }).ToListAsync(ct);
    }

    public async Task<Result<CscaLessonSessionDto>> CreateLessonSessionAsync(
        Guid classId, CreateCscaLessonSessionRequest request, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        var cls = await _db.CscaClasses.FirstOrDefaultAsync(item => item.Id == classId && item.CompanyId == companyId && !item.IsDeleted &&
            (!businessUnitId.HasValue || item.BusinessUnitId == businessUnitId), ct);
        if (cls == null)
            return Result<CscaLessonSessionDto>.Failure("Không tìm thấy lớp học CSCA.");
        var validation = ValidateLessonSession(request.LessonDate, request.StartTime, request.EndTime, request.Status)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaLessonSessionDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, cls.CompanyId, cls.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaLessonSessionDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        if (request.ScheduleId.HasValue && !await _db.CscaClassSchedules.AnyAsync(schedule => schedule.Id == request.ScheduleId.Value && schedule.ClassId == classId, ct))
            return Result<CscaLessonSessionDto>.Failure("Lịch tuần gốc không thuộc lớp này.");
        var conflict = await ValidateDatedSessionAvailabilityAsync(request.ClassroomId, classId, request.LessonDate, request.StartTime, request.EndTime, null, request.Status, ct);
        if (conflict != null)
            return Result<CscaLessonSessionDto>.Failure(conflict);

        var session = new CscaLessonSession
        {
            ClassId = classId,
            ScheduleId = request.ScheduleId,
            ClassroomId = request.ClassroomId,
            LessonDate = request.LessonDate,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = request.Status.Trim(),
            MeetingUrl = request.MeetingUrl?.Trim(),
            Notes = request.Notes?.Trim(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.Username ?? "System",
            Classroom = classroomResult.Value
        };
        _db.CscaLessonSessions.Add(session);
        await _db.SaveChangesAsync(ct);
        return Result<CscaLessonSessionDto>.Success(ToLessonSessionDto(session));
    }

    public async Task<Result<CscaLessonSessionDto>> UpdateLessonSessionAsync(
        Guid classId, Guid sessionId, UpdateCscaLessonSessionRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaLessonSessionDto>.Failure("Không tìm thấy lớp học CSCA.");

        var session = await _db.CscaLessonSessions.Include(item => item.Class).Include(item => item.Classroom)
            .FirstOrDefaultAsync(item => item.Id == sessionId && item.ClassId == classId, ct);
        if (session == null)
            return Result<CscaLessonSessionDto>.Failure("Không tìm thấy buổi học.");
        var validation = ValidateLessonSession(request.LessonDate, request.StartTime, request.EndTime, request.Status)
            ?? ValidateMeetingUrl(request.MeetingUrl);
        if (validation != null)
            return Result<CscaLessonSessionDto>.Failure(validation);
        var classroomResult = await ResolveClassroomAsync(request.ClassroomId, session.Class.CompanyId, session.Class.BusinessUnitId, ct);
        if (!classroomResult.Succeeded)
            return Result<CscaLessonSessionDto>.Failure(classroomResult.Errors.FirstOrDefault() ?? "Phòng học không hợp lệ.");
        var conflict = await ValidateDatedSessionAvailabilityAsync(request.ClassroomId, classId, request.LessonDate, request.StartTime, request.EndTime, sessionId, request.Status, ct);
        if (conflict != null)
            return Result<CscaLessonSessionDto>.Failure(conflict);

        session.ClassroomId = request.ClassroomId;
        session.Classroom = classroomResult.Value;
        session.LessonDate = request.LessonDate;
        session.StartTime = request.StartTime;
        session.EndTime = request.EndTime;
        session.Status = request.Status.Trim();
        session.MeetingUrl = request.MeetingUrl?.Trim();
        session.Notes = request.Notes?.Trim();
        session.UpdatedAt = DateTime.UtcNow;
        session.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<CscaLessonSessionDto>.Success(ToLessonSessionDto(session));
    }

    public async Task<Result<bool>> RemoveLessonSessionAsync(Guid classId, Guid sessionId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<bool>.Failure("Không tìm thấy lớp học CSCA.");
        var session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item => item.Id == sessionId && item.ClassId == classId, ct);
        if (session == null)
            return Result<bool>.Failure("Không tìm thấy buổi học.");

        _db.CscaLessonSessions.Remove(session);
        await _db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }

    public async Task<Result<int>> GenerateLessonSessionsAsync(Guid classId, GenerateCscaLessonSessionsRequest request, CancellationToken ct)
    {
        if (request.FromDate > request.ToDate)
            return Result<int>.Failure("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.");
        if (request.ToDate.DayNumber - request.FromDate.DayNumber > 366)
            return Result<int>.Failure("Chỉ được tạo buổi học tối đa trong 366 ngày cho một lần thao tác.");
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<int>.Failure("Không tìm thấy lớp học CSCA.");

        var schedules = await _db.CscaClassSchedules.AsNoTracking()
            .Where(schedule => schedule.ClassId == classId)
            .OrderBy(schedule => schedule.DayOfWeek).ThenBy(schedule => schedule.StartTime)
            .ToListAsync(ct);
        if (schedules.Count == 0)
            return Result<int>.Failure("Lớp chưa có lịch tuần. Hãy tạo lịch tuần trước khi sinh buổi học.");

        var existing = await _db.CscaLessonSessions
            .Where(session => session.LessonDate >= request.FromDate && session.LessonDate <= request.ToDate)
            .ToListAsync(ct);
        var generated = new List<CscaLessonSession>();
        for (var date = request.FromDate; date <= request.ToDate; date = date.AddDays(1))
        {
            foreach (var schedule in schedules.Where(item => item.DayOfWeek == (int)date.DayOfWeek))
            {
                if (existing.Any(session => session.ScheduleId == schedule.Id && session.LessonDate == date))
                    continue;
                var hasClassConflict = existing.Concat(generated).Any(session => session.ClassId == classId && session.LessonDate == date &&
                    !string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) && schedule.StartTime < session.EndTime && schedule.EndTime > session.StartTime);
                if (hasClassConflict)
                    return Result<int>.Failure($"Không thể tạo buổi ngày {date:dd/MM/yyyy} vì bị chồng với buổi khác của lớp.");
                var hasRoomConflict = schedule.ClassroomId.HasValue && existing.Concat(generated).Any(session => session.ClassroomId == schedule.ClassroomId &&
                    session.ClassId != classId && session.LessonDate == date && !string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) &&
                    schedule.StartTime < session.EndTime && schedule.EndTime > session.StartTime);
                if (hasRoomConflict)
                    return Result<int>.Failure($"Phòng đã có lớp khác dùng vào {date:dd/MM/yyyy}, {schedule.StartTime:hh\\:mm} - {schedule.EndTime:hh\\:mm}.");

                generated.Add(new CscaLessonSession
                {
                    ClassId = classId,
                    ScheduleId = schedule.Id,
                    ClassroomId = schedule.ClassroomId,
                    LessonDate = date,
                    StartTime = schedule.StartTime,
                    EndTime = schedule.EndTime,
                    Status = "Scheduled",
                    MeetingUrl = schedule.MeetingUrl,
                    Notes = schedule.Notes,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.Username ?? "System"
                });
            }
        }

        if (generated.Count > 0)
        {
            _db.CscaLessonSessions.AddRange(generated);
            await _db.SaveChangesAsync(ct);
        }
        return Result<int>.Success(generated.Count);
    }

    public async Task<Result<IReadOnlyList<CscaLessonAttendanceDto>>> GetLessonAttendanceAsync(
        Guid classId, Guid sessionId, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<IReadOnlyList<CscaLessonAttendanceDto>>.Failure("Không tìm thấy lớp học CSCA.");
        var sessionExists = await _db.CscaLessonSessions.AnyAsync(session => session.Id == sessionId && session.ClassId == classId, ct);
        if (!sessionExists)
            return Result<IReadOnlyList<CscaLessonAttendanceDto>>.Failure("Không tìm thấy buổi học.");

        var students = await _db.CscaClassStudents.AsNoTracking().Where(student => student.ClassId == classId)
            .OrderBy(student => student.StudentName).ToListAsync(ct);
        var recorded = await _db.CscaLessonAttendances.AsNoTracking().Where(attendance => attendance.LessonSessionId == sessionId)
            .ToDictionaryAsync(attendance => attendance.StudentId, ct);
        var items = students.Select(student => recorded.TryGetValue(student.Id, out var attendance)
            ? ToAttendanceDto(attendance, student.StudentName)
            : new CscaLessonAttendanceDto { LessonSessionId = sessionId, StudentId = student.Id, StudentName = student.StudentName })
            .ToList();
        return Result<IReadOnlyList<CscaLessonAttendanceDto>>.Success(items);
    }

    public async Task<Result<CscaLessonAttendanceDto>> UpsertLessonAttendanceAsync(
        Guid classId, Guid sessionId, UpsertCscaLessonAttendanceRequest request, CancellationToken ct)
    {
        if (!await IsClassInScopeAsync(classId, ct))
            return Result<CscaLessonAttendanceDto>.Failure("Không tìm thấy lớp học CSCA.");
        if (!IsValidAttendanceStatus(request.Status))
            return Result<CscaLessonAttendanceDto>.Failure("Trạng thái điểm danh không hợp lệ.");

        var session = await _db.CscaLessonSessions.FirstOrDefaultAsync(item => item.Id == sessionId && item.ClassId == classId, ct);
        if (session == null)
            return Result<CscaLessonAttendanceDto>.Failure("Không tìm thấy buổi học.");
        if (string.Equals(session.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return Result<CscaLessonAttendanceDto>.Failure("Không thể điểm danh cho buổi học đã hủy.");
        var student = await _db.CscaClassStudents.FirstOrDefaultAsync(item => item.Id == request.StudentId && item.ClassId == classId, ct);
        if (student == null)
            return Result<CscaLessonAttendanceDto>.Failure("Học viên không thuộc lớp của buổi học này.");

        var attendance = await _db.CscaLessonAttendances.FirstOrDefaultAsync(item => item.LessonSessionId == sessionId && item.StudentId == request.StudentId, ct);
        if (attendance == null)
        {
            attendance = new CscaLessonAttendance
            {
                LessonSessionId = sessionId,
                StudentId = request.StudentId,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = _currentUser.Username ?? "System"
            };
            _db.CscaLessonAttendances.Add(attendance);
        }
        attendance.Status = request.Status.Trim();
        attendance.CheckInAt = request.CheckInAt;
        attendance.Notes = request.Notes?.Trim();
        attendance.UpdatedAt = DateTime.UtcNow;
        attendance.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);
        return Result<CscaLessonAttendanceDto>.Success(ToAttendanceDto(attendance, student.StudentName));
    }

    private static string DayShortLabel(int dayOfWeek) => dayOfWeek switch
    {
        1 => "T2",
        2 => "T3",
        3 => "T4",
        4 => "T5",
        5 => "T6",
        6 => "T7",
        0 => "CN",
        _ => $"T{dayOfWeek + 1}"
    };

    public static string FormatScheduleSummary(IEnumerable<CscaClassSchedule> schedules)
    {
        var list = schedules
            .OrderBy(s => s.DayOfWeek == 0 ? 7 : s.DayOfWeek)
            .ThenBy(s => s.StartTime)
            .ToList();

        if (list.Count == 0) return string.Empty;

        var timeGroups = list
            .GroupBy(s => (s.StartTime, s.EndTime))
            .ToList();

        var groupSummaries = new List<string>();
        foreach (var group in timeGroups)
        {
            var dayNames = group.Select(s => DayShortLabel(s.DayOfWeek)).Distinct().ToList();
            var daysPart = dayNames.Count switch
            {
                1 => dayNames[0],
                2 => $"{dayNames[0]} - {dayNames[1]}",
                _ => string.Join(", ", dayNames)
            };

            var startStr = group.Key.StartTime.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
            var endStr = group.Key.EndTime.ToString(@"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
            groupSummaries.Add($"{daysPart} ({startStr} - {endStr})");
        }

        return string.Join(", ", groupSummaries);
    }

    private async Task RefreshClassScheduleStringAsync(Guid classId, CancellationToken ct)
    {
        var cls = await _db.CscaClasses
            .Include(c => c.Schedules)
            .FirstOrDefaultAsync(c => c.Id == classId && !c.IsDeleted, ct);

        if (cls != null)
        {
            cls.Schedule = FormatScheduleSummary(cls.Schedules);
            cls.UpdatedAt = DateTime.UtcNow;
            cls.UpdatedBy = _currentUser.Username ?? "System";
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string? ValidateSchedule(int dayOfWeek, TimeSpan startTime, TimeSpan endTime)
    {
        if (dayOfWeek is < 0 or > 6)
            return "Ngày trong tuần phải nằm trong khoảng 0 (Chủ nhật) đến 6 (Thứ bảy).";
        if (startTime >= endTime)
            return "Giờ bắt đầu phải trước giờ kết thúc.";
        return null;
    }

    private static string? ValidateMeetingUrl(string? meetingUrl)
    {
        if (string.IsNullOrWhiteSpace(meetingUrl))
            return null;

        return Uri.TryCreate(meetingUrl.Trim(), UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? null
            : "Link học trực tuyến phải bắt đầu bằng http:// hoặc https://.";
    }

    private static string? ValidateClassroom(string code, string name, int? capacity)
    {
        if (string.IsNullOrWhiteSpace(code))
            return "Mã phòng là bắt buộc.";
        if (string.IsNullOrWhiteSpace(name))
            return "Tên phòng là bắt buộc.";
        if (capacity is < 1)
            return "Sức chứa phải lớn hơn 0.";
        return null;
    }

    private static string? ValidateLessonSession(DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, string? status)
    {
        if (lessonDate == default)
            return "Ngày học là bắt buộc.";
        if (startTime >= endTime)
            return "Giờ bắt đầu phải trước giờ kết thúc.";
        return IsValidLessonStatus(status)
            ? null
            : "Trạng thái buổi học phải là Đã lên lịch, Đổi lịch, Đã hủy hoặc Hoàn thành.";
    }

    private static bool IsValidLessonStatus(string? status) => new[] { "Scheduled", "Rescheduled", "Cancelled", "Completed" }
        .Contains(status?.Trim(), StringComparer.OrdinalIgnoreCase);

    private static bool IsValidAttendanceStatus(string? status) => new[] { "Present", "Late", "Absent", "Excused" }
        .Contains(status?.Trim(), StringComparer.OrdinalIgnoreCase);

    private async Task<Result<CscaClassroom?>> ResolveClassroomAsync(
        Guid? classroomId, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        if (!classroomId.HasValue)
            return Result<CscaClassroom?>.Success(null);

        var classroom = await _db.CscaClassrooms.FirstOrDefaultAsync(room => room.Id == classroomId.Value &&
            room.CompanyId == companyId && room.IsActive &&
            (room.BusinessUnitId == null || !businessUnitId.HasValue || room.BusinessUnitId == businessUnitId), ct);
        return classroom is null
            ? Result<CscaClassroom?>.Failure("Phòng học không tồn tại, đang ngừng dùng hoặc không thuộc đơn vị hiện tại.")
            : Result<CscaClassroom?>.Success(classroom);
    }

    private async Task<string?> ValidateRecurringClassroomAvailabilityAsync(
        Guid? classroomId, Guid classId, int dayOfWeek, TimeSpan startTime, TimeSpan endTime, Guid? excludedScheduleId, CancellationToken ct)
    {
        if (!classroomId.HasValue)
            return null;

        var hasConflict = await _db.CscaClassSchedules.AnyAsync(schedule => schedule.ClassroomId == classroomId.Value &&
            schedule.ClassId != classId && schedule.DayOfWeek == dayOfWeek &&
            (!excludedScheduleId.HasValue || schedule.Id != excludedScheduleId.Value) &&
            startTime < schedule.EndTime && endTime > schedule.StartTime && schedule.Class.Status == "Active", ct);
        return hasConflict
            ? "Phòng học đã được một lớp khác dùng ở khung giờ này trong lịch tuần. Hãy chọn phòng hoặc giờ khác."
            : null;
    }

    private async Task<string?> ValidateDatedSessionAvailabilityAsync(
        Guid? classroomId, Guid classId, DateOnly lessonDate, TimeSpan startTime, TimeSpan endTime, Guid? excludedSessionId, string status, CancellationToken ct)
    {
        if (string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            return null;

        var classConflict = await _db.CscaLessonSessions.AnyAsync(session => session.ClassId == classId && session.LessonDate == lessonDate &&
            (!excludedSessionId.HasValue || session.Id != excludedSessionId.Value) && session.Status != "Cancelled" &&
            startTime < session.EndTime && endTime > session.StartTime, ct);
        if (classConflict)
            return "Buổi học bị chồng giờ với một buổi khác của lớp trong ngày này.";
        if (!classroomId.HasValue)
            return null;

        var classroomConflict = await _db.CscaLessonSessions.AnyAsync(session => session.ClassroomId == classroomId.Value && session.ClassId != classId &&
            session.LessonDate == lessonDate && (!excludedSessionId.HasValue || session.Id != excludedSessionId.Value) &&
            session.Status != "Cancelled" && startTime < session.EndTime && endTime > session.StartTime, ct);
        return classroomConflict
            ? "Phòng học đã được một lớp khác sử dụng trong khung giờ này."
            : null;
    }

    private static string? ValidateStaffAssignment(string? roleInClass, decimal compensationRate)
    {
        if (string.IsNullOrWhiteSpace(roleInClass))
            return "Vai trò của giáo viên / nhân sự là bắt buộc.";
        if (compensationRate < 0)
            return "Mức thù lao không được âm.";

        var allowedRoles = new[] { "Teacher", "TeachingAssistant", "Mentor" };
        return allowedRoles.Contains(roleInClass.Trim(), StringComparer.OrdinalIgnoreCase)
            ? null
            : "Vai trò phải là Giáo viên, Trợ giảng hoặc Mentor.";
    }

    private static CscaScheduleDto ToScheduleDto(CscaClassSchedule schedule) => new()
    {
        Id = schedule.Id, ClassId = schedule.ClassId, DayOfWeek = schedule.DayOfWeek,
        ClassroomId = schedule.ClassroomId, ClassroomName = schedule.Classroom?.Name,
        StartTime = schedule.StartTime, EndTime = schedule.EndTime, Room = schedule.Room,
        MeetingUrl = schedule.MeetingUrl, Notes = schedule.Notes
    };

    private static CscaClassroomDto ToClassroomDto(CscaClassroom classroom) => new()
    {
        Id = classroom.Id,
        Code = classroom.Code,
        Name = classroom.Name,
        Capacity = classroom.Capacity,
        Location = classroom.Location,
        IsActive = classroom.IsActive
    };

    private static CscaLessonSessionDto ToLessonSessionDto(CscaLessonSession session) => new()
    {
        Id = session.Id,
        ClassId = session.ClassId,
        ScheduleId = session.ScheduleId,
        ClassroomId = session.ClassroomId,
        ClassroomName = session.Classroom?.Name,
        LessonDate = session.LessonDate,
        StartTime = session.StartTime,
        EndTime = session.EndTime,
        Status = session.Status,
        MeetingUrl = session.MeetingUrl,
        Notes = session.Notes
    };

    private static CscaLessonAttendanceDto ToAttendanceDto(CscaLessonAttendance attendance, string studentName) => new()
    {
        Id = attendance.Id,
        LessonSessionId = attendance.LessonSessionId,
        StudentId = attendance.StudentId,
        StudentName = studentName,
        Status = attendance.Status,
        CheckInAt = attendance.CheckInAt,
        Notes = attendance.Notes
    };

    private async Task<bool> IsClassInScopeAsync(Guid classId, CancellationToken ct)
    {
        var (companyId, businessUnitId) = await GetContextAsync(ct);
        return await _db.CscaClasses.AsNoTracking().AnyAsync(c => c.Id == classId && !c.IsDeleted &&
            c.CompanyId == companyId && (!businessUnitId.HasValue || c.BusinessUnitId == businessUnitId), ct);
    }

    private async Task<Result<Domain.Entities.EdTech.Course>> ResolveCourseAsync(
        Guid? requestedCourseId, Guid companyId, Guid? businessUnitId, CancellationToken ct)
    {
        if (requestedCourseId.HasValue)
        {
            var selected = await _db.Courses.FirstOrDefaultAsync(c => c.Id == requestedCourseId.Value &&
                c.CompanyId == companyId && !c.IsDeleted, ct);
            return selected is null
                ? Result<Domain.Entities.EdTech.Course>.Failure("Khóa học không tồn tại hoặc không thuộc mảng hiện tại.")
                : Result<Domain.Entities.EdTech.Course>.Success(selected);
        }

        // Backward-compatible fallback for old clients/tests. The desktop flow always requires
        // the operator to select an existing course before creating a class.
        const string legacySourceId = "LEGACY-CSCA-COURSE";
        var legacy = await _db.Courses.FirstOrDefaultAsync(c => c.CompanyId == companyId && c.CourseSourceId == legacySourceId, ct);
        if (legacy != null)
            return Result<Domain.Entities.EdTech.Course>.Success(legacy);

        legacy = new Domain.Entities.EdTech.Course
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            CourseSourceId = legacySourceId,
            Title = "Khóa học CSCA chưa phân loại",
            Description = "Khóa học tạm dùng để tương thích dữ liệu lớp học cũ.",
            Price = 0,
            Status = "Draft",
            CreatedBy = _currentUser.Username ?? "System"
        };
        _db.Courses.Add(legacy);
        await _db.SaveChangesAsync(ct);
        return Result<Domain.Entities.EdTech.Course>.Success(legacy);
    }

    private async Task SyncClassProfitAllocationAsync(CscaClass cls, CancellationToken ct)
    {
        var allocation = await _db.ProfitAllocations
            .FirstOrDefaultAsync(p => p.ReferenceType == "CscaClass" && p.ReferenceId == cls.Id, ct);

        var actualRevenue = cls.Students.Sum(s => s.PaidAmount);
        var totalStaffExpense = cls.Staff.Sum(st => st.CompensationRate);

        if (allocation == null)
        {
            allocation = new ProfitAllocation
            {
                CompanyId = cls.CompanyId,
                BusinessUnitId = cls.BusinessUnitId,
                ReferenceType = "CscaClass",
                ReferenceId = cls.Id,
                IncomeAmount = actualRevenue,
                ExpenseAmount = totalStaffExpense,
                AllocatedAt = DateTime.UtcNow
            };
            _db.ProfitAllocations.Add(allocation);
        }
        else
        {
            allocation.IncomeAmount = actualRevenue;
            allocation.ExpenseAmount = totalStaffExpense;
            allocation.AllocatedAt = DateTime.UtcNow;
        }
    }
}
