using System.Text.Json;

namespace InternalManagement.Application.Features.Integration.Interfaces;

/// <summary>
/// Kết quả mapping một bản ghi từ JSON sang domain entity.
/// </summary>
public enum MapResultStatus
{
    /// <summary>Bản ghi mới được tạo hoặc cập nhật.</summary>
    Written,
    /// <summary>Bản ghi đã tồn tại và không thay đổi — bỏ qua.</summary>
    Skipped,
    /// <summary>Bản ghi lỗi validation hoặc mapping — chuyển dead-letter.</summary>
    Failed
}

public sealed record EntityMapResult(
    MapResultStatus Status,
    string? SourceId = null,
    string? ErrorCode = null,
    string? ErrorMessage = null);

/// <summary>
/// Mapper chuyển đổi raw JSON từ connector thành domain entity,
/// thực hiện validation và idempotent upsert.
/// </summary>
public interface IEntityMapper
{
    /// <summary>Entity type mà mapper xử lý (e.g. "Courses", "Customers").</summary>
    string EntityType { get; }

    /// <summary>
    /// Map một JSON element thành domain entity, validate, và upsert vào DB.
    /// Trả về Written/Skipped/Failed.
    /// </summary>
    Task<EntityMapResult> MapAndUpsertAsync(
        JsonElement item,
        string sourceSystem,
        Guid companyId,
        Guid businessUnitId,
        CancellationToken ct);
}

/// <summary>
/// Factory resolve mapper theo entity type.
/// </summary>
public interface IEntityMapperFactory
{
    IEntityMapper GetMapper(string entityType);
    bool HasMapper(string entityType);
}
