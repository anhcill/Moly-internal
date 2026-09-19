using InternalManagement.Application.Common.Models;

namespace InternalManagement.Application.Common.Interfaces;

public sealed record StoredFileResult(
    string SecureUrl,
    string PublicId,
    string ResourceType,
    string? Format,
    long Bytes);

public interface IFileStorageService
{
    bool IsConfigured { get; }

    Task<Result<StoredFileResult>> UploadAsync(
        Stream content,
        string fileName,
        string? contentType,
        long length,
        string folder,
        CancellationToken ct);
}
