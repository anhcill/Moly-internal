using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.InternalData.DTOs;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.InternalData;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;

namespace InternalManagement.UnitTests.InternalData;

public sealed class InternalDataServiceTests
{
    [Fact]
    public async Task CreateCustomer_AllowsSameCodeInTwoSegments_WithoutMixingData()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var service = CreateService(db, new FakeCurrentUser(company.Id));
        var request = new CreateInternalCustomerRequest("KH-001", "Khách hàng số 1", Email: "khach@example.com");

        var edtech = await service.CreateCustomerAsync(BusinessSegment.TECHNOLOGY_EDUCATION, request, default);
        var fashion = await service.CreateCustomerAsync(BusinessSegment.FASHION, request, default);
        var edtechList = await service.GetCustomersAsync(BusinessSegment.TECHNOLOGY_EDUCATION, null, null, null, 1, 20, default);
        var fashionList = await service.GetCustomersAsync(BusinessSegment.FASHION, null, null, null, 1, 20, default);

        edtech.Succeeded.Should().BeTrue();
        fashion.Succeeded.Should().BeTrue();
        edtech.Value!.BusinessUnitId.Should().NotBe(fashion.Value!.BusinessUnitId);
        edtechList.Value!.Items.Should().ContainSingle(x => x.Id == edtech.Value.Id);
        edtechList.Value.Items.Should().NotContain(x => x.Id == fashion.Value.Id);
        fashionList.Value!.Items.Should().ContainSingle(x => x.Id == fashion.Value.Id);
    }

    [Fact]
    public async Task ScopedFashionUser_CannotAccessTechnologyEducationSegment()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var fashionUnit = await db.BusinessUnits.SingleAsync(x => x.Code == "FASHION");
        var service = CreateService(db, new FakeCurrentUser(company.Id, fashionUnit.Id));

        var result = await service.GetCustomersAsync(
            BusinessSegment.TECHNOLOGY_EDUCATION, null, null, null, 1, 20, default);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("không thuộc mảng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HeadOfficeUser_CanAccessBothSegments_WhenEndpointPermissionIsGranted()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var headOffice = await db.BusinessUnits.SingleAsync(x => x.Code == "HQ");
        var service = CreateService(db, new FakeCurrentUser(company.Id, headOffice.Id));

        var technology = await service.GetCustomersAsync(
            BusinessSegment.TECHNOLOGY_EDUCATION, null, null, null, 1, 20, default);
        var fashion = await service.GetCustomersAsync(
            BusinessSegment.FASHION, null, null, null, 1, 20, default);

        technology.Succeeded.Should().BeTrue();
        fashion.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task EdtechCscaAndInterviewUsers_ShareCanonicalTechnologyEducationRepository()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var edtechUnit = await db.BusinessUnits.SingleAsync(x => x.Code == "EDTECH");
        var cscaUnit = await db.BusinessUnits.SingleAsync(x => x.Code == "CSCA");
        var interviewUnit = await db.BusinessUnits.SingleAsync(x => x.Code == "INTERVIEW");

        var cscaService = CreateService(db, new FakeCurrentUser(company.Id, cscaUnit.Id));
        var created = await cscaService.CreateResourceAsync(
            BusinessSegment.TECHNOLOGY_EDUCATION,
            new CreateInternalResourceRequest(
                "DE-CSCA-001", "Đề luyện thi dùng chung", "de-thi", "files/edtech/de-csca-001.pdf"),
            default);

        var edtechService = CreateService(db, new FakeCurrentUser(company.Id, edtechUnit.Id));
        var interviewService = CreateService(db, new FakeCurrentUser(company.Id, interviewUnit.Id));
        var fromEdtech = await edtechService.GetResourceByIdAsync(
            BusinessSegment.TECHNOLOGY_EDUCATION, created.Value!.Id, default);
        var fromInterview = await interviewService.GetResourceByIdAsync(
            BusinessSegment.TECHNOLOGY_EDUCATION, created.Value.Id, default);

        created.Succeeded.Should().BeTrue();
        created.Value.BusinessUnitId.Should().Be(edtechUnit.Id, "EDTECH là BU lưu trữ chuẩn của cả mảng");
        fromEdtech.Succeeded.Should().BeTrue();
        fromInterview.Succeeded.Should().BeTrue();
        fromInterview.Value!.Id.Should().Be(created.Value.Id);
    }

    [Fact]
    public async Task CreateResource_RejectsResourceTypeFromOtherSegment()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var service = CreateService(db, new FakeCurrentUser(company.Id));
        var request = new CreateInternalResourceRequest(
            "TK-001", "Thiết kế áo dài", "DESIGN_SAMPLE", "s3://moli/designs/ao-dai-v1.pdf");

        var result = await service.CreateResourceAsync(BusinessSegment.TECHNOLOGY_EDUCATION, request, default);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("EXAM", StringComparison.Ordinal));
        db.InternalResources.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateResource_RejectsEmbeddedBinaryDataUri()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var service = CreateService(db, new FakeCurrentUser(company.Id));
        var request = new CreateInternalResourceRequest(
            "TL-001", "Tài liệu nội bộ", "DOCUMENT", "data:application/pdf;base64,JVBERi0x");

        var result = await service.CreateResourceAsync(BusinessSegment.TECHNOLOGY_EDUCATION, request, default);

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().Contain(x => x.Contains("base64", StringComparison.OrdinalIgnoreCase));
        db.InternalResources.Should().BeEmpty();
    }

    [Fact]
    public async Task ResourceList_SearchesAndFiltersByNormalizedTag()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var service = CreateService(db, new FakeCurrentUser(company.Id));

        await service.CreateResourceAsync(
            BusinessSegment.FASHION,
            new CreateInternalResourceRequest(
                "KH-AD-2026", "Kế hoạch bộ sưu tập áo dài", "ke-hoach", "files/fashion/ke-hoach-ao-dai.docx",
                Tags: ["Áo dài", "Bộ sưu tập"]),
            default);
        await service.CreateResourceAsync(
            BusinessSegment.FASHION,
            new CreateInternalResourceRequest(
                "TL-FASHION", "Quy trình đóng gói", "tai-lieu", "files/fashion/dong-goi.pdf",
                Tags: ["Vận hành"]),
            default);

        var result = await service.GetResourcesAsync(
            BusinessSegment.FASHION, "áo dài", null, null, "ÁO DÀI", 1, 20, default);

        result.Succeeded.Should().BeTrue();
        result.Value!.Items.Should().ContainSingle(x => x.Code == "KH-AD-2026");
        result.Value.Items.Single().Tags.Should().Contain("áo dài");
    }

    [Fact]
    public async Task DeleteCustomer_SoftDeletesAndKeepsOtherSegmentUntouched()
    {
        await using var db = CreateDatabase();
        var company = await db.Companies.SingleAsync();
        var service = CreateService(db, new FakeCurrentUser(company.Id));
        var created = await service.CreateCustomerAsync(
            BusinessSegment.FASHION,
            new CreateInternalCustomerRequest("KH-XOA", "Khách cần xóa"),
            default);

        var deleted = await service.DeleteCustomerAsync(BusinessSegment.FASHION, created.Value!.Id, default);
        var visible = await service.GetCustomerByIdAsync(BusinessSegment.FASHION, created.Value.Id, default);
        var stored = await db.InternalCustomers.IgnoreQueryFilters().SingleAsync(x => x.Id == created.Value.Id);

        deleted.Succeeded.Should().BeTrue();
        visible.Succeeded.Should().BeFalse();
        stored.IsDeleted.Should().BeTrue();
        stored.DeletedAt.Should().NotBeNull();
    }

    private static ApplicationDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"InternalDataTests_{Guid.NewGuid():N}")
            .Options;
        var db = new ApplicationDbContext(options);
        var company = new Company { Code = "MOLI", Name = "MOLI Group" };
        db.Companies.Add(company);
        db.BusinessUnits.AddRange(
            new BusinessUnit { CompanyId = company.Id, Code = "HQ", Name = "Trụ sở chính" },
            new BusinessUnit { CompanyId = company.Id, Code = "EDTECH", Name = "Công nghệ - Giáo dục" },
            new BusinessUnit { CompanyId = company.Id, Code = "CSCA", Name = "CSCA" },
            new BusinessUnit { CompanyId = company.Id, Code = "INTERVIEW", Name = "Phỏng vấn" },
            new BusinessUnit { CompanyId = company.Id, Code = "FASHION", Name = "Thời trang" });
        db.SaveChanges();
        return db;
    }

    private static InternalDataService CreateService(ApplicationDbContext db, ICurrentUserService currentUser) =>
        new(db, currentUser, NullLogger<InternalDataService>.Instance);

    private sealed class FakeCurrentUser(Guid companyId, Guid? businessUnitId = null) : ICurrentUserService
    {
        public Guid? UserId => Guid.NewGuid();
        public string? Username => "unit-test";
        public Guid? CompanyId => companyId;
        public Guid? BusinessUnitId => businessUnitId;
        public IReadOnlyList<string> Permissions => Array.Empty<string>();
        public bool HasPermission(string permission) => true;
    }
}
