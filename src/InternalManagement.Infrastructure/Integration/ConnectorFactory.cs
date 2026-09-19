using InternalManagement.Application.Features.Integration.Interfaces;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Resolve connector theo tên hệ thống nguồn từ danh sách đã đăng ký qua DI.
/// </summary>
public sealed class ConnectorFactory : IConnectorFactory
{
    private readonly Dictionary<string, IExternalConnector> _connectors;

    public ConnectorFactory(IEnumerable<IExternalConnector> connectors)
    {
        _connectors = connectors.ToDictionary(
            c => c.SourceSystem,
            c => c,
            StringComparer.OrdinalIgnoreCase);
    }

    public IExternalConnector GetConnector(string sourceSystem)
    {
        if (_connectors.TryGetValue(sourceSystem, out var connector))
            return connector;

        throw new InvalidOperationException(
            $"Không tìm thấy connector cho hệ thống nguồn '{sourceSystem}'. " +
            $"Các connector khả dụng: {string.Join(", ", _connectors.Keys)}");
    }

    public IReadOnlyList<IExternalConnector> GetAllConnectors()
        => _connectors.Values.ToList().AsReadOnly();

    public bool HasConnector(string sourceSystem)
        => _connectors.ContainsKey(sourceSystem);
}
