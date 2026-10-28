using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class FinanceEntityConfigurations :
    IEntityTypeConfiguration<FinanceCategory>,
    IEntityTypeConfiguration<FinanceTransaction>,
    IEntityTypeConfiguration<CashAccount>,
    IEntityTypeConfiguration<MonthlyClosing>
    , IEntityTypeConfiguration<PlannedCashMovement>
    , IEntityTypeConfiguration<TeacherAdvance>
    , IEntityTypeConfiguration<TeacherAdvanceReturn>
    , IEntityTypeConfiguration<TeacherPayment>
    , IEntityTypeConfiguration<StudentReceivable>
    , IEntityTypeConfiguration<StudentReceipt>
    , IEntityTypeConfiguration<StudentRefund>
    , IEntityTypeConfiguration<TeacherPaymentAllocation>
{
    // Finance
    public void Configure(EntityTypeBuilder<FinanceCategory> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.CompanyId, c.Code }).IsUnique();
        builder.Property(c => c.Code).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<FinanceTransaction> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => new { t.CompanyId, t.TransactionDate });
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.TransactionType).HasConversion<string>();
        builder.HasOne(t => t.Category).WithMany().HasForeignKey(t => t.CategoryId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<CashAccount> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.CompanyId, a.Code }).IsUnique();
        builder.Property(a => a.Code).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(200).IsRequired();
        builder.Property(a => a.CurrentBalance).HasPrecision(18, 2);
    }

    public void Configure(EntityTypeBuilder<MonthlyClosing> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.CompanyId, m.BusinessUnitId, m.Year, m.Month }).IsUnique();
        builder.Property(m => m.TotalIncome).HasPrecision(18, 2);
        builder.Property(m => m.TotalExpense).HasPrecision(18, 2);
    }

    public void Configure(EntityTypeBuilder<PlannedCashMovement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.PlannedDate, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.TransactionType).HasConversion<string>();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).IsConcurrencyToken();
    }

    public void Configure(EntityTypeBuilder<TeacherAdvance> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.IssuedAt });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.ReturnedAmount).HasPrecision(18, 2);
        builder.Property(x => x.AppliedAmount).HasPrecision(18, 2);
        builder.Property(x => x.ReturnedAmount).IsConcurrencyToken();
        builder.Property(x => x.AppliedAmount).IsConcurrencyToken();
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TeacherAdvanceReturn> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.TeacherAdvanceId, x.ReturnedAt });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne<TeacherAdvance>().WithMany().HasForeignKey(x => x.TeacherAdvanceId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TeacherPayment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.EmployeeId, x.PaidAt });
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.AdvanceAppliedAmount).HasPrecision(18, 2);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<StudentReceivable> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.StudentId, x.ClassId }).IsUnique();
        builder.Property(x => x.DueAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaidAmount).HasPrecision(18, 2);
        builder.Property(x => x.RefundedAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaidAmount).IsConcurrencyToken();
        builder.Property(x => x.RefundedAmount).IsConcurrencyToken();
        builder.Property(x => x.DueAmount).IsConcurrencyToken();
        builder.Property(x => x.Status).IsConcurrencyToken();
        builder.HasOne<CscaClassStudent>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CscaClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<StudentReceipt> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne<StudentReceivable>().WithMany().HasForeignKey(x => x.StudentReceivableId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<StudentRefund> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne<StudentReceivable>().WithMany().HasForeignKey(x => x.StudentReceivableId).OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<TeacherPaymentAllocation> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.TeacherPaymentId, x.TeacherCostSnapshotId }).IsUnique();
        builder.HasIndex(x => x.TeacherCostSnapshotId);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.HasOne<TeacherPayment>().WithMany().HasForeignKey(x => x.TeacherPaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CscaTeacherCostSnapshot>().WithMany().HasForeignKey(x => x.TeacherCostSnapshotId).OnDelete(DeleteBehavior.Restrict);
    }
}
