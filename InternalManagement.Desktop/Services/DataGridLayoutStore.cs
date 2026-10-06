using System.IO;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

/// <summary>
/// Lưu các tùy chỉnh bố cục bảng theo người dùng Windows hiện tại.
/// </summary>
public sealed class DataGridLayoutStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _syncRoot = new();
    private readonly string _filePath;
    private Dictionary<string, Dictionary<string, double>>? _layouts;

    public DataGridLayoutStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MOLY",
        "InternalManagement",
        "table-layouts.json");

    public double? GetColumnWidth(string tableKey, string columnKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);

        lock (_syncRoot)
        {
            var layouts = EnsureLoaded();
            if (!layouts.TryGetValue(tableKey, out var columns) ||
                !columns.TryGetValue(columnKey, out var width) ||
                !IsValidWidth(width))
            {
                return null;
            }

            return width;
        }
    }

    public void SaveColumnWidth(string tableKey, string columnKey, double width)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(columnKey);

        if (!IsValidWidth(width))
        {
            return;
        }

        lock (_syncRoot)
        {
            var layouts = EnsureLoaded();
            if (!layouts.TryGetValue(tableKey, out var columns))
            {
                columns = new Dictionary<string, double>(StringComparer.Ordinal);
                layouts[tableKey] = columns;
            }

            columns[columnKey] = Math.Round(width, 2, MidpointRounding.AwayFromZero);
            TrySave(layouts);
        }
    }

    private Dictionary<string, Dictionary<string, double>> EnsureLoaded()
    {
        if (_layouts is not null)
        {
            return _layouts;
        }

        _layouts = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(_filePath))
            {
                return _layouts;
            }

            using var stream = File.OpenRead(_filePath);
            var savedLayouts = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, double>>>(
                stream,
                JsonOptions);

            if (savedLayouts is not null)
            {
                _layouts = new Dictionary<string, Dictionary<string, double>>(savedLayouts, StringComparer.Ordinal);
            }
        }
        catch (JsonException)
        {
            // File cấu hình hỏng không được phép ngăn ứng dụng khởi động.
        }
        catch (IOException)
        {
            // Có thể file đang được một tiến trình khác cập nhật; dùng bố cục mặc định.
        }
        catch (UnauthorizedAccessException)
        {
            // Không có quyền ghi cấu hình thì bảng vẫn hoạt động với bố cục mặc định.
        }

        return _layouts;
    }

    private void TrySave(Dictionary<string, Dictionary<string, double>> layouts)
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = $"{_filePath}.{Environment.ProcessId}.tmp";
            using (var stream = File.Create(temporaryPath))
            {
                JsonSerializer.Serialize(stream, layouts, JsonOptions);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        catch (IOException)
        {
            // Lưu bố cục là tiện ích phụ, không làm gián đoạn thao tác chính.
        }
        catch (UnauthorizedAccessException)
        {
            // Lưu bố cục là tiện ích phụ, không làm gián đoạn thao tác chính.
        }
    }

    private static bool IsValidWidth(double width) =>
        double.IsFinite(width) && width is >= 24 and <= 10_000;
}
