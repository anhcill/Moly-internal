using FluentAssertions;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Infrastructure.Persistence;
using InternalManagement.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace InternalManagement.UnitTests.HrPayroll;

public sealed class CscaTeacherCostSnapshotServiceTests
{
    [Fact]
    public async Task GetAsync_WithNoSnapshots_ReturnsSuccessfulZeroSummary()
    {
        using var db = CreateInMemoryDb();
        var service = CreateService(db, Guid.NewGuid());

        var result = await service.GetAsync(null, null, null, null, null, true, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.ItemCount.Should().Be(0);
        result.Value.TeacherCount.Should().Be(0);
        result.Value.ClassCount.Should().Be(0);
        result.Value.ClassCostAmount.Should().Be(0);
        result.Value.ProjectedClassCostAmount.Should().Be(0);
        result.Value.LastSyncedAt.Should().BeNull();
        result.Value.Items.Should().BeEmpty();
        result.Value.ClassSummaries.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public async Task GetAsync_FiltersClassAndTreatsToAsExclusive()
    {
        using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        db.CscaTeacherCostSnapshots.AddRange(
            Snapshot(companyId, "class-a", "session-a", new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), 100m, 150m),
            Snapshot(companyId, "class-b", "session-b", new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc), 200m, 250m),
            Snapshot(companyId, "class-a", "session-boundary", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), 300m, 350m));
        await db.SaveChangesAsync();
        var service = CreateService(db, companyId);
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var all = await service.GetAsync(from, to, null, null, null, true, CancellationToken.None);
        var selected = await service.GetAsync(from, to, null, "class-a", null, true, CancellationToken.None);

        all.Succeeded.Should().BeTrue();
        all.Value!.ItemCount.Should().Be(2);
        all.Value.ClassCount.Should().Be(2);
        all.Value.ClassCostAmount.Should().Be(300m);
        all.Value.ProjectedClassCostAmount.Should().Be(400m);
        all.Value.ClassSummaries.Should().HaveCount(2);

        selected.Succeeded.Should().BeTrue();
        selected.Value!.ItemCount.Should().Be(1);
        selected.Value.ClassCount.Should().Be(1);
        selected.Value.ClassCostAmount.Should().Be(100m);
        selected.Value.ProjectedClassCostAmount.Should().Be(150m);
        selected.Value.ClassSummaries.Should().ContainSingle(x => x.ClassSourceId == "class-a");
    }

    [Fact]
    public async Task UpdateSettlementAsync_UpdatesMolyCourseThenRefreshesLocalSnapshot()
    {
        using var db = CreateInMemoryDb();
        var companyId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        db.Employees.Add(new Employee
        {
            Id = employeeId,
            CompanyId = companyId,
            EmployeeCode = "GV-001",
            FullName = "Teacher A",
            Email = "teacher-a@example.test"
        });
        var snapshot = Snapshot(
            companyId,
            "class-a",
            "session-a",
            new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc),
            400000m,
            800000m);
        snapshot.EmployeeId = employeeId;
        snapshot.TeacherSourceId = employeeId.ToString();
        snapshot.TeacherLmsId = 7;
        snapshot.ClassLmsId = 8;
        snapshot.SessionLmsId = 9;
        snapshot.SourceKey = "7:8:9";
        db.CscaTeacherCostSnapshots.Add(snapshot);
        await db.SaveChangesAsync();

        var lmsClient = new Mock<ICscaCourseLmsClient>();
        lmsClient.Setup(x => x.UpdateTeacherSettlementAsync(
                It.Is<LmsTeacherSettlementUpdate>(update =>
                    update.TeacherLmsId == 7
                    && update.BonusAmount == 125000m
                    && update.DeductionAmount == 25000m
                    && update.PaymentStatus == "processing"
                    && update.Notes == "Reviewed from management"),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lmsClient.Setup(x => x.QueryTeacherCostsAsync(
                It.Is<LmsTeacherCostQuery>(query =>
                    query.TeacherSourceId == employeeId.ToString()
                    && query.IncludeProjected),
                It.IsAny<LmsOutboundRequestContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LmsTeacherCostReport(
                "CSCA_COURSE",
                new DateTime(2026, 11, 6, 3, 0, 0, DateTimeKind.Utc),
                true,
                [new LmsTeacherCostItem(
                    employeeId.ToString(), 7, "Teacher A", "teacher-a",
                    "course-a", "CSCA", "class-a", 8, "Class A", "lead",
                    "session-a", 9, "Lesson 1",
                    new DateTime(2026, 11, 1, 1, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc),
                    "ended", true, 2m, 200000m, 400000m, 800000m,
                    125000m, 25000m, "processing", null,
                    new DateTime(2026, 11, 6, 2, 59, 0, DateTimeKind.Utc))]));
        var service = CreateService(db, companyId, lmsClient.Object);

        var result = await service.UpdateSettlementAsync(
            employeeId,
            new UpdateCscaTeacherSettlementRequest(
                125000m,
                25000m,
                "Processing",
                " Reviewed from management "),
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.BonusAmount.Should().Be(125000m);
        result.Value.DeductionAmount.Should().Be(25000m);
        result.Value.NetAccruedAmount.Should().Be(500000m);
        result.Value.Items.Should().ContainSingle(x => x.PaymentStatus == "processing");
        lmsClient.VerifyAll();
    }

    private static ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("CscaTeacherCostSnapshotServiceTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CscaTeacherCostSnapshotService CreateService(
        ApplicationDbContext db,
        Guid companyId,
        ICscaCourseLmsClient? lmsClient = null)
    {
        var currentUser = new Mock<ICurrentUserService>();
        currentUser.SetupGet(x => x.CompanyId).Returns(companyId);
        return new CscaTeacherCostSnapshotService(
            db,
            currentUser.Object,
            lmsClient ?? Mock.Of<ICscaCourseLmsClient>(),
            NullLogger<CscaTeacherCostSnapshotService>.Instance);
    }

    private static CscaTeacherCostSnapshot Snapshot(
        Guid companyId,
        string classSourceId,
        string sessionSourceId,
        DateTime startTime,
        decimal accruedAmount,
        decimal projectedAmount) => new()
    {
        CompanyId = companyId,
        SourceSystem = "CSCA_COURSE",
        SourceKey = $"teacher:{classSourceId}:{sessionSourceId}",
        TeacherLmsId = 10,
        TeacherName = "Teacher",
        ClassSourceId = classSourceId,
        ClassLmsId = classSourceId == "class-a" ? 1 : 2,
        ClassTitle = classSourceId,
        SessionSourceId = sessionSourceId,
        SessionLmsId = sessionSourceId.GetHashCode(),
        StartTime = startTime,
        EndTime = startTime.AddHours(2),
        Status = "ended",
        IsCompleted = true,
        DurationHours = 2,
        EffectiveHourlyRate = 50,
        AccruedAmount = accruedAmount,
        ProjectedAmount = projectedAmount,
        SourceCalculatedAt = startTime,
        SyncedAt = startTime,
        IsCurrent = true
    };
}
