using InternalManagement.Application.Features.CscaInterview.DTOs;

namespace InternalManagement.Application.Features.CscaInterview.Services;

public interface ICscaOnlineService
{
    Task<IReadOnlyList<CscaOnlineMaterialDto>> GetMaterialsAsync(string? search, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<CscaOnlineVocabularyDto>> GetVocabularyAsync(string? search, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<CscaOnlinePostDto>> GetPostsAsync(string? search, int pageSize, CancellationToken ct);
}
