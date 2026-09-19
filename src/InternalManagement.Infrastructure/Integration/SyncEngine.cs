using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.Integration.DTOs;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Domain.Entities.Integration;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Điều phối pull-based sync: tạo IntegrationRun → gọi connector → loop pages → entity mapping → ghi nhận kết quả → cập nhật cursor.
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    private readonly IApplicationDbContext _db;
    private readonly IConnectorFactory _connectorFactory;
    private readonly IEntityMapperFactory _mapperFactory;
    private readonly IDeadLetterService _deadLetterService;
    private readonly ILogger<SyncEngine> _logger;

    public SyncEngine(
        IApplicationDbContext db,
        IConnectorFactory connectorFactory,
        IEntityMapperFactory mapperFactory,
        IDeadLetterService deadLetterService,
        ILogger<SyncEngine> logger)
    {
        _db = db;
        _connectorFactory = connectorFactory;
        _mapperFactory = mapperFactory;
        _deadLetterService = deadLetterService;
        _logger = logger;
    }

    public async Task<SyncRunResult> ExecutePullAsync(
        string sourceSystem, string entityType, bool forceFullSync, CancellationToken ct)
    {
        var connector = _connectorFactory.GetConnector(sourceSystem);

        // Tạo IntegrationRun mới
        var run = new IntegrationRun
        {
            SourceSystem = sourceSystem,
            EntityType = entityType,
            StartedAt = DateTime.UtcNow,
            Status = IntegrationStatus.Processing
        };
        _db.IntegrationRuns.Add(run);
        await _db.SaveChangesAsync(ct);

        var result = new SyncRunResult
        {
            RunId = run.Id,
            SourceSystem = sourceSystem,
            EntityType = entityType
        };

        var businessUnitCode = ResolveBusinessUnitCode(sourceSystem);

        // Resolve company & BU context cho entity mapping
        var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
        var companyId = company?.Id ?? Guid.Empty;
        var buId = Guid.Empty;
        if (company != null)
        {
            var bu = await _db.BusinessUnits.FirstOrDefaultAsync(
                b => b.CompanyId == company.Id && b.Code == businessUnitCode, ct);
            buId = bu?.Id ?? Guid.Empty;
        }

        try
        {
            if (!connector.SupportedEntityTypes.Contains(entityType, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Connector {sourceSystem} không hỗ trợ entity type '{entityType}'. " +
                    $"Hỗ trợ: {string.Join(", ", connector.SupportedEntityTypes)}");
            }

            // Đọc cursor từ lần chạy thành công trước đó
            var cursor = forceFullSync
                ? new SyncCursor()
                : await GetLastSuccessfulCursorAsync(sourceSystem, entityType, ct);

            _logger.LogInformation(
                "Bắt đầu pull sync {SourceSystem}/{EntityType}, ForceFullSync={ForceFullSync}, Cursor={Cursor}",
                sourceSystem, entityType, forceFullSync, cursor.Serialize());

            // Resolve entity mapper (nếu có)
            var hasMapper = _mapperFactory.HasMapper(entityType);
            IEntityMapper? mapper = hasMapper ? _mapperFactory.GetMapper(entityType) : null;

            bool hasMore = true;
            while (hasMore)
            {
                ct.ThrowIfCancellationRequested();

                var page = await connector.PullAsync(entityType, cursor, ct);

                result.RecordsRead += page.Items.Count;

                if (mapper != null)
                {
                    // Entity mapping pipeline: validate + idempotent upsert
                    foreach (var item in page.Items)
                    {
                        try
                        {
                            var mapResult = await mapper.MapAndUpsertAsync(
                                item, sourceSystem, companyId, buId, ct);

                            switch (mapResult.Status)
                            {
                                case MapResultStatus.Written:
                                    result.RecordsWritten++;
                                    break;
                                case MapResultStatus.Skipped:
                                    result.RecordsSkipped++;
                                    break;
                                case MapResultStatus.Failed:
                                    result.RecordsFailed++;
                                    await _deadLetterService.AddAsync(
                                        sourceSystem, entityType,
                                        mapResult.SourceId ?? "unknown",
                                        item.GetRawText(),
                                        mapResult.ErrorCode ?? "MAPPING_ERROR",
                                        mapResult.ErrorMessage ?? "Entity mapping failed.",
                                        ct);
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            result.RecordsFailed++;
                            await _deadLetterService.AddAsync(
                                sourceSystem, entityType, "unknown",
                                item.GetRawText(), "EXCEPTION", ex.Message, ct);

                            _logger.LogError(ex, "Entity mapping exception for {EntityType}", entityType);
                        }
                    }
                }
                else
                {
                    // Không có mapper nghĩa là chưa có đường ghi dữ liệu an toàn.
                    // Không được báo cáo synthetic "written" khi thực tế không upsert gì.
                    result.RecordsSkipped += page.Items.Count;
                    _logger.LogWarning(
                        "Bỏ qua {Count} bản ghi {EntityType} từ {SourceSystem} vì chưa có mapper.",
                        page.Items.Count, entityType, sourceSystem);
                }

                cursor = page.NextCursor;
                hasMore = page.HasMore;
            }

            // Cập nhật run thành công
            run.Status = IntegrationStatus.Success;
            run.RecordsRead = result.RecordsRead;
            run.RecordsWritten = result.RecordsWritten;
            run.RecordsSkipped = result.RecordsSkipped;
            run.RecordsFailed = result.RecordsFailed;
            run.Cursor = cursor.Serialize();
            run.CompletedAt = DateTime.UtcNow;

            result.NextCursor = cursor;
            result.Succeeded = true;

            _logger.LogInformation(
                "Pull sync hoàn thành {SourceSystem}/{EntityType}: Read={Read}, Written={Written}, Skipped={Skipped}, Failed={Failed}",
                sourceSystem, entityType, result.RecordsRead, result.RecordsWritten, result.RecordsSkipped, result.RecordsFailed);
        }
        catch (OperationCanceledException)
        {
            run.Status = IntegrationStatus.Failed;
            run.ErrorMessage = "Sync bị hủy.";
            run.CompletedAt = DateTime.UtcNow;
            result.ErrorMessage = run.ErrorMessage;

            _logger.LogWarning("Pull sync bị hủy {SourceSystem}/{EntityType}", sourceSystem, entityType);
        }
        catch (Exception ex)
        {
            run.Status = IntegrationStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTime.UtcNow;
            run.RecordsRead = result.RecordsRead;
            run.RecordsWritten = result.RecordsWritten;
            run.RecordsSkipped = result.RecordsSkipped;
            run.RecordsFailed = result.RecordsFailed;
            result.ErrorMessage = ex.Message;

            _logger.LogError(ex, "Pull sync thất bại {SourceSystem}/{EntityType}", sourceSystem, entityType);
        }

        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<PaginatedResult<SyncRunDto>> GetRunsAsync(
        string? sourceSystem, string? entityType, string? status,
        int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.IntegrationRuns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(sourceSystem))
            query = query.Where(r => r.SourceSystem == sourceSystem);

        if (!string.IsNullOrWhiteSpace(entityType))
            query = query.Where(r => r.EntityType == entityType);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<IntegrationStatus>(status, true, out var statusEnum))
            query = query.Where(r => r.Status == statusEnum);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new SyncRunDto
            {
                Id = r.Id,
                SourceSystem = r.SourceSystem,
                EntityType = r.EntityType,
                StartedAt = r.StartedAt,
                CompletedAt = r.CompletedAt,
                Status = r.Status.ToString(),
                RecordsRead = r.RecordsRead,
                RecordsWritten = r.RecordsWritten,
                RecordsSkipped = r.RecordsSkipped,
                RecordsFailed = r.RecordsFailed,
                Cursor = r.Cursor,
                ErrorMessage = r.ErrorMessage
            })
            .ToListAsync(ct);

        return new PaginatedResult<SyncRunDto>(items, totalCount, pageIndex, pageSize);
    }

    private async Task<SyncCursor> GetLastSuccessfulCursorAsync(
        string sourceSystem, string entityType, CancellationToken ct)
    {
        var lastCursor = await _db.IntegrationRuns
            .AsNoTracking()
            .Where(r => r.SourceSystem == sourceSystem
                        && r.EntityType == entityType
                        && r.Status == IntegrationStatus.Success)
            .OrderByDescending(r => r.CompletedAt)
            .Select(r => r.Cursor)
            .FirstOrDefaultAsync(ct);

        return SyncCursor.Deserialize(lastCursor);
    }

    private static string ResolveBusinessUnitCode(string sourceSystem)
        => sourceSystem.ToUpperInvariant() switch
        {
            "WEBSITE_INTERVIEW" or "CSCA_INTERVIEW" => "INTERVIEW",
            "CSCA_MOLI_STUDIO" or "CSCA" => "CSCA",
            _ => "EDTECH"
        };
}
