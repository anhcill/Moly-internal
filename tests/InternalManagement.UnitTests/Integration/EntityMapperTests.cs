using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration.Mappers;
using InternalManagement.Infrastructure.Persistence;

namespace InternalManagement.UnitTests.Integration;

public class EntityMapperTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("EntityMapperTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CourseMapper_NewCourse_ShouldInsertWithModules()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var mapper = new CourseMapper(db, NullLogger<CourseMapper>.Instance);
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "course_101",
            title = "Khóa học .NET 10 Microservices",
            slug = "dotnet-10-microservices",
            description = "Học thiết kế hệ thống phân tán",
            thumbnailUrl = "https://cdn.moli.vn/dotnet10.png",
            price = 599000m,
            status = "Published",
            version = 1,
            modules = new[]
            {
                new { title = "Module 1: Kiến trúc tổng quan", orderIndex = 0 },
                new { title = "Module 2: Clean Architecture", orderIndex = 1 }
            }
        });

        // Act
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Written);
        result.SourceId.Should().Be("course_101");

        var course = await db.Courses.Include(c => c.Modules).FirstOrDefaultAsync(c => c.CourseSourceId == "course_101");
        course.Should().NotBeNull();
        course!.Title.Should().Be("Khóa học .NET 10 Microservices");
        course.Price.Should().Be(599000m);
        course.Modules.Count.Should().Be(2);
    }

    [Fact]
    public async Task CourseMapper_ExistingCourseSameVersion_ShouldSkip()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var mapper = new CourseMapper(db, NullLogger<CourseMapper>.Instance);
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "course_102",
            title = "React & TypeScript",
            price = 399000m,
            status = "Published",
            version = 1
        });

        // First insert
        await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Act: same version
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Skipped);
    }

    [Fact]
    public async Task CustomerMapper_ValidData_ShouldInsertAndSkipDuplicate()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var mapper = new CustomerMapper(db, NullLogger<CustomerMapper>.Instance);
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "cust_201",
            fullName = "Trần Đình Trọng",
            email = "trong.tran@moli.vn",
            phoneNumber = "0987654321"
        });

        // Act 1: Insert
        var res1 = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);
        res1.Status.Should().Be(MapResultStatus.Written);

        var cust = await db.EdTechCustomers.FirstOrDefaultAsync(c => c.SourceId == "cust_201");
        cust.Should().NotBeNull();
        cust!.FullName.Should().Be("Trần Đình Trọng");

        // Act 2: Duplicate
        var res2 = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);
        res2.Status.Should().Be(MapResultStatus.Skipped);
    }

    [Fact]
    public async Task CustomerMapper_InvalidEmail_ShouldFail()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var mapper = new CustomerMapper(db, NullLogger<CustomerMapper>.Instance);
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "cust_bad",
            fullName = "Khách Hàng Sai Mail",
            email = "invalid-email-format"
        });

        // Act
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Failed);
        result.ErrorCode.Should().Be("VALIDATION");
        result.ErrorMessage.Should().Contain("Invalid email");
    }

    [Fact]
    public async Task PaymentMapper_ValidPayment_ShouldInsertWithCustomerFK()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        // Seed customer
        var customer = new EdTechCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            SourceSystem = "WEBSITE_EDTECH",
            SourceId = "cust_301",
            FullName = "Hoàng Hải Yến",
            Email = "yen.hoang@moli.vn"
        };
        db.EdTechCustomers.Add(customer);
        await db.SaveChangesAsync();

        var mapper = new PaymentMapper(db, NullLogger<PaymentMapper>.Instance);
        var json = JsonSerializer.SerializeToElement(new
        {
            id = "pay_301",
            customerId = "cust_301",
            amount = 499000m,
            currency = "VND",
            status = "Paid",
            paidAt = DateTime.UtcNow,
            paymentMethod = "VNPAY",
            transactionReference = "VNPAY_998877"
        });

        // Act
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Written);
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.SourcePaymentId == "pay_301");
        payment.Should().NotBeNull();
        payment!.CustomerId.Should().Be(customer.Id);
        payment.Amount.Should().Be(499000m);
        payment.Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task PaymentMapper_NonExistentCustomer_ShouldFail()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var mapper = new PaymentMapper(db, NullLogger<PaymentMapper>.Instance);

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "pay_orphan",
            customerId = "cust_ghost",
            amount = 100000m,
            currency = "VND",
            status = "Paid",
            paidAt = DateTime.UtcNow
        });

        // Act
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Failed);
        result.ErrorCode.Should().Be("FK_NOT_FOUND");
    }

    [Fact]
    public async Task SubscriptionMapper_ValidSubscription_ShouldInsert()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        var customer = new EdTechCustomer
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            SourceSystem = "WEBSITE_EDTECH",
            SourceId = "cust_sub_1",
            FullName = "Nguyễn Văn Sub",
            Email = "sub@moli.vn"
        };
        db.EdTechCustomers.Add(customer);
        await db.SaveChangesAsync();

        var mapper = new SubscriptionMapper(db, NullLogger<SubscriptionMapper>.Instance);
        var starts = DateTime.UtcNow;
        var expires = starts.AddMonths(6);

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "sub_401",
            customerId = "cust_sub_1",
            packageName = "VIP Combo 6 Months",
            startsAt = starts,
            expiresAt = expires,
            status = "Active"
        });

        // Act
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Written);
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.CustomerId == customer.Id);
        sub.Should().NotBeNull();
        sub!.PackageName.Should().Be("VIP Combo 6 Months");
        sub.Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task QuestionMapper_NewQuestion_ShouldCreateBankSubjectTopicVersionAndChoices()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var mapper = new QuestionMapper(db, NullLogger<QuestionMapper>.Instance);
        var companyId = Guid.NewGuid();
        var buId = Guid.NewGuid();

        var json = JsonSerializer.SerializeToElement(new
        {
            id = "q_501",
            bankName = "Ngân Hàng Đề Thi C#",
            subject = "Công Nghệ Thông Tin",
            topic = "C# Nâng Cao",
            difficultyLevel = "Hard",
            contentHtml = "<p>Pattern Matching trong C# 9+ hỗ trợ cú pháp nào sau đây?</p>",
            explanationHtml = "<p>Relational và Logical patterns</p>",
            choices = new[]
            {
                new { label = "A", contentHtml = "is >= 10 and <= 20", isCorrect = true, orderIndex = 0 },
                new { label = "B", contentHtml = "in (10..20)", isCorrect = false, orderIndex = 1 },
                new { label = "C", contentHtml = "between 10 and 20", isCorrect = false, orderIndex = 2 }
            },
            tags = new[] { "csharp", "pattern-matching", "advanced" }
        });

        // Act
        var result = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);
        var duplicateResult = await mapper.MapAndUpsertAsync(json, "WEBSITE_EDTECH", companyId, buId, CancellationToken.None);

        // Assert
        result.Status.Should().Be(MapResultStatus.Written);
        duplicateResult.Status.Should().Be(MapResultStatus.Skipped);

        var bank = await db.QuestionBanks.FirstOrDefaultAsync(b => b.Name == "Ngân Hàng Đề Thi C#");
        bank.Should().NotBeNull();

        var subject = await db.Subjects.Include(s => s.Topics).FirstOrDefaultAsync(s => s.Name == "Công Nghệ Thông Tin");
        subject.Should().NotBeNull();
        subject!.Topics.Should().Contain(t => t.Name == "C# Nâng Cao");

        var question = await db.Questions
            .Include(q => q.Versions)
                .ThenInclude(v => v.Choices)
            .Include(q => q.Tags)
            .FirstOrDefaultAsync(q => q.QuestionBankId == bank!.Id);

        question.Should().NotBeNull();
        question!.DifficultyLevel.Should().Be("Hard");
        question.Tags.Count.Should().Be(3);
        question.Versions.Count.Should().Be(1);

        var version = question.Versions.First();
        version.Choices.Count.Should().Be(3);
        version.Choices.Should().Contain(c => c.IsCorrect && c.Label == "A");
    }
}
