using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class EdTechEntityConfigurations :
    IEntityTypeConfiguration<Course>,
    IEntityTypeConfiguration<CourseModule>,
    IEntityTypeConfiguration<Subject>,
    IEntityTypeConfiguration<Topic>,
    IEntityTypeConfiguration<QuestionBank>,
    IEntityTypeConfiguration<Question>,
    IEntityTypeConfiguration<QuestionVersion>,
    IEntityTypeConfiguration<QuestionChoice>,
    IEntityTypeConfiguration<QuestionTag>,
    IEntityTypeConfiguration<ContentPublication>,
    IEntityTypeConfiguration<EdTechCustomer>,
    IEntityTypeConfiguration<Subscription>,
    IEntityTypeConfiguration<Payment>
{
    // EdTech
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Title).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Price).HasPrecision(18, 2);
        builder.HasMany(c => c.CscaClasses).WithOne(c => c.Course).HasForeignKey(c => c.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(c => !c.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<CourseModule> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).HasMaxLength(300).IsRequired();
        builder.HasOne(m => m.Course).WithMany(c => c.Modules).HasForeignKey(m => m.CourseId);
        builder.HasQueryFilter(m => !m.Course.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();
        builder.Property(s => s.Code).HasMaxLength(50).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(t => t.Subject).WithMany(s => s.Topics).HasForeignKey(t => t.SubjectId);
    }

    public void Configure(EntityTypeBuilder<QuestionBank> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasKey(q => q.Id);
        builder.HasIndex(q => new { q.SourceSystem, q.SourceId })
            .IsUnique()
            .HasFilter("source_id IS NOT NULL");
        builder.HasOne(q => q.QuestionBank).WithMany(b => b.Questions).HasForeignKey(q => q.QuestionBankId);
        builder.HasOne(q => q.Subject).WithMany().HasForeignKey(q => q.SubjectId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(q => q.Topic).WithMany().HasForeignKey(q => q.TopicId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<QuestionVersion> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => new { v.QuestionId, v.VersionNumber }).IsUnique();
        builder.Property(v => v.Status).HasConversion<string>();
        builder.HasOne(v => v.Question).WithMany(q => q.Versions).HasForeignKey(v => v.QuestionId);
    }

    public void Configure(EntityTypeBuilder<QuestionChoice> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Label).HasMaxLength(10).IsRequired();
        builder.HasOne(c => c.QuestionVersion).WithMany(v => v.Choices).HasForeignKey(c => c.QuestionVersionId);
    }

    public void Configure(EntityTypeBuilder<QuestionTag> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Tag).HasMaxLength(100).IsRequired();
        builder.HasOne(t => t.Question).WithMany(q => q.Tags).HasForeignKey(t => t.QuestionId);
    }

    public void Configure(EntityTypeBuilder<ContentPublication> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasOne(p => p.QuestionVersion).WithMany(v => v.Publications).HasForeignKey(p => p.QuestionVersionId);
    }

    public void Configure(EntityTypeBuilder<EdTechCustomer> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.SourceSystem, c.SourceId }).IsUnique();
        builder.Property(c => c.FullName).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.PackageName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>();
        builder.HasOne(s => s.Customer).WithMany(c => c.Subscriptions).HasForeignKey(s => s.CustomerId);
    }

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => new { p.SourceSystem, p.SourcePaymentId }).IsUnique();
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.Status).HasConversion<string>();
        builder.HasOne(p => p.Customer).WithMany(c => c.Payments).HasForeignKey(p => p.CustomerId);
    }

}
