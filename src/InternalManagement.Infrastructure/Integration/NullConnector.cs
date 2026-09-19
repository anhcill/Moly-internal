using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Connector placeholder cho dev/test. Trả về dữ liệu rỗng, luôn healthy.
/// Sẽ được thay bằng connector thật ở Ngày 7-8.
/// </summary>
public sealed class NullConnector : IExternalConnector
{
    public string SourceSystem => "NULL_CONNECTOR";

    public IReadOnlyList<string> SupportedEntityTypes { get; } = ["Test"];

    public Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct)
    {
        return Task.FromResult(SyncPage.Empty());
    }

    public Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct)
    {
        return Task.FromResult(new ConnectorHealth
        {
            SourceSystem = SourceSystem,
            IsHealthy = true,
            Message = "Null connector — always healthy",
            CheckedAt = DateTime.UtcNow
        });
    }
}
