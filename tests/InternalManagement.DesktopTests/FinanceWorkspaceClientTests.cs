using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class FinanceWorkspaceClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceivableUpdate_SendsDueDateAndCancellationToPutEndpoint(bool cancelled)
    {
        var receivableId = Guid.NewGuid();
        var dueDate = new DateTimeOffset(2026, 10, 30, 23, 59, 59, TimeSpan.FromHours(7)).AddTicks(9_999_999).UtcDateTime;
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal($"/api/v1/finance/workspace/receivables/{receivableId}", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(dueDate, json.RootElement.GetProperty("dueDate").GetDateTime());
            Assert.Equal(cancelled, json.RootElement.GetProperty("cancelled").GetBoolean());
            return Json("{\"success\":true}");
        })) { BaseAddress = new Uri("http://localhost/") };
        using var api = new ApiClient(http);
        Assert.True(await api.UpdateStudentReceivableAsync(receivableId, dueDate, cancelled));
    }

    [Fact]
    public void ReceivableAlerts_UseNetPaidAndOutstandingDebt()
    {
        var row = new ApiClient.FinanceReceivableItem(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Student", "Class", 100, 130, 10, 0, DateTime.UtcNow.AddDays(-1), "Open", "Đã thu đủ");
        Assert.Equal(20, row.OverpaidAmount);
        Assert.StartsWith("Thu thừa", row.CollectionAlert);
        Assert.Equal("Quá hạn", (row with { PaidAmount = 10, RefundedAmount = 0, OutstandingAmount = 90 }).CollectionAlert);
        Assert.Empty((row with { Status = "Cancelled" }).CollectionAlert);
    }

    [Fact]
    public async Task Report_UsesInvariantScenarioQueryAndReadsSplitFacts()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("vi-VN");
        try
        {
            using var http = new HttpClient(new Handler((request, _) =>
            {
                Assert.Contains("from=2026-10-01", request.RequestUri!.Query);
                Assert.Contains("costFactor=1.25", request.RequestUri.Query);
                return Task.FromResult(Json("""
                    {"success":true,"data":{"from":"2026-09-30T17:00:00Z","to":"2027-03-31T17:00:00Z","currentCash":42,"studentDebt":20,"teacherPayable":10,"forecastClosingCash":22,"months":[{"month":"2026-10-01T00:00:00Z","kind":"Mixed","openingCash":50,"totalInflow":20,"totalOutflow":48,"studentCollections":10,"teacherPayments":12,"plannedIncome":10,"plannedExpense":36,"closingCash":22,"actualInflow":4,"actualOutflow":12,"forecastInflow":16,"forecastOutflow":36}],"receivables":[],"teacherCosts":[],"plannedMovements":[],"cashMovements":[],"asOf":"2026-10-28T00:00:00Z","currentCashIsReconciled":false,"warnings":["Chưa đối soát"]}}
                    """));
            })) { BaseAddress = new Uri("http://localhost/") };
            using var api = new ApiClient(http);
            var report = await api.GetFinanceWorkspaceAsync(new DateTime(2026, 10, 1), costFactor: 1.25m);
            Assert.NotNull(report);
            Assert.Equal(16, Assert.Single(report.Months).ForecastInflow);
            Assert.Equal("Thực tế + dự báo", report.Months[0].KindLabel);
            Assert.Single(report.Warnings);
            Assert.False(report.CurrentCashIsReconciled);
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public async Task TeacherPayment_SendsCashAndAdvanceCreditWithStableKey()
    {
        var employeeId = Guid.NewGuid();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal("/api/v1/finance/workspace/teacher-payments", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(employeeId, json.RootElement.GetProperty("employeeId").GetGuid());
            Assert.Equal(0, json.RootElement.GetProperty("amount").GetDecimal());
            Assert.Equal(200, json.RootElement.GetProperty("applyAdvanceAmount").GetDecimal());
            Assert.Equal("retry-key", json.RootElement.GetProperty("idempotencyKey").GetString());
            return Json("{\"success\":true}");
        })) { BaseAddress = new Uri("http://localhost/") };
        using var api = new ApiClient(http);
        Assert.True(await api.PayTeacherAsync(employeeId, 0, DateTime.UtcNow, "Cấn trừ", "retry-key", 200));
    }

    [Fact]
    public async Task FailedPlan_ReportsBackendValidationAndSendsRecurrenceCount()
    {
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal("/api/v1/finance/workspace/planned-movements", request.RequestUri!.AbsolutePath);
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(6, json.RootElement.GetProperty("recurrenceCount").GetInt32());
            Assert.Equal("Expense", json.RootElement.GetProperty("transactionType").GetString());
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"success\":false,\"message\":\"Kỳ kế hoạch không hợp lệ\"}", Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("http://localhost/") };
        using var api = new ApiClient(http);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => api.CreateFinancePlanAsync(DateTime.UtcNow, "Expense", "Rent", 100, "Monthly", null, "key", 6));
        Assert.Contains("Kỳ kế hoạch không hợp lệ", error.Message);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => send(request, ct);
    }
}
