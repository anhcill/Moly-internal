using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record InternalCustomerModel(
        string Code,
        string Name,
        string? ContactPerson,
        string? Email,
        string? Phone,
        string? Address,
        string? Source,
        string Status,
        string? Notes);

    public sealed record InternalCustomerItem(
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

    public sealed record InternalResourceModel(
        string Code,
        string Title,
        string ResourceType,
        string StorageUri,
        string? FileName,
        string? ContentType,
        long? FileSizeBytes,
        string? ChecksumSha256,
        string Version,
        string Status,
        IReadOnlyCollection<string>? Tags,
        string? MetadataJson,
        string? Notes);

    public sealed record InternalResourceItem(
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
        DateTime? UpdatedAt)
    {
        public string TagsDisplay => string.Join(", ", Tags ?? Array.Empty<string>());
    }

}
