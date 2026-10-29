using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public async Task<FinanceWorkspaceReportItem?> GetFinanceWorkspaceAsync(
        DateTime fromMonth, int horizonMonths = 6, decimal collectionPercent = 85m,
        decimal costFactor = 1m, decimal openingCash = 0m, CancellationToken ct = default)
    {
        var query = FormattableString.Invariant($"from={fromMonth:yyyy-MM-dd}&horizonMonths={horizonMonths}&collectionPercent={collectionPercent}&costFactor={costFactor}&openingCash={openingCash}");
        using var response = await SendFinanceWorkspaceAsync(HttpMethod.Get, $"api/v1/finance/workspace?{query}", null, ct);
        await EnsureFinanceWorkspaceSuccessAsync(response, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<FinanceWorkspaceReportItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    private async Task<bool> FinanceWorkspacePostAsync(string path, object body, CancellationToken ct)
    {
        using var response = await SendFinanceWorkspaceAsync(HttpMethod.Post, path, body, ct);
        await EnsureFinanceWorkspaceSuccessAsync(response, ct);
        return true;
    }

    private Task<System.Net.Http.HttpResponseMessage> SendFinanceWorkspaceAsync(
        HttpMethod method, string path, object? body, CancellationToken ct)
    {
        return SendWithRefreshAsync(async () =>
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: _jsonOptions);
            }

            var cscaBusinessUnitId = CurrentSession?.User.AccessibleBusinessUnits?
                .FirstOrDefault(unit => string.Equals(unit.Code, "CSCA", StringComparison.OrdinalIgnoreCase))?.Id;
            if (cscaBusinessUnitId.HasValue)
            {
                request.Headers.Add("X-Business-Unit-ID", cscaBusinessUnitId.Value.ToString("D"));
            }

            return await _httpClient.SendAsync(request, ct);
        }, ct);
    }

    private async Task EnsureFinanceWorkspaceSuccessAsync(System.Net.Http.HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await TryExtractErrorMessageAsync(response, ct);
        throw new InvalidOperationException(detail ?? $"Không thể thực hiện thao tác tài chính (HTTP {(int)response.StatusCode}).");
    }

    public Task<bool> ReceiveStudentDebtAsync(Guid receivableId, decimal amount, DateTime occurredAt, string? notes, string idempotencyKey, CancellationToken ct = default)
        => FinanceWorkspacePostAsync($"api/v1/finance/workspace/receivables/{receivableId:D}/receipts", new { Amount = amount, ReceivedAt = occurredAt, Description = notes, IdempotencyKey = idempotencyKey }, ct);

    public Task<bool> RefundStudentDebtAsync(Guid receivableId, decimal amount, DateTime occurredAt, string? notes, string idempotencyKey, CancellationToken ct = default)
        => FinanceWorkspacePostAsync($"api/v1/finance/workspace/receivables/{receivableId:D}/refunds", new { Amount = amount, RefundedAt = occurredAt, Description = notes, IdempotencyKey = idempotencyKey }, ct);

    public async Task<bool> UpdateStudentReceivableAsync(Guid receivableId, DateTime? dueDate, bool cancelled = false, CancellationToken ct = default)
    {
        using var response = await SendFinanceWorkspaceAsync(
            HttpMethod.Put,
            $"api/v1/finance/workspace/receivables/{receivableId:D}",
            new { DueDate = dueDate, Cancelled = cancelled },
            ct);
        await EnsureFinanceWorkspaceSuccessAsync(response, ct);
        return true;
    }

    public Task<bool> IssueTeacherAdvanceAsync(Guid employeeId, decimal amount, DateTime occurredAt, string? notes, string idempotencyKey, CancellationToken ct = default)
        => FinanceWorkspacePostAsync("api/v1/finance/workspace/teacher-advances", new { EmployeeId = employeeId, Amount = amount, IssuedAt = occurredAt, Description = notes, IdempotencyKey = idempotencyKey }, ct);

    public Task<bool> ReturnTeacherAdvanceAsync(Guid advanceId, decimal amount, DateTime occurredAt, string? notes, string idempotencyKey, CancellationToken ct = default)
        => FinanceWorkspacePostAsync($"api/v1/finance/workspace/teacher-advances/{advanceId:D}/returns", new { Amount = amount, ReturnedAt = occurredAt, Description = notes, IdempotencyKey = idempotencyKey }, ct);

    public Task<bool> PayTeacherAsync(Guid employeeId, decimal amount, DateTime occurredAt, string? notes, string idempotencyKey, decimal applyAdvanceAmount = 0, CancellationToken ct = default)
        => FinanceWorkspacePostAsync("api/v1/finance/workspace/teacher-payments", new { EmployeeId = employeeId, Amount = amount, PaidAt = occurredAt, Description = notes, IdempotencyKey = idempotencyKey, ApplyAdvanceAmount = applyAdvanceAmount }, ct);

    public Task<bool> CreateFinancePlanAsync(DateTime plannedDate, string type, string title, decimal amount, string recurrence, string? notes, string idempotencyKey, int recurrenceCount = 1, CancellationToken ct = default)
        => FinanceWorkspacePostAsync("api/v1/finance/workspace/planned-movements", new { PlannedDate = plannedDate, TransactionType = type, Title = title, Amount = amount, Recurrence = recurrence, Notes = notes, IdempotencyKey = idempotencyKey, RecurrenceCount = recurrenceCount }, ct);

    public Task<bool> SettleFinancePlanAsync(Guid planId, CancellationToken ct = default)
        => FinanceWorkspacePostAsync($"api/v1/finance/workspace/planned-movements/{planId:D}/settle", new { }, ct);

    public Task<bool> CancelFinancePlanAsync(Guid planId, CancellationToken ct = default)
        => FinanceWorkspacePostAsync($"api/v1/finance/workspace/planned-movements/{planId:D}/cancel", new { }, ct);
}
