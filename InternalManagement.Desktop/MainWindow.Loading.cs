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
using InternalManagement.Desktop.ViewModels;
using InternalManagement.Desktop.Views;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    // ── Data Loading Methods ──

    private async Task LoadDashboardMetricsAsync()
    {
        await _dashboardViewModel.RefreshAsync(ViewDashboardContainer.SelectedScope);
        var isHealthy = _dashboardViewModel.IsApiHealthy;
        ApiHealthBorder.Background = isHealthy ? new SolidColorBrush(Color.FromRgb(236, 253, 245)) : new SolidColorBrush(Color.FromRgb(254, 242, 242));
        ApiHealthBorder.BorderBrush = isHealthy ? new SolidColorBrush(Color.FromRgb(167, 243, 208)) : new SolidColorBrush(Color.FromRgb(254, 202, 202));
        ApiHealthDot.Fill = isHealthy ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(239, 68, 68));
        ApiHealthText.Foreground = isHealthy ? new SolidColorBrush(Color.FromRgb(6, 95, 70)) : new SolidColorBrush(Color.FromRgb(153, 27, 27));
        ApiHealthText.Text = isHealthy ? "API Trực Tuyến" : "API Không Khả Dụng";
    }

    private void DashboardScopeChanged(object? sender, DashboardScopeChangedEventArgs e)
    {
        _isDashboardScopeSelectionSyncing = true;
        try
        {
            SegmentTechnologyButton.IsChecked = e.Scope == DashboardViewModel.EducationScope;
            SegmentFashionButton.IsChecked = e.Scope == DashboardViewModel.FashionScope;
            TechnologyNavigationGroup.Visibility = e.Scope == DashboardViewModel.EducationScope ? Visibility.Visible : Visibility.Collapsed;
            FashionNavigationGroup.Visibility = e.Scope == DashboardViewModel.FashionScope ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _isDashboardScopeSelectionSyncing = false;
        }

        SetActiveArea(e.Scope switch
        {
            DashboardViewModel.EducationScope => TechnologySegment,
            DashboardViewModel.FashionScope => FashionSegment,
            _ => null
        });
        ApplyDashboardHeader(e.Scope);
        InvalidateLoadedView("dashboard");
        _ = RunWithBusyAsync("Đang cập nhật báo cáo tổng quan...", LoadDashboardMetricsAsync, "Tổng hợp KPI, biểu đồ và cảnh báo vận hành");
    }

    private void RefreshDashboard_Click(object sender, RoutedEventArgs e)
    {
        InvalidateLoadedView("dashboard");
        _ = RunWithBusyAsync("Đang làm mới báo cáo tổng quan...", LoadDashboardMetricsAsync, "Đối chiếu dữ liệu mới nhất từ các phân hệ");
    }

    private void ApplyDashboardHeader(string scope)
    {
        ViewHeaderTitle.Text = scope switch
        {
            DashboardViewModel.EducationScope => "Tổng Quan Công Nghệ - Giáo Dục",
            DashboardViewModel.FashionScope => "Tổng Quan Thời Trang",
            _ => "Tổng Quan Toàn Công Ty"
        };
        ViewHeaderSubtitle.Text = scope switch
        {
            DashboardViewModel.EducationScope => "Báo cáo lớp học, học viên, công nợ, nhân sự và tài chính CSCA",
            DashboardViewModel.FashionScope => "Báo cáo sản phẩm, tồn kho, đơn hàng, nhân sự và tài chính thời trang",
            _ => "Báo cáo điều hành hợp nhất toàn bộ hệ thống MOLY"
        };
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
                filtered = filtered.Where(c => c.CourseId == _selectedCourseFilterId.Value || string.Equals(c.CourseTitle, title, StringComparison.OrdinalIgnoreCase));
            }
            CscaCourseFilterBanner.Visibility = Visibility.Visible;
            CscaCourseFilterText.Text = $"Đang lọc các lớp thuộc khóa học: {title} ({filtered.Count()} lớp)";
        }
        else
        {
            CscaCourseFilterBanner.Visibility = Visibility.Collapsed;
        }

        if (CscaClassTypeFilterComboBox != null && CscaClassTypeFilterComboBox.SelectedIndex > 0)
        {
            if (CscaClassTypeFilterComboBox.SelectedIndex == 1) // Lớp chính thức
                filtered = filtered.Where(c => !c.IsTrial && c.TuitionFee > 0);
            else if (CscaClassTypeFilterComboBox.SelectedIndex == 2) // Lớp học thử
                filtered = filtered.Where(c => c.IsTrial || c.TuitionFee == 0);
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
                filtered = filtered.Where(c => c.CourseId == _selectedCourseFilterId.Value || string.Equals(c.CourseTitle, title, StringComparison.OrdinalIgnoreCase));
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

    private async Task LoadCostBreakEvenAsync()
    {
        var from = ViewCostBreakEvenContainer.FromDate;
        var toExclusive = ViewCostBreakEvenContainer.ToDateExclusive;
        if (from.HasValue && toExclusive.HasValue && from >= toExclusive)
        {
            SetViewStatus("Ngày bắt đầu phải trước hoặc bằng ngày kết thúc.", isError: true);
            return;
        }

        var selectedClassId = ViewCostBreakEvenContainer.SelectedClassId;
        var classesTask = _apiClient.GetAllCscaClassesAsync();
        var teacherCostsTask = _apiClient.GetCscaTeacherCostSummaryAsync(
            from,
            toExclusive,
            selectedClassId?.ToString("N"));
        var breakEvenTask = selectedClassId.HasValue
            ? _apiClient.GetCscaClassBreakEvenAsync(selectedClassId.Value, from, toExclusive)
            : Task.FromResult<ApiClient.CscaClassBreakEvenItem?>(null);
        await Task.WhenAll(classesTask, teacherCostsTask, breakEvenTask);

        var classes = await classesTask ?? [];
        var teacherCosts = await teacherCostsTask;
        var breakEven = await breakEvenTask;
        ViewCostBreakEvenContainer.SetClassOptions(classes);

        var scopedClasses = classes.AsEnumerable();
        if (selectedClassId.HasValue)
            scopedClasses = scopedClasses.Where(x => x.Id == selectedClassId.Value);
        if (from.HasValue)
            scopedClasses = scopedClasses.Where(x => !x.EndDate.HasValue || x.EndDate.Value >= from.Value);
        if (toExclusive.HasValue)
            scopedClasses = scopedClasses.Where(x => !x.StartDate.HasValue || x.StartDate.Value < toExclusive.Value);

        var scopedClassList = scopedClasses.ToList();
        CostBreakEvenRevenueMetric.Text = $"{scopedClassList.Sum(x => x.TotalRevenue):N0} đ";
        ViewCostBreakEvenContainer.SetFilterScope(selectedClassId.HasValue
            ? $"Lớp đã chọn · {scopedClassList.Count:N0} lớp trong phạm vi"
            : $"Tất cả lớp · {scopedClassList.Count:N0} lớp trong phạm vi");
        CostBreakEvenOtherCostMetric.Text = breakEven is null
            ? "Chọn một lớp"
            : $"{breakEven.LedgerFixedCost + breakEven.LedgerVariableCost:N0} đ";
        CostBreakEvenMetric.Text = breakEven is null
            ? "Chọn một lớp"
            : breakEven.BreakEvenReachable
                ? $"{breakEven.BreakEvenStudents:N0} HV · {breakEven.BreakEvenRevenue:N0} đ"
                : "Chưa thể hòa vốn";
        ViewCostBreakEvenContainer.SetAnalysisNote(breakEven is null
            ? "Chọn một lớp để xem kết quả hòa vốn thực tế từ API."
            : breakEven.Warnings.Count > 0
                ? string.Join(" ", breakEven.Warnings)
                : $"Biên đóng góp bình quân {breakEven.ContributionMarginPerStudent:N0} đ/học viên; dữ liệu trong kỳ {breakEven.From.ToLocalTime():dd/MM/yyyy} - {breakEven.To.ToLocalTime().AddTicks(-1):dd/MM/yyyy}.");
        if (breakEven is not null)
        {
            CostBreakEvenRevenueMetric.Text = $"{breakEven.ActualRevenue:N0} đ";
            CostBreakEvenTeacherCostMetric.Text = $"{breakEven.TeacherAccruedCost:N0} đ";
        }
        CostBreakEvenRefreshStatusText.Text = $"Tải lúc {DateTime.Now:dd/MM/yyyy HH:mm}";

        if (teacherCosts is null)
        {
            if (breakEven is null) CostBreakEvenTeacherCostMetric.Text = "Chưa đồng bộ";
            CostBreakEvenLmsStatusText.Text = breakEven?.TeacherCostLastSyncedAt is { } breakEvenSyncedAt
                ? $"LMS đồng bộ {breakEvenSyncedAt.ToLocalTime():dd/MM HH:mm}"
                : "Chưa đồng bộ dữ liệu LMS";
            SetViewStatus(breakEven is null
                ? "Đã tải doanh thu lớp; chưa có snapshot chi phí giáo viên từ LMS."
                : "Đã tải phân tích hòa vốn lớp; API tổng hợp chi phí LMS chưa khả dụng.", isError: true);
            return;
        }

        if (breakEven is null)
            CostBreakEvenTeacherCostMetric.Text = $"{teacherCosts.ClassCostAmount:N0} đ";
        CostBreakEvenLmsStatusText.Text = teacherCosts.LastSyncedAt.HasValue
            ? $"LMS đồng bộ {teacherCosts.LastSyncedAt.Value.ToLocalTime():dd/MM HH:mm}"
            : "Chưa đồng bộ dữ liệu LMS";
        SetViewStatus($"Đã tải {teacherCosts.ItemCount:N0} dòng chi phí giáo viên từ LMS.");
    }

    private void RefreshCostBreakEven_Click(object sender, RoutedEventArgs e)
    {
        InvalidateLoadedView("cost-break-even");
        _ = RunWithBusyAsync(
            "Đang tải lại phân tích chi phí & hòa vốn...",
            LoadCostBreakEvenAsync,
            "Đối chiếu doanh thu lớp học với snapshot chi phí giáo viên từ LMS");
    }

    private void CostBreakEvenFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isUiReady || ViewCostBreakEvenContainer.Visibility != Visibility.Visible) return;
        InvalidateLoadedView("cost-break-even");
        _ = RunWithBusyAsync(
            "Đang cập nhật phạm vi phân tích...",
            LoadCostBreakEvenAsync,
            "Áp dụng bộ lọc thời gian và lớp học");
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
