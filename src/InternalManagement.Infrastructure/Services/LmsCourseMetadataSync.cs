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
        CancellationToken ct)
    {
        var link = await db.LmsCourseLinks.FirstOrDefaultAsync(item =>
            item.CompanyId == course.CompanyId && item.CourseId == course.Id &&
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.Status != IntegrationStatus.Pending, ct);
        if (link is null) return;

        var teacherAssignments = await db.CscaClassStaffs
            .AsNoTracking()
            .Include(item => item.Class)
            .Include(item => item.Employee)
            .Where(item => item.Class.CourseId == course.Id && !item.Class.IsDeleted &&
                item.RoleInClass.ToLower() == "teacher")
            .OrderBy(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);
        var leadTeacher = teacherAssignments.FirstOrDefault(item =>
            item.Employee is not null &&
            LmsTeacherRosterSync.HasActiveAccount(item.Employee) &&
            !string.IsNullOrWhiteSpace(item.Employee.FullName) &&
            item.Employee.FullName.Length <= 160 &&
            item.Employee.Email!.Length <= 255);
        if (leadTeacher is null) return;

        var now = DateTime.UtcNow;
        LmsManagementOutboxQueue.Queue(db, currentUser, link, "course.upserted", new
        {
            CourseSourceId = link.ExternalCourseId,
            course.Title,
            course.Description,
            TeacherSourceId = leadTeacher.EmployeeId.ToString("N"),
            IsPublished = string.Equals(course.Status, "Published", StringComparison.OrdinalIgnoreCase),
            SourceUpdatedAt = now
        }, now, Guid.NewGuid().ToString("N"));
    }
}
