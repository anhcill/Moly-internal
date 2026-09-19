using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Infrastructure.Integration.Connectors;

/// <summary>
/// Read-only connector tới CSCA-MOLI.STUDIO trên Railway.
/// Các endpoint admin cần BearerToken; không đặt token trong source/appsettings.
/// </summary>
public sealed class CscaMoliStudioConnector : RailwayHttpConnectorBase
{
    private readonly IConfiguration _configuration;

    public CscaMoliStudioConnector(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CscaMoliStudioConnector> logger)
        : base(
            httpClient,
            configuration,
            "CscaMoliStudio",
            "https://csca-molistudio-production.up.railway.app",
            logger)
    {
        _configuration = configuration;
    }

    public override string SourceSystem => "CSCA_MOLI_STUDIO";

    public override IReadOnlyList<string> SupportedEntityTypes { get; } =
        ["Courses", "Questions", "Customers", "Subscriptions", "Payments"];

    protected override string BuildRequestPath(string entityType, SyncCursor cursor)
    {
        var path = entityType switch
        {
            "Courses" => Configured("CoursesPath", "/api/integrations/v1/courses"),
            "Questions" => Configured("QuestionsPath", "/api/integrations/v1/questions"),
            "Customers" => Configured("CustomersPath", "/api/integrations/v1/customers"),
            "Subscriptions" => Configured("SubscriptionsPath", "/api/integrations/v1/subscriptions"),
            "Payments" => RequiredConfigured("PaymentsPath"),
            _ => throw new InvalidOperationException($"Unsupported entity type: {entityType}")
        };

        return AddPageQuery(path, cursor);
    }

    protected override IReadOnlyList<JsonElement> NormalizeItems(
        string entityType,
        IReadOnlyList<JsonElement> rawItems)
    {
        return entityType switch
        {
            "Customers" => rawItems.Select(NormalizeCustomer).Where(IsValidRecord).ToList(),
            "Subscriptions" => rawItems.Select(NormalizeSubscription).Where(IsValidRecord).ToList(),
            "Questions" => NormalizeQuestions(rawItems),
            _ => rawItems
        };
    }

    private string Configured(string optionName, string defaultPath)
        => GetConfiguredPath(_configuration, "CscaMoliStudio", optionName, defaultPath);

    private string RequiredConfigured(string optionName)
    {
        var path = _configuration.GetSection("Integrations:CscaMoliStudio")[optionName];
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"CscaMoliStudio integration requires {optionName} for read-only payment synchronization.");

        return path;
    }

    private static JsonElement NormalizeCustomer(JsonElement user)
    {
        var id = GetString(user, "id", "userId", "user_id") ?? string.Empty;
        return ToJsonElement(new
        {
            id,
            fullName = GetString(user, "fullName", "full_name", "name") ?? string.Empty,
            email = GetString(user, "email") ?? string.Empty,
            phoneNumber = GetString(user, "phoneNumber", "phone_number", "phone"),
            updatedAt = GetDateTime(user, "updatedAt", "updated_at", "createdAt", "created_at")
        });
    }

    private static JsonElement NormalizeSubscription(JsonElement user)
    {
        var id = GetString(user, "id", "userId", "user_id") ?? string.Empty;
        var tier = GetString(user, "subscriptionTier", "subscription_tier", "tier") ?? string.Empty;
        var startsAt = GetDateTime(user, "subscriptionStartsAt", "subscription_starts_at", "createdAt", "created_at")
                       ?? DateTime.UtcNow;
        var expiresAt = GetDateTime(user, "vipExpiresAt", "vip_expires_at", "expiresAt", "expires_at")
                        ?? startsAt.AddYears(1);
        var isVip = GetBool(user, "isVip", "is_vip") || tier.Equals("vip", StringComparison.OrdinalIgnoreCase)
                    || tier.Equals("premium", StringComparison.OrdinalIgnoreCase);

        return ToJsonElement(new
        {
            id = $"user:{id}:subscription",
            customerId = id,
            packageName = string.IsNullOrWhiteSpace(tier) ? "basic" : tier,
            startsAt,
            expiresAt,
            status = isVip && expiresAt > DateTime.UtcNow ? "Active" : "Expired"
        });
    }

    private static List<JsonElement> NormalizeQuestions(IReadOnlyList<JsonElement> exams)
    {
        var output = new List<JsonElement>();
        foreach (var exam in exams)
        {
            var bankName = GetString(exam, "bankName", "title", "name", "examName", "exam_name") ?? "CSCA Questions";
            var subject = GetString(exam, "subject", "subjectName", "subject_name");

            if (TryGetArray(exam, out var questions, "questions", "items") && questions.Count > 0)
            {
                for (var index = 0; index < questions.Count; index++)
                {
                    var normalized = NormalizeQuestion(questions[index], bankName, subject, index);
                    if (normalized.HasValue)
                        output.Add(normalized.Value);
                }
            }
            else
            {
                var normalized = NormalizeQuestion(exam, bankName, subject, 0);
                if (normalized.HasValue)
                    output.Add(normalized.Value);
            }
        }

        return output;
    }

    private static JsonElement? NormalizeQuestion(JsonElement question, string bankName, string? subject, int index)
    {
        var id = GetString(question, "id", "questionId", "question_id");
        var content = GetString(question, "contentHtml", "content_html", "questionText", "question_text", "content", "text");
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(content))
            return null;

        var choices = new List<object>();
        if (TryGetArray(question, out var rawChoices, "choices", "options", "answers"))
        {
            for (var choiceIndex = 0; choiceIndex < rawChoices.Count; choiceIndex++)
            {
                var choice = rawChoices[choiceIndex];
                choices.Add(new
                {
                    label = GetString(choice, "label", "key", "option") ?? ((char)('A' + choiceIndex)).ToString(),
                    contentHtml = GetString(choice, "contentHtml", "content_html", "text", "value") ?? string.Empty,
                    isCorrect = GetBool(choice, "isCorrect", "is_correct", "correct"),
                    orderIndex = choiceIndex
                });
            }
        }

        return ToJsonElement(new
        {
            id,
            bankName,
            subject,
            topic = GetString(question, "topic", "topicName", "topic_name"),
            difficultyLevel = GetString(question, "difficultyLevel", "difficulty_level", "difficulty") ?? "Medium",
            contentHtml = content,
            explanationHtml = GetString(question, "explanationHtml", "explanation_html", "explanation"),
            choices,
            tags = new[] { "CSCA", $"source-index-{index}" }
        });
    }

    private static bool IsValidRecord(JsonElement item)
        => !string.IsNullOrWhiteSpace(GetString(item, "id"));
}
