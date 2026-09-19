// This is intentionally kept beside the EF snapshot. The previous migration
// was authored by hand because production must not be seeded with synthetic
// users; keeping this model fragment prevents the next EF migration from
// attempting to recreate the six control-plane tables.
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternalManagement.Infrastructure.Persistence.Migrations;

internal static class ApplicationOrchestrationSnapshot
{
    public static void Apply(ModelBuilder modelBuilder)
    {
        ConfigureApplications(modelBuilder.Entity<ManagedApplication>());
        ConfigureMemberships(modelBuilder.Entity<ApplicationMembership>());
        ConfigureRoles(modelBuilder.Entity<ApplicationRoleAssignment>());
        ConfigureCourseMaps(modelBuilder.Entity<ApplicationCourseMap>());
        ConfigureClassMaps(modelBuilder.Entity<ApplicationClassMap>());
        ConfigureEntitlements(modelBuilder.Entity<ApplicationEntitlement>());
    }

    private static void ConfigureApplications(EntityTypeBuilder<ManagedApplication> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnType("uuid").HasColumnName("company_id");
        b.Property(x => x.BusinessUnitId).HasColumnType("uuid").HasColumnName("business_unit_id");
        b.Property(x => x.Code).IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("code");
        b.Property(x => x.Name).IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("name");
        b.Property(x => x.BaseUrl).IsRequired().HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("base_url");
        b.Property(x => x.Description).HasMaxLength(2000).HasColumnType("character varying(2000)").HasColumnName("description");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
        b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnType("text").HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnType("text").HasColumnName("updated_by");
        b.Property(x => x.IsDeleted).HasColumnType("boolean").HasColumnName("is_deleted");
        b.Property(x => x.DeletedAt).HasColumnType("timestamp with time zone").HasColumnName("deleted_at");
        b.Property(x => x.DeletedBy).HasColumnType("text").HasColumnName("deleted_by");
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique().HasDatabaseName("ix_managed_apps_company_code");
        b.HasIndex(x => new { x.CompanyId, x.Status }).HasDatabaseName("ix_managed_apps_company_status");
        b.HasQueryFilter(x => !x.IsDeleted);
        b.ToTable("managed_applications");
    }

    private static void ConfigureMemberships(EntityTypeBuilder<ApplicationMembership> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnType("uuid").HasColumnName("company_id");
        b.Property(x => x.BusinessUnitId).HasColumnType("uuid").HasColumnName("business_unit_id");
        b.Property(x => x.ManagedApplicationId).HasColumnType("uuid").HasColumnName("managed_application_id");
        b.Property(x => x.PartyId).HasColumnType("uuid").HasColumnName("party_id");
        b.Property(x => x.ExternalUserId).HasMaxLength(250).HasColumnType("character varying(250)").HasColumnName("external_user_id");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
        b.Property(x => x.ActivatedAt).HasColumnType("timestamp with time zone").HasColumnName("activated_at");
        b.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone").HasColumnName("revoked_at");
        b.Property(x => x.RevocationReason).HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("revocation_reason");
        b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnType("text").HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnType("text").HasColumnName("updated_by");
        b.HasIndex(x => x.ManagedApplicationId).HasDatabaseName("ix_app_memberships_application");
        b.HasIndex(x => x.PartyId).HasDatabaseName("ix_app_memberships_party");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.PartyId }).IsUnique().HasDatabaseName("ix_app_memberships_company_app_party");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalUserId }).IsUnique().HasDatabaseName("ix_app_memberships_company_app_user");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.Status }).HasDatabaseName("ix_app_memberships_company_app_status");
        b.HasOne(x => x.ManagedApplication).WithMany(x => x.Memberships).HasForeignKey(x => x.ManagedApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("application_memberships");
    }

    private static void ConfigureRoles(EntityTypeBuilder<ApplicationRoleAssignment> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnType("uuid").HasColumnName("company_id");
        b.Property(x => x.BusinessUnitId).HasColumnType("uuid").HasColumnName("business_unit_id");
        b.Property(x => x.ApplicationMembershipId).HasColumnType("uuid").HasColumnName("application_membership_id");
        b.Property(x => x.RoleCode).IsRequired().HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("role_code");
        b.Property(x => x.ScopeType).HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("scope_type");
        b.Property(x => x.ScopeId).HasColumnType("uuid").HasColumnName("scope_id");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
        b.Property(x => x.ValidFrom).HasColumnType("timestamp with time zone").HasColumnName("valid_from");
        b.Property(x => x.ValidUntil).HasColumnType("timestamp with time zone").HasColumnName("valid_until");
        b.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone").HasColumnName("revoked_at");
        b.Property(x => x.RevocationReason).HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("revocation_reason");
        b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnType("text").HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnType("text").HasColumnName("updated_by");
        b.HasIndex(x => x.ApplicationMembershipId).HasDatabaseName("ix_app_roles_membership");
        b.HasIndex(x => new { x.ApplicationMembershipId, x.RoleCode, x.ScopeType, x.ScopeId }).IsUnique().HasDatabaseName("ix_app_roles_membership_role_scope");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.ValidUntil }).HasDatabaseName("ix_app_roles_company_status_until");
        b.HasOne(x => x.ApplicationMembership).WithMany(x => x.RoleAssignments).HasForeignKey(x => x.ApplicationMembershipId).OnDelete(DeleteBehavior.Cascade);
        b.ToTable("application_role_assignments", table => table.HasCheckConstraint("ck_application_role_assignments_scope", "(scope_type = 'Application' AND scope_id IS NULL) OR (scope_type <> 'Application' AND scope_id IS NOT NULL)"));
    }

    private static void ConfigureCourseMaps(EntityTypeBuilder<ApplicationCourseMap> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnType("uuid").HasColumnName("company_id");
        b.Property(x => x.BusinessUnitId).HasColumnType("uuid").HasColumnName("business_unit_id");
        b.Property(x => x.ManagedApplicationId).HasColumnType("uuid").HasColumnName("managed_application_id");
        b.Property(x => x.CourseId).HasColumnType("uuid").HasColumnName("course_id");
        b.Property(x => x.ExternalCourseId).IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("external_course_id");
        b.Property(x => x.ExternalCourseNumericId).HasColumnType("bigint").HasColumnName("external_course_numeric_id");
        b.Property(x => x.ExternalCourseSlug).HasMaxLength(250).HasColumnType("character varying(250)").HasColumnName("external_course_slug");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
        b.Property(x => x.ActivatedAt).HasColumnType("timestamp with time zone").HasColumnName("activated_at");
        b.Property(x => x.LastSyncError).HasMaxLength(2000).HasColumnType("character varying(2000)").HasColumnName("last_sync_error");
        b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnType("text").HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnType("text").HasColumnName("updated_by");
        b.HasIndex(x => x.ManagedApplicationId).HasDatabaseName("ix_app_course_maps_application");
        b.HasIndex(x => x.CourseId).HasDatabaseName("ix_app_course_maps_course");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.CourseId }).IsUnique().HasDatabaseName("ix_app_course_maps_company_app_course");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalCourseId }).IsUnique().HasDatabaseName("ix_app_course_maps_company_app_ext_id");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalCourseNumericId }).IsUnique().HasDatabaseName("ix_app_course_maps_company_app_ext_num");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.Status }).HasDatabaseName("ix_app_course_maps_company_app_status");
        b.HasOne(x => x.ManagedApplication).WithMany(x => x.CourseMaps).HasForeignKey(x => x.ManagedApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Course).WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable("application_course_maps");
    }

    private static void ConfigureClassMaps(EntityTypeBuilder<ApplicationClassMap> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnType("uuid").HasColumnName("company_id");
        b.Property(x => x.BusinessUnitId).HasColumnType("uuid").HasColumnName("business_unit_id");
        b.Property(x => x.ManagedApplicationId).HasColumnType("uuid").HasColumnName("managed_application_id");
        b.Property(x => x.CscaClassId).HasColumnType("uuid").HasColumnName("csca_class_id");
        b.Property(x => x.ApplicationCourseMapId).HasColumnType("uuid").HasColumnName("application_course_map_id");
        b.Property(x => x.ExternalClassId).IsRequired().HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("external_class_id");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("status");
        b.Property(x => x.ActivatedAt).HasColumnType("timestamp with time zone").HasColumnName("activated_at");
        b.Property(x => x.LastSyncError).HasMaxLength(2000).HasColumnType("character varying(2000)").HasColumnName("last_sync_error");
        b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnType("text").HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnType("text").HasColumnName("updated_by");
        b.HasIndex(x => x.ManagedApplicationId).HasDatabaseName("ix_app_class_maps_application");
        b.HasIndex(x => x.CscaClassId).HasDatabaseName("ix_app_class_maps_csca_class");
        b.HasIndex(x => x.ApplicationCourseMapId).HasDatabaseName("ix_app_class_maps_course_map");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.CscaClassId }).IsUnique().HasDatabaseName("ix_app_class_maps_company_app_class");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.ExternalClassId }).IsUnique().HasDatabaseName("ix_app_class_maps_company_app_ext_id");
        b.HasIndex(x => new { x.CompanyId, x.ManagedApplicationId, x.Status }).HasDatabaseName("ix_app_class_maps_company_app_status");
        b.HasOne(x => x.ManagedApplication).WithMany(x => x.ClassMaps).HasForeignKey(x => x.ManagedApplicationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CscaClass).WithMany().HasForeignKey(x => x.CscaClassId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ApplicationCourseMap).WithMany(x => x.ClassMaps).HasForeignKey(x => x.ApplicationCourseMapId).OnDelete(DeleteBehavior.SetNull);
        b.ToTable("application_class_maps");
    }

    private static void ConfigureEntitlements(EntityTypeBuilder<ApplicationEntitlement> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedOnAdd().HasColumnType("uuid").HasColumnName("id");
        b.Property(x => x.CompanyId).HasColumnType("uuid").HasColumnName("company_id");
        b.Property(x => x.BusinessUnitId).HasColumnType("uuid").HasColumnName("business_unit_id");
        b.Property(x => x.ApplicationMembershipId).HasColumnType("uuid").HasColumnName("application_membership_id");
        b.Property(x => x.ApplicationCourseMapId).HasColumnType("uuid").HasColumnName("application_course_map_id");
        b.Property(x => x.ApplicationClassMapId).HasColumnType("uuid").HasColumnName("application_class_map_id");
        b.Property(x => x.CscaClassStudentId).HasColumnType("uuid").HasColumnName("csca_class_student_id");
        b.Property(x => x.SourcePaymentId).HasMaxLength(200).HasColumnType("character varying(200)").HasColumnName("source_payment_id");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30).HasColumnType("character varying(30)").HasColumnName("status");
        b.Property(x => x.Reason).HasMaxLength(500).HasColumnType("character varying(500)").HasColumnName("reason");
        b.Property(x => x.ValidFrom).HasColumnType("timestamp with time zone").HasColumnName("valid_from");
        b.Property(x => x.ValidUntil).HasColumnType("timestamp with time zone").HasColumnName("valid_until");
        b.Property(x => x.RevokedAt).HasColumnType("timestamp with time zone").HasColumnName("revoked_at");
        b.Property(x => x.LastCorrelationId).HasMaxLength(100).HasColumnType("character varying(100)").HasColumnName("last_correlation_id");
        b.Property(x => x.LastSyncedAt).HasColumnType("timestamp with time zone").HasColumnName("last_synced_at");
        b.Property(x => x.LastSyncError).HasMaxLength(2000).HasColumnType("character varying(2000)").HasColumnName("last_sync_error");
        b.Property(x => x.CreatedAt).HasColumnType("timestamp with time zone").HasColumnName("created_at");
        b.Property(x => x.CreatedBy).HasColumnType("text").HasColumnName("created_by");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamp with time zone").HasColumnName("updated_at");
        b.Property(x => x.UpdatedBy).HasColumnType("text").HasColumnName("updated_by");
        b.HasIndex(x => x.ApplicationMembershipId).HasDatabaseName("ix_app_entitlements_membership");
        b.HasIndex(x => x.ApplicationCourseMapId).HasDatabaseName("ix_app_entitlements_course_map");
        b.HasIndex(x => x.ApplicationClassMapId).HasDatabaseName("ix_app_entitlements_class_map");
        b.HasIndex(x => x.CscaClassStudentId).HasDatabaseName("ix_app_entitlements_student");
        b.HasIndex(x => new { x.ApplicationMembershipId, x.ApplicationCourseMapId, x.ApplicationClassMapId }).IsUnique().HasDatabaseName("ix_app_entitlements_membership_course_class");
        b.HasIndex(x => new { x.CompanyId, x.Status, x.ValidUntil }).HasDatabaseName("ix_app_entitlements_company_status_until");
        b.HasIndex(x => new { x.CscaClassStudentId, x.ApplicationCourseMapId }).IsUnique().HasDatabaseName("ix_app_entitlements_student_course");
        b.HasOne(x => x.ApplicationMembership).WithMany(x => x.Entitlements).HasForeignKey(x => x.ApplicationMembershipId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ApplicationCourseMap).WithMany(x => x.Entitlements).HasForeignKey(x => x.ApplicationCourseMapId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ApplicationClassMap).WithMany(x => x.Entitlements).HasForeignKey(x => x.ApplicationClassMapId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne(x => x.CscaClassStudent).WithMany().HasForeignKey(x => x.CscaClassStudentId).OnDelete(DeleteBehavior.SetNull);
        b.ToTable("application_entitlements", table => table.HasCheckConstraint("ck_application_entitlements_valid_range", "valid_until IS NULL OR valid_until > valid_from"));
    }
}
