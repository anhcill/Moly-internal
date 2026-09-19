using System.Text.Json;

namespace InternalManagement.Application.Features.Integration.Models;

/// <summary>
/// Cursor cho việc pull dữ liệu gia tăng từ nguồn bên ngoài.
/// Lưu trữ dưới dạng JSON string trong IntegrationRun.Cursor.
/// </summary>
public sealed class SyncCursor
{
    /// <summary>Lấy dữ liệu cập nhật từ thời điểm này.</summary>
    public DateTime? UpdatedSince { get; set; }

    /// <summary>Offset phân trang (dùng cho API hỗ trợ offset pagination).</summary>
    public int Offset { get; set; }

    /// <summary>Token phân trang (dùng cho API hỗ trợ cursor-based pagination).</summary>
    public string? ContinuationToken { get; set; }

    /// <summary>Serialize cursor thành JSON string để lưu vào DB.</summary>
    public string Serialize() => JsonSerializer.Serialize(this);

    /// <summary>Deserialize cursor từ JSON string đọc từ DB.</summary>
    public static SyncCursor Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new SyncCursor();

        return JsonSerializer.Deserialize<SyncCursor>(json) ?? new SyncCursor();
    }
}

/// <summary>
/// Kết quả một trang dữ liệu pull từ connector.
/// Items là raw JsonElement — mapping sang domain entity thực hiện ở lớp riêng (Ngày 8+).
/// </summary>
public sealed class SyncPage
{
    public IReadOnlyList<JsonElement> Items { get; init; } = [];
    public SyncCursor NextCursor { get; init; } = new();
    public bool HasMore { get; init; }
    public int? TotalAvailable { get; init; }

    public static SyncPage Empty() => new()
    {
        Items = [],
        NextCursor = new SyncCursor(),
        HasMore = false,
        TotalAvailable = 0
    };
}

/// <summary>Trạng thái sức khỏe của một connector.</summary>
public sealed class ConnectorHealth
{
    public required string SourceSystem { get; init; }
    public bool IsHealthy { get; init; }
    public string? Message { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>Kết quả tổng hợp sau một lần chạy sync (pull hoặc process inbox).</summary>
public sealed class SyncRunResult
{
    public Guid RunId { get; init; }
    public string SourceSystem { get; init; } = string.Empty;
    public string EntityType { get; init; } = string.Empty;
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsSkipped { get; set; }
    public int RecordsFailed { get; set; }
    public SyncCursor? NextCursor { get; set; }
    public string? ErrorMessage { get; set; }
    public bool Succeeded { get; set; }
}
