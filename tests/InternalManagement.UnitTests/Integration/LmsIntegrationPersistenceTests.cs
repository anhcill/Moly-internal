using FluentAssertions;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace InternalManagement.UnitTests.Integration;

public sealed class LmsIntegrationPersistenceTests
{
    [Fact]
    public void LmsIntegrationModel_ShouldProtectIdentityAndIdempotency()
    {
        using var db = CreateInMemoryDb();

        var accountLink = db.Model.FindEntityType(typeof(LmsAccountLink))!;
        var courseLink = db.Model.FindEntityType(typeof(LmsCourseLink))!;
        var grant = db.Model.FindEntityType(typeof(LmsAccessGrant))!;
        var outbox = db.Model.FindEntityType(typeof(IntegrationOutbox))!;
        var syncStatus = db.Model.FindEntityType(typeof(LmsSyncStatus))!;

        accountLink.GetCheckConstraints()
            .Should().Contain(x => x.Name == "ck_lms_account_links_identity");
        HasUniqueIndex(accountLink, "CompanyId", "SourceSystem", "ExternalStudentId").Should().BeTrue();
        HasUniqueIndex(accountLink, "CompanyId", "SourceSystem", "PartyId").Should().BeTrue();
        HasUniqueIndex(courseLink, "CompanyId", "SourceSystem", "ExternalCourseId").Should().BeTrue();
        HasUniqueIndex(grant, "CompanyId", "SourceSystem", "ExternalGrantId").Should().BeTrue();
        HasUniqueIndex(grant, "CscaClassStudentId", "LmsCourseLinkId").Should().BeTrue();
        HasUniqueIndex(outbox, "SourceSystem", "EventId").Should().BeTrue();
        HasUniqueIndex(outbox, "SourceSystem", "IdempotencyKey").Should().BeTrue();
        HasUniqueIndex(syncStatus, "CompanyId", "SourceSystem", "EntityType", "ExternalId").Should().BeTrue();
    }

    [Fact]
    public async Task LmsIntegrationRecords_ShouldPersistWithPaymentPendingAccess()
    {
        await using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var party = new Party { CompanyId = companyId, DisplayName = "Nguyen Van A" };
        var course = new Course
        {
            CompanyId = companyId,
            CourseSourceId = "course-csca-001",
            Title = "CSCA Foundation"
        };
        var cscaClass = new CscaClass
        {
            CompanyId = companyId,
            Course = course,
            CourseId = course.Id,
            Code = "CSCA-01",
            Name = "CSCA K01",
            Batch = "K01",
            Schedule = "T2-T4-T6",
            TuitionFee = 5_000_000m
        };
        var student = new CscaClassStudent
        {
            Class = cscaClass,
            ClassId = cscaClass.Id,
            Party = party,
            PartyId = party.Id,
            StudentName = party.DisplayName,
            Email = "student@example.com",
            PaymentStatus = PaymentStatus.Pending
        };
        var accountLink = new LmsAccountLink
        {
            CompanyId = companyId,
            Party = party,
            PartyId = party.Id,
            CscaClassStudent = student,
            CscaClassStudentId = student.Id,
            ExternalStudentId = "student-csca-001",
            LmsEmail = student.Email
        };
        var courseLink = new LmsCourseLink
        {
            CompanyId = companyId,
            Course = course,
            CourseId = course.Id,
            ExternalCourseId = course.CourseSourceId,
            LmsCourseSlug = "csca-foundation"
        };
        var grant = new LmsAccessGrant
        {
            CompanyId = companyId,
            LmsAccountLink = accountLink,
            LmsAccountLinkId = accountLink.Id,
            LmsCourseLink = courseLink,
            LmsCourseLinkId = courseLink.Id,
            CscaClassStudent = student,
            CscaClassStudentId = student.Id,
            ExternalGrantId = "grant-csca-001"
        };
        var outbox = new IntegrationOutbox
        {
            CompanyId = companyId,
            EventId = "evt-csca-001",
            EventType = "lms.access.pending",
            AggregateType = nameof(LmsAccessGrant),
            AggregateId = grant.Id.ToString(),
            IdempotencyKey = "lms.access.pending:grant-csca-001",
            PayloadJson = "{}"
        };
        var syncStatus = new LmsSyncStatus
        {
            CompanyId = companyId,
            EntityType = nameof(LmsAccessGrant),
            ExternalId = grant.ExternalGrantId
        };

        db.AddRange(grant, outbox, syncStatus);
        await db.SaveChangesAsync();

        (await db.LmsAccountLinks.SingleAsync()).Status.Should().Be(LmsAccountStatus.PendingPayment);
        (await db.LmsCourseLinks.SingleAsync()).Status.Should().Be(IntegrationStatus.Pending);
        (await db.LmsAccessGrants.SingleAsync()).Status.Should().Be(LmsAccessGrantStatus.PendingPayment);
        (await db.IntegrationOutboxes.SingleAsync()).Status.Should().Be(IntegrationStatus.Pending);
        (await db.LmsSyncStatuses.SingleAsync()).Status.Should().Be(IntegrationStatus.Pending);
    }

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"LmsIntegrationPersistence_{Guid.NewGuid():N}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static bool HasUniqueIndex(IEntityType entityType, params string[] propertyNames) =>
        entityType.GetIndexes().Any(index =>
            index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(propertyNames));
}
