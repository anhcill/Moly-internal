using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Integration;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface IPayrollDbContext
{
    DbSet<ApplicationClassMap> ApplicationClassMaps { get; }
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
    DbSet<CscaTeacherCostSnapshot> CscaTeacherCostSnapshots { get; }
    DbSet<TeacherAdvance> TeacherAdvances { get; }
    DbSet<TeacherPayment> TeacherPayments { get; }
    DbSet<TeacherPaymentAllocation> TeacherPaymentAllocations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
