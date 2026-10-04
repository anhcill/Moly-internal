using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Integration;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Application.Features.CscaInterview.Services;

public interface ICscaDbContext
{
    DbSet<Company> Companies { get; }
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<Course> Courses { get; }
    DbSet<CscaClass> CscaClasses { get; }
    DbSet<CscaClassroom> CscaClassrooms { get; }
    DbSet<CscaClassSchedule> CscaClassSchedules { get; }
    DbSet<CscaClassStaff> CscaClassStaffs { get; }
    DbSet<CscaClassStudent> CscaClassStudents { get; }
    DbSet<CscaLessonAttendance> CscaLessonAttendances { get; }
    DbSet<CscaLessonSession> CscaLessonSessions { get; }
    DbSet<Employee> Employees { get; }
    DbSet<LmsAccessGrant> LmsAccessGrants { get; }
    DbSet<ProfitAllocation> ProfitAllocations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
