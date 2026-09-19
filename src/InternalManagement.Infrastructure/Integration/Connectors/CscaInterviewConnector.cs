using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Infrastructure.Integration.Connectors;

/// <summary>
/// Read-only connector tới CSCA-Interview trên Railway.
/// Endpoint integration cần X-Integration-Key; endpoint admin users có thể dùng BearerToken có quyền admin.
/// </summary>
public sealed class CscaInterviewConnector : RailwayHttpConnectorBase
{
    private readonly IConfiguration _configuration;

    public CscaInterviewConnector(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CscaInterviewConnector> logger)
        : base(
            httpClient,
            configuration,
            "CscaInterview",
            "https://api.molyinterview.online",
            logger)
    {
        _configuration = configuration;
    }

    public override string SourceSystem => "WEBSITE_INTERVIEW";

    public override IReadOnlyList<string> SupportedEntityTypes { get; } = ["InterviewCustomers"];

    protected override string BuildRequestPath(string entityType, SyncCursor cursor)
    {
        var path = GetConfiguredPath(
            _configuration,
            "CscaInterview",
            "CustomersPath",
            "/api/integrations/v1/customers");
        return AddPageQuery(path, cursor);
    }

    protected override IReadOnlyList<JsonElement> NormalizeItems(
        string entityType,
        IReadOnlyList<JsonElement> rawItems)
    {
        return rawItems
            .Select(NormalizeCustomer)
            .Where(item => !string.IsNullOrWhiteSpace(GetString(item, "id")))
            .ToList();
    }

    private static JsonElement NormalizeCustomer(JsonElement user)
    {
        var paidAmount = GetDecimal(user, "paidAmount", "paid_amount", "totalPaid", "total_paid");
        var createdAt = GetDateTime(user, "createdAt", "created_at");
        var updatedAt = GetDateTime(user, "updatedAt", "updated_at", "lastLoginAt", "last_login_at") ?? createdAt;
        var explicitStatus = GetString(user, "status", "paymentStatus", "payment_status");

        return ToJsonElement(new
        {
            id = GetString(user, "id", "userId", "user_id") ?? string.Empty,
            fullName = GetString(user, "fullName", "full_name", "name") ?? string.Empty,
            email = GetString(user, "email") ?? string.Empty,
            phoneNumber = GetString(user, "phoneNumber", "phone_number", "phone"),
            packageName = GetString(user, "packageName", "package_name", "plan", "subscriptionPlan", "subscription_plan") ?? "Interview",
            sessionCount = Math.Max(1, GetNestedInt(user, "_count", "interviewSessions", "interview_sessions")),
            paidAmount,
            status = explicitStatus ?? (paidAmount > 0 ? "Paid" : "Pending"),
            createdAt,
            updatedAt
        });
    }
}
