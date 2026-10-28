using InternalManagement.Domain.Entities.HrPayroll;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class CscaTeacherCostSnapshotConfiguration : IEntityTypeConfiguration<CscaTeacherCostSnapshot>
{
    public void Configure(EntityTypeBuilder<CscaTeacherCostSnapshot> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.SourceKey }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.StartTime });
        builder.HasIndex(x => new { x.CompanyId, x.IsCurrent, x.StartTime });
        builder.HasIndex(x => new { x.CompanyId, x.ClassSourceId, x.StartTime });
        builder.Property(x => x.SourceSystem).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceKey).HasMaxLength(220).IsRequired();
        builder.Property(x => x.TeacherSourceId).HasMaxLength(128);
        builder.Property(x => x.TeacherName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TeacherUsername).HasMaxLength(200);
        builder.Property(x => x.CourseSourceId).HasMaxLength(128);
        builder.Property(x => x.CourseTitle).HasMaxLength(300);
        builder.Property(x => x.ClassSourceId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ClassTitle).HasMaxLength(300).IsRequired();
        builder.Property(x => x.TeachingRole).HasMaxLength(40).IsRequired();
        builder.Property(x => x.SessionSourceId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SessionTitle).HasMaxLength(300);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.DurationHours).HasPrecision(10, 2);
        builder.Property(x => x.EffectiveHourlyRate).HasPrecision(18, 2);
        builder.Property(x => x.AccruedAmount).HasPrecision(18, 2);
        builder.Property(x => x.ProjectedAmount).HasPrecision(18, 2);
        builder.Property(x => x.BonusAmount).HasPrecision(18, 2);
        builder.Property(x => x.DeductionAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentStatus).HasMaxLength(40).IsRequired();
        builder.Property(x => x.LocalSettlementVersion).IsConcurrencyToken();
        builder.Property(x => x.AccruedAmount).IsConcurrencyToken();
        builder.Property(x => x.PaymentStatus).IsConcurrencyToken();
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
