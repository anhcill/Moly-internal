using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using InternalManagement.Domain.Entities.CscaInterview;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using InternalManagement.Infrastructure.Integration;
using InternalManagement.Infrastructure.Persistence;

namespace InternalManagement.UnitTests.Integration;

public class WebhookProcessorTests
{
    private ApplicationDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("WebhookProcessorTest_" + Guid.NewGuid())
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task Ingest_NewEvent_ShouldSaveToInbox()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        // Act
        var (accepted, message) = await processor.IngestAsync(
            "WEBSITE_EDTECH", "evt_001", "course.updated",
            "{\"courseId\":123}", null, CancellationToken.None);

        // Assert
        accepted.Should().BeTrue();
        message.Should().Contain("tiếp nhận thành công");

        var inbox = await db.IntegrationInboxes.FirstOrDefaultAsync();
        inbox.Should().NotBeNull();
        inbox!.SourceSystem.Should().Be("WEBSITE_EDTECH");
        inbox.EventId.Should().Be("evt_001");
        inbox.EventType.Should().Be("course.updated");
        inbox.Status.Should().Be(IntegrationStatus.Pending);
    }

    [Fact]
    public async Task Ingest_DuplicateEvent_ShouldReturnAcceptedWithoutDuplicate()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        // First ingest
        await processor.IngestAsync(
            "WEBSITE_EDTECH", "evt_002", "payment.created",
            "{\"paymentId\":456}", null, CancellationToken.None);

        // Act: duplicate ingest
        var (accepted, message) = await processor.IngestAsync(
            "WEBSITE_EDTECH", "evt_002", "payment.created",
            "{\"paymentId\":456}", null, CancellationToken.None);

        // Assert
        accepted.Should().BeTrue();
        message.Should().Contain("idempotent");

        // Should only have 1 record
        var count = await db.IntegrationInboxes.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPending_ShouldMarkItemsAsSuccess()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        await processor.IngestAsync("SRC", "evt_a", "type.a", "{}", null, CancellationToken.None);
        await processor.IngestAsync("SRC", "evt_b", "type.b", "{}", null, CancellationToken.None);

        // Act
        var processed = await processor.ProcessPendingAsync(10, CancellationToken.None);

        // Assert
        processed.Should().Be(2);

        var items = await db.IntegrationInboxes.ToListAsync();
        items.Should().AllSatisfy(i =>
        {
            i.Status.Should().Be(IntegrationStatus.Success);
            i.ProcessedAt.Should().NotBeNull();
        });
    }

    [Fact]
    public async Task ProcessPending_WithNoPending_ShouldReturnZero()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        // Act
        var processed = await processor.ProcessPendingAsync(10, CancellationToken.None);

        // Assert
        processed.Should().Be(0);
    }

    [Fact]
    public async Task Ingest_CscaLmsAttendance_ShouldCreateAndThenUpdateManagementAttendance()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var classId = Guid.NewGuid();
        var studentId = Guid.NewGuid();
        const string secret = "test-webhook-secret";
        db.CscaClasses.Add(new CscaClass { Id = classId, Code = "CSCA-01", Name = "CSCA 01", IsDeleted = false });
        db.CscaClassStudents.Add(new CscaClassStudent { Id = studentId, ClassId = classId, StudentName = "Học viên thử" });
        db.IntegrationSources.Add(new IntegrationSource
        {
            CompanyId = Guid.NewGuid(),
            Code = "CSCA_COURSE_LMS",
            Name = "CSCA Course LMS",
            BaseUrl = "https://lms.example.test",
            AuthType = "HMAC",
            CredentialReference = secret,
            IsActive = true
        });
        await db.SaveChangesAsync();
        var processor = new WebhookProcessor(db, NullLogger<WebhookProcessor>.Instance);

        var firstPayload = AttendancePayload(classId, studentId, "present");

        // Act
        var (accepted, message) = await processor.IngestAsync(
            "CSCA_COURSE_LMS", "attendance-evt-1", "lms.attendance.recorded",
            firstPayload, Sign(firstPayload, secret), CancellationToken.None);

        // Assert
        accepted.Should().BeTrue();
        message.Should().Contain("lưu trực tiếp");
        var session = await db.CscaLessonSessions.SingleAsync();
        session.ExternalSource.Should().Be("CSCA_COURSE_LMS");
        session.ExternalSessionId.Should().Be("lms-session-101");
        var attendance = await db.CscaLessonAttendances.SingleAsync();
        attendance.StudentId.Should().Be(studentId);
        attendance.Status.Should().Be("Present");

        // A later teacher correction for the same LMS session updates, rather
        // than duplicating, the Management lesson attendance.
        var correctedPayload = AttendancePayload(classId, studentId, "absent");
        var corrected = await processor.IngestAsync(
            "CSCA_COURSE_LMS", "attendance-evt-2", "lms.attendance.recorded",
            correctedPayload, Sign(correctedPayload, secret), CancellationToken.None);

        corrected.Accepted.Should().BeTrue();
        (await db.CscaLessonSessions.CountAsync()).Should().Be(1);
        (await db.CscaLessonAttendances.SingleAsync()).Status.Should().Be("Absent");
    }

    private static string AttendancePayload(Guid classId, Guid studentId, string status) =>
        $$"""{"schemaVersion":1,"managementClassId":"{{classId}}","lmsSession":{"id":"lms-session-101","title":"Buổi 1","startTime":"2026-09-21T09:00:00.000Z","endTime":"2026-09-21T10:30:00.000Z","status":"scheduled"},"attendance":[{"managementStudentId":"{{studentId}}","status":"{{status}}","checkedAt":"2026-09-21T09:05:00.000Z","note":""}]}""";

    private static string Sign(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return $"sha256={Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))}";
    }
}
