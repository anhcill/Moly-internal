using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.Finance;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.InternalData;
using InternalManagement.Domain.Entities.MasterData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternalManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps shared identities and the cross-module document registry.  Existing
/// module records retain their operational snapshots while nullable foreign
/// keys make their canonical identity/document discoverable everywhere.
/// </summary>
public sealed class MasterDataConfigurations :
    IEntityTypeConfiguration<Party>,
    IEntityTypeConfiguration<PartyContact>,
    IEntityTypeConfiguration<PartyExternalIdentity>,
    IEntityTypeConfiguration<PartyBusinessProfile>,
    IEntityTypeConfiguration<BusinessDocument>,
    IEntityTypeConfiguration<BusinessDocumentLink>,
    IEntityTypeConfiguration<EdTechCustomer>,
    IEntityTypeConfiguration<Payment>,
    IEntityTypeConfiguration<CscaClassStudent>,
    IEntityTypeConfiguration<InterviewCustomer>,
    IEntityTypeConfiguration<Supplier>,
    IEntityTypeConfiguration<PurchaseReceipt>,
    IEntityTypeConfiguration<SalesOrder>,
    IEntityTypeConfiguration<SalesDocument>,
    IEntityTypeConfiguration<PayrollPeriod>,
    IEntityTypeConfiguration<FinanceTransaction>,
    IEntityTypeConfiguration<InternalCustomer>
{
    public void Configure(EntityTypeBuilder<Party> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.DisplayName });
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DisplayName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.TaxCode).HasMaxLength(50);
        builder.HasQueryFilter(x => !x.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PartyContact> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.PartyId, x.Type, x.NormalizedValue }).IsUnique();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Value).HasMaxLength(500).IsRequired();
        builder.Property(x => x.NormalizedValue).HasMaxLength(500).IsRequired();
        builder.HasOne(x => x.Party).WithMany(x => x.Contacts).HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(x => !x.Party.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PartyExternalIdentity> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.SourceSystem, x.SourceId }).IsUnique();
        builder.Property(x => x.SourceSystem).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SourceId).HasMaxLength(200).IsRequired();
        builder.HasOne(x => x.Party).WithMany(x => x.ExternalIdentities).HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(x => !x.Party.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<PartyBusinessProfile> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.PartyId, x.BusinessUnitId, x.Role }).IsUnique();
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(x => x.Party).WithMany(x => x.BusinessProfiles).HasForeignKey(x => x.PartyId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasQueryFilter(x => !x.Party.IsDeleted);
    }

    public void Configure(EntityTypeBuilder<BusinessDocument> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.CompanyId, x.DocumentType, x.SourceEntityType, x.SourceEntityId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.BusinessUnitId, x.IssuedAt });
        builder.Property(x => x.DocumentType).HasConversion<string>().HasMaxLength(40);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.DocumentNumber).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SourceEntityType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExternalSourceSystem).HasMaxLength(100);
        builder.Property(x => x.ExternalSourceId).HasMaxLength(200);
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<BusinessDocumentLink> builder)
    {
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.FromDocumentId, x.ToDocumentId, x.LinkType }).IsUnique();
        builder.Property(x => x.LinkType).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasOne(x => x.FromDocument).WithMany(x => x.OutgoingLinks).HasForeignKey(x => x.FromDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ToDocument).WithMany(x => x.IncomingLinks).HasForeignKey(x => x.ToDocumentId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    public void Configure(EntityTypeBuilder<EdTechCustomer> builder) =>
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);

    public void Configure(EntityTypeBuilder<Payment> builder) =>
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);

    public void Configure(EntityTypeBuilder<CscaClassStudent> builder)
    {
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<InterviewCustomer> builder)
    {
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<Supplier> builder) =>
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);

    public void Configure(EntityTypeBuilder<PurchaseReceipt> builder) =>
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);

    public void Configure(EntityTypeBuilder<SalesOrder> builder)
    {
        builder.HasOne(x => x.CustomerParty).WithMany().HasForeignKey(x => x.CustomerPartyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<SalesDocument> builder)
    {
        builder.HasOne(x => x.CustomerParty).WithMany().HasForeignKey(x => x.CustomerPartyId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);
    }

    public void Configure(EntityTypeBuilder<PayrollPeriod> builder) =>
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);

    public void Configure(EntityTypeBuilder<FinanceTransaction> builder) =>
        builder.HasOne(x => x.BusinessDocument).WithMany().HasForeignKey(x => x.BusinessDocumentId).OnDelete(DeleteBehavior.SetNull);

    public void Configure(EntityTypeBuilder<InternalCustomer> builder) =>
        builder.HasOne(x => x.Party).WithMany().HasForeignKey(x => x.PartyId).OnDelete(DeleteBehavior.SetNull);
}
