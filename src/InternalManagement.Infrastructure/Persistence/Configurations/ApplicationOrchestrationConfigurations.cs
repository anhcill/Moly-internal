using InternalManagement.Domain.Entities.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Relational safeguards for the multi-application control plane. Every
/// relationship stays within one InternalManagement database; external IDs are
/// attributes, never cross-database foreign keys.
/// </summary>
public sealed class ApplicationOrchestrationConfigurations :
    IEntityTypeConfiguration<ManagedApplication>,
    IEntityTypeConfiguration<ApplicationMembership>,
    IEntityTypeConfiguration<ApplicationRoleAssignment>,
    IEntityTypeConfiguration<ApplicationCourseMap>,
    IEntityTypeConfiguration<ApplicationClassMap>,
    IEntityTypeConfiguration<ApplicationEntitlement>
{
    public void Configure(EntityTypeBuilder<ManagedApplication> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ix_managed_apps_company_code");
        builder.HasIndex(x => new { x.CompanyId, x.Status }).HasDatabaseName("ix_managed_apps_company_status");
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<ApplicationMembership> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ManagedApplicationId).HasDatabaseName("ix_app_memberships_application");
        builder.HasIndex(x => x.PartyId).HasDatabaseName("ix_app_memberships_party");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.PartyId }).IsUnique().HasDatabaseName("ix_app_memberships_company_app_party");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalUserId }).IsUnique().HasDatabaseName("ix_app_memberships_company_app_user");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.Status }).HasDatabaseName("ix_app_memberships_company_app_status");
        builder.Property(x => x.ExternalUserId).HasMaxLength(250);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RevocationReason).HasMaxLength(500);
        builder.HasOne(x => x.ManagedApplication).WithMany(x => x.Memberships)
            .HasForeignKey(x => x.ManagedApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ApplicationRoleAssignment> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ApplicationMembershipId).HasDatabaseName("ix_app_roles_membership");
        builder.HasIndex(x => new { x.ApplicationMembershipId, x.RoleCode, x.ScopeType, x.ScopeId }).IsUnique().HasDatabaseName("ix_app_roles_membership_role_scope");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ValidUntil }).HasDatabaseName("ix_app_roles_company_status_until");
        builder.Property(x => x.RoleCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RevocationReason).HasMaxLength(500);
        builder.HasOne(x => x.ApplicationMembership).WithMany(x => x.RoleAssignments)
            .HasForeignKey(x => x.ApplicationMembershipId).OnDelete(DeleteBehavior.Cascade);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_application_role_assignments_scope",
            "(scope_type = 'Application' AND scope_id IS NULL) OR (scope_type <> 'Application' AND scope_id IS NOT NULL)"));
    }

    public void Configure(EntityTypeBuilder<ApplicationCourseMap> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ManagedApplicationId).HasDatabaseName("ix_app_course_maps_application");
        builder.HasIndex(x => x.CourseId).HasDatabaseName("ix_app_course_maps_course");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.CourseId }).IsUnique().HasDatabaseName("ix_app_course_maps_company_app_course");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalCourseId }).IsUnique().HasDatabaseName("ix_app_course_maps_company_app_ext_id");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalCourseNumericId }).IsUnique().HasDatabaseName("ix_app_course_maps_company_app_ext_num");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.Status }).HasDatabaseName("ix_app_course_maps_company_app_status");
        builder.Property(x => x.ExternalCourseId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ExternalCourseSlug).HasMaxLength(250);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.HasOne(x => x.ManagedApplication).WithMany(x => x.CourseMaps)
            .HasForeignKey(x => x.ManagedApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<ApplicationClassMap> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ManagedApplicationId).HasDatabaseName("ix_app_class_maps_application");
        builder.HasIndex(x => x.CscaClassId).HasDatabaseName("ix_app_class_maps_csca_class");
        builder.HasIndex(x => x.ApplicationCourseMapId).HasDatabaseName("ix_app_class_maps_course_map");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.CscaClassId }).IsUnique().HasDatabaseName("ix_app_class_maps_company_app_class");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalClassId }).IsUnique().HasDatabaseName("ix_app_class_maps_company_app_ext_id");
        builder.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.Status }).HasDatabaseName("ix_app_class_maps_company_app_status");
        builder.Property(x => x.ExternalClassId).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.HasOne(x => x.ManagedApplication).WithMany(x => x.ClassMaps)
            .HasForeignKey(x => x.ManagedApplicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CscaClass).WithMany().HasForeignKey(x => x.CscaClassId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApplicationCourseMap).WithMany(x => x.ClassMaps)
            .HasForeignKey(x => x.ApplicationCourseMapId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<ApplicationEntitlement> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.ApplicationMembershipId).HasDatabaseName("ix_app_entitlements_membership");
        builder.HasIndex(x => x.ApplicationCourseMapId).HasDatabaseName("ix_app_entitlements_course_map");
        builder.HasIndex(x => x.ApplicationClassMapId).HasDatabaseName("ix_app_entitlements_class_map");
        builder.HasIndex(x => x.CscaClassStudentId).HasDatabaseName("ix_app_entitlements_student");
        builder.HasIndex(x => new { x.ApplicationMembershipId, x.ApplicationCourseMapId, x.ApplicationClassMapId }).IsUnique().HasDatabaseName("ix_app_entitlements_membership_course_class");
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.ValidUntil }).HasDatabaseName("ix_app_entitlements_company_status_until");
        builder.HasIndex(x => new { x.CscaClassStudentId, x.ApplicationCourseMapId }).IsUnique().HasDatabaseName("ix_app_entitlements_student_course");
        builder.Property(x => x.SourcePaymentId).HasMaxLength(200);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.LastCorrelationId).HasMaxLength(100);
        builder.Property(x => x.LastSyncError).HasMaxLength(2000);
        builder.HasOne(x => x.ApplicationMembership).WithMany(x => x.Entitlements)
            .HasForeignKey(x => x.ApplicationMembershipId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApplicationCourseMap).WithMany(x => x.Entitlements)
            .HasForeignKey(x => x.ApplicationCourseMapId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApplicationClassMap).WithMany(x => x.Entitlements)
            .HasForeignKey(x => x.ApplicationClassMapId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.CscaClassStudent).WithMany().HasForeignKey(x => x.CscaClassStudentId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_application_entitlements_valid_range",
            "valid_until IS NULL OR valid_until > valid_from"));
    }
}
