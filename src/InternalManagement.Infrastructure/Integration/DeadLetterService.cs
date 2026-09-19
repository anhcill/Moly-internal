using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Domain.Entities.Integration;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Quản lý dead-letter queue: thêm bản ghi lỗi, retry, liệt kê phân trang.
/// </summary>
public sealed class DeadLetterService : IDeadLetterService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<DeadLetterService> _logger;

    public DeadLetterService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<DeadLetterService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task AddAsync(
        string sourceSystem, string entityType, string sourceId,
        string payloadJson, string errorCode, string errorMessage, CancellationToken ct)
    {
        var deadLetter = new IntegrationDeadLetter
        {
            SourceSystem = sourceSystem,
            EntityType = entityType,
            SourceId = sourceId,
            PayloadJson = payloadJson,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            RetryCount = 0,
            Resolved = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.IntegrationDeadLetters.Add(deadLetter);
        await _db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Dead letter tạo mới: {SourceSystem}/{EntityType}/{SourceId} — {ErrorCode}: {ErrorMessage}",
            sourceSystem, entityType, sourceId, errorCode, errorMessage);
    }

    public async Task<bool> RetryAsync(Guid deadLetterId, CancellationToken ct)
    {
        var deadLetter = await _db.IntegrationDeadLetters
            .FirstOrDefaultAsync(d => d.Id == deadLetterId, ct);

        if (deadLetter == null)
        {
            _logger.LogWarning("Dead letter không tồn tại: {DeadLetterId}", deadLetterId);
            return false;
        }

        if (deadLetter.Resolved)
        {
            _logger.LogInformation("Dead letter đã resolved: {DeadLetterId}", deadLetterId);
            return true;
        }

        deadLetter.RetryCount++;

        // TODO (Ngày 8+): Thực tế sẽ gọi entity mapper để xử lý lại payload.
        // Hiện tại stub: đánh dấu resolved khi retry.
        deadLetter.Resolved = true;
        deadLetter.ResolvedAt = DateTime.UtcNow;
        deadLetter.ResolvedBy = _currentUser.Username ?? "system";

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Dead letter retry thành công: {SourceSystem}/{EntityType}/{SourceId}, RetryCount={RetryCount}",
            deadLetter.SourceSystem, deadLetter.EntityType, deadLetter.SourceId, deadLetter.RetryCount);

        return true;
    }

    public async Task<PaginatedResult<DeadLetterDto>> GetListAsync(
        string? sourceSystem, bool? resolved,
        int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.IntegrationDeadLetters.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(sourceSystem))
            query = query.Where(d => d.SourceSystem == sourceSystem);

        if (resolved.HasValue)
            query = query.Where(d => d.Resolved == resolved.Value);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new DeadLetterDto
            {
                Id = d.Id,
                SourceSystem = d.SourceSystem,
                EntityType = d.EntityType,
                SourceId = d.SourceId,
                ErrorCode = d.ErrorCode,
                ErrorMessage = d.ErrorMessage,
                RetryCount = d.RetryCount,
                Resolved = d.Resolved,
                CreatedAt = d.CreatedAt,
                ResolvedAt = d.ResolvedAt,
                ResolvedBy = d.ResolvedBy
            })
            .ToListAsync(ct);

        return new PaginatedResult<DeadLetterDto>(items, totalCount, pageIndex, pageSize);
    }
}
