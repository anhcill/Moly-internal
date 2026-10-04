using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class QuestionBankViewModel : ObservableViewModel
{
    private readonly ApiClient _apiClient;
    private IReadOnlyList<ApiClient.QuestionItem> _items = Array.Empty<ApiClient.QuestionItem>();
    private int _totalCount;

    public QuestionBankViewModel(ApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public IReadOnlyList<ApiClient.QuestionItem> Items
    {
        get => _items;
        private set => Set(ref _items, value);
    }

    public int TotalCount
    {
        get => _totalCount;
        private set => Set(ref _totalCount, value);
    }

    public async Task<bool> LoadAsync(string? search = null, CancellationToken ct = default)
    {
        var page = await _apiClient.GetQuestionsAsync(search, ct: ct);
        Items = page?.Items ?? Array.Empty<ApiClient.QuestionItem>();
        TotalCount = page?.TotalCount ?? 0;
        return page is not null;
    }

}
