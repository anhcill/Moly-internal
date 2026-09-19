using InternalManagement.Domain.Entities.InternalData;

namespace InternalManagement.Application.Features.InternalData.DTOs;

public sealed record InternalCustomerDto(
    Guid Id,
    Guid CompanyId,
    Guid BusinessUnitId,
    string BusinessSegment,
    string BusinessSegmentName,
    string Code,
    string Name,
    string? ContactPerson,
    string? Email,
    string? Phone,
    string? Address,
    string? Source,
    string Status,
    string StatusName,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CreateInternalCustomerRequest(
    string Code,
    string Name,
    string? ContactPerson = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    string? Source = null,
    string Status = "ACTIVE",
    string? Notes = null);

public sealed record UpdateInternalCustomerRequest(
    string Code,
    string Name,
    string? ContactPerson = null,
    string? Email = null,
    string? Phone = null,
    string? Address = null,
    string? Source = null,
    string Status = "ACTIVE",
    string? Notes = null);

public sealed record InternalResourceDto(
    Guid Id,
    Guid CompanyId,
    Guid BusinessUnitId,
    string BusinessSegment,
    string BusinessSegmentName,
    string Code,
    string Title,
    string ResourceType,
    string ResourceTypeName,
    string StorageUri,
    string? FileName,
    string? ContentType,
    long? FileSizeBytes,
    string? ChecksumSha256,
    string Version,
    string Status,
    string StatusName,
    IReadOnlyCollection<string> Tags,
    string? MetadataJson,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record CreateInternalResourceRequest(
    string Code,
    string Title,
    string ResourceType,
    string StorageUri,
    string? FileName = null,
    string? ContentType = null,
    long? FileSizeBytes = null,
    string? ChecksumSha256 = null,
    string Version = "1.0",
    string Status = "DRAFT",
    IReadOnlyCollection<string>? Tags = null,
    string? MetadataJson = null,
    string? Notes = null);

public sealed record UpdateInternalResourceRequest(
    string Code,
    string Title,
    string ResourceType,
    string StorageUri,
    string? FileName = null,
    string? ContentType = null,
    long? FileSizeBytes = null,
    string? ChecksumSha256 = null,
    string Version = "1.0",
    string Status = "DRAFT",
    IReadOnlyCollection<string>? Tags = null,
    string? MetadataJson = null,
    string? Notes = null);

public static class InternalDataContract
{
    public static bool TryParseSegment(string? value, out BusinessSegment segment)
    {
        var normalized = Normalize(value);
        if (normalized is "cong-nghe-giao-duc" or "technology-education" or "edtech")
        {
            segment = BusinessSegment.TECHNOLOGY_EDUCATION;
            return true;
        }

        if (normalized is "thoi-trang" or "fashion")
        {
            segment = BusinessSegment.FASHION;
            return true;
        }

        segment = default;
        return false;
    }

    public static string SegmentName(BusinessSegment segment) => segment switch
    {
        BusinessSegment.TECHNOLOGY_EDUCATION => "Công nghệ - Giáo dục",
        BusinessSegment.FASHION => "Thời trang",
        _ => segment.ToString()
    };

    public static string CustomerStatusName(InternalCustomerStatus status) => status switch
    {
        InternalCustomerStatus.LEAD => "Tiềm năng",
        InternalCustomerStatus.ACTIVE => "Đang hoạt động",
        InternalCustomerStatus.INACTIVE => "Ngừng hoạt động",
        InternalCustomerStatus.ARCHIVED => "Đã lưu trữ",
        _ => status.ToString()
    };

    public static string ResourceTypeName(InternalResourceType type) => type switch
    {
        InternalResourceType.EXAM => "Đề thi/Đề bài",
        InternalResourceType.DOCUMENT => "Tài liệu",
        InternalResourceType.PLAN => "Bản kế hoạch",
        InternalResourceType.DESIGN_SAMPLE => "Mẫu thiết kế",
        _ => type.ToString()
    };

    public static string ResourceStatusName(InternalResourceStatus status) => status switch
    {
        InternalResourceStatus.DRAFT => "Bản nháp",
        InternalResourceStatus.ACTIVE => "Đang sử dụng",
        InternalResourceStatus.ARCHIVED => "Đã lưu trữ",
        _ => status.ToString()
    };

    private static string Normalize(string? value) => (value ?? string.Empty)
        .Trim()
        .ToLowerInvariant()
        .Replace('_', '-')
        .Replace(' ', '-');
}
