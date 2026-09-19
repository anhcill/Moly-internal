using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.CscaInterview.DTOs;

// ── Interview Customer DTOs ──

public sealed record InterviewCustomerDto
{
    public Guid Id { get; init; }
    public string SourceSystem { get; init; } = "WEBSITE_INTERVIEW";
    public string SourceId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string PackageName { get; init; } = string.Empty;
    public int SessionCount { get; init; } = 1;
    public decimal PaidAmount { get; init; }
    public PaymentStatus Status { get; init; } = PaymentStatus.Paid;
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record CreateInterviewCustomerRequest(
    string FullName,
    string Email,
    string? Phone,
    string PackageName,
    int SessionCount,
    decimal PaidAmount,
    PaymentStatus Status = PaymentStatus.Paid,
    string? SourceId = null);

public sealed record UpdateInterviewCustomerRequest(
    string FullName,
    string Email,
    string? Phone,
    string PackageName,
    int SessionCount,
    decimal PaidAmount,
    PaymentStatus Status);

// ── Interview Financial Summary DTO ──

public sealed record InterviewFinancialSummaryDto
{
    public int TotalCustomers { get; init; }
    public int TotalSessions { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal PendingRevenue { get; init; }
}
