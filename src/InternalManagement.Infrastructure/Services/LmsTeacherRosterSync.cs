using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

internal readonly record struct RevokedTeacherAssignment(Guid EmployeeId, string Role);

internal static class LmsTeacherRosterSync
{
    public static async Task<string?> QueueAsync(
        IApplicationDbContext? db,
        ICurrentUserService currentUser,
        CscaClass cls,
        Employee? changedEmployee,
        IReadOnlyCollection<RevokedTeacherAssignment>? revokedAssignments,
        CancellationToken ct)
    {
        if (db is null) return null;

        if (IsMirrorRecordingClass(cls))
        {
            return await QueueClassAsync(db, currentUser, cls, ct);
        }

        var link = await db.LmsCourseLinks.FirstOrDefaultAsync(item =>
            item.CompanyId == cls.CompanyId && item.CourseId == cls.CourseId &&
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.Status != IntegrationStatus.Pending, ct);
        if (link is null) return null;

        var assignments = cls.Staff
            .Where(item => MapTeachingRole(item.RoleInClass) is not null && item.Employee is not null)
            .GroupBy(item => item.EmployeeId)
            .Select(group => group
                .OrderByDescending(item => item.UpdatedAt ?? item.CreatedAt)
                .ThenByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.Id)
                .First())
            .ToList();
        var activeAssignments = assignments
            .Where(item => HasActiveAccount(item.Employee))
            .ToList();
        var lead = activeAssignments
            .Where(item => string.Equals(item.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefault();

        var now = DateTime.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");
        var employees = assignments
            .Select(item => item.Employee)
            .Append(changedEmployee)
            .Where(employee => employee is not null && !string.IsNullOrWhiteSpace(employee.Email))
            .Cast<Employee>()
            .GroupBy(employee => employee.Id)
            .Select(group => group.First())
            .ToList();
        var legacyTeacherSourceIds = await LoadLegacyTeacherSourceIdsAsync(
            db, cls.CompanyId, employees, ct);
        foreach (var employee in employees)
        {
            QueueTeacherUpsertEvent(db, currentUser, link, employee,
                legacyTeacherSourceIds.GetValueOrDefault(employee.Id, []), now, correlationId);
        }

        QueueClassEvent(db, currentUser, link, cls, lead, now, correlationId);

        if (IsRecordingClass(cls))
        {
            if (cls.Course is not null && lead is not null)
            {
                LmsCourseMetadataSync.QueueEvent(
                    db, currentUser, link, cls.Course, lead.EmployeeId, now, correlationId);
            }
            return null;
        }

        if (revokedAssignments is not null)
        {
            foreach (var revoked in revokedAssignments.Distinct())
            {
                var teachingRole = ResolveRevokedTeachingRole(cls, revoked);
                if (teachingRole is null) continue;
                QueueAssignmentEvent(db, currentUser, link, cls.Id, revoked.EmployeeId,
                    teachingRole, "revoked", now, correlationId);
            }
        }

        foreach (var assignment in activeAssignments)
        {
            QueueAssignmentEvent(db, currentUser, link, cls.Id, assignment.EmployeeId,
                lead is not null && assignment.Id == lead.Id ? "lead" : MapTeachingRole(assignment.RoleInClass)!,
                "active", now, correlationId);
        }

        if (lead?.Employee is not null)
        {
            await QueueDependentMirrorCoursesAsync(
                db, currentUser, cls, lead.Employee, legacyTeacherSourceIds, now, ct);
        }

        return null;
    }

    public static async Task<string?> QueueClassAsync(
        IApplicationDbContext? db,
        ICurrentUserService currentUser,
        CscaClass cls,
        CancellationToken ct)
    {
        if (db is null) return null;

        var link = await db.LmsCourseLinks.FirstOrDefaultAsync(item =>
            item.CompanyId == cls.CompanyId && item.CourseId == cls.CourseId &&
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.Status != IntegrationStatus.Pending, ct);
        if (link is null) return null;

        var lead = cls.Staff
            .Where(item => string.Equals(item.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase) &&
                item.Employee is not null && HasActiveAccount(item.Employee))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        var now = DateTime.UtcNow;
        QueueClassEvent(db, currentUser, link, cls, lead, now, Guid.NewGuid().ToString("N"));
        return null;
    }

    public static bool HasActiveAccount(Employee employee) =>
        !employee.IsDeleted &&
        !string.IsNullOrWhiteSpace(employee.Email) &&
        !string.Equals(employee.Status, "Resigned", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(employee.Status, "Inactive", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(employee.Status, "Blacklisted", StringComparison.OrdinalIgnoreCase);

    public static async Task<IReadOnlyDictionary<Guid, string[]>> LoadLegacyTeacherSourceIdsAsync(
        IApplicationDbContext db,
        Guid companyId,
        IReadOnlyCollection<Employee> employees,
        CancellationToken ct)
    {
        var activeTeachers = employees
            .Where(HasActiveAccount)
            .Where(employee => !string.IsNullOrWhiteSpace(employee.Email))
            .Select(employee => new
            {
                employee.Id,
                NormalizedEmail = employee.Email.Trim().ToLowerInvariant()
            })
            .ToList();
        if (activeTeachers.Count == 0)
            return new Dictionary<Guid, string[]>();

        var normalizedEmails = activeTeachers
            .Select(employee => employee.NormalizedEmail)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var deletedEmployees = (await db.Employees
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(employee => employee.CompanyId == companyId &&
                employee.IsDeleted &&
                employee.Email != null)
            .Select(employee => new LegacyTeacherRow(employee.Id, employee.Email!))
            .ToListAsync(ct))
            .Where(employee => normalizedEmails.Contains(employee.Email.Trim().ToLowerInvariant()))
            .ToList();

        return activeTeachers.ToDictionary(
            employee => employee.Id,
            employee => deletedEmployees
                .Where(legacy => legacy.Id != employee.Id &&
                    string.Equals(legacy.Email.Trim(), employee.NormalizedEmail, StringComparison.OrdinalIgnoreCase))
                .OrderBy(legacy => legacy.Id)
                .Select(legacy => legacy.Id.ToString("N"))
                .ToArray());
    }

    private sealed record LegacyTeacherRow(Guid Id, string Email);

    public static string? ResolveTeachingRole(CscaClass cls, CscaClassStaff assignment)
    {
        var mappedRole = MapTeachingRole(assignment.RoleInClass);
        if (mappedRole is null) return null;
        if (!string.Equals(assignment.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase))
            return mappedRole;

        var lead = cls.Staff
            .Where(item => string.Equals(item.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase) &&
                           item.Employee is not null && HasActiveAccount(item.Employee))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        return lead?.EmployeeId == assignment.EmployeeId ? "lead" : mappedRole;
    }

    private static string? ResolveRevokedTeachingRole(CscaClass cls, RevokedTeacherAssignment revoked)
    {
        var normalizedRole = revoked.Role.Trim().ToLowerInvariant();
        if (normalizedRole is "lead" or "co_teacher" or "assistant")
            return normalizedRole;
        if (normalizedRole != "teacher")
            return MapTeachingRole(revoked.Role);

        var previousLead = cls.Staff
            .Where(item => string.Equals(item.RoleInClass, "Teacher", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefault();
        return previousLead?.EmployeeId == revoked.EmployeeId ? "lead" : "co_teacher";
    }

    public static string? MapTeachingRole(string roleInClass) => roleInClass.Trim().ToLowerInvariant() switch
    {
        "teacher" => "co_teacher",
        "mentor" => "co_teacher",
        "teachingassistant" => "assistant",
        _ => null
    };

    private static void QueueAssignmentEvent(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        Guid classId,
        Guid employeeId,
        string teachingRole,
        string status,
        DateTime now,
        string correlationId) =>
        LmsManagementOutboxQueue.Queue(db, currentUser, link, "class.teacher.assigned", new
        {
            ClassSourceId = classId.ToString("N"),
            TeacherSourceId = employeeId.ToString("N"),
            TeachingRole = teachingRole,
            Status = status,
            SourceUpdatedAt = now
        }, now, correlationId);

    private static void QueueTeacherUpsertEvent(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        Employee employee,
        string[] legacyTeacherSourceIds,
        DateTime now,
        string correlationId) =>
        LmsManagementOutboxQueue.Queue(db, currentUser, link, "teacher.upserted", new
        {
            TeacherSourceId = employee.Id.ToString("N"),
            LegacyTeacherSourceIds = legacyTeacherSourceIds,
            TeacherCode = employee.EmployeeCode,
            employee.FullName,
            employee.Email,
            Phone = employee.Phone,
            Position = employee.Position,
            Department = employee.Department?.Name,
            AccountStatus = HasActiveAccount(employee) ? "active" : "suspended",
            SourceUpdatedAt = now
        }, now, correlationId);

    private static async Task QueueDependentMirrorCoursesAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        CscaClass sourceLiveClass,
        Employee leadTeacher,
        IReadOnlyDictionary<Guid, string[]> legacyTeacherSourceIds,
        DateTime now,
        CancellationToken ct)
    {
        var mirrorCourseIds = await db.CscaClasses
            .AsNoTracking()
            .Where(mirror => mirror.CompanyId == sourceLiveClass.CompanyId &&
                mirror.CourseId != sourceLiveClass.CourseId &&
                !mirror.IsDeleted &&
                mirror.SourceClassId == sourceLiveClass.Id &&
                mirror.ClassType == "RECORDING" &&
                mirror.RecordingContentMode != "INDEPENDENT")
            .Select(mirror => mirror.CourseId)
            .Distinct()
            .ToListAsync(ct);
        if (mirrorCourseIds.Count == 0) return;

        var mirrorLinks = await db.LmsCourseLinks
            .Include(item => item.Course)
            .Where(item => item.CompanyId == sourceLiveClass.CompanyId &&
                mirrorCourseIds.Contains(item.CourseId) &&
                item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                item.Status != IntegrationStatus.Pending)
            .ToListAsync(ct);
        foreach (var mirrorLink in mirrorLinks)
        {
            if (mirrorLink.Course is null || mirrorLink.Course.IsDeleted) continue;
            var mirrorCorrelationId = Guid.NewGuid().ToString("N");
            QueueTeacherUpsertEvent(db, currentUser, mirrorLink, leadTeacher,
                legacyTeacherSourceIds.GetValueOrDefault(leadTeacher.Id, []), now, mirrorCorrelationId);
            LmsCourseMetadataSync.QueueEvent(
                db, currentUser, mirrorLink, mirrorLink.Course, leadTeacher.Id, now, mirrorCorrelationId);
        }
    }

    private static void QueueClassEvent(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        CscaClass cls,
        CscaClassStaff? lead,
        DateTime now,
        string correlationId)
    {
        var isCancelled = cls.IsDeleted || string.Equals(cls.Status, "Cancelled", StringComparison.OrdinalIgnoreCase);
        LmsManagementOutboxQueue.Queue(db, currentUser, link, "class.upserted", new
        {
            ClassSourceId = cls.Id.ToString("N"),
            CourseSourceId = link.ExternalCourseId,
            Title = cls.Name,
            Description = cls.Batch,
            PriceVnd = isCancelled ? (decimal?)null : (cls.IsTrial ? 0m : cls.TuitionFee),
            MaxStudents = Math.Max(30, cls.Students.Count),
            Status = isCancelled ? "cancelled" :
                string.Equals(cls.Status, "Completed", StringComparison.OrdinalIgnoreCase) ? "completed" : "active",
            LeadTeacherSourceId = lead is null ? null : lead.EmployeeId.ToString("N"),
            ClassType = NormalizeClassType(cls.ClassType),
            RecordingContentMode = NormalizeRecordingContentMode(cls),
            SourceClassSourceId = IsMirrorRecordingClass(cls) ? cls.SourceClassId?.ToString("N") : null,
            SourceUpdatedAt = now
        }, now, correlationId);
    }

    private static bool IsRecordingClass(CscaClass cls) =>
        string.Equals(cls.ClassType, "RECORDING", StringComparison.OrdinalIgnoreCase);

    private static bool IsMirrorRecordingClass(CscaClass cls) =>
        IsRecordingClass(cls) &&
        !string.Equals(cls.RecordingContentMode, "INDEPENDENT", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeClassType(string? value) =>
        string.Equals(value, "RECORDING", StringComparison.OrdinalIgnoreCase) ? "RECORDING" : "LIVE";

    private static string? NormalizeRecordingContentMode(CscaClass cls) =>
        IsRecordingClass(cls)
            ? string.Equals(cls.RecordingContentMode, "INDEPENDENT", StringComparison.OrdinalIgnoreCase)
                ? "INDEPENDENT"
                : "MIRROR"
            : null;
}
