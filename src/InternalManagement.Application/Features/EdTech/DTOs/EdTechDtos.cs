using InternalManagement.Domain.Enums;

namespace InternalManagement.Application.Features.EdTech.DTOs;

// ── Course DTOs ──

public sealed record CourseDto
{
    public Guid Id { get; init; }
    public string CourseSourceId { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public decimal Price { get; init; }
    public string Status { get; init; } = "Published";
    public int Version { get; init; }
    public int ModuleCount { get; init; }
    public int ClassCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public List<CourseModuleDto> Modules { get; init; } = [];
}

public sealed record CourseModuleDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public int OrderIndex { get; init; }
}

public sealed record CreateCourseRequest(
    string Title,
    string? Slug,
    string? Description,
    string? ThumbnailUrl,
    decimal Price,
    string Status = "Draft",
    List<string>? ModuleTitles = null);

public sealed record UpdateCourseRequest(
    string Title,
    string? Slug,
    string? Description,
    string? ThumbnailUrl,
    decimal Price,
    string Status);

// ── Question DTOs ──

public sealed record QuestionDto
{
    public Guid Id { get; init; }
    public string BankName { get; init; } = string.Empty;
    public string? SubjectName { get; init; }
    public string? TopicName { get; init; }
    public string DifficultyLevel { get; init; } = "Medium";
    public Guid? CurrentVersionId { get; init; }
    public int CurrentVersionNumber { get; init; }
    public string Status { get; init; } = "Draft";
    public string ContentHtml { get; init; } = string.Empty;
    public string? ExplanationHtml { get; init; }
    public List<QuestionChoiceDto> Choices { get; init; } = [];
    public List<string> Tags { get; init; } = [];
    public List<QuestionVersionSummaryDto> Versions { get; init; } = [];
}

public sealed record QuestionChoiceDto
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string ContentHtml { get; init; } = string.Empty;
    public bool IsCorrect { get; init; }
    public int OrderIndex { get; init; }
}

public sealed record QuestionVersionSummaryDto
{
    public Guid Id { get; init; }
    public int VersionNumber { get; init; }
    public string Status { get; init; } = "Draft";
    public DateTime CreatedAt { get; init; }
    public string? CreatedBy { get; init; }
}

public sealed record CreateQuestionRequest(
    string BankName,
    string? SubjectName,
    string? TopicName,
    string DifficultyLevel,
    string ContentHtml,
    string? ExplanationHtml,
    List<CreateQuestionChoiceRequest> Choices,
    List<string>? Tags = null);

public sealed record CreateQuestionChoiceRequest(
    string Label,
    string ContentHtml,
    bool IsCorrect,
    int OrderIndex);

public sealed record CreateQuestionVersionRequest(
    string ContentHtml,
    string? ExplanationHtml,
    List<CreateQuestionChoiceRequest> Choices);

public sealed record PublishQuestionVersionRequest(
    string? Notes = null);

public sealed record ContentPublicationDto
{
    public Guid Id { get; init; }
    public Guid QuestionVersionId { get; init; }
    public int VersionNumber { get; init; }
    public string PublishedBy { get; init; } = string.Empty;
    public DateTime PublishedAt { get; init; }
    public string? Notes { get; init; }
}

// ── Customer, Subscription & Payment DTOs ──

public sealed record CustomerSummaryDto
{
    public Guid Id { get; init; }
    public string SourceId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public int SubscriptionCount { get; init; }
    public decimal TotalPaidAmount { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record SubscriptionDto
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public DateTime StartsAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string Status { get; init; } = "Active";
}

public sealed record PaymentDto
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public string SourcePaymentId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "VND";
    public string Status { get; init; } = "Paid";
    public DateTime PaidAt { get; init; }
    public string? PaymentMethod { get; init; }
    public string? TransactionReference { get; init; }
}
