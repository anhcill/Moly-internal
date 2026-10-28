using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.CscaInterview;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InternalManagement.Infrastructure.Services;

public sealed class CscaTeacherCostSnapshotService : ICscaTeacherCostSnapshotService
{
    private const string SourceSystem = "CSCA_COURSE";
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ICscaCourseLmsClient _lmsClient;
    private readonly ILogger<CscaTeacherCostSnapshotService> _logger;

    public CscaTeacherCostSnapshotService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ICscaCourseLmsClient lmsClient,
        ILogger<CscaTeacherCostSnapshotService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _lmsClient = lmsClient;
        _logger = logger;
    }

    public async Task<Result<CscaTeacherCostSyncResultDto>> SyncAsync(
        SyncCscaTeacherCostsRequest request,
        CancellationToken ct)
    {
        var validation = ValidatePeriod(request.From, request.To);
        if (validation != null)
            return Result<CscaTeacherCostSyncResultDto>.Failure(validation);

        var companyId = await GetCompanyIdAsync(ct);
        if (companyId == Guid.Empty)
            return Result<CscaTeacherCostSyncResultDto>.Failure("Không xác định được công ty để đồng bộ chi phí giảng viên.");

        LmsTeacherCostReport report;
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            report = await _lmsClient.QueryTeacherCostsAsync(
                new LmsTeacherCostQuery(
                    request.From,
                    request.To,
                    Normalize(request.CourseSourceId),
                    Normalize(request.ClassSourceId),
                    Normalize(request.TeacherSourceId),
                    request.IncludeProjected),
                new LmsOutboundRequestContext($"payroll-query:{correlationId}", correlationId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Unable to import CSCA Course teacher costs for company {CompanyId}", companyId);
            return Result<CscaTeacherCostSyncResultDto>.Failure(ex.Message);
        }

        if (!string.Equals(report.SourceSystem, SourceSystem, StringComparison.OrdinalIgnoreCase))
            return Result<CscaTeacherCostSyncResultDto>.Failure("Nguồn báo cáo lương không phải CSCA Course.");

        var scopeQuery = _db.CscaTeacherCostSnapshots
            .Where(x => x.CompanyId == companyId && x.SourceSystem == SourceSystem && x.IsCurrent);
        if (request.From.HasValue) scopeQuery = scopeQuery.Where(x => x.StartTime >= request.From.Value);
        if (request.To.HasValue) scopeQuery = scopeQuery.Where(x => x.StartTime < request.To.Value);
        if (!string.IsNullOrWhiteSpace(request.CourseSourceId))
            scopeQuery = scopeQuery.Where(x => x.CourseSourceId == request.CourseSourceId.Trim());
        if (!string.IsNullOrWhiteSpace(request.ClassSourceId))
            scopeQuery = scopeQuery.Where(x => x.ClassSourceId == request.ClassSourceId.Trim());
        if (!string.IsNullOrWhiteSpace(request.TeacherSourceId))
            scopeQuery = scopeQuery.Where(x => x.TeacherSourceId == request.TeacherSourceId.Trim());

        var existingScope = await scopeQuery.ToListAsync(ct);
        var affectedClassSourceIds = existingScope
            .Select(x => x.ClassSourceId)
            .Concat(report.Items.Select(x => x.ClassSourceId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var stale in existingScope) stale.IsCurrent = false;

        var keys = report.Items.Select(BuildSourceKey).Distinct(StringComparer.Ordinal).ToList();
        var existing = keys.Count == 0
            ? new List<CscaTeacherCostSnapshot>()
            : await _db.CscaTeacherCostSnapshots
                .Where(x => x.CompanyId == companyId && x.SourceSystem == SourceSystem && keys.Contains(x.SourceKey))
                .ToListAsync(ct);
        var byKey = existing.ToDictionary(x => x.SourceKey, StringComparer.Ordinal);

        var employeeIds = report.Items
            .Select(x => ParseGuid(x.TeacherSourceId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var validEmployeeIds = employeeIds.Count == 0
            ? new HashSet<Guid>()
            : (await _db.Employees.AsNoTracking()
                .Where(x => x.CompanyId == companyId && employeeIds.Contains(x.Id))
                .Select(x => x.Id)
                .ToListAsync(ct)).ToHashSet();

        var classIds = report.Items
            .Select(x => ParseGuid(x.ClassSourceId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        var classBusinessUnits = classIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _db.CscaClasses.AsNoTracking()
                .Where(x => x.CompanyId == companyId && classIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.BusinessUnitId, ct);

        var now = DateTime.UtcNow;
        var inserted = 0;
        var updated = 0;
        foreach (var item in report.Items)
        {
            var key = BuildSourceKey(item);
            if (!byKey.TryGetValue(key, out var entity))
            {
                entity = new CscaTeacherCostSnapshot
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    SourceSystem = SourceSystem,
                    SourceKey = key
                };
                _db.CscaTeacherCostSnapshots.Add(entity);
                byKey[key] = entity;
                inserted++;
            }
            else
            {
                updated++;
            }

            Apply(entity, item, report.CalculatedAt, now, validEmployeeIds, classBusinessUnits);
        }

        await _db.SaveChangesAsync(ct);
        await RefreshClassProfitAllocationsAsync(companyId, affectedClassSourceIds, ct);
        return Result<CscaTeacherCostSyncResultDto>.Success(new(
            report.Items.Count,
            inserted,
            updated,
            report.CalculatedAt,
            now));
    }

    public async Task<Result<CscaTeacherCostSummaryDto>> GetAsync(
        DateTime? from,
        DateTime? to,
        string? courseSourceId,
        string? classSourceId,
        string? teacherSourceId,
        bool includeProjected,
        CancellationToken ct)
    {
        var validation = ValidatePeriod(from, to);
        if (validation != null)
            return Result<CscaTeacherCostSummaryDto>.Failure(validation);

        var companyId = await GetCompanyIdAsync(ct);
        var query = _db.CscaTeacherCostSnapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SourceSystem == SourceSystem && x.IsCurrent);
        if (from.HasValue) query = query.Where(x => x.StartTime >= from.Value);
        if (to.HasValue) query = query.Where(x => x.StartTime < to.Value);
        if (!string.IsNullOrWhiteSpace(courseSourceId)) query = query.Where(x => x.CourseSourceId == courseSourceId.Trim());
        if (!string.IsNullOrWhiteSpace(classSourceId)) query = query.Where(x => x.ClassSourceId == classSourceId.Trim());
        if (!string.IsNullOrWhiteSpace(teacherSourceId)) query = query.Where(x => x.TeacherSourceId == teacherSourceId.Trim());

        var rows = await query.OrderByDescending(x => x.StartTime).ToListAsync(ct);
        var teachers = rows.GroupBy(x => x.TeacherSourceId ?? $"lms:{x.TeacherLmsId}").ToList();
        var bonus = teachers.Sum(group => group.OrderByDescending(x => x.SourceUpdatedAt ?? x.SourceCalculatedAt).First().BonusAmount);
        var deduction = teachers.Sum(group => group.OrderByDescending(x => x.SourceUpdatedAt ?? x.SourceCalculatedAt).First().DeductionAmount);
        var accrued = rows.Sum(x => x.AccruedAmount);
        var projected = includeProjected ? rows.Sum(x => x.ProjectedAmount ?? 0) : 0;
        var netAccrued = teachers.Sum(group =>
        {
            var settlement = group.OrderByDescending(x => x.SourceUpdatedAt ?? x.SourceCalculatedAt).First();
            return Math.Max(0, group.Sum(x => x.AccruedAmount) + settlement.BonusAmount - settlement.DeductionAmount);
        });
        var classSummaries = rows
            .GroupBy(x => new
            {
                TeacherKey = x.TeacherSourceId ?? $"lms:{x.TeacherLmsId}",
                x.ClassSourceId
            })
            .Select(group => new CscaTeacherCostClassSummaryDto(
                group.Key.ClassSourceId,
                group.First().ClassTitle,
                group.First().CourseSourceId,
                group.First().CourseTitle,
                group.First().TeacherSourceId,
                group.First().TeacherLmsId,
                group.First().TeacherName,
                group.Count(),
                group.Count(x => x.IsCompleted),
                group.Sum(x => x.DurationHours),
                group.Where(x => x.IsCompleted).Sum(x => x.DurationHours),
                group.Sum(x => x.AccruedAmount),
                includeProjected ? group.Sum(x => x.ProjectedAmount ?? 0) : 0))
            .OrderBy(x => x.ClassTitle)
            .ThenBy(x => x.TeacherName)
            .ToList();

        return Result<CscaTeacherCostSummaryDto>.Success(new(
            rows.Count,
            teachers.Count,
            accrued,
            projected,
            bonus,
            deduction,
            netAccrued,
            rows.Count == 0 ? null : rows.Max(x => x.SyncedAt),
            rows.Select(Map).ToList(),
            classSummaries.Select(x => x.ClassSourceId).Distinct(StringComparer.Ordinal).Count(),
            accrued,
            projected,
            "teacher",
            "teacher",
            classSummaries));
    }

    private async Task<Guid> GetCompanyIdAsync(CancellationToken ct)
    {
        if (_currentUser.CompanyId is { } companyId && companyId != Guid.Empty) return companyId;
        return await _db.Companies.AsNoTracking()
            .Where(x => x.Code == "MOLI")
            .Select(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static void Apply(
        CscaTeacherCostSnapshot entity,
        LmsTeacherCostItem item,
        DateTime calculatedAt,
        DateTime syncedAt,
        HashSet<Guid> validEmployeeIds,
        Dictionary<Guid, Guid?> classBusinessUnits)
    {
        var employeeId = ParseGuid(item.TeacherSourceId);
        var classId = ParseGuid(item.ClassSourceId);
        entity.EmployeeId = employeeId.HasValue && validEmployeeIds.Contains(employeeId.Value) ? employeeId : null;
        entity.BusinessUnitId = classId.HasValue && classBusinessUnits.TryGetValue(classId.Value, out var unitId) ? unitId : null;
        entity.TeacherSourceId = Normalize(item.TeacherSourceId);
        entity.TeacherLmsId = item.TeacherLmsId;
        entity.TeacherName = item.TeacherName;
        entity.TeacherUsername = Normalize(item.TeacherUsername);
        entity.CourseSourceId = Normalize(item.CourseSourceId);
        entity.CourseTitle = Normalize(item.CourseTitle);
        entity.ClassSourceId = item.ClassSourceId;
        entity.ClassLmsId = item.ClassLmsId;
        entity.ClassTitle = item.ClassTitle;
        entity.TeachingRole = item.TeachingRole;
        entity.SessionSourceId = item.SessionSourceId;
        entity.SessionLmsId = item.SessionLmsId;
        entity.SessionTitle = Normalize(item.SessionTitle);
        entity.StartTime = item.StartTime;
        entity.EndTime = item.EndTime;
        entity.Status = item.Status;
        entity.IsCompleted = item.IsCompleted;
        entity.DurationHours = item.DurationHours;
        entity.EffectiveHourlyRate = item.EffectiveHourlyRate;
        entity.AccruedAmount = item.AccruedAmount;
        // A query may deliberately omit projections; do not erase a projection
        // captured by an earlier full sync in that case.
        entity.ProjectedAmount = item.ProjectedAmount ?? entity.ProjectedAmount;
        entity.BonusAmount = item.BonusAmount;
        entity.DeductionAmount = item.DeductionAmount;
        entity.PaymentStatus = item.PaymentStatus;
        entity.PaidAt = item.PaidAt;
        entity.SourceUpdatedAt = item.SourceUpdatedAt;
        entity.SourceCalculatedAt = calculatedAt;
        entity.SyncedAt = syncedAt;
        entity.IsCurrent = true;
    }

    private static CscaTeacherCostSnapshotDto Map(CscaTeacherCostSnapshot x) => new(
        x.Id, x.EmployeeId, x.TeacherSourceId, x.TeacherLmsId, x.TeacherName,
        x.CourseSourceId, x.CourseTitle, x.ClassSourceId, x.ClassTitle,
        x.SessionSourceId, x.SessionTitle, x.StartTime, x.EndTime, x.Status,
        x.IsCompleted, x.DurationHours, x.EffectiveHourlyRate, x.AccruedAmount,
        x.ProjectedAmount, x.PaymentStatus, x.PaidAt, x.SourceUpdatedAt, x.SyncedAt);

    private static string BuildSourceKey(LmsTeacherCostItem item) =>
        $"{item.TeacherLmsId}:{item.ClassLmsId}:{item.SessionLmsId}";

    private static Guid? ParseGuid(string? value) => Guid.TryParse(value, out var id) ? id : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ValidatePeriod(DateTime? from, DateTime? to)
    {
        if (from.HasValue && to.HasValue && to.Value <= from.Value)
            return "Ngày kết thúc phải sau ngày bắt đầu.";
        if (from.HasValue && to.HasValue && to.Value - from.Value > TimeSpan.FromDays(366))
            return "Khoảng đồng bộ chi phí giảng viên không được vượt quá 366 ngày.";
        return null;
    }

    private async Task RefreshClassProfitAllocationsAsync(
        Guid companyId,
        IReadOnlyCollection<string> requestedSourceIds,
        CancellationToken ct)
    {
        var sourceIds = requestedSourceIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal).ToList();
        if (sourceIds.Count == 0) return;

        var mappings = await _db.ApplicationClassMaps.AsNoTracking()
            .Where(x => x.CompanyId == companyId && sourceIds.Contains(x.ExternalClassId))
            .Select(x => new { x.CscaClassId, x.ExternalClassId })
            .ToListAsync(ct);
        var sourceToClass = mappings.ToDictionary(x => x.ExternalClassId, x => x.CscaClassId, StringComparer.Ordinal);
        foreach (var sourceId in sourceIds)
            if (Guid.TryParse(sourceId, out var classId)) sourceToClass[sourceId] = classId;

        var classIds = sourceToClass.Values.Distinct().ToList();
        if (classIds.Count == 0) return;
        var mappedClassSourceIds = sourceToClass.Where(x => classIds.Contains(x.Value)).Select(x => x.Key).ToList();
        var expenses = await _db.CscaTeacherCostSnapshots.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SourceSystem == SourceSystem && x.IsCurrent && mappedClassSourceIds.Contains(x.ClassSourceId))
            .GroupBy(x => x.ClassSourceId)
            .Select(x => new { SourceId = x.Key, Amount = x.Sum(row => row.AccruedAmount) })
            .ToDictionaryAsync(x => x.SourceId, x => x.Amount, StringComparer.Ordinal, ct);
        var classes = await _db.CscaClasses
            .Include(x => x.Students)
            .Where(x => x.CompanyId == companyId && classIds.Contains(x.Id))
            .ToListAsync(ct);
        var allocations = await _db.ProfitAllocations
            .Where(x => x.CompanyId == companyId && x.ReferenceType == "CscaClass" && classIds.Contains(x.ReferenceId))
            .ToDictionaryAsync(x => x.ReferenceId, ct);

        foreach (var cls in classes)
        {
            var classSources = sourceToClass.Where(x => x.Value == cls.Id).Select(x => x.Key);
            var expense = classSources.Sum(source => expenses.GetValueOrDefault(source));
            if (!allocations.TryGetValue(cls.Id, out var allocation))
            {
                allocation = new ProfitAllocation
                {
                    CompanyId = cls.CompanyId,
                    BusinessUnitId = cls.BusinessUnitId,
                    ReferenceType = "CscaClass",
                    ReferenceId = cls.Id
                };
                _db.ProfitAllocations.Add(allocation);
            }
            allocation.IncomeAmount = cls.Students.Sum(x => x.PaidAmount);
            allocation.ExpenseAmount = expense;
            allocation.AllocatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
