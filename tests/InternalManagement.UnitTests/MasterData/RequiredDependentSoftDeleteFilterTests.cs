using FluentAssertions;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.MasterData;
using InternalManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InternalManagement.UnitTests.MasterData;

public sealed class RequiredDependentSoftDeleteFilterTests
{
    [Fact]
    public async Task RequiredDependents_ShouldBeHiddenTogetherWithTheirSoftDeletedParents()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"RequiredDependentFilters_{Guid.NewGuid():N}")
            .Options;
        await using var db = new ApplicationDbContext(options);

        var company = new Company { Code = "DEL", Name = "Deleted company", IsDeleted = true };
        var branch = new Branch { Company = company, CompanyId = company.Id, Code = "DEL-BR", Name = "Deleted branch" };

        var course = new Course { CompanyId = company.Id, Title = "Deleted course", IsDeleted = true };
        var cscaClass = new CscaClass
        {
            CompanyId = company.Id,
            Course = course,
            CourseId = course.Id,
            Code = "DEL-CLASS",
            Name = "Deleted class",
            IsDeleted = true
        };
        var schedule = new CscaClassSchedule
        {
            Class = cscaClass,
            ClassId = cscaClass.Id,
            DayOfWeek = 1,
            StartTime = TimeSpan.FromHours(8),
            EndTime = TimeSpan.FromHours(10)
        };

        var party = new Party
        {
            CompanyId = company.Id,
            DisplayName = "Deleted party",
            IsDeleted = true
        };
        var contact = new PartyContact
        {
            Party = party,
            PartyId = party.Id,
            Type = PartyContactType.Email,
            Value = "deleted@example.com",
            NormalizedValue = "deleted@example.com"
        };

        var product = new Product
        {
            CompanyId = company.Id,
            Code = "DEL-PRODUCT",
            Name = "Deleted product",
            IsDeleted = true
        };
        var variant = new ProductVariant
        {
            Product = product,
            ProductId = product.Id,
            Sku = "DEL-SKU"
        };
        var order = new SalesOrder
        {
            CompanyId = company.Id,
            OrderNumber = "DEL-ORDER",
            SourceSystem = "TEST"
        };
        var orderItem = new SalesOrderItem
        {
            SalesOrder = order,
            SalesOrderId = order.Id,
            ProductVariant = variant,
            ProductVariantId = variant.Id,
            SkuSnapshot = variant.Sku,
            ProductNameSnapshot = product.Name,
            Quantity = 1,
            UnitPrice = 100_000m
        };

        db.AddRange(branch, schedule, contact, orderItem);
        await db.SaveChangesAsync();

        (await db.Branches.ToListAsync()).Should().BeEmpty();
        (await db.CscaClassSchedules.ToListAsync()).Should().BeEmpty();
        (await db.PartyContacts.ToListAsync()).Should().BeEmpty();
        (await db.ProductVariants.ToListAsync()).Should().BeEmpty();
        (await db.SalesOrderItems.ToListAsync()).Should().BeEmpty();

        (await db.Branches.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.CscaClassSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.PartyContacts.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.ProductVariants.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.SalesOrderItems.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public void EveryRequiredDependentWithASoftDeletedParent_ShouldHaveAMatchingQueryFilter()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"RequiredDependentFilterModel_{Guid.NewGuid():N}")
            .Options;
        using var db = new ApplicationDbContext(options);

        var filteredDependentTypes = new[]
        {
            typeof(Branch), typeof(Department), typeof(Role), typeof(UserRole), typeof(UserRefreshToken), typeof(RolePermission),
            typeof(CourseModule), typeof(CscaClassStudent), typeof(CscaClassStaff), typeof(CscaClassSchedule), typeof(CscaLessonSession), typeof(CscaLessonAttendance),
            typeof(AttendanceRecord), typeof(PayrollAdjustment), typeof(Payslip), typeof(PayrollApproval),
            typeof(ProductVariant), typeof(PurchaseReceiptItem), typeof(InventoryMovement), typeof(InventoryBalance),
            typeof(SalesOrderItem), typeof(ReturnItem), typeof(SalesDocumentItem),
            typeof(Bom), typeof(BomItem), typeof(ProductionOrder), typeof(ProductionOrderOutput),
            typeof(ProductionOrderMaterial), typeof(ProductionOperation), typeof(OrderCostSnapshotItem),
            typeof(PartyContact), typeof(PartyExternalIdentity), typeof(PartyBusinessProfile)
        };

        foreach (var entityType in filteredDependentTypes)
        {
            db.Model.FindEntityType(entityType)!.GetQueryFilter().Should().NotBeNull(entityType.Name);
        }
    }
}
