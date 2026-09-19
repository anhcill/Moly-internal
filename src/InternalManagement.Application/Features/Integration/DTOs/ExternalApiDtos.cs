namespace InternalManagement.Application.Features.Integration.DTOs;

// ── External API DTOs ──
// Đại diện cho cấu trúc JSON trả về từ API bên ngoài (website EdTech).
// Tách biệt khỏi domain entities để cho phép thay đổi API mà không ảnh hưởng domain.

public sealed record ExternalCourseDto
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Slug { get; init; }
    public string? Description { get; init; }
    public string? ThumbnailUrl { get; init; }
    public decimal Price { get; init; }
    public string Status { get; init; } = "Published";
    public int Version { get; init; } = 1;
    public DateTime? UpdatedAt { get; init; }
    public List<ExternalCourseModuleDto> Modules { get; init; } = [];
}

public sealed record ExternalCourseModuleDto
{
    public string Title { get; init; } = string.Empty;
    public int OrderIndex { get; init; }
}

public sealed record ExternalCustomerDto
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// Customer từ nền tảng Interview. Giữ DTO riêng vì nguồn này không dùng
/// cùng mô hình customer của EdTech.
/// </summary>
public sealed record ExternalInterviewCustomerDto
{
    public string Id { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string PackageName { get; init; } = "Interview";
    public int SessionCount { get; init; } = 1;
    public decimal PaidAmount { get; init; }
    public string Status { get; init; } = "Pending";
    public DateTime? CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record ExternalPaymentDto
{
    public string Id { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "VND";
    public string Status { get; init; } = "Paid";
    public DateTime PaidAt { get; init; }
    public string? PaymentMethod { get; init; }
    public string? TransactionReference { get; init; }
}

public sealed record ExternalSubscriptionDto
{
    public string Id { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string PackageName { get; init; } = string.Empty;
    public DateTime StartsAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string Status { get; init; } = "Active";
}

public sealed record ExternalQuestionDto
{
    public string Id { get; init; } = string.Empty;
    public string BankName { get; init; } = string.Empty;
    public string? Subject { get; init; }
    public string? Topic { get; init; }
    public string DifficultyLevel { get; init; } = "Medium";
    public string ContentHtml { get; init; } = string.Empty;
    public string? ExplanationHtml { get; init; }
    public List<ExternalQuestionChoiceDto> Choices { get; init; } = [];
    public List<string> Tags { get; init; } = [];
}

public sealed record ExternalQuestionChoiceDto
{
    public string Label { get; init; } = string.Empty;
    public string ContentHtml { get; init; } = string.Empty;
    public bool IsCorrect { get; init; }
    public int OrderIndex { get; init; }
}
