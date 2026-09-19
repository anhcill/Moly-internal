namespace InternalManagement.Application.Features.CscaInterview.DTOs;

/// <summary>
/// Read-only content exposed by the CSCA-MOLI.STUDIO website.
/// These records intentionally contain only fields needed by the backoffice.
/// </summary>
public sealed record CscaOnlineMaterialDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Subject { get; init; }
    public string? Topic { get; init; }
    public string? FileType { get; init; }
    public string? FileUrl { get; init; }
    public string? ThumbnailUrl { get; init; }
    public int ViewCount { get; init; }
    public int DownloadCount { get; init; }
    public bool IsPremium { get; init; }
    public string? VipTier { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

public sealed record CscaOnlineVocabularyDto
{
    public int Id { get; init; }
    public string WordChinese { get; init; } = string.Empty;
    public string? Pinyin { get; init; }
    public string? WordVietnamese { get; init; }
    public string? WordEnglish { get; init; }
    public string? Subject { get; init; }
    public string? Topic { get; init; }
    public string? ExampleChinese { get; init; }
    public string? ExampleVietnamese { get; init; }
    public bool IsPremium { get; init; }
    public string? VipTier { get; init; }
}

public sealed record CscaOnlinePostDto
{
    public int Id { get; init; }
    public string Content { get; init; } = string.Empty;
    public string? ImageUrl { get; init; }
    public string? PostType { get; init; }
    public bool IsOfficial { get; init; }
    public string? ModerationStatus { get; init; }
    public string? AuthorName { get; init; }
    public string? AuthorRole { get; init; }
    public int LikeCount { get; init; }
    public int CommentCount { get; init; }
    public DateTime? CreatedAt { get; init; }
}
