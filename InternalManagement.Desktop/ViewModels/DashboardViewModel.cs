using System.Globalization;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class DashboardViewModel : ObservableViewModel
{
    private readonly ApiClient _apiClient;
    private string _coursesCount = "—";
    private string _questionsCount = "—";
    private string _customersCount = "—";
    private string _syncHealthSummary = "Đang kiểm tra...";
    private string _deadLetterSummary = "Dead-Letters: đang kiểm tra...";
    private bool _isApiHealthy;

    public DashboardViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string CoursesCount { get => _coursesCount; set => Set(ref _coursesCount, value); }
    public string QuestionsCount { get => _questionsCount; set => Set(ref _questionsCount, value); }
    public string CustomersCount { get => _customersCount; set => Set(ref _customersCount, value); }
    public string SyncHealthSummary { get => _syncHealthSummary; private set => Set(ref _syncHealthSummary, value); }
    public string DeadLetterSummary { get => _deadLetterSummary; private set => Set(ref _deadLetterSummary, value); }
    public bool IsApiHealthy { get => _isApiHealthy; private set => Set(ref _isApiHealthy, value); }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        CoursesCount = QuestionsCount = CustomersCount = "Đang tải...";
        SyncHealthSummary = "Đang kiểm tra...";
        DeadLetterSummary = "Dead-Letters: đang kiểm tra...";

        var healthTask = _apiClient.CheckHealthAsync(ct);
        var syncRunsTask = _apiClient.GetSyncRunsAsync(ct);
        var deadLettersTask = _apiClient.GetDeadLettersAsync(resolved: false, ct: ct);
        var coursesTask = _apiClient.GetCoursesAsync(ct: ct);
        var questionsTask = _apiClient.GetQuestionsAsync(ct: ct);
        var customersTask = _apiClient.GetCustomersAsync(ct: ct);

        await Task.WhenAll(healthTask, syncRunsTask, deadLettersTask, coursesTask, questionsTask, customersTask);

        CoursesCount = coursesTask.Result?.TotalCount.ToString(CultureInfo.InvariantCulture) ?? "Không khả dụng";
        QuestionsCount = questionsTask.Result?.TotalCount.ToString(CultureInfo.InvariantCulture) ?? "Không khả dụng";
        CustomersCount = customersTask.Result?.TotalCount.ToString(CultureInfo.InvariantCulture) ?? "Không khả dụng";
        IsApiHealthy = healthTask.Result.IsHealthy;

        var syncRuns = syncRunsTask.Result;
        var deadLetters = deadLettersTask.Result;
        if (syncRuns is null || deadLetters is null)
        {
            SyncHealthSummary = "Không khả dụng";
            DeadLetterSummary = "Dead-Letters: không thể kiểm tra";
            return;
        }

        SyncHealthSummary = syncRuns.Items.Count == 0 ? "Chưa có lần chạy" : syncRuns.Items[0].StatusLabel;
        DeadLetterSummary = $"Dead-Letters: {deadLetters.TotalCount:N0} bản ghi chờ xử lý";
    }

}
