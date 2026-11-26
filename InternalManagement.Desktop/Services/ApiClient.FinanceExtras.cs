using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record CashAccountItem(
        Guid Id,
        Guid CompanyId,
        string Code,
        string Name,
        string? AccountNumber,
        string? BankName,
        decimal CurrentBalance,
        DateTime CreatedAt);

    public sealed record FinanceAuditEntryItem(
        Guid Id,
        DateTime OccurredAt,
        string Actor,
        string ActionType,
        string ActionLabel,
        string Direction,
        decimal Amount,
        string Description,
        string ReferenceLabel)
    {
        public DateTime LocalOccurredAt => OccurredAt.AddHours(7);
        public bool IsInflow => string.Equals(Direction, "Inflow", StringComparison.OrdinalIgnoreCase);
        public string SignedAmountDisplay => IsInflow ? $"+{Amount:N0} đ" : $"-{Amount:N0} đ";
    }

    public async Task<IReadOnlyList<CashAccountItem>> GetCashAccountsAsync(CancellationToken ct = default)
    {
        using var response = await SendFinanceExtrasAsync(HttpMethod.Get, "api/v1/finance/cash-accounts", null, ct);
        await EnsureFinanceExtrasSuccessAsync(response, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<CashAccountItem>>>(_jsonOptions, ct);
        return envelope?.Data ?? [];
    }

    public async Task<CashAccountItem?> CreateCashAccountAsync(
        string code,
        string name,
        string? accountNumber,
        string? bankName,
        decimal openingBalance,
        CancellationToken ct = default)
    {
        var payload = new
        {
            Code = code,
            Name = name,
            AccountNumber = accountNumber,
            BankName = bankName,
            OpeningBalance = openingBalance
        };
        using var response = await SendFinanceExtrasAsync(HttpMethod.Post, "api/v1/finance/cash-accounts", payload, ct);
        await EnsureFinanceExtrasSuccessAsync(response, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CashAccountItem>>(_jsonOptions, ct);
        return envelope?.Data;
    }

    public async Task<bool> AdjustCashAccountAsync(
        Guid cashAccountId,
        string transactionType,
        decimal amount,
        DateTime transactionDateUtc,
        string description,
        CancellationToken ct = default)
    {
        var payload = new
        {
            TransactionType = transactionType,
            Amount = amount,
            TransactionDate = transactionDateUtc,
            CategoryId = (Guid?)null,
            BusinessUnitId = (Guid?)null,
            Description = description
        };
        using var response = await SendFinanceExtrasAsync(
            HttpMethod.Post,
            $"api/v1/finance/cash-accounts/{cashAccountId:D}/adjust",
            payload,
            ct);
        await EnsureFinanceExtrasSuccessAsync(response, ct);
        return true;
    }

    public async Task<IReadOnlyList<FinanceAuditEntryItem>> GetFinanceAuditTrailAsync(
        DateTime? from = null,
        DateTime? to = null,
        string? search = null,
        int take = 200,
        CancellationToken ct = default)
    {
        var queryParts = new List<string>
        {
            FormattableString.Invariant($"take={Math.Clamp(take, 1, 500)}")
        };
        if (from.HasValue)
            queryParts.Add($"from={Uri.EscapeDataString(from.Value.ToString("O", CultureInfo.InvariantCulture))}");
        if (to.HasValue)
            queryParts.Add($"to={Uri.EscapeDataString(to.Value.ToString("O", CultureInfo.InvariantCulture))}");
        if (!string.IsNullOrWhiteSpace(search))
            queryParts.Add($"search={Uri.EscapeDataString(search.Trim())}");

        var path = $"api/v1/finance/audit-trail?{string.Join("&", queryParts)}";
        using var response = await SendFinanceExtrasAsync(HttpMethod.Get, path, null, ct);
        await EnsureFinanceExtrasSuccessAsync(response, ct);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<FinanceAuditEntryItem>>>(_jsonOptions, ct);
        return envelope?.Data ?? [];
    }

    public async Task<bool> CreateFinanceLedgerTransactionWithAccountAsync(
        string transactionType,
        decimal amount,
        DateTime transactionDateUtc,
        string description,
        Guid? cashAccountId,
        string? referenceType = "Manual",
        CancellationToken ct = default)
    {
        string? effectiveReferenceType = null;
        Guid? effectiveReferenceId = null;
        if (!cashAccountId.HasValue && !string.IsNullOrWhiteSpace(referenceType))
        {
            effectiveReferenceType = referenceType.Trim();
            effectiveReferenceId = Guid.NewGuid();
        }

        var payload = new
        {
            TransactionType = transactionType,
            Amount = amount,
            TransactionDate = transactionDateUtc,
            CategoryId = (Guid?)null,
            BusinessUnitId = (Guid?)null,
            ReferenceType = effectiveReferenceType,
            ReferenceId = effectiveReferenceId,
            Description = description,
            CashAccountId = cashAccountId
        };

        using var response = await SendFinanceExtrasAsync(HttpMethod.Post, "api/v1/finance/transactions", payload, ct);
        await EnsureFinanceExtrasSuccessAsync(response, ct);
        return true;
    }

    private Task<HttpResponseMessage> SendFinanceExtrasAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct)
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

    private async Task EnsureFinanceExtrasSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var detail = await TryExtractErrorMessageAsync(response, ct);
        throw new InvalidOperationException(
            detail ?? $"Không thể thực hiện thao tác sổ quỹ / kiểm toán tài chính (HTTP {(int)response.StatusCode}).");
    }
}
