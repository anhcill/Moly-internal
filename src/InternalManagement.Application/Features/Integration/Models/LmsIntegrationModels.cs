using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.Integration.Models;

/// <summary>
/// Event names written to the transactional outbox for the CSCA Course LMS.
/// They are commands from Management, not events received from the LMS.
/// </summary>
public static class LmsOutboxEventTypes
{
    public const string StudentProvisionRequested = "lms.student.provision.requested";
    public const string StudentAccessRequested = "lms.student.access.requested";
}

/// <summary>
/// Snapshot of a CSCA enrollment used to calculate the LMS entitlement. It is
/// deliberately independent from EF entities so the business rule can be used
/// safely within the same Management transaction.
/// </summary>
public sealed record LmsAccessEvaluationRequest(
    Guid CompanyId,
    Guid? BusinessUnitId,
    Guid StudentId,
    Guid? PartyId,
    Guid ClassId,
    Guid CourseId,
    string CourseSourceId,
    string StudentName,
    string? Email,
    string? PhoneNumber,
    decimal PaidAmount,
    decimal TuitionFee,
    PaymentStatus PaymentStatus,
    DateTime SourceUpdatedAt,
    string? SourcePaymentId);

/// <summary>Payload for POST /students/provision on the CSCA Course LMS.</summary>
public sealed record LmsProvisionCommand(
    string ExternalStudentId,
    string? ExternalPartyId,
    string FullName,
    string Email,
    string? Phone,
    string AccountStatus,
    string PaymentStatus,
    IReadOnlyList<string> CourseSourceIds,
    string ClassSourceId,
    DateTime SourceUpdatedAt);

/// <summary>Payload for PATCH /students/{externalStudentId}/access on the LMS.</summary>
public sealed record LmsAccessCommand(
    string AccessStatus,
    string Reason,
    string? SourcePaymentId,
    DateTime ValidFrom,
    DateTime? ValidUntil,
    IReadOnlyList<string> CourseSourceIds);

/// <summary>Durable outbox payload for a per-student access command.</summary>
public sealed record LmsAccessOutboxPayload(string ExternalStudentId, LmsAccessCommand Access);

/// <summary>Response fields needed by Management after a successful provision.</summary>
public sealed record LmsProvisionResult(
    bool AlreadyExists,
    long? LmsUserId,
    string? ProvisionStatus,
    string? CorrelationId);

/// <summary>Outbound headers assigned from a durable IntegrationOutbox record.</summary>
public sealed record LmsOutboundRequestContext(string IdempotencyKey, string CorrelationId);

public sealed record LmsOutboxDispatchResult(int Processed, int Succeeded, int Retrying, int DeadLettered);
