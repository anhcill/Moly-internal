using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Application.Features.Integration.Interfaces;

/// <summary>
/// Điều phối pull-based sync: tạo IntegrationRun, gọi connector, ghi nhận kết quả, cập nhật cursor.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Thực thi một lần pull sync cho sourceSystem + entityType.
    /// Nếu forceFullSync = true, reset cursor về đầu.
    /// </summary>
    Task<SyncRunResult> ExecutePullAsync(string sourceSystem, string entityType, bool forceFullSync, CancellationToken ct);

    /// <summary>Lấy danh sách IntegrationRun phân trang, có thể filter.</summary>
    Task<PaginatedResult<SyncRunDto>> GetRunsAsync(
        string? sourceSystem, string? entityType, string? status,
        int pageIndex, int pageSize, CancellationToken ct);
}

/// <summary>
/// Xử lý webhook: nhận event, xác thực HMAC, kiểm tra idempotency, lưu inbox.
/// </summary>
public interface IWebhookProcessor
{
    /// <summary>
    /// Nhận một webhook event. Trả về (Accepted, Message).
    /// Nếu eventId đã tồn tại → trả (true, "Already received") mà không tạo bản ghi mới (idempotent).
    /// </summary>
    Task<(bool Accepted, string Message)> IngestAsync(
        string sourceSystem, string eventId, string eventType,
        string payloadJson, string? signature, CancellationToken ct);

    /// <summary>Xử lý batch các sự kiện đang chờ trong inbox (Status = Pending).</summary>
    Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct);
}

/// <summary>
/// Quản lý dead-letter queue: thêm, retry, liệt kê.
/// </summary>
public interface IDeadLetterService
{
    /// <summary>Thêm bản ghi lỗi vào dead-letter queue.</summary>
    Task AddAsync(string sourceSystem, string entityType, string sourceId,
        string payloadJson, string errorCode, string errorMessage, CancellationToken ct);

    /// <summary>Retry một bản ghi dead-letter. Trả về true nếu retry thành công (mark resolved).</summary>
    Task<bool> RetryAsync(Guid deadLetterId, CancellationToken ct);

    /// <summary>Lấy danh sách dead letters phân trang, filter theo sourceSystem và resolved.</summary>
    Task<PaginatedResult<DeadLetterDto>> GetListAsync(
        string? sourceSystem, bool? resolved,
        int pageIndex, int pageSize, CancellationToken ct);
}
