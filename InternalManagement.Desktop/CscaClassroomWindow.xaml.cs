using System.Globalization;
using System.Windows;
using System.Windows.Input;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class CscaClassroomWindow : Window
{
    private readonly ApiClient _apiClient;

    public CscaClassroomWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadClassroomsAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadClassroomsAsync();

    private async Task LoadClassroomsAsync()
    {
        SetBusy(true, "Đang tải danh mục phòng học...");
        StatusText.Text = "Đang tải danh mục phòng học...";
        try
        {
            var rooms = await _apiClient.GetCscaClassroomsAsync(includeInactive: true);
            if (rooms is null)
            {
                StatusText.Text = "Không tải được danh mục phòng học.";
                return;
            }

            ClassroomsDataGrid.ItemsSource = rooms.Select(ClassroomRow.From).ToList();
            StatusText.Text = $"{rooms.Count} phòng học";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Không tải được dữ liệu: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void AddClassroom_Click(object sender, RoutedEventArgs e)
    {
        var nextCode = await _apiClient.GetNextCscaClassroomCodeAsync() ?? ComputeNextClassroomCode();
        if (!PromptDialog.TryShow(this, "Thêm phòng học", ClassroomFields(null, nextCode), out var values)) return;
        if (!TryCapacity(values["capacity"], out var capacity)) return;

        var code = string.IsNullOrWhiteSpace(values["code"]) ? nextCode : values["code"].Trim();

        SetBusy(true, "Đang tạo phòng học mới...");
        try
        {
            if (!await _apiClient.CreateCscaClassroomAsync(code, values["name"], capacity, Null(values["location"]), values["isActive"] == "true"))
            {
                ShowInvalid("Không thể tạo phòng. Kiểm tra lại mã phòng hoặc quyền quản lý lớp.");
                return;
            }

            await LoadClassroomsAsync();
            StatusText.Text = "Đã tạo phòng học.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void EditClassroom_Click(object sender, RoutedEventArgs e)
    {
        if (ClassroomsDataGrid.SelectedItem is not ClassroomRow classroom)
        {
            ShowInvalid("Hãy chọn phòng học cần sửa.");
            return;
        }

        if (!PromptDialog.TryShow(this, $"Sửa phòng {classroom.Code}", ClassroomFields(classroom), out var values)) return;
        if (!TryCapacity(values["capacity"], out var capacity)) return;

        SetBusy(true, $"Đang cập nhật phòng {classroom.Code}...");
        try
        {
            if (!await _apiClient.UpdateCscaClassroomAsync(classroom.Id, values["name"], capacity, Null(values["location"]), values["isActive"] == "true"))
            {
                ShowInvalid("Không thể cập nhật phòng học.");
                return;
            }

            await LoadClassroomsAsync();
            StatusText.Text = "Đã cập nhật phòng học.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void RemoveClassroom_Click(object sender, RoutedEventArgs e)
    {
        if (ClassroomsDataGrid.SelectedItem is not ClassroomRow classroom)
        {
            ShowInvalid("Hãy chọn phòng học cần xóa.");
            return;
        }
        if (MessageBox.Show(this, $"Xóa phòng '{classroom.Code} — {classroom.Name}'?", "Xác nhận", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        SetBusy(true, $"Đang xóa phòng {classroom.Code}...");
        try
        {
            if (!await _apiClient.RemoveCscaClassroomAsync(classroom.Id))
            {
                ShowInvalid("Không thể xóa phòng đang được dùng. Hãy sửa và chuyển trạng thái thành Ngừng dùng.");
                return;
            }

            await LoadClassroomsAsync();
            StatusText.Text = "Đã xóa phòng học.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetBusy(isBusy, message));
            return;
        }

        if (ClassroomBusyOverlay != null)
        {
            ClassroomBusyOverlay.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
            if (ClassroomBusyText != null)
            {
                ClassroomBusyText.Text = string.IsNullOrWhiteSpace(message) ? "Đang xử lý dữ liệu..." : message;
            }
        }
        Mouse.OverrideCursor = isBusy ? Cursors.Wait : null;
    }

    private void ClassroomsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ClassroomsDataGrid.SelectedItem is ClassroomRow)
            EditClassroom_Click(sender, e);
    }

    private IReadOnlyList<PromptField> ClassroomFields(ClassroomRow? classroom = null, string? defaultCode = null) => classroom is null
        ? new[]
        {
            new PromptField("code", "Mã phòng (tự động tăng, có thể sửa)", defaultCode ?? ComputeNextClassroomCode()),
            new PromptField("name", "Tên phòng"),
            new PromptField("capacity", "Sức chứa", IsRequired: false),
            new PromptField("location", "Vị trí / tầng", IsRequired: false),
            new PromptField("isActive", "Trạng thái", "true", Options: StatusOptions())
        }
        : new[]
        {
            new PromptField("name", "Tên phòng", classroom.Name),
            new PromptField("capacity", "Sức chứa", classroom.Capacity?.ToString(CultureInfo.InvariantCulture), IsRequired: false),
            new PromptField("location", "Vị trí / tầng", classroom.Location, IsRequired: false),
            new PromptField("isActive", "Trạng thái", classroom.IsActive ? "true" : "false", Options: StatusOptions())
        };

    private string ComputeNextClassroomCode()
    {
        var maxSeq = 0;
        var rooms = ClassroomsDataGrid.ItemsSource as IEnumerable<ClassroomRow> ?? Array.Empty<ClassroomRow>();
        foreach (var r in rooms)
        {
            var code = r.Code?.Trim() ?? string.Empty;
            if (code.StartsWith("P-", StringComparison.OrdinalIgnoreCase))
            {
                var digits = new string(code.Substring(2).Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var seq) && seq > maxSeq)
                    maxSeq = seq;
            }
            else
            {
                var digits = new string(code.Where(char.IsDigit).ToArray());
                if (int.TryParse(digits, out var seq) && seq > maxSeq)
                    maxSeq = seq;
            }
        }
        return $"P-{(maxSeq + 1):D2}";
    }

    private static IReadOnlyList<PromptOption> StatusOptions() => new[]
    {
        new PromptOption("true", "Đang hoạt động"),
        new PromptOption("false", "Ngừng dùng")
    };

    private static bool TryCapacity(string value, out int? capacity)
    {
        capacity = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            capacity = parsed;
            return true;
        }

        MessageBox.Show("Sức chứa phải là số nguyên lớn hơn 0.", "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ShowInvalid(string message) => MessageBox.Show(this, message, "Dữ liệu không hợp lệ", MessageBoxButton.OK, MessageBoxImage.Warning);

    private sealed class ClassroomRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int? Capacity { get; init; }
        public string CapacityLabel => Capacity?.ToString(CultureInfo.CurrentCulture) ?? "Chưa thiết lập";
        public string? Location { get; init; }
        public bool IsActive { get; init; }
        public string StatusLabel => IsActive ? "Đang hoạt động" : "Ngừng dùng";

        public static ClassroomRow From(ApiClient.CscaClassroomItem item) => new()
        {
            Id = item.Id,
            Code = item.Code,
            Name = item.Name,
            Capacity = item.Capacity,
            Location = item.Location,
            IsActive = item.IsActive
        };
    }
}
