using System.Text.Json;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Application.Features.Integration.Interfaces;

/// <summary>
/// Abstraction cho connector kết nối đến một nguồn dữ liệu bên ngoài.
/// Mỗi nguồn (Website EdTech, CSCA, Interview, Shopee, TikTok) implement một connector riêng.
/// </summary>
public interface IExternalConnector
{
    /// <summary>Mã định danh hệ thống nguồn (e.g. "WEBSITE_EDTECH", "SHOPEE").</summary>
    string SourceSystem { get; }

    /// <summary>Danh sách entity types mà connector hỗ trợ pull (e.g. "Courses", "Questions").</summary>
    IReadOnlyList<string> SupportedEntityTypes { get; }

    /// <summary>
    /// Pull một trang dữ liệu từ nguồn.
    /// Trả về raw JsonElement — việc mapping sang domain entity nằm ở entity mapper layer.
    /// </summary>
    Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct);

    /// <summary>Kiểm tra sức khỏe kết nối đến nguồn.</summary>
    Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct);
}

/// <summary>
/// Factory resolve connector theo tên hệ thống nguồn.
/// </summary>
public interface IConnectorFactory
{
    /// <summary>Lấy connector cho hệ thống nguồn cụ thể. Throw nếu không tìm thấy.</summary>
    IExternalConnector GetConnector(string sourceSystem);

    /// <summary>Lấy tất cả connector đã đăng ký.</summary>
    IReadOnlyList<IExternalConnector> GetAllConnectors();

    /// <summary>Kiểm tra connector có tồn tại không.</summary>
    bool HasConnector(string sourceSystem);
}
