using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

internal static class LmsCourseMetadataSync
{
    public static async Task QueueAsync(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        Course course,
        CancellationToken ct,
        bool includeDeletedClasses = false)
    {
        var link = await db.LmsCourseLinks.FirstOrDefaultAsync(item =>
            item.CompanyId == course.CompanyId && item.CourseId == course.Id &&
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.Status != IntegrationStatus.Pending, ct);
        if (link is null) return;

        var directClasses = await db.CscaClasses
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(cls => cls.CompanyId == course.CompanyId &&
                cls.CourseId == course.Id &&
                (includeDeletedClasses || !cls.IsDeleted))
            .Select(cls => new { cls.Id, cls.ClassType, cls.RecordingContentMode, cls.SourceClassId })
            .ToListAsync(ct);
        var mirrorSourceIds = directClasses
            .Where(cls => string.Equals(cls.ClassType, "RECORDING", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(cls.RecordingContentMode, "INDEPENDENT", StringComparison.OrdinalIgnoreCase) &&
                          cls.SourceClassId.HasValue)
            .Select(cls => cls.SourceClassId!.Value)
            .Distinct()
            .ToList();
        var ownerClassIds = directClasses
            .Where(cls => !string.Equals(cls.ClassType, "RECORDING", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(cls.RecordingContentMode, "INDEPENDENT", StringComparison.OrdinalIgnoreCase))
            .Select(cls => cls.Id)
            .Concat(mirrorSourceIds)
            .Distinct()
            .ToList();
        if (ownerClassIds.Count == 0) return;

        var teacherAssignments = await db.CscaClassStaffs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(item => item.Class)
            .Include(item => item.Employee)
            .Where(item => ownerClassIds.Contains(item.ClassId) &&
                (includeDeletedClasses || !item.Class.IsDeleted) &&
                item.RoleInClass.ToLower() == "teacher")
            .OrderBy(item => item.Class.CourseId == course.Id ? 0 : 1)
            .ThenBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);
        var leadTeacher = teacherAssignments.FirstOrDefault(item =>
            item.Employee is not null &&
            LmsTeacherRosterSync.HasActiveAccount(item.Employee) &&
            !string.IsNullOrWhiteSpace(item.Employee.FullName) &&
            item.Employee.FullName.Length <= 160 &&
            item.Employee.Email!.Length <= 255);
        if (leadTeacher is null) return;

        QueueEvent(db, currentUser, link, course, leadTeacher.EmployeeId, DateTime.UtcNow, Guid.NewGuid().ToString("N"));
    }

    public static void QueueEvent(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        Course course,
        Guid leadTeacherEmployeeId,
        DateTime now,
        string correlationId)
    {
        LmsManagementOutboxQueue.Queue(db, currentUser, link, "course.upserted", new
        {
            CourseSourceId = link.ExternalCourseId,
            course.Title,
            course.Description,
            PriceVnd = course.Price,
            TeacherSourceId = leadTeacherEmployeeId.ToString("N"),
            IsPublished = string.Equals(course.Status, "Published", StringComparison.OrdinalIgnoreCase),
            SourceUpdatedAt = now
        }, now, correlationId);
    }
}
