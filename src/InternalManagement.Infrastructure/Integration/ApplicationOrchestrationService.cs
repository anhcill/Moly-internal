using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Tenant-scoped control plane for applications governed by InternalManagement.
/// It owns canonical membership, scoped roles and entitlement decisions. A
/// connector can safely project the committed outbox commands to a child app,
/// but must never manufacture a paid entitlement itself.
/// </summary>
public sealed class ApplicationOrchestrationService : IApplicationOrchestrationService
{
    private const int DefaultPageSize = 20;
    private const int MaximumPageSize = 200;
    private const string ApplicationSourcePrefix = "APPLICATION:";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ApplicationOrchestrationService(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<ApplicationOrchestrationOverviewDto>> GetOverviewAsync(CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationOrchestrationOverviewDto>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var applications = _db.ManagedApplications.AsNoTracking().Where(x => x.CompanyId == companyId);
        var outbox = _db.IntegrationOutboxes.AsNoTracking().Where(x =>
            x.CompanyId == companyId && x.SourceSystem.StartsWith(ApplicationSourcePrefix));

        var overview = new ApplicationOrchestrationOverviewDto
        {
            TotalApplications = await applications.CountAsync(ct),
            ActiveApplications = await applications.CountAsync(x => x.Status == ManagedApplicationStatus.Active, ct),
            ActiveMemberships = await _db.ApplicationMemberships.CountAsync(x =>
                x.CompanyId == companyId && x.Status == ApplicationMembershipStatus.Active, ct),
            ActiveRoleAssignments = await _db.ApplicationRoleAssignments.CountAsync(x =>
                x.CompanyId == companyId && x.Status == ApplicationRoleAssignmentStatus.Active, ct),
            ReadyCourseMaps = await _db.ApplicationCourseMaps.CountAsync(x =>
                x.CompanyId == companyId && x.Status == IntegrationStatus.Success, ct),
            ReadyClassMaps = await _db.ApplicationClassMaps.CountAsync(x =>
                x.CompanyId == companyId && x.Status == IntegrationStatus.Success, ct),
            ActiveEntitlements = await _db.ApplicationEntitlements.CountAsync(x =>
                x.CompanyId == companyId && x.Status == ApplicationEntitlementStatus.Active, ct),
            PendingEntitlements = await _db.ApplicationEntitlements.CountAsync(x =>
                x.CompanyId == companyId && x.Status == ApplicationEntitlementStatus.PendingPayment, ct),
            PendingDelivery = await outbox.CountAsync(x => x.Status == IntegrationStatus.Pending, ct),
            FailedDelivery = await outbox.CountAsync(x => x.Status == IntegrationStatus.Failed, ct),
            // The legacy dead-letter table has no company column. The central
            // dashboard deliberately uses the tenant-scoped outbox DeadLetter
            // state instead of leaking another tenant's failed payload.
            OpenDeadLetters = await outbox.CountAsync(x => x.Status == IntegrationStatus.DeadLetter, ct)
        };

        return Result<ApplicationOrchestrationOverviewDto>.Success(overview);
    }

    public async Task<Result<PaginatedResult<ManagedApplicationDto>>> GetApplicationsAsync(
        string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<ManagedApplicationDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var query = _db.ManagedApplications.AsNoTracking().Where(x => x.CompanyId == companyId);
        var normalizedSearch = TrimToNull(search);
        if (normalizedSearch is not null)
        {
            query = query.Where(x => x.Code.Contains(normalizedSearch) || x.Name.Contains(normalizedSearch));
        }

        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Code)
            .Skip((safePageIndex - 1) * safePageSize).Take(safePageSize)
            .Select(x => ToApplicationDto(x)).ToListAsync(ct);

        return Result<PaginatedResult<ManagedApplicationDto>>.Success(
            new PaginatedResult<ManagedApplicationDto>(items, totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<ManagedApplicationDto>> CreateApplicationAsync(
        UpsertManagedApplicationRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ManagedApplicationDto>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var validation = await ValidateApplicationRequestAsync(companyId, request, ct);
        if (validation.Error is not null)
            return Result<ManagedApplicationDto>.Failure(validation.Error);

        if (await _db.ManagedApplications.AnyAsync(x => x.CompanyId == companyId && x.Code == validation.Code, ct))
            return Result<ManagedApplicationDto>.Failure("Mã ứng dụng này đã tồn tại trong công ty hiện tại.");

        var application = new ManagedApplication
        {
            CompanyId = companyId,
            BusinessUnitId = request.BusinessUnitId,
            Code = validation.Code!,
            Name = validation.Name!,
            BaseUrl = validation.BaseUrl!,
            Description = TrimToNull(request.Description),
            Status = request.Status,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Actor()
        };
        _db.ManagedApplications.Add(application);
        await _db.SaveChangesAsync(ct);
        return Result<ManagedApplicationDto>.Success(ToApplicationDto(application));
    }

    public async Task<Result<ManagedApplicationDto>> UpdateApplicationAsync(
        Guid applicationId, UpsertManagedApplicationRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ManagedApplicationDto>.Failure("Không xác định được công ty của người dùng hiện tại.");

        var application = await FindApplicationAsync(companyId, applicationId, ct);
        if (application is null)
            return Result<ManagedApplicationDto>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");

        var validation = await ValidateApplicationRequestAsync(companyId, request, ct);
        if (validation.Error is not null)
            return Result<ManagedApplicationDto>.Failure(validation.Error);

        if (await _db.ManagedApplications.AnyAsync(x => x.CompanyId == companyId && x.Code == validation.Code && x.Id != applicationId, ct))
            return Result<ManagedApplicationDto>.Failure("Mã ứng dụng này đã tồn tại trong công ty hiện tại.");

        application.BusinessUnitId = request.BusinessUnitId;
        application.Code = validation.Code!;
        application.Name = validation.Name!;
        application.BaseUrl = validation.BaseUrl!;
        application.Description = TrimToNull(request.Description);
        application.Status = request.Status;
        application.UpdatedAt = DateTime.UtcNow;
        application.UpdatedBy = Actor();
        await _db.SaveChangesAsync(ct);
        return Result<ManagedApplicationDto>.Success(ToApplicationDto(application));
    }

    public async Task<Result<PaginatedResult<ApplicationMembershipDto>>> GetMembershipsAsync(
        Guid applicationId, string? search, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<ApplicationMembershipDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");
        if (await FindApplicationAsync(companyId, applicationId, ct) is null)
            return Result<PaginatedResult<ApplicationMembershipDto>>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");

        var query = _db.ApplicationMemberships.AsNoTracking()
            .Include(x => x.ManagedApplication).Include(x => x.Party)
            .Where(x => x.CompanyId == companyId && x.ManagedApplicationId == applicationId);
        var normalizedSearch = TrimToNull(search);
        if (normalizedSearch is not null)
            query = query.Where(x => x.Party.DisplayName.Contains(normalizedSearch) || (x.ExternalUserId != null && x.ExternalUserId.Contains(normalizedSearch)));

        var hasStatusFilter = !string.IsNullOrWhiteSpace(status);
        if (!TryParseStatus(status, out ApplicationMembershipStatus requestedStatus, out var statusError))
            return Result<PaginatedResult<ApplicationMembershipDto>>.Failure(statusError!);
        if (hasStatusFilter)
            query = query.Where(x => x.Status == requestedStatus);

        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Party.DisplayName)
            .Skip((safePageIndex - 1) * safePageSize).Take(safePageSize).ToListAsync(ct);
        return Result<PaginatedResult<ApplicationMembershipDto>>.Success(
            new PaginatedResult<ApplicationMembershipDto>(items.Select(ToMembershipDto).ToList(), totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<ApplicationMembershipDto>> UpsertMembershipAsync(
        Guid applicationId, UpsertApplicationMembershipRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationMembershipDto>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var application = await FindApplicationAsync(companyId, applicationId, ct);
        if (application is null)
            return Result<ApplicationMembershipDto>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");
        if (request.Status == ApplicationMembershipStatus.Active && application.Status != ManagedApplicationStatus.Active)
            return Result<ApplicationMembershipDto>.Failure("Chỉ có thể kích hoạt membership khi ứng dụng đang Active.");
        if (!await _db.Parties.AnyAsync(x => x.Id == request.PartyId && x.CompanyId == companyId && !x.IsDeleted, ct))
            return Result<ApplicationMembershipDto>.Failure("Không tìm thấy hồ sơ người dùng trung tâm (Party) trong công ty hiện tại.");
        if (!await IsValidBusinessUnitAsync(companyId, request.BusinessUnitId, ct))
            return Result<ApplicationMembershipDto>.Failure("Business unit không thuộc công ty hiện tại.");

        var externalUserId = TrimToNull(request.ExternalUserId);
        if (externalUserId?.Length > 250)
            return Result<ApplicationMembershipDto>.Failure("External user ID vượt quá 250 ký tự.");

        var membership = await _db.ApplicationMemberships
            .Include(x => x.Party).Include(x => x.ManagedApplication)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ManagedApplicationId == applicationId && x.PartyId == request.PartyId, ct);

        if (externalUserId is not null && await _db.ApplicationMemberships.AnyAsync(x =>
                x.CompanyId == companyId && x.ManagedApplicationId == applicationId &&
                x.ExternalUserId == externalUserId && (membership == null || x.Id != membership.Id), ct))
            return Result<ApplicationMembershipDto>.Failure("External user ID này đã liên kết với một Party khác trong ứng dụng.");

        var now = DateTime.UtcNow;
        if (membership is null)
        {
            var party = await _db.Parties.FirstAsync(x => x.Id == request.PartyId, ct);
            membership = new ApplicationMembership
            {
                CompanyId = companyId,
                BusinessUnitId = request.BusinessUnitId ?? party.BusinessUnitId ?? application.BusinessUnitId,
                ManagedApplicationId = applicationId,
                ManagedApplication = application,
                PartyId = party.Id,
                Party = party,
                CreatedAt = now,
                CreatedBy = Actor()
            };
            _db.ApplicationMemberships.Add(membership);
        }

        membership.ExternalUserId = externalUserId;
        membership.Status = request.Status;
        membership.ActivatedAt = request.Status == ApplicationMembershipStatus.Active ? now : membership.ActivatedAt;
        membership.RevokedAt = request.Status == ApplicationMembershipStatus.Revoked ? now : null;
        membership.RevocationReason = request.Status == ApplicationMembershipStatus.Revoked ? TrimToNull(request.RevocationReason) : null;
        membership.UpdatedAt = now;
        membership.UpdatedBy = Actor();
        await _db.SaveChangesAsync(ct);
        return Result<ApplicationMembershipDto>.Success(ToMembershipDto(membership));
    }

    public async Task<Result<ApplicationRoleAssignmentDto>> UpsertRoleAssignmentAsync(
        Guid membershipId, UpsertApplicationRoleAssignmentRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationRoleAssignmentDto>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var membership = await _db.ApplicationMemberships
            .Include(x => x.ManagedApplication)
            .FirstOrDefaultAsync(x => x.Id == membershipId && x.CompanyId == companyId, ct);
        if (membership is null)
            return Result<ApplicationRoleAssignmentDto>.Failure("Không tìm thấy membership trong phạm vi công ty hiện tại.");

        var roleCode = TrimToNull(request.RoleCode)?.ToUpperInvariant();
        if (roleCode is null || roleCode.Length > 100)
            return Result<ApplicationRoleAssignmentDto>.Failure("Role code là bắt buộc và tối đa 100 ký tự.");
        if (request.ValidUntil.HasValue && request.ValidFrom.HasValue && request.ValidUntil <= request.ValidFrom)
            return Result<ApplicationRoleAssignmentDto>.Failure("ValidUntil phải sau ValidFrom.");
        if (!await IsValidRoleScopeAsync(companyId, request.ScopeType, request.ScopeId, ct))
            return Result<ApplicationRoleAssignmentDto>.Failure("Scope role không hợp lệ hoặc không thuộc công ty hiện tại.");
        if (request.Status == ApplicationRoleAssignmentStatus.Active && membership.Status != ApplicationMembershipStatus.Active)
            return Result<ApplicationRoleAssignmentDto>.Failure("Chỉ membership Active mới có thể nhận role Active.");

        var role = await _db.ApplicationRoleAssignments.FirstOrDefaultAsync(x =>
            x.ApplicationMembershipId == membershipId && x.RoleCode == roleCode &&
            x.ScopeType == request.ScopeType && x.ScopeId == request.ScopeId, ct);
        var now = DateTime.UtcNow;
        if (role is null)
        {
            role = new ApplicationRoleAssignment
            {
                CompanyId = companyId,
                BusinessUnitId = membership.BusinessUnitId,
                ApplicationMembershipId = membershipId,
                RoleCode = roleCode,
                ScopeType = request.ScopeType,
                ScopeId = request.ScopeId,
                CreatedAt = now,
                CreatedBy = Actor()
            };
            _db.ApplicationRoleAssignments.Add(role);
        }

        role.Status = request.Status;
        role.ValidFrom = request.ValidFrom;
        role.ValidUntil = request.ValidUntil;
        role.RevokedAt = request.Status == ApplicationRoleAssignmentStatus.Revoked ? now : null;
        role.RevocationReason = request.Status == ApplicationRoleAssignmentStatus.Revoked ? TrimToNull(request.RevocationReason) : null;
        role.UpdatedAt = now;
        role.UpdatedBy = Actor();
        await _db.SaveChangesAsync(ct);
        return Result<ApplicationRoleAssignmentDto>.Success(ToRoleDto(role));
    }

    public async Task<Result<PaginatedResult<ApplicationCourseMapDto>>> GetCourseMapsAsync(
        Guid applicationId, int pageIndex, int pageSize, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<ApplicationCourseMapDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");
        if (await FindApplicationAsync(companyId, applicationId, ct) is null)
            return Result<PaginatedResult<ApplicationCourseMapDto>>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");

        var query = _db.ApplicationCourseMaps.AsNoTracking().Include(x => x.Course)
            .Where(x => x.CompanyId == companyId && x.ManagedApplicationId == applicationId);
        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Course.Title).ThenBy(x => x.ExternalCourseId)
            .Skip((safePageIndex - 1) * safePageSize).Take(safePageSize).ToListAsync(ct);
        return Result<PaginatedResult<ApplicationCourseMapDto>>.Success(
            new PaginatedResult<ApplicationCourseMapDto>(items.Select(ToCourseMapDto).ToList(), totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<ApplicationCourseMapDto>> UpsertCourseMapAsync(
        Guid applicationId, Guid courseId, UpsertApplicationCourseMapRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationCourseMapDto>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var application = await FindApplicationAsync(companyId, applicationId, ct);
        if (application is null)
            return Result<ApplicationCourseMapDto>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");
        var course = await _db.Courses.FirstOrDefaultAsync(x => x.Id == courseId && x.CompanyId == companyId && !x.IsDeleted, ct);
        if (course is null)
            return Result<ApplicationCourseMapDto>.Failure("Không tìm thấy khóa học quản lý trong phạm vi công ty hiện tại.");

        var externalCourseId = TrimToNull(request.ExternalCourseId) ?? TrimToNull(course.CourseSourceId);
        var slug = TrimToNull(request.ExternalCourseSlug);
        if (externalCourseId is null || externalCourseId.Length > 200 || slug?.Length > 250)
            return Result<ApplicationCourseMapDto>.Failure("External course ID là bắt buộc và phải đúng giới hạn độ dài.");
        if (request.Activate && application.Status != ManagedApplicationStatus.Active)
            return Result<ApplicationCourseMapDto>.Failure("Chỉ có thể kích hoạt mapping khi ứng dụng đang Active.");
        if (request.Activate && request.ExternalCourseNumericId is null && slug is null)
            return Result<ApplicationCourseMapDto>.Failure("Để kích hoạt mapping, cần xác nhận external numeric ID hoặc slug của khóa học web con.");

        var mapping = await _db.ApplicationCourseMaps.Include(x => x.Course).FirstOrDefaultAsync(x =>
            x.CompanyId == companyId && x.ManagedApplicationId == applicationId && x.CourseId == courseId, ct);
        if (await _db.ApplicationCourseMaps.AnyAsync(x => x.CompanyId == companyId && x.ManagedApplicationId == applicationId &&
                x.ExternalCourseId == externalCourseId && (mapping == null || x.Id != mapping.Id), ct))
            return Result<ApplicationCourseMapDto>.Failure("External course ID này đã được dùng trong ứng dụng.");
        if (request.ExternalCourseNumericId.HasValue && await _db.ApplicationCourseMaps.AnyAsync(x => x.CompanyId == companyId &&
                x.ManagedApplicationId == applicationId && x.ExternalCourseNumericId == request.ExternalCourseNumericId &&
                (mapping == null || x.Id != mapping.Id), ct))
            return Result<ApplicationCourseMapDto>.Failure("External numeric course ID này đã được dùng trong ứng dụng.");
        if (mapping is not null && !string.Equals(mapping.ExternalCourseId, externalCourseId, StringComparison.Ordinal) &&
            await _db.ApplicationEntitlements.AnyAsync(x => x.ApplicationCourseMapId == mapping.Id, ct))
            return Result<ApplicationCourseMapDto>.Failure("Không thể đổi external course ID sau khi đã phát sinh entitlement.");

        var now = DateTime.UtcNow;
        if (mapping is null)
        {
            mapping = new ApplicationCourseMap
            {
                CompanyId = companyId,
                BusinessUnitId = course.BusinessUnitId ?? application.BusinessUnitId,
                ManagedApplicationId = applicationId,
                CourseId = courseId,
                Course = course,
                CreatedAt = now,
                CreatedBy = Actor()
            };
            _db.ApplicationCourseMaps.Add(mapping);
        }
        mapping.ExternalCourseId = externalCourseId;
        mapping.ExternalCourseNumericId = request.ExternalCourseNumericId;
        mapping.ExternalCourseSlug = slug;
        mapping.Status = request.Activate ? IntegrationStatus.Success : IntegrationStatus.Pending;
        mapping.ActivatedAt = request.Activate ? now : null;
        mapping.LastSyncError = null;
        mapping.UpdatedAt = now;
        mapping.UpdatedBy = Actor();
        await _db.SaveChangesAsync(ct);
        return Result<ApplicationCourseMapDto>.Success(ToCourseMapDto(mapping));
    }

    public async Task<Result<PaginatedResult<ApplicationClassMapDto>>> GetClassMapsAsync(
        Guid applicationId, int pageIndex, int pageSize, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<ApplicationClassMapDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");
        if (await FindApplicationAsync(companyId, applicationId, ct) is null)
            return Result<PaginatedResult<ApplicationClassMapDto>>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");

        var query = _db.ApplicationClassMaps.AsNoTracking().Include(x => x.CscaClass)
            .Where(x => x.CompanyId == companyId && x.ManagedApplicationId == applicationId);
        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.CscaClass.Name).ThenBy(x => x.CscaClass.Code)
            .Skip((safePageIndex - 1) * safePageSize).Take(safePageSize).ToListAsync(ct);
        return Result<PaginatedResult<ApplicationClassMapDto>>.Success(
            new PaginatedResult<ApplicationClassMapDto>(items.Select(ToClassMapDto).ToList(), totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<ApplicationClassMapDto>> UpsertClassMapAsync(
        Guid applicationId, Guid cscaClassId, UpsertApplicationClassMapRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationClassMapDto>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var application = await FindApplicationAsync(companyId, applicationId, ct);
        if (application is null)
            return Result<ApplicationClassMapDto>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");
        var cscaClass = await _db.CscaClasses.FirstOrDefaultAsync(x => x.Id == cscaClassId && x.CompanyId == companyId && !x.IsDeleted, ct);
        if (cscaClass is null)
            return Result<ApplicationClassMapDto>.Failure("Không tìm thấy lớp CSCA trong phạm vi công ty hiện tại.");

        var externalClassId = TrimToNull(request.ExternalClassId);
        if (externalClassId is null || externalClassId.Length > 200)
            return Result<ApplicationClassMapDto>.Failure("External class ID là bắt buộc và tối đa 200 ký tự.");
        ApplicationCourseMap? courseMap = null;
        if (request.CourseMapId.HasValue)
        {
            courseMap = await _db.ApplicationCourseMaps.FirstOrDefaultAsync(x => x.Id == request.CourseMapId &&
                x.CompanyId == companyId && x.ManagedApplicationId == applicationId, ct);
            if (courseMap is null || courseMap.CourseId != cscaClass.CourseId)
                return Result<ApplicationClassMapDto>.Failure("Course mapping phải thuộc cùng ứng dụng và cùng khóa học với lớp CSCA.");
        }
        if (request.Activate && (application.Status != ManagedApplicationStatus.Active || courseMap?.Status != IntegrationStatus.Success))
            return Result<ApplicationClassMapDto>.Failure("Để kích hoạt class mapping, ứng dụng và course mapping phải đang Active/Success.");

        var mapping = await _db.ApplicationClassMaps.Include(x => x.CscaClass).FirstOrDefaultAsync(x =>
            x.CompanyId == companyId && x.ManagedApplicationId == applicationId && x.CscaClassId == cscaClassId, ct);
        if (await _db.ApplicationClassMaps.AnyAsync(x => x.CompanyId == companyId && x.ManagedApplicationId == applicationId &&
                x.ExternalClassId == externalClassId && (mapping == null || x.Id != mapping.Id), ct))
            return Result<ApplicationClassMapDto>.Failure("External class ID này đã được dùng trong ứng dụng.");
        if (mapping is not null && !string.Equals(mapping.ExternalClassId, externalClassId, StringComparison.Ordinal) &&
            await _db.ApplicationEntitlements.AnyAsync(x => x.ApplicationClassMapId == mapping.Id, ct))
            return Result<ApplicationClassMapDto>.Failure("Không thể đổi external class ID sau khi đã phát sinh entitlement.");

        var now = DateTime.UtcNow;
        if (mapping is null)
        {
            mapping = new ApplicationClassMap
            {
                CompanyId = companyId,
                BusinessUnitId = cscaClass.BusinessUnitId ?? application.BusinessUnitId,
                ManagedApplicationId = applicationId,
                CscaClassId = cscaClassId,
                CscaClass = cscaClass,
                CreatedAt = now,
                CreatedBy = Actor()
            };
            _db.ApplicationClassMaps.Add(mapping);
        }
        mapping.ApplicationCourseMapId = courseMap?.Id;
        mapping.ApplicationCourseMap = courseMap;
        mapping.ExternalClassId = externalClassId;
        mapping.Status = request.Activate ? IntegrationStatus.Success : IntegrationStatus.Pending;
        mapping.ActivatedAt = request.Activate ? now : null;
        mapping.LastSyncError = null;
        mapping.UpdatedAt = now;
        mapping.UpdatedBy = Actor();
        await _db.SaveChangesAsync(ct);
        return Result<ApplicationClassMapDto>.Success(ToClassMapDto(mapping));
    }

    public async Task<Result<PaginatedResult<ApplicationEntitlementDto>>> GetEntitlementsAsync(
        Guid applicationId, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<ApplicationEntitlementDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");
        if (await FindApplicationAsync(companyId, applicationId, ct) is null)
            return Result<PaginatedResult<ApplicationEntitlementDto>>.Failure("Không tìm thấy ứng dụng trong phạm vi công ty hiện tại.");

        var query = _db.ApplicationEntitlements.AsNoTracking()
            .Include(x => x.ApplicationMembership).ThenInclude(x => x.ManagedApplication)
            .Include(x => x.ApplicationCourseMap)
            .Where(x => x.CompanyId == companyId && x.ApplicationMembership.ManagedApplicationId == applicationId);
        var hasStatusFilter = !string.IsNullOrWhiteSpace(status);
        if (!TryParseStatus(status, out ApplicationEntitlementStatus requestedStatus, out var statusError))
            return Result<PaginatedResult<ApplicationEntitlementDto>>.Failure(statusError!);
        if (hasStatusFilter)
            query = query.Where(x => x.Status == requestedStatus);

        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Skip((safePageIndex - 1) * safePageSize).Take(safePageSize).ToListAsync(ct);
        return Result<PaginatedResult<ApplicationEntitlementDto>>.Success(
            new PaginatedResult<ApplicationEntitlementDto>(items.Select(ToEntitlementDto).ToList(), totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<ApplicationEntitlementDto>> UpsertEntitlementAsync(
        Guid applicationId, Guid membershipId, UpsertApplicationEntitlementRequest request, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationEntitlementDto>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var membership = await _db.ApplicationMemberships.Include(x => x.ManagedApplication).Include(x => x.Party)
            .FirstOrDefaultAsync(x => x.Id == membershipId && x.CompanyId == companyId && x.ManagedApplicationId == applicationId, ct);
        if (membership is null)
            return Result<ApplicationEntitlementDto>.Failure("Không tìm thấy membership trong ứng dụng hiện tại.");
        var courseMap = await _db.ApplicationCourseMaps.FirstOrDefaultAsync(x => x.Id == request.CourseMapId && x.CompanyId == companyId &&
            x.ManagedApplicationId == applicationId, ct);
        if (courseMap is null)
            return Result<ApplicationEntitlementDto>.Failure("Không tìm thấy course mapping trong ứng dụng hiện tại.");
        ApplicationClassMap? classMap = null;
        if (request.ClassMapId.HasValue)
        {
            classMap = await _db.ApplicationClassMaps.FirstOrDefaultAsync(x => x.Id == request.ClassMapId && x.CompanyId == companyId &&
                x.ManagedApplicationId == applicationId && x.ApplicationCourseMapId == courseMap.Id, ct);
            if (classMap is null)
                return Result<ApplicationEntitlementDto>.Failure("Class mapping không thuộc course mapping đã chọn.");
        }
        if (request.ValidUntil.HasValue && request.ValidFrom.HasValue && request.ValidUntil <= request.ValidFrom)
            return Result<ApplicationEntitlementDto>.Failure("ValidUntil phải sau ValidFrom.");
        if (request.Status == ApplicationEntitlementStatus.Active &&
            (membership.Status != ApplicationMembershipStatus.Active || membership.ManagedApplication.Status != ManagedApplicationStatus.Active ||
             courseMap.Status != IntegrationStatus.Success || (classMap is not null && classMap.Status != IntegrationStatus.Success)))
            return Result<ApplicationEntitlementDto>.Failure("Entitlement Active cần ứng dụng/membership Active và các mapping Success.");

        CscaClassStudent? classStudent = null;
        if (request.CscaClassStudentId.HasValue)
        {
            classStudent = await _db.CscaClassStudents.Include(x => x.Class).FirstOrDefaultAsync(x => x.Id == request.CscaClassStudentId, ct);
            if (classStudent is null || classStudent.Class.CompanyId != companyId)
                return Result<ApplicationEntitlementDto>.Failure("Không tìm thấy học viên lớp CSCA trong phạm vi công ty hiện tại.");
            if (classMap is not null && classStudent.ClassId != classMap.CscaClassId)
                return Result<ApplicationEntitlementDto>.Failure("Học viên phải thuộc đúng lớp đã map vào ứng dụng.");
            if (classStudent.PartyId != membership.PartyId)
                return Result<ApplicationEntitlementDto>.Failure("Học viên lớp CSCA phải được liên kết với đúng Party của membership trước khi cấp quyền.");
        }

        var entitlement = await _db.ApplicationEntitlements
            .Include(x => x.ApplicationMembership).ThenInclude(x => x.ManagedApplication)
            .Include(x => x.ApplicationCourseMap)
            .FirstOrDefaultAsync(x => x.ApplicationMembershipId == membershipId && x.ApplicationCourseMapId == courseMap.Id &&
                x.ApplicationClassMapId == request.ClassMapId, ct);
        var now = DateTime.UtcNow;
        var validFrom = request.ValidFrom ?? entitlement?.ValidFrom ?? now;
        var sourcePaymentId = TrimToNull(request.SourcePaymentId);
        var reason = TrimToNull(request.Reason);
        if (sourcePaymentId?.Length > 200 || reason?.Length > 500)
            return Result<ApplicationEntitlementDto>.Failure("Thông tin payment/reason vượt quá giới hạn độ dài.");

        var changed = entitlement is null;
        if (entitlement is null)
        {
            entitlement = new ApplicationEntitlement
            {
                CompanyId = companyId,
                BusinessUnitId = membership.BusinessUnitId,
                ApplicationMembershipId = membershipId,
                ApplicationMembership = membership,
                ApplicationCourseMapId = courseMap.Id,
                ApplicationCourseMap = courseMap,
                ApplicationClassMapId = classMap?.Id,
                ApplicationClassMap = classMap,
                CscaClassStudentId = classStudent?.Id,
                CscaClassStudent = classStudent,
                CreatedAt = now,
                CreatedBy = Actor()
            };
            _db.ApplicationEntitlements.Add(entitlement);
        }
        else
        {
            changed = entitlement.Status != request.Status || entitlement.SourcePaymentId != sourcePaymentId ||
                      entitlement.Reason != reason || entitlement.ValidFrom != validFrom || entitlement.ValidUntil != request.ValidUntil ||
                      entitlement.CscaClassStudentId != classStudent?.Id;
        }

        entitlement.Status = request.Status;
        entitlement.SourcePaymentId = sourcePaymentId;
        entitlement.Reason = reason;
        entitlement.ValidFrom = validFrom;
        entitlement.ValidUntil = request.ValidUntil;
        entitlement.CscaClassStudentId = classStudent?.Id;
        entitlement.RevokedAt = request.Status == ApplicationEntitlementStatus.Revoked ? now : null;
        entitlement.LastSyncError = null;
        entitlement.UpdatedAt = now;
        entitlement.UpdatedBy = Actor();

        if (changed)
            QueueEntitlementCommand(applicationId, membership, courseMap, classMap, entitlement, now);

        await _db.SaveChangesAsync(ct);
        return Result<ApplicationEntitlementDto>.Success(ToEntitlementDto(entitlement));
    }

    public async Task<Result<PaginatedResult<ApplicationDeliveryItemDto>>> GetDeliveryQueueAsync(
        string? applicationCode, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<PaginatedResult<ApplicationDeliveryItemDto>>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var query = _db.IntegrationOutboxes.AsNoTracking().Where(x => x.CompanyId == companyId && x.SourceSystem.StartsWith(ApplicationSourcePrefix));
        var code = NormalizeApplicationCode(applicationCode, required: false, out var codeError);
        if (codeError is not null)
            return Result<PaginatedResult<ApplicationDeliveryItemDto>>.Failure(codeError);
        if (code is not null)
            query = query.Where(x => x.SourceSystem == SourceSystemFor(code));
        var hasStatusFilter = !string.IsNullOrWhiteSpace(status);
        if (!TryParseStatus(status, out IntegrationStatus requestedStatus, out var statusError))
            return Result<PaginatedResult<ApplicationDeliveryItemDto>>.Failure(statusError!);
        if (hasStatusFilter)
            query = query.Where(x => x.Status == requestedStatus);

        var (safePageIndex, safePageSize) = NormalizePagination(pageIndex, pageSize);
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedAt).Skip((safePageIndex - 1) * safePageSize)
            .Take(safePageSize).ToListAsync(ct);
        return Result<PaginatedResult<ApplicationDeliveryItemDto>>.Success(
            new PaginatedResult<ApplicationDeliveryItemDto>(items.Select(ToDeliveryDto).ToList(), totalCount, safePageIndex, safePageSize));
    }

    public async Task<Result<ApplicationDeliveryItemDto>> RetryDeliveryAsync(Guid outboxId, CancellationToken ct)
    {
        if (!TryGetCompanyId(out var companyId))
            return Result<ApplicationDeliveryItemDto>.Failure("Không xác định được công ty của người dùng hiện tại.");
        var item = await _db.IntegrationOutboxes.FirstOrDefaultAsync(x => x.Id == outboxId && x.CompanyId == companyId &&
            x.SourceSystem.StartsWith(ApplicationSourcePrefix), ct);
        if (item is null)
            return Result<ApplicationDeliveryItemDto>.Failure("Không tìm thấy application delivery trong phạm vi công ty hiện tại.");
        if (item.Status == IntegrationStatus.Success)
            return Result<ApplicationDeliveryItemDto>.Failure("Delivery đã thành công, không cần retry.");
        if (item.Status == IntegrationStatus.Processing)
            return Result<ApplicationDeliveryItemDto>.Failure("Delivery đang được xử lý, không thể retry đồng thời.");

        item.Status = IntegrationStatus.Pending;
        item.AttemptCount = 0;
        item.NextAttemptAt = DateTime.UtcNow;
        item.LastError = null;
        item.UpdatedAt = DateTime.UtcNow;
        item.UpdatedBy = Actor();
        await _db.SaveChangesAsync(ct);
        return Result<ApplicationDeliveryItemDto>.Success(ToDeliveryDto(item));
    }

    private async Task<(string? Code, string? Name, string? BaseUrl, string? Error)> ValidateApplicationRequestAsync(
        Guid companyId, UpsertManagedApplicationRequest request, CancellationToken ct)
    {
        var code = NormalizeApplicationCode(request.Code, required: true, out var codeError);
        if (codeError is not null)
            return (null, null, null, codeError);
        var name = TrimToNull(request.Name);
        if (name is null || name.Length > 200)
            return (null, null, null, "Tên ứng dụng là bắt buộc và tối đa 200 ký tự.");
        var baseUrl = TrimToNull(request.BaseUrl);
        if (baseUrl is null || baseUrl.Length > 500 || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return (null, null, null, "Base URL phải là địa chỉ HTTP/HTTPS hợp lệ.");
        if (!await IsValidBusinessUnitAsync(companyId, request.BusinessUnitId, ct))
            return (null, null, null, "Business unit không thuộc công ty hiện tại.");
        return (code, name, baseUrl.TrimEnd('/'), null);
    }

    private async Task<ManagedApplication?> FindApplicationAsync(Guid companyId, Guid applicationId, CancellationToken ct) =>
        await _db.ManagedApplications.FirstOrDefaultAsync(x => x.Id == applicationId && x.CompanyId == companyId, ct);

    private async Task<bool> IsValidBusinessUnitAsync(Guid companyId, Guid? businessUnitId, CancellationToken ct) =>
        !businessUnitId.HasValue || await _db.BusinessUnits.AnyAsync(x => x.Id == businessUnitId && x.CompanyId == companyId && !x.IsDeleted, ct);

    private async Task<bool> IsValidRoleScopeAsync(Guid companyId, ApplicationScopeType scopeType, Guid? scopeId, CancellationToken ct)
    {
        if (scopeType == ApplicationScopeType.Application)
            return !scopeId.HasValue;
        if (!scopeId.HasValue)
            return false;
        return scopeType switch
        {
            ApplicationScopeType.Course => await _db.Courses.AnyAsync(x => x.Id == scopeId && x.CompanyId == companyId && !x.IsDeleted, ct),
            ApplicationScopeType.Class => await _db.CscaClasses.AnyAsync(x => x.Id == scopeId && x.CompanyId == companyId && !x.IsDeleted, ct),
            _ => false
        };
    }

    private void QueueEntitlementCommand(
        Guid applicationId,
        ApplicationMembership membership,
        ApplicationCourseMap courseMap,
        ApplicationClassMap? classMap,
        ApplicationEntitlement entitlement,
        DateTime now)
    {
        var sourceSystem = SourceSystemFor(membership.ManagedApplication.Code);
        var correlationId = Guid.NewGuid().ToString("N");
        entitlement.LastCorrelationId = correlationId;
        var versionKey = $"{entitlement.Status}:{entitlement.ValidFrom.Ticks}:{entitlement.ValidUntil?.Ticks}:{entitlement.CscaClassStudentId}";
        var command = new
        {
            Version = 1,
            Type = "entitlement.upserted",
            ApplicationId = applicationId,
            ApplicationCode = membership.ManagedApplication.Code,
            EntitlementId = entitlement.Id,
            MembershipId = membership.Id,
            PartyId = membership.PartyId,
            ExternalUserId = membership.ExternalUserId,
            Course = new { courseMap.ExternalCourseId, courseMap.ExternalCourseNumericId, courseMap.ExternalCourseSlug },
            ExternalClassId = classMap?.ExternalClassId,
            Status = entitlement.Status.ToString(),
            entitlement.SourcePaymentId,
            entitlement.ValidFrom,
            entitlement.ValidUntil,
            CorrelationId = correlationId
        };
        _db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = entitlement.CompanyId,
            BusinessUnitId = entitlement.BusinessUnitId,
            SourceSystem = sourceSystem,
            EventId = Guid.NewGuid().ToString("N"),
            EventType = "Entitlement.Upserted",
            AggregateType = nameof(ApplicationEntitlement),
            AggregateId = entitlement.Id.ToString("N"),
            IdempotencyKey = $"application-entitlement:{entitlement.Id:N}:{versionKey}",
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(command, JsonOptions),
            Status = IntegrationStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now,
            CreatedBy = Actor()
        });
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

    private string Actor() => _currentUser.Username ?? "System";
    private static (int PageIndex, int PageSize) NormalizePagination(int pageIndex, int pageSize) =>
        (Math.Max(1, pageIndex), Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaximumPageSize));
    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string SourceSystemFor(string applicationCode) => $"{ApplicationSourcePrefix}{applicationCode}";

    private static string? NormalizeApplicationCode(string? value, bool required, out string? error)
    {
        var code = TrimToNull(value)?.ToUpperInvariant();
        if (code is null)
        {
            error = required ? "Mã ứng dụng là bắt buộc." : null;
            return null;
        }
        if (code.Length < 3 || code.Length > 100 || code.Any(x => !(char.IsAsciiLetterOrDigit(x) || x == '_')))
        {
            error = "Mã ứng dụng dùng 3-100 ký tự A-Z, 0-9 hoặc dấu gạch dưới.";
            return null;
        }
        error = null;
        return code;
    }

    private static bool TryParseStatus<TEnum>(string? raw, out TEnum status, out string? error) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            status = default;
            error = null;
            return true;
        }
        if (Enum.TryParse<TEnum>(raw, true, out status) && Enum.IsDefined(status))
        {
            error = null;
            return true;
        }
        status = default;
        error = "Trạng thái không hợp lệ.";
        return false;
    }

    private static ManagedApplicationDto ToApplicationDto(ManagedApplication value) => new()
    {
        Id = value.Id, Code = value.Code, Name = value.Name, BaseUrl = value.BaseUrl, Description = value.Description,
        Status = value.Status.ToString(), BusinessUnitId = value.BusinessUnitId, CreatedAt = value.CreatedAt, UpdatedAt = value.UpdatedAt
    };

    private static ApplicationMembershipDto ToMembershipDto(ApplicationMembership value) => new()
    {
        Id = value.Id, ApplicationId = value.ManagedApplicationId, ApplicationCode = value.ManagedApplication.Code,
        PartyId = value.PartyId, PartyName = value.Party.DisplayName, ExternalUserId = value.ExternalUserId,
        Status = value.Status.ToString(), ActivatedAt = value.ActivatedAt, RevokedAt = value.RevokedAt,
        RevocationReason = value.RevocationReason, BusinessUnitId = value.BusinessUnitId
    };

    private static ApplicationRoleAssignmentDto ToRoleDto(ApplicationRoleAssignment value) => new()
    {
        Id = value.Id, MembershipId = value.ApplicationMembershipId, RoleCode = value.RoleCode,
        ScopeType = value.ScopeType.ToString(), ScopeId = value.ScopeId, Status = value.Status.ToString(),
        ValidFrom = value.ValidFrom, ValidUntil = value.ValidUntil, RevokedAt = value.RevokedAt, RevocationReason = value.RevocationReason
    };

    private static ApplicationCourseMapDto ToCourseMapDto(ApplicationCourseMap value) => new()
    {
        Id = value.Id, ApplicationId = value.ManagedApplicationId, CourseId = value.CourseId,
        CourseSourceId = value.Course.CourseSourceId, CourseTitle = value.Course.Title, ExternalCourseId = value.ExternalCourseId,
        ExternalCourseNumericId = value.ExternalCourseNumericId, ExternalCourseSlug = value.ExternalCourseSlug,
        Status = value.Status.ToString(), ActivatedAt = value.ActivatedAt, LastSyncError = value.LastSyncError
    };

    private static ApplicationClassMapDto ToClassMapDto(ApplicationClassMap value) => new()
    {
        Id = value.Id, ApplicationId = value.ManagedApplicationId, CscaClassId = value.CscaClassId,
        ClassCode = value.CscaClass.Code, ClassName = value.CscaClass.Name, CourseMapId = value.ApplicationCourseMapId,
        ExternalClassId = value.ExternalClassId, Status = value.Status.ToString(), ActivatedAt = value.ActivatedAt, LastSyncError = value.LastSyncError
    };

    private static ApplicationEntitlementDto ToEntitlementDto(ApplicationEntitlement value) => new()
    {
        Id = value.Id, MembershipId = value.ApplicationMembershipId, CourseMapId = value.ApplicationCourseMapId,
        ClassMapId = value.ApplicationClassMapId, CscaClassStudentId = value.CscaClassStudentId,
        ApplicationCode = value.ApplicationMembership.ManagedApplication.Code, ExternalCourseId = value.ApplicationCourseMap.ExternalCourseId,
        Status = value.Status.ToString(), SourcePaymentId = value.SourcePaymentId, Reason = value.Reason,
        ValidFrom = value.ValidFrom, ValidUntil = value.ValidUntil, RevokedAt = value.RevokedAt,
        LastSyncedAt = value.LastSyncedAt, LastSyncError = value.LastSyncError
    };

    private static ApplicationDeliveryItemDto ToDeliveryDto(IntegrationOutbox value) => new()
    {
        Id = value.Id, ApplicationCode = value.SourceSystem[ApplicationSourcePrefix.Length..], EventType = value.EventType,
        AggregateType = value.AggregateType, AggregateId = value.AggregateId, Status = value.Status.ToString(),
        AttemptCount = value.AttemptCount, CreatedAt = value.CreatedAt, NextAttemptAt = value.NextAttemptAt,
        PublishedAt = value.PublishedAt, CorrelationId = value.CorrelationId, LastError = value.LastError
    };
}
