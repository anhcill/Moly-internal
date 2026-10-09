using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface IPayrollDbContext
{
    DbSet<AttendanceRecord> AttendanceRecords { get; }
    DbSet<CscaLessonSession> CscaLessonSessions { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<BusinessUnit> BusinessUnits { get; }
    DbSet<Company> Companies { get; }
    DbSet<Employee> Employees { get; }
    DbSet<PayrollAdjustment> PayrollAdjustments { get; }
    DbSet<PayrollWorkEntry> PayrollWorkEntries { get; }
    DbSet<PayrollApproval> PayrollApprovals { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollPolicyVersion> PayrollPolicyVersions { get; }
    DbSet<Payslip> Payslips { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
