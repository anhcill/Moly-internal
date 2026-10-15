using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Infrastructure.Services;

internal readonly record struct RevokedTeacherAssignment(Guid EmployeeId, string Role);

internal static class LmsTeacherRosterSync
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<string?> QueueAsync(
        IApplicationDbContext? db,
        ICurrentUserService currentUser,
        CscaClass cls,
        Employee? changedEmployee,
        IReadOnlyCollection<RevokedTeacherAssignment>? revokedAssignments,
        CancellationToken ct)
    {
        if (db is null) return null;

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
        if (lead is null)
            return "Lớp đã liên kết Web nên phải có ít nhất một giáo viên với email hợp lệ. Hãy phân công giáo viên thay thế trước.";

        var now = DateTime.UtcNow;
        var correlationId = Guid.NewGuid().ToString("N");
        var employees = assignments
            .Select(item => item.Employee)
            .Append(changedEmployee)
            .Where(employee => employee is not null && !string.IsNullOrWhiteSpace(employee.Email))
            .Cast<Employee>()
            .GroupBy(employee => employee.Id)
            .Select(group => group.First());
        foreach (var employee in employees)
        {
            QueueEvent(db, currentUser, link, "teacher.upserted", new
            {
                TeacherSourceId = employee.Id.ToString("N"),
                employee.FullName,
                employee.Email,
                AccountStatus = HasActiveAccount(employee) ? "active" : "suspended",
                SourceUpdatedAt = now
            }, now, correlationId);
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
                assignment.Id == lead.Id ? "lead" : MapTeachingRole(assignment.RoleInClass)!,
                "active", now, correlationId);
        }

        QueueEvent(db, currentUser, link, "class.upserted", new
        {
            ClassSourceId = cls.Id.ToString("N"),
            CourseSourceId = link.ExternalCourseId,
            Title = cls.Name,
            Description = cls.Batch,
            MaxStudents = Math.Max(30, cls.Students.Count),
            Status = string.Equals(cls.Status, "Completed", StringComparison.OrdinalIgnoreCase) ? "completed" :
                string.Equals(cls.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) ? "cancelled" : "active",
            LeadTeacherSourceId = lead.EmployeeId.ToString("N"),
            SourceUpdatedAt = now
        }, now, correlationId);
        return null;
    }

    public static bool HasActiveAccount(Employee employee) =>
        !employee.IsDeleted &&
        !string.IsNullOrWhiteSpace(employee.Email) &&
        !string.Equals(employee.Status, "Resigned", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(employee.Status, "Inactive", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(employee.Status, "Blacklisted", StringComparison.OrdinalIgnoreCase);

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
        QueueEvent(db, currentUser, link, "class.teacher.assigned", new
        {
            ClassSourceId = classId.ToString("N"),
            TeacherSourceId = employeeId.ToString("N"),
            TeachingRole = teachingRole,
            Status = status,
            SourceUpdatedAt = now
        }, now, correlationId);

    private static void QueueEvent(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        LmsCourseLink link,
        string eventType,
        object payload,
        DateTime now,
        string correlationId)
    {
        var eventId = Guid.NewGuid().ToString("N");
        var sequence = db.IntegrationOutboxes.Local.Count(item => item.CorrelationId == correlationId);
        var command = new LmsManagementEvent(eventId, eventType, now, "internal-management", payload);
        db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = link.CompanyId,
            BusinessUnitId = link.BusinessUnitId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = eventId,
            EventType = LmsOutboxEventTypes.ManagementEventRequested,
            AggregateType = nameof(LmsCourseLink),
            AggregateId = link.Id.ToString("N"),
            IdempotencyKey = $"management-event:{eventId}",
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(command, JsonOptions),
            Status = IntegrationStatus.Pending,
            NextAttemptAt = now,
            CreatedAt = now.AddMilliseconds(sequence),
            CreatedBy = currentUser.Username ?? "System"
        });
        if (link.Status != IntegrationStatus.Success)
            link.Status = IntegrationStatus.Processing;
        link.LastSyncError = null;
        link.LastSyncedAt = null;
        link.UpdatedAt = now;
        link.UpdatedBy = currentUser.Username ?? "System";
    }
}
