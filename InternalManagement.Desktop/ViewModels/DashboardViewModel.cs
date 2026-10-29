using System.Globalization;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class DashboardViewModel : ObservableViewModel
{
    public const string CompanyScope = "company";
    public const string EducationScope = "education";
    public const string FashionScope = "fashion";

    private readonly ApiClient _apiClient;
    private string _coursesCount = "—";
    private string _questionsCount = "—";
    private string _customersCount = "—";
    private string _syncHealthSummary = "Đang kiểm tra...";
    private string _deadLetterSummary = "Dead-Letters: đang kiểm tra...";
    private bool _isApiHealthy;
    private string _scope = CompanyScope;
    private string _scopeTitle = "Toàn công ty";
    private string _scopeDescription = "Bức tranh hợp nhất Công nghệ - Giáo dục và Thời trang";
    private string _updatedAt = "Đang tải dữ liệu...";
    private string _financeNote = "Dữ liệu tài chính từ đầu năm đến hiện tại";
    private string _peopleSummary = "Đang tổng hợp nhân sự";
    private string _attendanceSummary = "Đang tổng hợp chấm công";
    private IReadOnlyList<DashboardKpiItem> _kpis = [];
    private IReadOnlyList<DashboardFinanceBarItem> _financeBars = [];
    private IReadOnlyList<DashboardOperationItem> _operationBars = [];
    private IReadOnlyList<DashboardAlertItem> _alerts = [];

    public DashboardViewModel(ApiClient apiClient) => _apiClient = apiClient;

    public string CoursesCount { get => _coursesCount; set => Set(ref _coursesCount, value); }
    public string QuestionsCount { get => _questionsCount; set => Set(ref _questionsCount, value); }
    public string CustomersCount { get => _customersCount; set => Set(ref _customersCount, value); }
    public string SyncHealthSummary { get => _syncHealthSummary; private set => Set(ref _syncHealthSummary, value); }
    public string DeadLetterSummary { get => _deadLetterSummary; private set => Set(ref _deadLetterSummary, value); }
    public bool IsApiHealthy { get => _isApiHealthy; private set => Set(ref _isApiHealthy, value); }
    public string Scope { get => _scope; private set => Set(ref _scope, value); }
    public string ScopeTitle { get => _scopeTitle; private set => Set(ref _scopeTitle, value); }
    public string ScopeDescription { get => _scopeDescription; private set => Set(ref _scopeDescription, value); }
    public string UpdatedAt { get => _updatedAt; private set => Set(ref _updatedAt, value); }
    public string FinanceNote { get => _financeNote; private set => Set(ref _financeNote, value); }
    public string PeopleSummary { get => _peopleSummary; private set => Set(ref _peopleSummary, value); }
    public string AttendanceSummary { get => _attendanceSummary; private set => Set(ref _attendanceSummary, value); }
    public IReadOnlyList<DashboardKpiItem> Kpis { get => _kpis; private set => Set(ref _kpis, value); }
    public IReadOnlyList<DashboardFinanceBarItem> FinanceBars { get => _financeBars; private set => Set(ref _financeBars, value); }
    public IReadOnlyList<DashboardOperationItem> OperationBars { get => _operationBars; private set => Set(ref _operationBars, value); }
    public IReadOnlyList<DashboardAlertItem> Alerts { get => _alerts; private set => Set(ref _alerts, value); }

    public async Task RefreshAsync(string scope = CompanyScope, CancellationToken ct = default)
    {
        Scope = NormalizeScope(scope);
        ApplyScopeCopy();
        UpdatedAt = "Đang cập nhật báo cáo...";

        var businessSegment = Scope switch
        {
            EducationScope => "TECHNOLOGY_EDUCATION",
            FashionScope => "FASHION",
            _ => null
        };
        var yearStart = new DateTime(DateTime.Today.Year, 1, 1);
        var tomorrow = DateTime.Today.AddDays(1);
        var monthStart = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        var monthEnd = DateOnly.FromDateTime(DateTime.Today);

        var healthTask = _apiClient.CheckHealthAsync(ct);
        var financeTask = SafeAsync(() => _apiClient.GetCompanyFinancialOverviewAsync(yearStart, tomorrow, ct));
        var syncRunsTask = SafeAsync(() => _apiClient.GetSyncRunsAsync(ct));
        var deadLettersTask = SafeAsync(() => _apiClient.GetDeadLettersAsync(resolved: false, ct: ct));
        var employeesTask = SafeAsync(() => _apiClient.GetEmployeesAsync(businessSegment: businessSegment, ct: ct));
        var attendanceTask = SafeAsync(() => _apiClient.GetAttendanceSummaryAsync(monthStart, monthEnd, businessSegment: businessSegment, ct: ct));

        var includeEducation = Scope is CompanyScope or EducationScope;
        var includeFashion = Scope is CompanyScope or FashionScope;
        var coursesTask = includeEducation ? SafeAsync(() => _apiClient.GetCoursesAsync(ct: ct)) : NullTask<ApiClient.PaginatedData<ApiClient.CourseItem>>();
        var questionsTask = includeEducation ? SafeAsync(() => _apiClient.GetQuestionsAsync(ct: ct)) : NullTask<ApiClient.PaginatedData<ApiClient.QuestionItem>>();
        var customersTask = includeEducation ? SafeAsync(() => _apiClient.GetCustomersAsync(ct: ct)) : NullTask<ApiClient.PaginatedData<ApiClient.CustomerItem>>();
        var classesTask = includeEducation ? SafeAsync(() => _apiClient.GetAllCscaClassesAsync(ct)) : NullTask<IReadOnlyList<ApiClient.CscaClassItem>>();
        var productsTask = includeFashion ? SafeAsync(() => _apiClient.GetFashionProductsAsync(ct: ct)) : NullTask<ApiClient.PaginatedData<ApiClient.FashionProductItem>>();
        var variantsTask = includeFashion ? SafeAsync(() => _apiClient.GetActiveSellableFashionVariantsAsync(ct)) : NullTask<IReadOnlyList<ApiClient.FashionVariantItem>>();
        var ordersTask = includeFashion ? SafeAsync(() => _apiClient.GetSalesOrdersAsync(ct: ct)) : NullTask<ApiClient.PaginatedData<ApiClient.SalesOrderSummaryItem>>();

        await Task.WhenAll(
            healthTask, financeTask, syncRunsTask, deadLettersTask, employeesTask, attendanceTask,
            coursesTask, questionsTask, customersTask, classesTask, productsTask, variantsTask, ordersTask);

        var finance = await financeTask;
        var syncRuns = await syncRunsTask;
        var deadLetters = await deadLettersTask;
        var employees = await employeesTask;
        var attendance = await attendanceTask;
        var courses = await coursesTask;
        var questions = await questionsTask;
        var customers = await customersTask;
        var classes = await classesTask ?? [];
        var products = await productsTask;
        var variants = await variantsTask ?? [];
        var orders = await ordersTask;

        CoursesCount = (courses?.TotalCount ?? 0).ToString(CultureInfo.InvariantCulture);
        QuestionsCount = (questions?.TotalCount ?? 0).ToString(CultureInfo.InvariantCulture);
        CustomersCount = (customers?.TotalCount ?? 0).ToString(CultureInfo.InvariantCulture);
        IsApiHealthy = (await healthTask).IsHealthy;
        SyncHealthSummary = syncRuns?.Items.Count > 0 ? syncRuns.Items[0].StatusLabel : "Chưa có lần chạy";
        DeadLetterSummary = $"Dead-Letters: {deadLetters?.TotalCount ?? 0:N0} bản ghi chờ xử lý";

        var selectedFinance = ResolveFinanceScope(finance, Scope);
        var activeEmployees = employees?.Items.Count(x => x.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase)) ?? 0;
        var totalEmployees = employees?.TotalCount ?? 0;
        var studentCount = classes.Sum(x => x.StudentCount);
        var educationDebt = classes.Sum(x => x.DebtAmount);
        var productCount = products?.TotalCount ?? 0;
        var orderCount = orders?.TotalCount ?? 0;
        var stockUnits = variants.Sum(x => Math.Max(0, x.OnHandQuantity));
        var attendanceRate = attendance is { TotalRecords: > 0 }
            ? (decimal)attendance.PresentCount / attendance.TotalRecords * 100m
            : 0m;

        PeopleSummary = $"{activeEmployees:N0}/{totalEmployees:N0} nhân sự đang làm việc";
        AttendanceSummary = attendance is null
            ? "Chưa có dữ liệu chấm công tháng này"
            : $"{attendance.PresentCount:N0} đúng giờ · {attendance.LateCount:N0} đi muộn · {attendanceRate:N0}% hiện diện";
        FinanceNote = $"Từ 01/01/{DateTime.Today.Year} đến {DateTime.Today:dd/MM/yyyy} · {selectedFinance?.ProfitDataStatusName ?? "Chưa có trạng thái dữ liệu"}";

        Kpis = BuildKpis(selectedFinance, activeEmployees, totalEmployees, classes.Count, studentCount,
            educationDebt, productCount, variants.Count, stockUnits, orderCount, attendanceRate, deadLetters?.TotalCount ?? 0);
        FinanceBars = BuildFinanceBars(finance, Scope);
        OperationBars = BuildOperationBars(Scope, courses?.TotalCount ?? 0, questions?.TotalCount ?? 0,
            classes.Count, studentCount, productCount, variants.Count, stockUnits, orderCount);
        Alerts = BuildAlerts(finance, attendance, educationDebt, variants, deadLetters?.TotalCount ?? 0, IsApiHealthy);
        UpdatedAt = $"Cập nhật lúc {DateTime.Now:HH:mm · dd/MM/yyyy}";
    }

    private IReadOnlyList<DashboardKpiItem> BuildKpis(
        ApiClient.FinancialAreaSummaryItem? finance, int activeEmployees, int totalEmployees,
        int classCount, int studentCount, decimal educationDebt, int productCount, int variantCount,
        int stockUnits, int orderCount, decimal attendanceRate, int deadLetters)
    {
        var income = finance?.TotalIncome ?? 0m;
        var profit = finance?.OperatingProfit ?? 0m;
        var margin = income == 0 ? 0 : profit / income * 100m;
        var common = new List<DashboardKpiItem>
        {
            new("DOANH THU TỪ ĐẦU NĂM", CompactMoney(income), "Thực thu đã ghi nhận", "\uE8C7", "#0F766E", "#ECFDF5"),
            new("LỢI NHUẬN HOẠT ĐỘNG", CompactMoney(profit), $"Biên lợi nhuận {margin:N1}%", "\uE9D2", profit >= 0 ? "#2563EB" : "#DC2626", profit >= 0 ? "#EFF6FF" : "#FEF2F2"),
            new("NHÂN SỰ ĐANG LÀM VIỆC", activeEmployees.ToString("N0"), $"Trên tổng {totalEmployees:N0} hồ sơ", "\uE716", "#7C3AED", "#F5F3FF")
        };

        if (Scope == EducationScope)
        {
            common.Add(new("LỚP & HỌC VIÊN", $"{classCount:N0} · {studentCount:N0}", "Lớp đang quản lý · học viên", "\uE7BE", "#0284C7", "#F0F9FF"));
            common.Add(new("CÔNG NỢ HỌC PHÍ", CompactMoney(educationDebt), educationDebt > 0 ? "Cần theo dõi thu học phí" : "Không có công nợ", "\uE8C7", "#D97706", "#FFFBEB"));
        }
        else if (Scope == FashionScope)
        {
            common.Add(new("SẢN PHẨM & SKU", $"{productCount:N0} · {variantCount:N0}", "Danh mục sản phẩm · biến thể", "\uE719", "#BE185D", "#FDF2F8"));
            common.Add(new("TỒN KHO & ĐƠN HÀNG", $"{stockUnits:N0} · {orderCount:N0}", "Sản phẩm tồn · tổng đơn", "\uE7B8", "#C2410C", "#FFF7ED"));
        }
        else
        {
            common.Add(new("QUY MÔ GIÁO DỤC", $"{classCount:N0} lớp", $"{studentCount:N0} học viên đang quản lý", "\uE7BE", "#0284C7", "#F0F9FF"));
            common.Add(new("QUY MÔ THỜI TRANG", $"{productCount:N0} sản phẩm", $"{orderCount:N0} đơn · {stockUnits:N0} tồn kho", "\uE719", "#BE185D", "#FDF2F8"));
        }

        common.Add(new("SỨC KHỎE VẬN HÀNH", IsApiHealthy ? "Ổn định" : "Gián đoạn", deadLetters > 0 ? $"{deadLetters:N0} lỗi đồng bộ cần xử lý" : $"Chấm công hiện diện {attendanceRate:N0}%", "\uE9D9", IsApiHealthy && deadLetters == 0 ? "#059669" : "#DC2626", IsApiHealthy && deadLetters == 0 ? "#ECFDF5" : "#FEF2F2"));
        return common;
    }

    private static IReadOnlyList<DashboardFinanceBarItem> BuildFinanceBars(ApiClient.CompanyFinancialOverviewItem? finance, string scope)
    {
        var areas = finance?.Areas ?? [];
        var max = areas.SelectMany(x => new[] { x.TotalIncome, x.TotalExpense }).DefaultIfEmpty(1m).Max();
        if (max <= 0) max = 1;
        return areas.Select(area => new DashboardFinanceBarItem(
            area.AreaName,
            CompactMoney(area.TotalIncome),
            CompactMoney(area.TotalExpense),
            CompactMoney(area.OperatingProfit),
            34 + (double)(area.TotalIncome / max * 210m),
            34 + (double)(area.TotalExpense / max * 210m),
            scope == CompanyScope || area.AreaCode.Equals(scope == EducationScope ? "TECHNOLOGY_EDUCATION" : "FASHION", StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static IReadOnlyList<DashboardOperationItem> BuildOperationBars(
        string scope, int courses, int questions, int classes, int students,
        int products, int variants, int stockUnits, int orders)
    {
        var values = scope switch
        {
            EducationScope => new[]
            {
                new OperationValue("Khóa học", courses, "khóa"),
                new OperationValue("Lớp CSCA", classes, "lớp"),
                new OperationValue("Học viên", students, "người"),
                new OperationValue("Ngân hàng câu hỏi", questions, "câu")
            },
            FashionScope => new[]
            {
                new OperationValue("Sản phẩm", products, "sản phẩm"),
                new OperationValue("SKU đang bán", variants, "SKU"),
                new OperationValue("Tồn kho", stockUnits, "đơn vị"),
                new OperationValue("Đơn hàng", orders, "đơn")
            },
            _ => new[]
            {
                new OperationValue("Lớp CSCA", classes, "lớp"),
                new OperationValue("Học viên", students, "người"),
                new OperationValue("Sản phẩm thời trang", products, "sản phẩm"),
                new OperationValue("Đơn hàng", orders, "đơn")
            }
        };
        var max = Math.Max(1, values.Max(x => x.Value));
        var colors = new[] { "#0EA5E9", "#14B8A6", "#F59E0B", "#EC4899" };
        return values.Select((item, index) => new DashboardOperationItem(
            item.Label, $"{item.Value:N0} {item.Unit}", 28 + item.Value / (double)max * 190, colors[index])).ToArray();
    }

    private static IReadOnlyList<DashboardAlertItem> BuildAlerts(
        ApiClient.CompanyFinancialOverviewItem? finance, ApiClient.AttendanceSummaryItem? attendance,
        decimal educationDebt, IReadOnlyList<ApiClient.FashionVariantItem> variants, int deadLetters, bool apiHealthy)
    {
        var alerts = new List<DashboardAlertItem>();
        if (!apiHealthy) alerts.Add(new("API nội bộ đang gián đoạn", "Một số chỉ số có thể chưa cập nhật.", "#DC2626", "#FEF2F2", "\uEA39"));
        if (deadLetters > 0) alerts.Add(new("Đồng bộ cần xử lý", $"{deadLetters:N0} bản ghi đang nằm trong Dead-Letter Queue.", "#DC2626", "#FEF2F2", "\uE7BA"));
        if (finance?.HasUnclassifiedTransactions == true) alerts.Add(new("Giao dịch chưa phân loại", "Có dòng tiền chưa được gắn đúng mảng hoạt động.", "#D97706", "#FFFBEB", "\uE9CE"));
        if (educationDebt > 0) alerts.Add(new("Công nợ học phí", $"Còn {CompactMoney(educationDebt)} cần theo dõi thu.", "#D97706", "#FFFBEB", "\uE8C7"));
        var outOfStock = variants.Count(x => x.OnHandQuantity <= 0);
        if (outOfStock > 0) alerts.Add(new("SKU hết hàng", $"{outOfStock:N0} SKU đang có tồn kho bằng 0.", "#C2410C", "#FFF7ED", "\uE7B8"));
        if (attendance is { } summary && summary.LateCount + summary.AbsentCount > 0)
            alerts.Add(new("Chấm công tháng này", $"{summary.LateCount:N0} lượt đi muộn · {summary.AbsentCount:N0} lượt vắng.", "#7C3AED", "#F5F3FF", "\uE823"));
        if (alerts.Count == 0) alerts.Add(new("Vận hành ổn định", "Chưa phát hiện cảnh báo cần ưu tiên xử lý.", "#059669", "#ECFDF5", "\uE73E"));
        return alerts.Take(5).ToArray();
    }

    private static ApiClient.FinancialAreaSummaryItem? ResolveFinanceScope(ApiClient.CompanyFinancialOverviewItem? finance, string scope)
    {
        if (finance is null) return null;
        if (scope == CompanyScope) return finance.CompanyTotal;
        var code = scope == EducationScope ? "TECHNOLOGY_EDUCATION" : "FASHION";
        return finance.Areas?.FirstOrDefault(x => x.AreaCode.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyScopeCopy()
    {
        (ScopeTitle, ScopeDescription) = Scope switch
        {
            EducationScope => ("Công nghệ - Giáo dục", "Lớp học, học viên, công nợ, nhân sự và hiệu quả vận hành CSCA"),
            FashionScope => ("Thời trang", "Sản phẩm, tồn kho, đơn hàng, nhân sự và hiệu quả kinh doanh"),
            _ => ("Toàn công ty", "Bức tranh hợp nhất Công nghệ - Giáo dục và Thời trang")
        };
    }

    private static string NormalizeScope(string? scope) => scope?.Trim().ToLowerInvariant() switch
    {
        EducationScope => EducationScope,
        FashionScope => FashionScope,
        _ => CompanyScope
    };

    private static string CompactMoney(decimal value)
    {
        var absolute = Math.Abs(value);
        if (absolute >= 1_000_000_000m) return $"{value / 1_000_000_000m:N1} tỷ";
        if (absolute >= 1_000_000m) return $"{value / 1_000_000m:N1} triệu";
        if (absolute >= 1_000m) return $"{value / 1_000m:N0} nghìn";
        return $"{value:N0} đ";
    }

    private static async Task<T?> SafeAsync<T>(Func<Task<T?>> action) where T : class
    {
        try { return await action(); }
        catch { return null; }
    }

    private static Task<T?> NullTask<T>() where T : class => Task.FromResult<T?>(null);
    private sealed record OperationValue(string Label, int Value, string Unit);
}

public sealed record DashboardKpiItem(string Label, string Value, string Detail, string Icon, string Accent, string Background);
public sealed record DashboardFinanceBarItem(string Label, string Income, string Expense, string Profit, double IncomeWidth, double ExpenseWidth, bool IsSelected);
public sealed record DashboardOperationItem(string Label, string Value, double Width, string Color);
public sealed record DashboardAlertItem(string Title, string Detail, string Foreground, string Background, string Icon);
