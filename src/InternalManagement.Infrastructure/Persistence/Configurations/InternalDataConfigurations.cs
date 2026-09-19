using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.InternalData;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

public sealed class InternalCustomerConfiguration : IEntityTypeConfiguration<InternalCustomer>
{
    public void Configure(EntityTypeBuilder<InternalCustomer> builder)
    {
        builder.ToTable("internal_customers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessUnitId).IsRequired();
        builder.Property(x => x.BusinessSegment).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(250).IsRequired();
        builder.Property(x => x.ContactPerson).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.Phone).HasMaxLength(30);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.Source).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.CreatedBy).HasMaxLength(150);
        builder.Property(x => x.UpdatedBy).HasMaxLength(150);
        builder.Property(x => x.DeletedBy).HasMaxLength(150);

        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.BusinessSegment, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.BusinessSegment, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.BusinessSegment, x.Name });
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InternalResourceConfiguration : IEntityTypeConfiguration<InternalResource>
{
    public void Configure(EntityTypeBuilder<InternalResource> builder)
    {
        builder.ToTable("internal_resources", table => table.HasCheckConstraint(
            "ck_internal_resources_segment_type",
            "(business_segment = 'TECHNOLOGY_EDUCATION' AND resource_type IN ('EXAM', 'DOCUMENT')) OR " +
            "(business_segment = 'FASHION' AND resource_type IN ('PLAN', 'DESIGN_SAMPLE', 'DOCUMENT'))"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.BusinessUnitId).IsRequired();
        builder.Property(x => x.BusinessSegment).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(250).IsRequired();
        builder.Property(x => x.ResourceType).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.StorageUri).HasMaxLength(2048).IsRequired();
        builder.Property(x => x.FileName).HasMaxLength(255);
        builder.Property(x => x.ContentType).HasMaxLength(150);
        builder.Property(x => x.ChecksumSha256).HasMaxLength(64);
        builder.Property(x => x.Version).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.TagsCsv).HasMaxLength(1200);
        builder.Property(x => x.MetadataJson).HasMaxLength(10_000);
        builder.Property(x => x.Notes).HasMaxLength(4000);
        builder.Property(x => x.CreatedBy).HasMaxLength(150);
        builder.Property(x => x.UpdatedBy).HasMaxLength(150);
        builder.Property(x => x.DeletedBy).HasMaxLength(150);

        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.BusinessSegment, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.BusinessSegment, x.ResourceType, x.Status });
        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.BusinessSegment, x.Title });
        builder.HasQueryFilter(x => !x.IsDeleted);

        builder.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BusinessUnit>().WithMany().HasForeignKey(x => x.BusinessUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
