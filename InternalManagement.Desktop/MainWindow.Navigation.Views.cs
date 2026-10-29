using System.Windows;
using System.Windows.Controls;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.Desktop;

public partial class MainWindow
{
    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (ViewDashboardContainer == null) return;

        if (sender is RadioButton navigationItem && navigationItem.Visibility != Visibility.Visible)
        {
            return;
        }

        if (_isCourseFunctionSelectionSyncing || _isEmployeeFunctionSelectionSyncing || _isPayrollFunctionSelectionSyncing || _isFashionFunctionSelectionSyncing)
        {
            return;
        }

        if (!IsCourseNavigationItem(sender))
        {
            SetCoursesSubmenuVisibility(false);
        }

        if (!IsCscaMolyNavigationItem(sender))
        {
            SetCscaMolySubmenuVisibility(false);
        }

        if (!IsEmployeeNavigationItem(sender))
        {
            SetEmployeesSubmenuVisibility(false, false);
        }

        if (!IsPayrollNavigationItem(sender))
        {
            SetPayrollSubmenuVisibility(false, false);
        }

        if (!IsFashionInventoryNavigationItem(sender))
        {
            SetFashionSubmenuVisibility(false);
        }

        HideAllViews();
        QuickSyncButton.Content = sender == NavLms
            ? "↻ Đồng bộ khóa đã chọn"
            : "↻ Nhập khóa học từ Website";

        if (sender == NavDashboard)
        {
            var dashboardScope = ViewDashboardContainer.SelectedScope;
            SetActiveArea(dashboardScope switch
            {
                DashboardViewModel.EducationScope => TechnologySegment,
                DashboardViewModel.FashionScope => FashionSegment,
                _ => null
            });
            ViewDashboardContainer.Visibility = Visibility.Visible;
            ApplyDashboardHeader(dashboardScope);
            SetViewStatus("Đã sẵn sàng");
            _ = LoadViewOnceAsync("dashboard", "Đang cập nhật chỉ số tổng quan...", async () =>
            {
                _cachedBusinessUnits = await _apiClient.GetBusinessUnitProfitSummaryAsync() ?? new List<ApiClient.BusinessUnitProfitItem>();
                await LoadDashboardMetricsAsync();
            }, "Tổng hợp doanh thu, học viên, nhân sự & tồn kho toàn hệ thống");
        }
        else if (IsEducationNavigationItem(sender))
        {
            SetActiveArea(TechnologySegment);
            SetCoursesSubmenuVisibility(IsCourseNavigationItem(sender));
            SetCscaMolySubmenuVisibility(IsCscaMolyNavigationItem(sender));
            ViewCoursesContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = sender == NavCourseClasses ? "Danh Sách Lớp Học CSCA"
                : sender == NavCourseFinance ? "Tài Chính & Công Nợ Lớp Học"
                : sender == NavCscaMoly ? "CSCA Moly"
                : sender == NavCourseMaterials ? "Kho Tài Liệu & Giáo Trình Trực Tuyến"
                : sender == NavCourseVocabulary ? "Kho Từ Vựng Chuyên Ngành CSCA"
                : sender == NavCourseCommunity ? "Cộng Đồng Hỏi Đáp & Thảo Luận"
                : "Khóa Học & Lớp Học";
            ViewHeaderSubtitle.Text = sender == NavCourseClasses ? "Quản lý danh sách lớp học, sĩ số và giáo viên phụ trách"
                : sender == NavCourseFinance ? "Báo cáo doanh thu, công nợ học phí và thù lao giảng dạy"
                : sender == NavCscaMoly ? "Tài liệu, từ vựng và cộng đồng dành cho CSCA Moly"
                : sender == NavCourseMaterials ? "Tài liệu bài giảng đồng bộ từ hệ thống CSCA Web"
                : sender == NavCourseVocabulary ? "Bộ từ điển HSK, thương mại và giao tiếp đa ngôn ngữ"
                : sender == NavCourseCommunity ? "Kiểm duyệt câu hỏi, bài viết và tương tác người học"
                : "Quản lý khóa học, danh sách lớp học, tài chính & công nợ học phí";
            SelectCourseFunction(sender);

            _ = LoadViewOnceAsync("courses_classes", "Đang tải dữ liệu Khóa học & Lớp học...", async () =>
            {
                await LoadCoursesAsync();
                await LoadCscaClassesAsync();
                await LoadCscaOnlineAsync();
            }, "Đồng bộ chương trình đào tạo, lớp học & công nợ");
        }
        else if (sender == NavInterview)
        {
            SetActiveArea(TechnologySegment);
            ViewInterviewContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Mock Interview & Khách Hàng";
            ViewHeaderSubtitle.Text = "Quản lý ứng viên phỏng vấn thử, gói dịch vụ, số buổi & doanh thu";
            _ = LoadViewOnceAsync("interview", "Đang tải danh sách Mock Interview...", () => LoadInterviewCustomersAsync(), "Tải ứng viên phỏng vấn thử, số buổi & doanh thu dịch vụ");
        }
        else if (IsEmployeeNavigationItem(sender))
        {
            var isFashionEmployee = IsFashionEmployeeNavigationItem(sender);
            SetActiveArea(isFashionEmployee ? FashionSegment : TechnologySegment);
            SetEmployeesSubmenuVisibility(isFashionEmployee, true);
            ViewEmployeesContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Nhân sự & CV";
            ViewHeaderSubtitle.Text = $"Hồ sơ, loại hợp đồng, mức lương và CV riêng của mảng {ActiveSegmentName}";
            SelectEmployeesFunction(sender);
            _ = LoadViewOnceAsync(SegmentViewKey("employees"), "Đang tải danh sách nhân sự...", async () =>
            {
                await LoadDepartmentsFilterAsync();
                await LoadEmployeesAsync();
            }, $"Tải phòng ban, hợp đồng và hồ sơ CV riêng của {ActiveSegmentName}");
        }
        else if (sender == NavTechAttendance || sender == NavFashionAttendance)
        {
            SetActiveArea(sender == NavFashionAttendance ? FashionSegment : TechnologySegment);
            ViewAttendanceContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Chấm công";
            ViewHeaderSubtitle.Text = $"Nhật ký chấm công chỉ của nhân sự mảng {ActiveSegmentName}";
            _ = LoadViewOnceAsync(SegmentViewKey("attendance"), "Đang tải dữ liệu chấm công...", () => LoadAttendanceAsync(), $"Tổng hợp bảng chấm công thời gian thực của {ActiveSegmentName}");
        }
        else if (sender == NavCustomers)
        {
            SetActiveArea(TechnologySegment);
            ViewCustomersContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Học Viên & Khách Hàng";
            ViewHeaderSubtitle.Text = "Tách riêng học viên từ lớp học và khách hàng đăng ký/đồng bộ";
            _ = LoadViewOnceAsync("customers", "Đang tải học viên và khách hàng...", () => LoadCustomersAsync(), "Tải đầy đủ liên hệ, khóa học, lớp học và công nợ");
        }
        else if (sender == NavSync)
        {
            SetActiveArea(null);
            ViewSyncContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Hạ Tầng Đồng Bộ Dữ Liệu";
            ViewHeaderSubtitle.Text = "Theo dõi tiến trình đồng bộ webhook, REST pull & quản lý Dead-Letter Queue";
            _ = LoadViewOnceAsync("sync", "Đang tải lịch sử đồng bộ...", () => LoadSyncRunsAsync(), "Kiểm tra tiến trình webhook, REST pull & Dead-Letter Queue");
        }
        else if (sender == NavLms)
        {
            SetActiveArea(TechnologySegment);
            ViewLmsContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Đồng bộ Web CSCA Course";
            ViewHeaderSubtitle.Text = "Liên kết khóa học, tự động kích hoạt tài khoản học viên khi đóng học phí và theo dõi tiến trình gửi Web";
            _ = LoadViewOnceAsync("csca-lms", "Đang tải trạng thái đồng bộ Web CSCA...", LoadLmsIntegrationAsync, "Kiểm tra liên kết khóa học và tài khoản học viên Web");
        }
        else if (IsPayrollNavigationItem(sender))
        {
            var isFashionPayroll = IsFashionPayrollNavigationItem(sender);
            SetActiveArea(isFashionPayroll ? FashionSegment : TechnologySegment);
            SetPayrollSubmenuVisibility(isFashionPayroll, true);
            ViewPayrollContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Bảng lương";
            ViewHeaderSubtitle.Text = $"Kỳ lương và phiếu lương full-time/part-time riêng của mảng {ActiveSegmentName}";
            SelectPayrollFunction(sender);
            _ = LoadViewOnceAsync(SegmentViewKey("payroll"), "Đang tải bảng tính lương...", () => LoadPayrollPeriodsAsync(), $"Tổng hợp kỳ lương và phiếu lương của mảng {ActiveSegmentName}");
        }
        else if (sender == NavTechInternalCustomers || sender == NavFashionInternalCustomers)
        {
            SetActiveArea(sender == NavFashionInternalCustomers ? FashionSegment : TechnologySegment);
            ShowInternalDataView(InternalDataViewMode.Customers);
        }
        else if (sender == NavTechResources || sender == NavFashionResources)
        {
            SetActiveArea(sender == NavFashionResources ? FashionSegment : TechnologySegment);
            ShowInternalDataView(InternalDataViewMode.Resources);
        }
        else if (sender == NavCompanyFinance)
        {
            SetActiveArea(null);
            ViewCompanyFinanceContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Dòng tiền & lợi nhuận";
            ViewHeaderSubtitle.Text = "Bảng tổng song song của Công nghệ - Giáo dục và Thời trang";
            _ = LoadViewOnceAsync("company-finance", "Đang tổng hợp tài chính hai mảng...", LoadCompanyFinanceAsync, "Đối chiếu dòng tiền P&L, doanh thu & chi phí hợp nhất");
        }
        else if (sender == NavFinanceWorkspace)
        {
            SetActiveArea(null);
            ViewFinanceWorkspaceContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Tài chính vận hành";
            ViewHeaderSubtitle.Text = "Dòng tiền thực tế và dự báo, công nợ học viên, thanh toán giáo viên và kế hoạch thu chi";
            _ = LoadViewOnceAsync("finance-workspace", "Đang tải bảng tài chính vận hành...", LoadFinanceWorkspaceAsync, "Tổng hợp số thực tế và kịch bản theo tháng");
        }
        else if (sender == NavCostBreakEven)
        {
            SetActiveArea(null);
            ViewCostBreakEvenContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Chi phí & Hòa vốn";
            ViewHeaderSubtitle.Text = "Đối chiếu doanh thu, chi phí giáo viên từ CSCA Course/LMS và các khoản chi khác";
            _ = LoadViewOnceAsync("cost-break-even", "Đang tải doanh thu và chi phí giáo viên từ LMS...", LoadCostBreakEvenAsync, "Đối chiếu doanh thu, chi phí giáo viên từ CSCA Course/LMS và các khoản chi khác");
        }
        else if (IsFashionInventoryNavigationItem(sender))
        {
            SetActiveArea(FashionSegment);
            SetFashionSubmenuVisibility(true);
            ViewFashionContainer.Visibility = Visibility.Visible;
            ViewHeaderTitle.Text = "Thời trang & Quản lý kho";
            ViewHeaderSubtitle.Text = "Một luồng dữ liệu từ API nội bộ: sản phẩm → SKU → nhập kho → tồn kho → sổ giao dịch";
            SelectFashionFunction(sender);
            _ = LoadViewOnceAsync("fashion-products", "Đang tải danh mục sản phẩm thời trang...", LoadFashionDataAsync, "Tổng hợp danh mục thiết kế, định mức nguyên phụ liệu & tồn kho xưởng");
        }
    }

    private void HideAllViews()
    {
        ViewDashboardContainer.Visibility = Visibility.Collapsed;
        ViewCoursesContainer.Visibility = Visibility.Collapsed;
        ViewQuestionsContainer.Visibility = Visibility.Collapsed;
        ViewInterviewContainer.Visibility = Visibility.Collapsed;
        ViewEmployeesContainer.Visibility = Visibility.Collapsed;
        ViewAttendanceContainer.Visibility = Visibility.Collapsed;
        ViewCustomersContainer.Visibility = Visibility.Collapsed;
        ViewSyncContainer.Visibility = Visibility.Collapsed;
        ViewLmsContainer.Visibility = Visibility.Collapsed;
        ViewPayrollContainer.Visibility = Visibility.Collapsed;
        if (ViewFashionContainer != null) ViewFashionContainer.Visibility = Visibility.Collapsed;
        ViewCompanyFinanceContainer.Visibility = Visibility.Collapsed;
        ViewFinanceWorkspaceContainer.Visibility = Visibility.Collapsed;
        ViewCostBreakEvenContainer.Visibility = Visibility.Collapsed;
        ViewInternalDataContainer.Visibility = Visibility.Collapsed;
    }
}
