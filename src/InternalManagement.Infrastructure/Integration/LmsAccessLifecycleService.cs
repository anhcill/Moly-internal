using System.Text.Json;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Converts a CSCA enrollment/payment snapshot into durable LMS account and
/// entitlement commands. This service intentionally does not call HTTP: its
/// writes are committed atomically with the enrollment/payment change, then a
/// separate outbox dispatcher sends the commands to the LMS.
/// </summary>
public sealed class LmsAccessLifecycleService : ILmsAccessLifecycleService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IApplicationDbContext _db;
    private readonly ILogger<LmsAccessLifecycleService> _logger;

    public LmsAccessLifecycleService(
        IApplicationDbContext db,
        ILogger<LmsAccessLifecycleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ReconcileStudentAccessAsync(LmsAccessEvaluationRequest request, CancellationToken ct)
    {
        var externalStudentId = request.StudentId.ToString("N");
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            await AddManualReviewAsync(
                request.StudentId,
                "LMS_EMAIL_REQUIRED",
                "Không thể provision LMS vì học viên chưa có email.",
                ct);
            return;
        }

        var account = await _db.LmsAccountLinks
            .FirstOrDefaultAsync(link =>
                link.CompanyId == request.CompanyId &&
                link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                link.ExternalStudentId == externalStudentId,
                ct);

        if (account is null && request.PartyId.HasValue)
        {
            var existingPartyAccount = await _db.LmsAccountLinks
                .AsNoTracking()
                .FirstOrDefaultAsync(link =>
                    link.CompanyId == request.CompanyId &&
                    link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                    link.PartyId == request.PartyId,
                    ct);

            if (existingPartyAccount is not null)
            {
                await AddManualReviewAsync(
                    request.StudentId,
                    "LMS_PARTY_IDENTITY_CONFLICT",
                    "Party đã được liên kết với một external student ID khác; cần rà soát thủ công.",
                    ct);
                return;
            }
        }

        var courseLink = await _db.LmsCourseLinks
            .FirstOrDefaultAsync(link =>
                link.CompanyId == request.CompanyId &&
                link.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                link.CourseId == request.CourseId,
                ct);
        // A course identifier alone is not enough to issue paid access. Operations
        // must explicitly confirm the LMS target mapping first.
        var hasUsableCourseLink = courseLink is not null
                                  && courseLink.Status == IntegrationStatus.Success
                                  && !string.IsNullOrWhiteSpace(courseLink.ExternalCourseId);

        var isEligibleForAccess = request.PaymentStatus == PaymentStatus.Paid
                                  && request.PaidAmount >= request.TuitionFee;
        var isTerminalPaymentState = request.PaymentStatus is PaymentStatus.Refunded
            or PaymentStatus.Cancelled
            or PaymentStatus.Failed;

        var desiredAccountStatus = hasUsableCourseLink && isEligibleForAccess
            ? LmsAccountStatus.Active
            : isTerminalPaymentState
                ? LmsAccountStatus.Revoked
                : LmsAccountStatus.PendingPayment;

        var accountWasCreated = account is null;
        var accountChanged = accountWasCreated;
        if (account is null)
        {
            account = new LmsAccountLink
            {
                CompanyId = request.CompanyId,
                BusinessUnitId = request.BusinessUnitId,
                PartyId = request.PartyId,
                CscaClassStudentId = request.StudentId,
                ExternalStudentId = externalStudentId,
                LmsEmail = request.Email.Trim(),
                Status = desiredAccountStatus
            };
            _db.LmsAccountLinks.Add(account);
        }
        else
        {
            var normalizedEmail = request.Email.Trim();
            accountChanged = account.Status != desiredAccountStatus
                             || !string.Equals(account.LmsEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase)
                             || (request.PartyId.HasValue && account.PartyId != request.PartyId);
            account.BusinessUnitId = request.BusinessUnitId;
            account.PartyId ??= request.PartyId;
            account.CscaClassStudentId ??= request.StudentId;
            account.LmsEmail = normalizedEmail;
            account.Status = desiredAccountStatus;
            account.LastSyncError = null;
        }

        if (accountChanged)
        {
            var provision = new LmsProvisionCommand(
                account.ExternalStudentId,
                request.PartyId?.ToString("N"),
                request.StudentName,
                account.LmsEmail!,
                request.PhoneNumber,
                account.Status.ToString(),
                request.PaymentStatus.ToString(),
                hasUsableCourseLink ? [courseLink!.ExternalCourseId] : [],
                request.ClassId.ToString("N"),
                request.SourceUpdatedAt);
            var correlationId = Guid.NewGuid().ToString("N");
            account.LastCorrelationId = correlationId;
            await QueueAsync(
                request.CompanyId,
                request.BusinessUnitId,
                LmsOutboxEventTypes.StudentProvisionRequested,
                nameof(LmsAccountLink),
                account.Id,
                $"lms-provision:{externalStudentId}:{account.Status}:{request.SourceUpdatedAt.Ticks}",
                correlationId,
                provision,
                ct);
        }

        if (!hasUsableCourseLink)
        {
            await AddManualReviewAsync(
                request.StudentId,
                "LMS_COURSE_MAPPING_MISSING",
                $"Học viên chưa được cấp quyền LMS vì chưa có course mapping cho source '{request.CourseSourceId}'.",
                ct);
            return;
        }

        var mappedCourseLink = courseLink!;

        var grant = await _db.LmsAccessGrants
            .FirstOrDefaultAsync(item =>
                item.CscaClassStudentId == request.StudentId &&
                item.LmsCourseLinkId == mappedCourseLink.Id,
                ct);

        var desiredGrantStatus = GetDesiredGrantStatus(grant?.Status, isEligibleForAccess, isTerminalPaymentState);
        var grantWasCreated = grant is null;
        var grantChanged = grantWasCreated;
        if (grant is null)
        {
            grant = new LmsAccessGrant
            {
                CompanyId = request.CompanyId,
                BusinessUnitId = request.BusinessUnitId,
                LmsAccountLinkId = account.Id,
                LmsCourseLinkId = mappedCourseLink.Id,
                CscaClassStudentId = request.StudentId,
                ExternalGrantId = BuildExternalGrantId(request.StudentId, mappedCourseLink.ExternalCourseId),
                SourcePaymentId = request.SourcePaymentId,
                Status = desiredGrantStatus,
                ValidFrom = request.SourceUpdatedAt,
                RevokedAt = desiredGrantStatus == LmsAccessGrantStatus.Revoked ? request.SourceUpdatedAt : null,
                RevocationReason = BuildAccessReason(request.PaymentStatus, desiredGrantStatus)
            };
            _db.LmsAccessGrants.Add(grant);
        }
        else
        {
            grantChanged = grant.Status != desiredGrantStatus
                           || !string.Equals(grant.SourcePaymentId, request.SourcePaymentId, StringComparison.Ordinal);
            grant.BusinessUnitId = request.BusinessUnitId;
            grant.LmsAccountLinkId = account.Id;
            grant.SourcePaymentId = request.SourcePaymentId;
            if (grant.Status != LmsAccessGrantStatus.Active && desiredGrantStatus == LmsAccessGrantStatus.Active)
                grant.ValidFrom = request.SourceUpdatedAt;
            grant.Status = desiredGrantStatus;
            grant.RevokedAt = desiredGrantStatus == LmsAccessGrantStatus.Revoked ? request.SourceUpdatedAt : null;
            grant.RevocationReason = BuildAccessReason(request.PaymentStatus, desiredGrantStatus);
            grant.LastSyncError = null;
        }

        // A new PendingPayment grant needs no access call; provisioning carries
        // the pending state. Any active/suspended/revoked change is sent later.
        if (grantChanged && desiredGrantStatus != LmsAccessGrantStatus.PendingPayment)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            var access = new LmsAccessCommand(
                desiredGrantStatus.ToString(),
                BuildAccessReason(request.PaymentStatus, desiredGrantStatus),
                request.SourcePaymentId,
                grant.ValidFrom,
                grant.ValidUntil,
                [mappedCourseLink.ExternalCourseId]);
            await QueueAsync(
                request.CompanyId,
                request.BusinessUnitId,
                LmsOutboxEventTypes.StudentAccessRequested,
                nameof(LmsAccessGrant),
                grant.Id,
                $"lms-access:{grant.Id:N}:{desiredGrantStatus}:{request.SourceUpdatedAt.Ticks}",
                correlationId,
                new LmsAccessOutboxPayload(account.ExternalStudentId, access),
                ct);
        }

        _logger.LogInformation(
            "LMS access reconciled for CSCA student {StudentId}: Account={AccountStatus}, Grant={GrantStatus}",
            request.StudentId,
            account.Status,
            grant.Status);
    }

    private async Task QueueAsync(
        Guid companyId,
        Guid? businessUnitId,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string idempotencyKey,
        string correlationId,
        object payload,
        CancellationToken ct)
    {
        var existsInContext = _db.IntegrationOutboxes.Local.Any(item =>
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.IdempotencyKey == idempotencyKey);
        var existsInDatabase = !existsInContext && await _db.IntegrationOutboxes.AnyAsync(item =>
            item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
            item.IdempotencyKey == idempotencyKey,
            ct);
        if (existsInContext || existsInDatabase)
            return;

        _db.IntegrationOutboxes.Add(new IntegrationOutbox
        {
            CompanyId = companyId,
            BusinessUnitId = businessUnitId,
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EventId = Guid.NewGuid().ToString("N"),
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId.ToString("N"),
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Status = IntegrationStatus.Pending,
            NextAttemptAt = DateTime.UtcNow
        });
    }

    private async Task AddManualReviewAsync(
        Guid studentId,
        string errorCode,
        string errorMessage,
        CancellationToken ct)
    {
        var sourceId = studentId.ToString("N");
        var alreadyOpen = _db.IntegrationDeadLetters.Local.Any(item =>
                              item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                              item.SourceId == sourceId &&
                              item.ErrorCode == errorCode &&
                              !item.Resolved)
                          || await _db.IntegrationDeadLetters.AnyAsync(item =>
                              item.SourceSystem == LmsIntegrationSourceSystems.CscaCourseLms &&
                              item.SourceId == sourceId &&
                              item.ErrorCode == errorCode &&
                              !item.Resolved,
                              ct);
        if (alreadyOpen)
            return;

        _db.IntegrationDeadLetters.Add(new IntegrationDeadLetter
        {
            SourceSystem = LmsIntegrationSourceSystems.CscaCourseLms,
            EntityType = nameof(LmsAccessGrant),
            SourceId = sourceId,
            PayloadJson = "{}",
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            CreatedAt = DateTime.UtcNow
        });
    }

    private static LmsAccessGrantStatus GetDesiredGrantStatus(
        LmsAccessGrantStatus? currentStatus,
        bool isEligibleForAccess,
        bool isTerminalPaymentState)
    {
        if (isEligibleForAccess)
            return LmsAccessGrantStatus.Active;
        if (isTerminalPaymentState)
            return LmsAccessGrantStatus.Revoked;
        return currentStatus is LmsAccessGrantStatus.Active or LmsAccessGrantStatus.Suspended
            ? LmsAccessGrantStatus.Suspended
            : LmsAccessGrantStatus.PendingPayment;
    }

    private static string BuildExternalGrantId(Guid studentId, string courseSourceId)
        => $"grant:{studentId:N}:{courseSourceId}";

    private static string BuildAccessReason(PaymentStatus paymentStatus, LmsAccessGrantStatus grantStatus) => grantStatus switch
    {
        LmsAccessGrantStatus.Active => "PAYMENT_PAID",
        LmsAccessGrantStatus.Suspended => "PAYMENT_NOT_SETTLED",
        LmsAccessGrantStatus.Revoked => $"PAYMENT_{paymentStatus.ToString().ToUpperInvariant()}",
        _ => "PAYMENT_PENDING"
    };

}
