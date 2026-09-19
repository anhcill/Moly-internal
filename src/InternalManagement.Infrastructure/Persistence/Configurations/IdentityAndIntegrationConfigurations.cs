using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public class IdentityConfigurations :
    IEntityTypeConfiguration<Company>,
    IEntityTypeConfiguration<BusinessUnit>,
    IEntityTypeConfiguration<Branch>,
    IEntityTypeConfiguration<Department>,
    IEntityTypeConfiguration<User>,
    IEntityTypeConfiguration<Role>,
    IEntityTypeConfiguration<Permission>,
    IEntityTypeConfiguration<UserRole>,
    IEntityTypeConfiguration<RolePermission>,
    IEntityTypeConfiguration<UserRefreshToken>,
    IEntityTypeConfiguration<AuditLog>,
    IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Company> builder)
    {
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.HasQueryFilter(c => !c.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<BusinessUnit> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();
        builder.Property(b => b.Code).HasMaxLength(50).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(b => b.Company).WithMany(c => c.BusinessUnits).HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(b => !b.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.HasKey(b => b.Id);
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();
        builder.Property(b => b.Code).HasMaxLength(50).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(b => b.Company).WithMany(c => c.Branches).HasForeignKey(b => b.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(b => !b.Company.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => new { d.CompanyId, d.Code }).IsUnique();
        builder.Property(d => d.Code).HasMaxLength(50).IsRequired();
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(d => d.Company).WithMany(c => c.Departments).HasForeignKey(d => d.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(d => !d.Company.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => u.Username).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.Username).HasMaxLength(100).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(200).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.HasIndex(u => u.LockoutEndAt);
        builder.HasOne(u => u.Company).WithMany(c => c.Users).HasForeignKey(u => u.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.DefaultBusinessUnit).WithMany().HasForeignKey(u => u.DefaultBusinessUnitId).OnDelete(DeleteBehavior.SetNull);
        builder.HasQueryFilter(u => !u.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.CompanyId, r.Code }).IsUnique();
        builder.Property(r => r.Code).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.HasOne(r => r.Company).WithMany(c => c.Roles).HasForeignKey(r => r.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasQueryFilter(r => !r.Company.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.Property(p => p.Code).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });
        builder.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId);
        builder.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId);
        builder.HasQueryFilter(ur => !ur.User.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionId });
        builder.HasOne(rp => rp.Role).WithMany(r => r.RolePermissions).HasForeignKey(rp => rp.RoleId);
        builder.HasOne(rp => rp.Permission).WithMany(p => p.RolePermissions).HasForeignKey(rp => rp.PermissionId);
        builder.HasQueryFilter(rp => !rp.Role.Company.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<UserRefreshToken> builder)
    {
        builder.HasKey(t => t.Id);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
        builder.HasOne(t => t.User).WithMany(u => u.RefreshTokens).HasForeignKey(t => t.UserId);
        builder.HasQueryFilter(t => !t.User.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.CompanyId, a.BusinessUnitId });
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
    }

    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FileName).HasMaxLength(300).IsRequired();
        builder.Property(d => d.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(d => d.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.StorageProvider).HasMaxLength(50).IsRequired();
        builder.Property(d => d.StoragePublicId).HasMaxLength(500);
        builder.HasIndex(d => new { d.CompanyId, d.EntityType, d.EntityId })
            .HasDatabaseName("ix_documents_company_id_entity_type_entity_id");
    }
}

public class IntegrationConfigurations :
    IEntityTypeConfiguration<IntegrationSource>,
    IEntityTypeConfiguration<IntegrationRun>,
    IEntityTypeConfiguration<IntegrationInbox>,
    IEntityTypeConfiguration<IntegrationDeadLetter>,
    IEntityTypeConfiguration<LmsAccountLink>,
    IEntityTypeConfiguration<LmsCourseLink>,
    IEntityTypeConfiguration<LmsAccessGrant>,
    IEntityTypeConfiguration<IntegrationOutbox>,
    IEntityTypeConfiguration<LmsSyncStatus>
{
    public void Configure(EntityTypeBuilder<IntegrationSource> builder)
    {
        builder.HasKey(s => s.Id);
        builder.HasIndex(s => new { s.CompanyId, s.Code }).IsUnique();
        builder.Property(s => s.Code).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<IntegrationRun> builder)
    {
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new { r.SourceSystem, r.StartedAt });
        builder.Property(r => r.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(r => r.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<IntegrationInbox> builder)
    {
        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new { i.SourceSystem, i.EventId }).IsUnique();
        builder.Property(i => i.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(i => i.EventId).HasMaxLength(200).IsRequired();
        builder.Property(i => i.EventType).HasMaxLength(100).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>();
    }

    public void Configure(EntityTypeBuilder<IntegrationDeadLetter> builder)
    {
        builder.HasKey(d => d.Id);
        builder.HasIndex(d => new { d.SourceSystem, d.SourceId });
        builder.Property(d => d.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(d => d.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.SourceId).HasMaxLength(200).IsRequired();
    }

    public void Configure(EntityTypeBuilder<LmsAccountLink> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.ExternalStudentId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.PartyId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.LmsUserId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalStudentId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LmsEmail).HasMaxLength(320);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.LastCorrelationId).HasMaxLength(100);
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.CscaClassStudent).WithMany().HasForeignKey(x => x.CscaClassStudentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_lms_account_links_identity",
            "party_id IS NOT NULL OR csca_class_student_id IS NOT NULL"));
    }

    public void Configure(EntityTypeBuilder<LmsCourseLink> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.CourseId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.ExternalCourseId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.LmsCourseId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.Status });
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalCourseId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.LmsCourseSlug).HasMaxLength(250);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<LmsAccessGrant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.ExternalGrantId }).IsUnique();
        builder.HasIndex(x => new { x.CscaClassStudentId, x.LmsCourseLinkId }).IsUnique();
        builder.HasIndex(x => new { x.LmsAccountLinkId, x.Status, x.ValidUntil });
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalGrantId).HasMaxLength(250).IsRequired();
        builder.Property(x => x.SourcePaymentId).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.RevocationReason).HasMaxLength(500);
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.HasOne(x => x.LmsAccountLink).WithMany(x => x.AccessGrants).HasForeignKey(x => x.LmsAccountLinkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.LmsCourseLink).WithMany(x => x.AccessGrants).HasForeignKey(x => x.LmsCourseLinkId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CscaClassStudent).WithMany().HasForeignKey(x => x.CscaClassStudentId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<IntegrationOutbox> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.SourceSystem, x.EventId }).IsUnique();
        builder.HasIndex(x => new { x.SourceSystem, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.SourceSystem, x.Status, x.NextAttemptAt });
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EventId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AggregateType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.AggregateId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(250).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastError).HasMaxLength(2000);
    }

    public void Configure(EntityTypeBuilder<LmsSyncStatus> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.EntityType, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.Status, x.LastSyncedAt });
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(250).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastPayloadHash).HasMaxLength(128);
        builder.Property(x => x.LastCorrelationId).HasMaxLength(100);
        builder.Property(x => x.LastError).HasMaxLength(2000);
    }
}
