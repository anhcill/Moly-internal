using System.Windows;
using System.Windows.Media;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private string ActiveSegmentName => _activeSegment == FashionSegment ? "Thời trang" : "Công nghệ - Giáo dục";

    private void SetActiveArea(string? segment)
    {
        if (!string.IsNullOrWhiteSpace(segment)) _activeSegment = segment;
        var isCompany = string.IsNullOrWhiteSpace(segment);
        var label = isCompany ? "TỔNG HỢP TOÀN CÔNG TY" : ActiveSegmentName.ToUpperInvariant();
        ActiveSegmentText.Text = label;
        ActiveSegmentBadgeText.Text = isCompany ? "TOÀN CÔNG TY" : label;
        ActiveSegmentBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            isCompany ? "#EEF2FF" : _activeSegment == FashionSegment ? "#FDF2F8" : "#ECFEFF"));
        ActiveSegmentBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            isCompany ? "#4F46E5" : _activeSegment == FashionSegment ? "#BE185D" : "#0F766E"));
    }

    private void ShowInternalDataView(InternalDataViewMode mode)
    {
        _internalDataMode = mode;
        ViewInternalDataContainer.Visibility = Visibility.Visible;
        var isCustomers = mode == InternalDataViewMode.Customers;
        var title = isCustomers
            ? $"Danh mục khách hàng · {ActiveSegmentName}"
            : _activeSegment == FashionSegment
                ? "Kế hoạch & mẫu thiết kế · Thời trang"
                : "Kho đề & tài liệu · Công nghệ - Giáo dục";
        var help = $"Dữ liệu thuộc riêng mảng {ActiveSegmentName}; hệ thống không trộn với mảng còn lại.";
        var canManage = HasPermission(_currentUser, isCustomers ? "Permissions.InternalCustomers.Manage" : "Permissions.InternalResources.Manage");
        ViewInternalDataContainer.Configure(isCustomers, title, help, canManage);
        ViewHeaderTitle.Text = isCustomers ? "Danh mục khách hàng" : (_activeSegment == FashionSegment ? "Kế hoạch & mẫu thiết kế" : "Kho đề & tài liệu");
        ViewHeaderSubtitle.Text = help;
        _ = LoadViewOnceAsync(
            $"internal:{_activeSegment}:{_internalDataMode}",
            isCustomers ? "Đang tải khách hàng..." : "Đang tải kho tài liệu...",
            LoadInternalDataAsync);
    }

    private void GoToCourses_Click(object sender, RoutedEventArgs e)
    {
        SegmentTechnologyButton.IsChecked = true;
        NavCourses.IsChecked = true;
    }

    private void GoToQuestions_Click(object sender, RoutedEventArgs e)
    {
        SegmentTechnologyButton.IsChecked = true;
        NavQuestions.IsChecked = true;
    }
}
