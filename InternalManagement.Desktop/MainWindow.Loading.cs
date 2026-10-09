using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    // ── Data Loading Methods ──

    private async Task LoadDashboardMetricsAsync()
    {
        await _dashboardViewModel.RefreshAsync();
        var isHealthy = _dashboardViewModel.IsApiHealthy;
        ApiHealthBorder.Background = isHealthy ? new SolidColorBrush(Color.FromRgb(236, 253, 245)) : new SolidColorBrush(Color.FromRgb(254, 242, 242));
        ApiHealthBorder.BorderBrush = isHealthy ? new SolidColorBrush(Color.FromRgb(167, 243, 208)) : new SolidColorBrush(Color.FromRgb(254, 202, 202));
        ApiHealthDot.Fill = isHealthy ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
        ApiHealthText.Foreground = isHealthy ? new SolidColorBrush(Color.FromRgb(6, 95, 70)) : new SolidColorBrush(Color.FromRgb(153, 27, 27));
        ApiHealthText.Text = isHealthy ? "API Trực Tuyến" : "API Không Khả Dụng";
    }

    private async Task LoadCoursesAsync(string? search = null)
    {
        var data = await _apiClient.GetCoursesAsync(search);
        if (data is null)
        {
            CoursesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh sách khóa học. Vui lòng thử lại.", isError: true);
            return;
        }

        _cachedCourses = data.Items.ToList();
        CoursesDataGrid.ItemsSource = _cachedCourses;

        // Populate CscaCourseFilterComboBox
        var filterOptions = new List<CourseFilterOption>
        {
            new CourseFilterOption(null, "Tất cả khóa học")
        };
        filterOptions.AddRange(_cachedCourses.Select(c => new CourseFilterOption(c.Id, c.Title)));
        _isCscaFilterSyncing = true;
        try
        {
            CscaCourseFilterComboBox.ItemsSource = filterOptions;
            CscaFinanceCourseFilterComboBox.ItemsSource = filterOptions;
            if (_selectedCourseFilterId.HasValue && filterOptions.Any(f => f.Id == _selectedCourseFilterId.Value))
            {
                CscaCourseFilterComboBox.SelectedValue = _selectedCourseFilterId.Value;
                CscaFinanceCourseFilterComboBox.SelectedValue = _selectedCourseFilterId.Value;
            }
            else
            {
                CscaCourseFilterComboBox.SelectedIndex = 0;
                CscaFinanceCourseFilterComboBox.SelectedIndex = 0;
            }
        }
        finally
        {
            _isCscaFilterSyncing = false;
        }
        ApplyCscaFinanceFilter();

        SetLoadedStatus("Khóa học", data.Items.Count);
    }

    private async Task LoadQuestionsAsync(string? search = null)
    {
        var viewModel = ViewQuestionsContainer.ViewModel!;
        if (!await viewModel.LoadAsync(search))
        {
            SetViewStatus("Không tải được ngân hàng câu hỏi. Vui lòng thử lại.", isError: true);
            return;
        }

        _dashboardViewModel.QuestionsCount = viewModel.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetLoadedStatus("Câu hỏi", viewModel.Items.Count);
    }

    private async Task LoadCscaClassesAsync(string? search = null)
    {
        var data = await _apiClient.GetCscaClassesAsync(search);
        if (data is null)
        {
            CscaClassesDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được danh sách lớp CSCA. Vui lòng thử lại.", isError: true);
            return;
        }

        _allCscaClasses = data.Items.ToList();
        ApplyCscaClassFilter();
        SetLoadedStatus("Lớp CSCA", data.Items.Count);
    }

    private async Task LoadCscaFinanceAsync()
    {
        var data = await _apiClient.GetCscaClassesAsync();
        if (data is null)
        {
            CscaFinanceDataGrid.ItemsSource = Array.Empty<object>();
            SetViewStatus("Không tải được tài chính lớp học. Vui lòng thử lại.", isError: true);
            return;
        }

        _allCscaFinanceClasses = data.Items.ToList();
        ApplyCscaFinanceFilter();
    }

    private void ApplyCscaClassFilter()
    {
        var filtered = _allCscaClasses.AsEnumerable();
        if (_selectedCourseFilterId.HasValue)
        {
            var course = _cachedCourses.FirstOrDefault(c => c.Id == _selectedCourseFilterId.Value);
            var title = course?.Title ?? _selectedCourseFilterTitle;
            if (!string.IsNullOrEmpty(title))
            {
                filtered = filtered.Where(c => string.Equals(c.CourseTitle, title, StringComparison.OrdinalIgnoreCase));
            }
            CscaCourseFilterBanner.Visibility = Visibility.Visible;
            CscaCourseFilterText.Text = $"Đang lọc các lớp thuộc khóa học: {title} ({filtered.Count()} lớp)";
        }
        else
        {
            CscaCourseFilterBanner.Visibility = Visibility.Collapsed;
        }

        var itemsList = filtered.ToList();
        CscaClassesDataGrid.ItemsSource = itemsList;

        MetricCscaClassCount.Text = itemsList.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        MetricCscaStudentCount.Text = itemsList.Sum(c => c.StudentCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private void ApplyCscaFinanceFilter()
    {
        var filtered = _allCscaFinanceClasses.AsEnumerable();
        if (_selectedCourseFilterId.HasValue)
        {
            var course = _cachedCourses.FirstOrDefault(c => c.Id == _selectedCourseFilterId.Value);
            var title = course?.Title ?? _selectedCourseFilterTitle;
            if (!string.IsNullOrEmpty(title))
                filtered = filtered.Where(c => string.Equals(c.CourseTitle, title, StringComparison.OrdinalIgnoreCase));
            CscaFinanceScopeText.Text = $"Khóa học: {title}";
        }
        else
        {
            CscaFinanceScopeText.Text = "Tất cả lớp học";
        }

        var itemsList = filtered.ToList();
        CscaFinanceDataGrid.ItemsSource = itemsList;
        var totalRev = itemsList.Sum(c => c.TotalRevenue);
        var totalDebt = itemsList.Sum(c => c.DebtAmount);
        var totalProfit = itemsList.Sum(c => c.NetProfit);
        MetricCscaRevenue.Text = $"{totalRev:N0} đ";
        MetricCscaDebt.Text = $"{totalDebt:N0} đ";
        MetricCscaNetProfit.Text = $"{totalProfit:N0} đ";
    }

    private async Task LoadCscaOnlineAsync(string? search = null)
    {
        var materialsTask = _apiClient.GetCscaOnlineMaterialsAsync(search);
        var vocabularyTask = _apiClient.GetCscaOnlineVocabularyAsync(search);
        var postsTask = _apiClient.GetCscaOnlinePostsAsync(search);
        await Task.WhenAll(materialsTask, vocabularyTask, postsTask);

        var materials = await materialsTask ?? [];
        var vocabulary = await vocabularyTask ?? [];
        var posts = await postsTask ?? [];

        CscaMaterialsDataGrid.ItemsSource = materials;
        CscaVocabularyDataGrid.ItemsSource = vocabulary;
        CscaPostsDataGrid.ItemsSource = posts;

        if (materialsTask.Result is null || vocabularyTask.Result is null || postsTask.Result is null)
        {
            SetViewStatus("Không tải đủ dữ liệu CSCA web. Kiểm tra API key/token hoặc endpoint cấu hình.", isError: true);
            return;
        }

        SetLoadedStatus("Nội dung CSCA web", materials.Count + vocabulary.Count + posts.Count);
    }

    private async Task LoadInterviewCustomersAsync(string? search = null)
    {
        var viewModel = ViewInterviewContainer.ViewModel!;
        if (!await viewModel.LoadAsync(search))
        {
            SetViewStatus("Không tải được danh sách khách hàng phỏng vấn. Vui lòng thử lại.", isError: true);
            return;
        }
        SetLoadedStatus("Khách hàng phỏng vấn", viewModel.Items.Count);
    }

    private async Task LoadCustomersAsync(string? search = null)
    {
        var viewModel = ViewCustomersContainer.ViewModel!;
        if (!await viewModel.LoadAsync(search))
        {
            SetViewStatus("Không tải được danh sách học viên. Vui lòng thử lại.", isError: true);
            return;
        }

        _dashboardViewModel.CustomersCount = viewModel.TotalCustomerCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetLoadedStatus("Học viên & khách hàng", viewModel.Students.Count + viewModel.Customers.Count);
    }

    private async Task LoadSyncRunsAsync()
    {
        var data = await _apiClient.GetSyncRunsAsync();
        if (data is null)
        {
            ViewSyncContainer.SetRuns(null);
            ViewSyncContainer.SetDeadLetters(null);
            SetViewStatus("Không tải được lịch sử đồng bộ. Vui lòng thử lại.", isError: true);
            return;
        }

        ViewSyncContainer.SetRuns(data.Items);
        await LoadDeadLettersAsync();
        SetLoadedStatus("Đợt đồng bộ", data.Items.Count);
    }

    private async Task LoadDeadLettersAsync()
    {
        var data = await _apiClient.GetDeadLettersAsync(resolved: false);
        if (data is null)
        {
            ViewSyncContainer.SetDeadLetters(null);
            SetViewStatus("Không tải được Dead-Letter Queue. Vui lòng thử lại.", isError: true);
            return;
        }

        ViewSyncContainer.SetDeadLetters(data.Items);
        SetViewStatus(data.Items.Count == 0
            ? "Dead-Letter Queue đang trống — không có bản ghi chờ xử lý"
            : $"Có {data.Items.Count:N0} bản ghi Dead-Letter chờ xử lý");
    }

    private async Task LoadLmsIntegrationAsync()
    {
        var selectedCourseId = (LmsCourseMappingsDataGrid.SelectedItem as ApiClient.LmsCourseMappingItem)?.CourseId;
        var overviewTask = _apiClient.GetLmsIntegrationOverviewAsync();
        var mappingsTask = _apiClient.GetLmsCourseMappingsAsync();
        var outboxTask = _apiClient.GetLmsOutboxAsync();
        await Task.WhenAll(overviewTask, mappingsTask, outboxTask);

        var overview = await overviewTask;
        var mappings = await mappingsTask;
        var outbox = await outboxTask;
        if (overview is null || mappings is null || outbox is null)
        {
            LmsCourseMappingsDataGrid.ItemsSource = Array.Empty<object>();
            LmsOutboxDataGrid.ItemsSource = Array.Empty<object>();
            ResetLmsMetrics();
            SetViewStatus("Không tải được dữ liệu CSCA LMS. Kiểm tra quyền System Sync và kết nối API.", isError: true);
            return;
        }

        LmsReadyMappingsMetricText.Text = overview.ReadyCourseMappings.ToString("N0", CultureInfo.InvariantCulture);
        LmsTotalCoursesMetricText.Text = $"trên {overview.TotalCourses.ToString("N0", CultureInfo.InvariantCulture)} khóa học";
        LmsActiveGrantsMetricText.Text = overview.ActiveGrants.ToString("N0", CultureInfo.InvariantCulture);
        LmsPendingOutboxMetricText.Text = overview.PendingOutbox.ToString("N0", CultureInfo.InvariantCulture);
        LmsFailedOutboxMetricText.Text = (overview.FailedOutbox + overview.DeadLetterOutbox).ToString("N0", CultureInfo.InvariantCulture);
        LmsCourseMappingsDataGrid.ItemsSource = mappings.Items;
        LmsCourseMappingsDataGrid.SelectedItem = mappings.Items.FirstOrDefault(item => item.CourseId == selectedCourseId)
            ?? mappings.Items.FirstOrDefault();
        LmsOutboxDataGrid.ItemsSource = outbox.Items;
        SetLoadedStatus("CSCA LMS", mappings.Items.Count + outbox.Items.Count);
    }

    private void ResetLmsMetrics()
    {
        LmsReadyMappingsMetricText.Text = "—";
        LmsTotalCoursesMetricText.Text = "trên — khóa học";
        LmsActiveGrantsMetricText.Text = "—";
        LmsPendingOutboxMetricText.Text = "—";
        LmsFailedOutboxMetricText.Text = "—";
    }

}
