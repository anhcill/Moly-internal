using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Safe, tenant-scoped operational controls for the CSCA Course LMS bridge.
/// It intentionally exposes metadata only: LMS payloads and shared secrets are
/// never returned through the InternalManagement API.
/// </summary>
public sealed class LmsIntegrationOperationsService : ILmsIntegrationOperationsService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 200;

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public LmsIntegrationOperationsService(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<LmsIntegrationOverviewDto>> GetOverviewAsync(CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<LmsIntegrationOverviewDto>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var mappings = _db.LmsCourseLinks.AsNoTracking().Where(link =>
            link.CompanyId == companyId &&
            link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms);
        var outbox = _db.IntegrationOutboxes.AsNoTracking().Where(item =>
            item.CompanyId == companyId &&
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms);

        var overview = new LmsIntegrationOverviewDto
        {
            TotalCourses = await _db.Courses.CountAsync(course => course.CompanyId == companyId && !course.IsDeleted, ct),
            MappedCourses = await mappings.CountAsync(ct),
            ReadyCourseMappings = await mappings.CountAsync(link => link.Status == IntegrationStatus.Success, ct),
            ActiveAccounts = await _db.LmsAccountLinks.CountAsync(link =>
                link.CompanyId == companyId &&
                link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                link.Status == LmsAccountStatus.Active, ct),
            PendingAccounts = await _db.LmsAccountLinks.CountAsync(link =>
                link.CompanyId == companyId &&
                link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                link.Status == LmsAccountStatus.PendingPayment, ct),
            ActiveGrants = await _db.LmsAccessGrants.CountAsync(grant =>
                grant.CompanyId == companyId &&
                grant.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                grant.Status == LmsAccessGrantStatus.Active, ct),
            PendingOutbox = await outbox.CountAsync(item => item.Status == IntegrationStatus.Pending, ct),
            FailedOutbox = await outbox.CountAsync(item => item.Status == IntegrationStatus.Failed, ct),
            DeadLetterOutbox = await outbox.CountAsync(item => item.Status == IntegrationStatus.DeadLetter, ct),
            LastSuccessfulDispatchAt = await outbox
                .Where(item => item.Status == IntegrationStatus.Success)
                .OrderByDescending(item => item.PublishedAt)
                .Select(item => item.PublishedAt)
                .FirstOrDefaultAsync(ct)
        };

        return Result<LmsIntegrationOverviewDto>.Success(overview);
    }

    public async Task<Result<PaginatedResult<LmsCourseMappingDto>>> GetCourseMappingsAsync(
        string? search,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<LmsCourseMappingDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var normalizedStatus = TrimToNull(status);
        var mappings = _db.LmsCourseLinks.AsNoTracking().Where(link =>
            link.CompanyId == companyId &&
            link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms);
        var courses = _db.Courses.AsNoTracking().Where(course =>
            course.CompanyId == companyId && !course.IsDeleted);

        var normalizedSearch = TrimToNull(search);
        if (normalizedSearch is not null)
        {
            courses = courses.Where(course =>
                course.Title.Contains(normalizedSearch) ||
                course.CourseSourceId.Contains(normalizedSearch) ||
                (course.Slug != null && course.Slug.Contains(normalizedSearch)));
        }

        if (normalizedStatus is not null)
        {
            if (string.Equals(normalizedStatus, "Unmapped", StringComparison.OrdinalIgnoreCase))
            {
                courses = courses.Where(course => !mappings.Any(link => link.CourseId == course.Id));
            }
            else if (Enum.TryParse<IntegrationStatus>(normalizedStatus, true, out var requestedStatus))
            {
                courses = courses.Where(course => mappings.Any(link =>
                    link.CourseId == course.Id && link.Status == requestedStatus));
            }
            else
            {
                return Result<PaginatedResult<LmsCourseMappingDto>>.Failure(
                    "Trạng thái mapping không hợp lệ. Dùng Unmapped, Pending, Processing, Success, Failed hoặc DeadLetter.");
            }
        }

        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await courses.CountAsync(ct);
        var pageCourses = await courses
            .OrderBy(course => course.Title)
            .ThenBy(course => course.CourseSourceId)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);

        var courseIds = pageCourses.Select(course => course.Id).ToList();
        var mappingByCourseId = courseIds.Count == 0
            ? new Dictionary<Guid, LmsCourseLink>()
            : (await mappings.Where(link => courseIds.Contains(link.CourseId)).ToListAsync(ct))
                .ToDictionary(link => link.CourseId);

        var items = pageCourses
            .Select(course => ToCourseMappingDto(course, mappingByCourseId.GetValueOrDefault(course.Id)))
            .ToList();

        return Result<PaginatedResult<LmsCourseMappingDto>>.Success(
            new PaginatedResult<LmsCourseMappingDto>(items, totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<LmsCourseMappingDto>> UpsertCourseMappingAsync(
        Guid courseId,
        UpsertLmsCourseMappingRequest request,
        CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<LmsCourseMappingDto>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var course = await _db.Courses.FirstOrDefaultAsync(value =>
            value.Id == courseId && value.CompanyId == companyId && !value.IsDeleted, ct);
        if (course is null)
            return Result<LmsCourseMappingDto>.Failure("Không tìm thấy khóa học trong phạm vi công ty hiện tại.");

        if (request.BusinessUnitId.HasValue && request.BusinessUnitId != course.BusinessUnitId)
        {
            return Result<LmsCourseMappingDto>.Failure(
                "Business unit của mapping phải khớp với business unit đang sở hữu khóa học.");
        }

        if (request.LmsCourseId is <= 0)
            return Result<LmsCourseMappingDto>.Failure("LMS course ID phải là số nguyên dương.");

        var externalCourseId = TrimToNull(request.ExternalCourseId) ?? TrimToNull(course.CourseSourceId);
        var lmsCourseSlug = TrimToNull(request.LmsCourseSlug);
        if (externalCourseId is null)
            return Result<LmsCourseMappingDto>.Failure("Khóa học chưa có CourseSourceId; cần nhập ExternalCourseId cho LMS.");

        if (externalCourseId.Length > 200 || lmsCourseSlug?.Length > 250)
            return Result<LmsCourseMappingDto>.Failure("Mã khóa học LMS vượt quá độ dài cho phép.");

        if (request.EnableAccess && request.LmsCourseId is null && lmsCourseSlug is null)
        {
            return Result<LmsCourseMappingDto>.Failure(
                "Để bật cấp quyền LMS, cần xác nhận LMS course ID hoặc LMS course slug.");
        }

        var existing = await _db.LmsCourseLinks.FirstOrDefaultAsync(link =>
            link.CompanyId == companyId &&
            link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            link.CourseId == courseId,
            ct);

        var duplicateExternalId = await _db.LmsCourseLinks.AnyAsync(link =>
            link.CompanyId == companyId &&
            link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            link.CourseId != courseId &&
            link.ExternalCourseId == externalCourseId,
            ct);
        if (duplicateExternalId)
            return Result<LmsCourseMappingDto>.Failure("ExternalCourseId này đã được dùng cho một khóa học khác.");

        if (existing is not null &&
            !string.Equals(existing.ExternalCourseId, externalCourseId, StringComparison.Ordinal) &&
            await _db.LmsAccessGrants.AnyAsync(grant => grant.LmsCourseLinkId == existing.Id, ct))
        {
            return Result<LmsCourseMappingDto>.Failure(
                "Không thể đổi ExternalCourseId sau khi đã phát sinh quyền học. Hãy tạo quy trình chuyển đổi dữ liệu riêng.");
        }

        var now = DateTime.UtcNow;
        var actor = _currentUser.Username ?? "System";
        if (existing is null)
        {
            existing = new LmsCourseLink
            {
                CompanyId = companyId,
                BusinessUnitId = course.BusinessUnitId,
                CourseId = course.Id,
                SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
                CreatedAt = now,
                CreatedBy = actor
            };
            _db.LmsCourseLinks.Add(existing);
        }

        existing.ExternalCourseId = externalCourseId;
        existing.LmsCourseId = request.LmsCourseId;
        existing.LmsCourseSlug = lmsCourseSlug;
        existing.Status = request.EnableAccess ? IntegrationStatus.Success : IntegrationStatus.Pending;
        existing.LastSyncedAt = request.EnableAccess ? now : null;
        existing.LastSyncError = null;
        existing.UpdatedAt = now;
        existing.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);

        return Result<LmsCourseMappingDto>.Success(ToCourseMappingDto(course, existing));
    }

    public async Task<Result<PaginatedResult<LmsOutboxItemDto>>> GetOutboxAsync(
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<LmsOutboxItemDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var outbox = _db.IntegrationOutboxes.AsNoTracking().Where(item =>
            item.CompanyId == companyId &&
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms);
        var normalizedStatus = TrimToNull(status);
        if (normalizedStatus is not null)
        {
            if (!Enum.TryParse<IntegrationStatus>(normalizedStatus, true, out var requestedStatus))
            {
                return Result<PaginatedResult<LmsOutboxItemDto>>.Failure(
                    "Trạng thái outbox không hợp lệ. Dùng Pending, Processing, Success, Failed hoặc DeadLetter.");
            }

            outbox = outbox.Where(item => item.Status == requestedStatus);
        }

        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await outbox.CountAsync(ct);
        var pageItems = await outbox
            .OrderByDescending(item => item.CreatedAt)
            .Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize)
            .ToListAsync(ct);
        var items = pageItems.Select(ToOutboxItemDto).ToList();

        return Result<PaginatedResult<LmsOutboxItemDto>>.Success(
            new PaginatedResult<LmsOutboxItemDto>(items, totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<LmsOutboxItemDto>> RetryOutboxAsync(Guid outboxId, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<LmsOutboxItemDto>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var item = await _db.IntegrationOutboxes.FirstOrDefaultAsync(value =>
            value.Id == outboxId &&
            value.CompanyId == companyId &&
            value.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms,
            ct);
        if (item is null)
            return Result<LmsOutboxItemDto>.Failure("Không tìm thấy bản ghi LMS outbox.");

        if (item.Status == IntegrationStatus.Success)
            return Result<LmsOutboxItemDto>.Failure("Bản ghi outbox đã được gửi thành công, không cần retry.");
        if (item.Status == IntegrationStatus.Processing)
            return Result<LmsOutboxItemDto>.Failure("Bản ghi outbox đang được xử lý, không thể retry đồng thời.");

        item.Status = IntegrationStatus.Pending;
        item.AttemptCount = 0;
        item.NextAttemptAt = DateTime.UtcNow;
        item.LastError = null;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = _currentUser.Username ?? "System";
        await _db.SaveChangesAsync(ct);

        return Result<LmsOutboxItemDto>.Success(ToOutboxItemDto(item));
    }

    private bool TryGetCompanyId(out Guid companyId)
    {
        if (_currentUser.CompanyId is { } currentCompanyId && currentCompanyId != Guid.Empty)
        {
            companyId = currentCompanyId;
            return true;
        }

        companyId = Guid.Empty;
        return false;
    }

    private static (int PageIndex, int PageSize) NormalizePagination(int pageIndex, int pageSize) =>
        (Math.Max(1, pageIndex), Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaximumPageSize));

    private static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static LmsCourseMappingDto ToCourseMappingDto(Course course, LmsCourseLink? mapping) => new()
    {
        CourseId = course.Id,
        CourseSourceId = course.CourseSourceId,
        CourseTitle = course.Title,
        CourseSlug = course.Slug,
        MappingId = mapping?.Id,
        ExternalCourseId = mapping?.ExternalCourseId,
        LmsCourseId = mapping?.LmsCourseId,
        LmsCourseSlug = mapping?.LmsCourseSlug,
        Status = mapping?.Status.ToString(),
        LastSyncedAt = mapping?.LastSyncedAt,
        LastSyncError = mapping?.LastSyncError,
        BusinessUnitId = mapping?.BusinessUnitId ?? course.BusinessUnitId
    };

    private static LmsOutboxItemDto ToOutboxItemDto(IntegrationOutbox item) => new()
    {
        Id = item.Id,
        EventType = item.EventType,
        AggregateType = item.AggregateType,
        AggregateId = item.AggregateId,
        Status = item.Status.ToString(),
        AttemptCount = item.AttemptCount,
        CreatedAt = item.CreatedAt,
        NextAttemptAt = item.NextAttemptAt,
        PublishedAt = item.PublishedAt,
        CorrelationId = item.CorrelationId,
        LastError = item.LastError
    };
}
