using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class HrPayrollEntityConfigurations :
    IEntityTypeConfiguration<Employee>,
    IEntityTypeConfiguration<AttendanceRecord>,
    IEntityTypeConfiguration<PayrollPeriod>,
    IEntityTypeConfiguration<PayrollPolicyVersion>,
    IEntityTypeConfiguration<PayrollAdjustment>,
    IEntityTypeConfiguration<PayrollWorkEntry>,
    IEntityTypeConfiguration<Payslip>,
    IEntityTypeConfiguration<PayrollApproval>
{
    // HR & Payroll
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.CompanyId, e.EmployeeCode }).IsUnique();
        builder.Property(e => e.EmployeeCode).HasMaxLength(50).IsRequired();
        builder.Property(e => e.FullName).HasMaxLength(200).IsRequired();
        builder.Property(e => e.BaseSalary).HasPrecision(18, 2);
        builder.Property(e => e.EmploymentType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(EmploymentType.FULL_TIME);
        builder.Property(e => e.PartTimeCalculationMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.PartTimeUnitRate).HasPrecision(18, 2);
        builder.Property(e => e.BankName).HasMaxLength(120);
        builder.Property(e => e.BankAccountNumber).HasMaxLength(34);
        builder.Property(e => e.BankAccountHolder).HasMaxLength(200);
        builder.Property(e => e.CvUrlOrPath).HasMaxLength(1000);
        builder.Property(e => e.ProfessionalSummary).HasMaxLength(4000);
        builder.Property(e => e.Skills).HasMaxLength(4000);
        builder.Property(e => e.Experience).HasMaxLength(8000);
        builder.Property(e => e.StatusReason).HasMaxLength(2000);
        builder.HasOne(e => e.Department).WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.Branch).WithMany().HasForeignKey(e => e.BranchId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(e => !e.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.EmployeeId, a.Date }).IsUnique();
        builder.Property(a => a.WorkHours).HasPrecision(4, 2);
        builder.HasOne(a => a.Employee).WithMany(e => e.AttendanceRecords).HasForeignKey(a => a.EmployeeId);
        builder.HasQueryFilter(a => !a.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PayrollPeriod> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>();
        builder.Property(p => p.Version).IsRowVersion();
        builder.Property(p => p.TotalGrossAmount).HasPrecision(18, 2);
        builder.Property(p => p.TotalNetAmount).HasPrecision(18, 2);
    }

    public void Configure(EntityTypeBuilder<PayrollPolicyVersion> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.CompanyId, p.VersionNumber }).IsUnique();
    }

    public void Configure(EntityTypeBuilder<PayrollAdjustment> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Amount).HasPrecision(18, 2);
        builder.HasOne(a => a.PayrollPeriod).WithMany(p => p.Adjustments).HasForeignKey(a => a.PayrollPeriodId);
        builder.HasOne(a => a.Employee).WithMany().HasForeignKey(a => a.EmployeeId);
        builder.HasQueryFilter(a => !a.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PayrollWorkEntry> builder)
    {
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => new { w.CompanyId, w.EmployeeId, w.WorkType, w.ReferenceCode })
            .IsUnique()
            .HasFilter("NOT is_voided");
        builder.HasIndex(w => new { w.PayrollPeriodId, w.EmployeeId });
        builder.Property(w => w.WorkType).HasMaxLength(40).IsRequired();
        builder.Property(w => w.ReferenceCode).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Title).HasMaxLength(300).IsRequired();
        builder.Property(w => w.EvidenceUrl).HasMaxLength(1000);
        builder.Property(w => w.Quantity).HasPrecision(12, 2);
        builder.Property(w => w.UnitRate).HasPrecision(18, 2);
        builder.Property(w => w.Amount).HasPrecision(18, 2);
        builder.HasOne(w => w.PayrollPeriod).WithMany(p => p.WorkEntries)
            .HasForeignKey(w => w.PayrollPeriodId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(w => w.Employee).WithMany(e => e.WorkEntries)
            .HasForeignKey(w => w.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<Payslip> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.PayrollPeriodId, p.EmployeeId }).IsUnique();
        builder.Property(p => p.BaseSalary).HasPrecision(18, 2);
        builder.Property(p => p.StandardWorkDays).HasPrecision(8, 2);
        builder.Property(p => p.ActualWorkDays).HasPrecision(8, 2);
        builder.Property(p => p.EmploymentType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(EmploymentType.FULL_TIME);
        builder.Property(p => p.PartTimeCalculationMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PartTimeUnitRate).HasPrecision(18, 2);
        builder.Property(p => p.ActualWorkHours).HasPrecision(10, 2);
        builder.Property(p => p.ActualShifts).HasPrecision(10, 2);
        builder.Property(p => p.GrossSalary).HasPrecision(18, 2);
        builder.Property(p => p.Allowances).HasPrecision(18, 2);
        builder.Property(p => p.KpiBonus).HasPrecision(18, 2);
        builder.Property(p => p.WorkEarnings).HasPrecision(18, 2);
        builder.Property(p => p.HealthInsurance).HasPrecision(18, 2);
        builder.Property(p => p.TotalIncome).HasPrecision(18, 2);
        builder.Property(p => p.Deductions).HasPrecision(18, 2);
        builder.Property(p => p.TotalDeductions).HasPrecision(18, 2);
        builder.Property(p => p.NetSalary).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>();
        builder.Property(p => p.Version).IsRowVersion();
        builder.HasOne(p => p.PayrollPeriod).WithMany(period => period.Payslips).HasForeignKey(p => p.PayrollPeriodId);
        builder.HasOne(p => p.Employee).WithMany(e => e.Payslips).HasForeignKey(p => p.EmployeeId);
        builder.HasQueryFilter(p => !p.Employee.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PayrollApproval> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<string>();
        builder.HasOne(a => a.PayrollPeriod).WithMany(p => p.Approvals).HasForeignKey(a => a.PayrollPeriodId);
        builder.HasOne(a => a.Approver).WithMany().HasForeignKey(a => a.ApproverId);
        builder.HasQueryFilter(a => !a.Approver.IsDeleted);
    }

}
