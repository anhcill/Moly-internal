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
}
