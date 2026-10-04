using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using InternalManagement.Desktop.Services;
using InternalManagement.Desktop.ViewModels;

namespace InternalManagement.DesktopTests;

public sealed class ApiClientTests
{
    [Fact]
    public async Task GetCourses_DisposesResponseContent()
    {
        var content = new TrackedContent(EmptyPage);
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content })))
        { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);

        var courses = await apiClient.GetCoursesAsync();

        Assert.NotNull(courses);
        Assert.True(content.IsDisposed);
    }

    [Fact]
    public async Task ParallelUnauthorizedRequests_RefreshRotatingTokenOnce()
    {
        var bothOldRequestsSent = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldRequestCount = 0;
        var refreshCount = 0;
        using var httpClient = new HttpClient(new StubHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/refresh-token", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref refreshCount);
                return JsonResponse(new ApiClient.ApiEnvelope<ApiClient.LoginResponse>(
                    true, Session("new-access", "new-refresh"), null));
            }

            if (request.Headers.Authorization?.Parameter == "old-access")
            {
                if (Interlocked.Increment(ref oldRequestCount) == 2)
                    bothOldRequestsSent.TrySetResult(true);
                await bothOldRequestsSent.Task.WaitAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            Assert.Equal("new-access", request.Headers.Authorization?.Parameter);
            return JsonResponse(EmptyPage);
        })) { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);
        apiClient.RestoreSession(Session("old-access", "old-refresh"));

        var results = await Task.WhenAll(apiClient.GetCoursesAsync(), apiClient.GetCoursesAsync())
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.All(results, result => Assert.NotNull(result));
        Assert.Equal(2, oldRequestCount);
        Assert.Equal(1, refreshCount);
        Assert.Equal("new-access", apiClient.CurrentAccessToken);
    }

    [Fact]
    public async Task DashboardRefresh_UpdatesBindableMetrics()
    {
        using var httpClient = new HttpClient(new StubHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath == "/health"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("healthy") }
                : JsonResponse(EmptyPage))))
        { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);
        var dashboard = new DashboardViewModel(apiClient);
        var changed = new List<string?>();
        dashboard.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await dashboard.RefreshAsync();

        Assert.Equal("0", dashboard.CoursesCount);
        Assert.Equal("0", dashboard.QuestionsCount);
        Assert.Equal("0", dashboard.CustomersCount);
        Assert.Equal("Chưa có lần chạy", dashboard.SyncHealthSummary);
        Assert.True(dashboard.IsApiHealthy);
        Assert.Contains(nameof(DashboardViewModel.CoursesCount), changed);
    }

    [Fact]
    public async Task QuestionBankLoad_UpdatesItemsAndClearsThemOnFailure()
    {
        var requestCount = 0;
        var item = new ApiClient.QuestionItem(Guid.NewGuid(), "Bank", "Subject", null,
            "Easy", Guid.NewGuid(), 1, "Draft", "Question", null, [], []);
        using var httpClient = new HttpClient(new StubHandler((_, _) =>
            Task.FromResult(Interlocked.Increment(ref requestCount) == 1
                ? JsonResponse(new ApiClient.ApiEnvelope<ApiClient.PaginatedData<ApiClient.QuestionItem>>(
                    true, new ApiClient.PaginatedData<ApiClient.QuestionItem>([item], 1, 1, 50), null))
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
        { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);
        var viewModel = new QuestionBankViewModel(apiClient);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.True(await viewModel.LoadAsync());
        Assert.Single(viewModel.Items);
        Assert.Equal(1, viewModel.TotalCount);
        Assert.Contains(nameof(QuestionBankViewModel.Items), changed);

        Assert.False(await viewModel.LoadAsync());
        Assert.Empty(viewModel.Items);
        Assert.Equal(0, viewModel.TotalCount);
    }

    [Fact]
    public async Task InterviewLoad_UsesFinancialSummaryForBindableMetrics()
    {
        var customer = new ApiClient.InterviewCustomerItem(Guid.NewGuid(), "Website", "SRC-1",
            "Candidate", "candidate@example.test", null, "Mock", 2, 100m, 1, DateTime.UtcNow);
        using var httpClient = new HttpClient(new StubHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("financial-summary", StringComparison.Ordinal)
                ? JsonResponse(new ApiClient.ApiEnvelope<ApiClient.InterviewFinancialSummaryItem>(
                    true, new ApiClient.InterviewFinancialSummaryItem(12, 30, 9000m, 1000m), null))
                : JsonResponse(new ApiClient.ApiEnvelope<ApiClient.PaginatedData<ApiClient.InterviewCustomerItem>>(
                    true, new ApiClient.PaginatedData<ApiClient.InterviewCustomerItem>([customer], 12, 1, 50), null)))))
        { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);
        var viewModel = new InterviewCustomersViewModel(apiClient);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        Assert.True(await viewModel.LoadAsync("Candidate"));

        Assert.Single(viewModel.Items);
        Assert.Equal("12", viewModel.CustomerCount);
        Assert.Equal("30", viewModel.SessionCount);
        Assert.StartsWith("9", viewModel.RevenueText);
        Assert.Contains(nameof(InterviewCustomersViewModel.RevenueText), changed);
    }

    [Fact]
    public async Task CustomersLoad_UpdatesBothTabsAndTotalCount()
    {
        var customer = new ApiClient.CustomerItem(Guid.NewGuid(), "SRC-1", "Customer",
            "customer@example.test", null, 2, 100m, DateTime.UtcNow);
        using var httpClient = new HttpClient(new StubHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.Contains("csca", StringComparison.Ordinal)
                ? JsonResponse(new ApiClient.ApiEnvelope<List<ApiClient.CscaStudentDirectoryItem>>(true, [], null))
                : JsonResponse(new ApiClient.ApiEnvelope<ApiClient.PaginatedData<ApiClient.CustomerItem>>(
                    true, new ApiClient.PaginatedData<ApiClient.CustomerItem>([customer], 17, 1, 50), null)))))
        { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);
        var viewModel = new CustomersViewModel(apiClient);

        Assert.True(await viewModel.LoadAsync("Customer"));

        Assert.Single(viewModel.Customers);
        Assert.Empty(viewModel.Students);
        Assert.Equal(17, viewModel.TotalCustomerCount);
    }

    [Fact]
    public async Task EmployeesLoad_PartitionsStatusesAndClearsOnFailure()
    {
        var departmentId = Guid.NewGuid();
        var department = new ApiClient.DepartmentItem(departmentId, Guid.NewGuid(), "SALE", "Kinh doanh", 3, DateTime.UtcNow);
        ApiClient.EmployeeItem Employee(string status, string? cv = null) => new(
            Guid.NewGuid(), Guid.NewGuid(), null, null, departmentId, department.Name,
            null, null, Guid.NewGuid().ToString("N"), "Nhân sự", "staff@example.test",
            null, null, 0m, null, status, DateTime.UtcNow, CvUrlOrPath: cv);
        var employees = new[] { Employee("Active"), Employee("Resigned", "cv.pdf"), Employee("Blacklisted") };
        var employeeRequests = 0;
        using var httpClient = new HttpClient(new StubHandler((request, _) =>
            Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("/departments", StringComparison.Ordinal)
                ? JsonResponse(new ApiClient.ApiEnvelope<List<ApiClient.DepartmentItem>>(true, [department], null))
                : Interlocked.Increment(ref employeeRequests) == 1
                    ? JsonResponse(new ApiClient.ApiEnvelope<ApiClient.PaginatedData<ApiClient.EmployeeItem>>(
                        true, new ApiClient.PaginatedData<ApiClient.EmployeeItem>(employees, 3, 1, 200), null))
                    : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
        { BaseAddress = new Uri("http://localhost/") };
        using var apiClient = new ApiClient(httpClient);
        var viewModel = new EmployeesViewModel(apiClient);

        Assert.True(await viewModel.LoadDepartmentsAsync());
        Assert.Equal(2, viewModel.DepartmentOptions.Count);
        viewModel.SelectedDepartmentId = departmentId;
        viewModel.SearchText = "Nhân sự";
        Assert.True(await viewModel.LoadAsync("TECHNOLOGY_EDUCATION"));
        Assert.Single(viewModel.WorkingEmployees);
        Assert.Single(viewModel.MissingCv);
        Assert.Single(viewModel.Resigned);
        Assert.Single(viewModel.Blacklisted);
        Assert.Equal(3, viewModel.TotalCount);

        Assert.False(await viewModel.LoadAsync("TECHNOLOGY_EDUCATION"));
        Assert.Empty(viewModel.Profiles);
        Assert.False(viewModel.IsWorkingEmpty);
        Assert.Equal(0, viewModel.TotalCount);
    }

    private const string EmptyPage = "{\"success\":true,\"data\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":50}}";

    private static ApiClient.LoginResponse Session(string accessToken, string refreshToken) =>
        new(accessToken, refreshToken, DateTime.UtcNow.AddHours(1),
            new ApiClient.UserInfo(Guid.NewGuid(), "admin", "admin@example.test", "Admin", [], []));

    private static HttpResponseMessage JsonResponse<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TrackedContent(string value) : StringContent(value)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing) IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
