using System.Net;
using System.Text.RegularExpressions;
using InternalManagement.Desktop.Services;

namespace InternalManagement.Desktop.ViewModels;

public sealed class QuestionBankViewModel : ObservableViewModel
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
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
        Items = (page?.Items ?? Array.Empty<ApiClient.QuestionItem>())
            .Select(SanitizeForDisplay)
            .ToList();
        TotalCount = page?.TotalCount ?? 0;
        return page is not null;
    }

    private static ApiClient.QuestionItem SanitizeForDisplay(ApiClient.QuestionItem item) =>
        item with
        {
            ContentHtml = StripHtml(item.ContentHtml),
            ExplanationHtml = item.ExplanationHtml is null ? null : StripHtml(item.ExplanationHtml),
            Choices = item.Choices.Select(c => c with { ContentHtml = StripHtml(c.ContentHtml) }).ToList()
        };

    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var decoded = WebUtility.HtmlDecode(HtmlTagRegex.Replace(html, " "));
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

}
