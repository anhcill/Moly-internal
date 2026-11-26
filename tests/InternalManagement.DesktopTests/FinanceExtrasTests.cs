using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using InternalManagement.Desktop.Services;

namespace InternalManagement.DesktopTests;

public sealed class FinanceExtrasTests
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static string ViMoney(decimal v) => string.Format(Vi, "{0:N0} đ", v);
    private static string ViSigned(decimal v) => v >= 0 ? string.Format(Vi, "+{0:N0} đ", v) : string.Format(Vi, "{0:N0} đ", v);

    [Fact]
    public void BuildMonthlyPnlHtml_ContainsVietnameseHeadersNumbersSvgChartWordsAndEscapesHtml()
    {
        var report = CreateSampleWorkspaceReport();

        var html = FinanceReportExporter.BuildMonthlyPnlHtml(report, "MOLY <script>evil()</script>");

        Assert.Contains("In / Lưu PDF", html);
        Assert.Contains("window.print()", html);
        Assert.Contains("@media print", html);
        Assert.Contains("<svg viewBox=", html);
        Assert.Contains("Tổng Thu", html);
        Assert.Contains("Tổng Chi", html);
        Assert.Contains("Lợi nhuận ròng", html);
        Assert.Contains("Biên lãi: 18.8%", html);
        Assert.Contains("Mười lăm triệu đồng chẵn", html);
        Assert.Contains("Công nợ &amp; Còn trả GV", html);
        Assert.Contains("<th>Tháng</th>", html);
        Assert.Contains("<th>Loại</th>", html);
        Assert.Contains("Đầu kỳ", html);
        Assert.Contains("Lãi/Lỗ", html);
        Assert.Contains("Cuối kỳ", html);
        Assert.Contains("Người lập biểu", html);
        Assert.Contains("Kế toán trưởng", html);
        Assert.Contains("Giám đốc", html);
        Assert.Contains(ViMoney(80_000_000m), html);
        Assert.Contains(ViMoney(65_000_000m), html);
        Assert.Contains(ViSigned(15_000_000m), html);
        Assert.Contains(ViSigned(25_000_000m), html);
        Assert.Contains(ViSigned(-10_000_000m), html);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;alert(&#39;xss&#39;)&lt;/script&gt;", html);
        Assert.Contains("Cô Lan &amp; Cộng sự", html);
        Assert.Contains("MOLY &lt;script&gt;evil()&lt;/script&gt;", html);
    }

    [Fact]
    public void BuildTeacherStatementHtml_IncludesTeacherNameNetUnpaidWordsAndSignatories()
    {
        var advances = new[]
        {
            new ApiClient.FinanceTeacherAdvanceItem(
                Guid.NewGuid(), 3_000_000m, 1_000_000m, 500_000m, 1_500_000m,
                new DateTime(2026, 11, 5, 2, 0, 0, DateTimeKind.Utc), "Ứng tiền công đợt 1 <ưu tiên>")
        };
        var teacher = new ApiClient.FinanceTeacherCostItem(
            Guid.NewGuid(), "Thầy Trần Minh Đức",
            14_500_000m, 2_000_000m, 5_000_000m, 3_000_000m, 1_000_000m, 1_500_000m, 8_000_000m, advances, 500_000m)
        {
            GrossEarnedAmount = 14_000_000m,
            BonusAmount = 1_000_000m,
            DeductionAmount = 500_000m,
            PaymentStatus = "processing"
        };

        var html = FinanceReportExporter.BuildTeacherStatementHtml(teacher, new DateTime(2026, 11, 26, 9, 30, 0));

        Assert.Contains("Thầy Trần Minh Đức", html);
        Assert.Contains("Tiền mặt còn phải chuyển", html);
        Assert.Contains(ViMoney(8_000_000m), html);
        Assert.Contains("Tám triệu đồng chẵn", html);
        Assert.Contains("Giáo viên xác nhận", html);
        Assert.Contains("Giám đốc duyệt", html);
        Assert.Contains("Công buổi đã dạy", html);
        Assert.Contains(ViMoney(14_000_000m), html);
        Assert.Contains("Ứng tiền công đợt 1 &lt;ưu tiên&gt;", html);
        Assert.Contains("05/11/2026", html);
        Assert.Contains("Đang xử lý", html);
    }

    [Fact]
    public void BuildStudentReceiptHtml_IncludesStudentClassRemainingDebtWordsAndReceiptSignatories()
    {
        var receivable = new ApiClient.FinanceReceivableItem(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "Lê Thị Mai", "CSCA Intensive K09",
            15_000_000m, 10_000_000m, 1_000_000m, 6_000_000m,
            new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc), "Partial", "Đóng một phần");

        var html = FinanceReportExporter.BuildStudentReceiptHtml(receivable, new DateTime(2026, 11, 26, 14, 0, 0));

        Assert.Contains("Lê Thị Mai", html);
        Assert.Contains("CSCA Intensive K09", html);
        Assert.Contains(ViMoney(15_000_000m), html);
        Assert.Contains(ViMoney(9_000_000m), html);
        Assert.Contains(ViMoney(6_000_000m), html);
        Assert.Contains("Chín triệu đồng chẵn", html);
        Assert.Contains("Sáu triệu đồng chẵn", html);
        Assert.Contains("Người nộp tiền", html);
        Assert.Contains("Thủ quỹ / Kế toán", html);
        Assert.Contains("28/11/2026", html);
        Assert.Contains("Đóng một phần", html);
    }

    [Theory]
    [InlineData(0, "Không đồng")]
    [InlineData(15_000, "Mười lăm nghìn đồng chẵn")]
    [InlineData(21_000, "Hai mươi mốt nghìn đồng chẵn")]
    [InlineData(105_000, "Một trăm linh năm nghìn đồng chẵn")]
    [InlineData(3_500_000, "Ba triệu năm trăm nghìn đồng chẵn")]
    [InlineData(1_005_000, "Một triệu không trăm linh năm nghìn đồng chẵn")]
    [InlineData(1_250_500, "Một triệu hai trăm năm mươi nghìn năm trăm đồng")]
    [InlineData(-2_400_000, "Âm hai triệu bốn trăm nghìn đồng")]
    public void VietnameseNumberToWords_FormatsStandardAccountingPhrases(decimal amount, string expected)
    {
        Assert.Equal(expected, VietnameseNumberToWords.ToVndWords(amount));
    }

    [Fact]
    public void FinanceExcelExporter_BuildsValidMultiSheetWorkbooksWithSumFormulasAndLeadingZeros()
    {
        var report = CreateSampleWorkspaceReport();
        var accounts = new[]
        {
            new ApiClient.CashAccountItem(Guid.NewGuid(), Guid.NewGuid(), "VCB", "Tài khoản VCB", "001100999888", "Vietcombank", 95_000_000m, DateTime.UtcNow)
        };

        var workspaceBytes = FinanceExcelExporter.BuildFinanceWorkspaceWorkbook(report, accounts);
        AssertValidWorkbook(workspaceBytes, ["Tổng quan & P&L", "Lương & Ứng GV", "Công nợ Học viên", "Sổ Thu Chi", "Kế hoạch Thu Chi", "Quỹ & Tài khoản"], expectSumFormula: true);

        var students = new[]
        {
            new ClassTuitionExportRow("HV001", "Nguyễn An", "0947387277", "an@example.com", 3_000_000m, 200_000m, "Ưu đãi", 2_800_000m, 2_000_000m, 800_000m, "20/11/2026", "Đóng một phần", "30/11/2026", "Ghi chú")
        };
        var tuitionBytes = FinanceExcelExporter.BuildClassTuitionWorkbook("CSCA-01", "Toán CSCA", "Khóa CSCA", students);
        AssertValidWorkbook(tuitionBytes, ["Học phí lớp học"], expectSumFormula: true, requiredInlineText: "0947387277");

        var auditEntries = new[]
        {
            new ApiClient.FinanceAuditEntryItem(Guid.NewGuid(), DateTime.UtcNow, "Admin", "Income", "Phiếu thu", "Inflow", 5_000_000m, "Thu học phí", "Manual")
        };
        var auditBytes = FinanceExcelExporter.BuildCashAccountsAuditWorkbook(accounts, auditEntries, DateTime.Today.AddDays(-7), DateTime.Today);
        AssertValidWorkbook(auditBytes, ["Quỹ & Tài khoản", "Nhật ký Kiểm toán"], expectSumFormula: true);

        var auditHtml = FinanceReportExporter.BuildCashAccountsAuditHtml(accounts, auditEntries, DateTime.Today.AddDays(-7), DateTime.Today);
        Assert.Contains("Chín mươi lăm triệu đồng chẵn", auditHtml);
        Assert.Contains("Tài khoản VCB", auditHtml);
    }

    [Fact]
    public void DebtReminderHelper_Summarize_DistinguishesOverdueDueSoonFutureAndCancelled()
    {
        var now = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);
        var items = new[]
        {
            MakeReceivable("Quá hạn 1", 2_000_000m, now.AddDays(-5), "Open"),
            MakeReceivable("Quá hạn 2", 1_500_000m, now.AddMinutes(-1), "Partial"),
            MakeReceivable("Sắp hạn hôm nay", 3_000_000m, now, "Open"),
            MakeReceivable("Sắp hạn 3 ngày", 2_500_000m, now.AddDays(3), "Open"),
            MakeReceivable("Tương lai xa", 4_000_000m, now.AddDays(4), "Open"),
            MakeReceivable("Không hạn", 1_000_000m, null, "Open"),
            MakeReceivable("Đã hủy", 9_000_000m, now.AddDays(-2), "Cancelled"),
            MakeReceivable("Đã đóng đủ", 0m, now.AddDays(-3), "Paid")
        };

        var summary = DebtReminderHelper.Summarize(items, now);
        Assert.Equal(2, summary.OverdueCount);
        Assert.Equal(3_500_000m, summary.OverdueAmount);
        Assert.Equal(2, summary.DueSoonCount);
        Assert.Equal(5_500_000m, summary.DueSoonAmount);
        Assert.Equal(new DebtAlertSummary(0, 0m, 0, 0m), DebtReminderHelper.Summarize(null, now));
    }

    [Fact]
    public void DebtReminderHelper_BuildZaloReminderMessage_FormatsStudentClassAmountAndOverdueStatus()
    {
        var now = new DateTime(2026, 11, 26, 10, 0, 0, DateTimeKind.Utc);

        var overdue = MakeReceivable("Phạm Hoàng Nam", 4_500_000m, new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), "Open", "CSCA K10");
        var overdueMsg = DebtReminderHelper.BuildZaloReminderMessage(overdue, now);
        Assert.Contains("Phạm Hoàng Nam", overdueMsg);
        Assert.Contains("CSCA K10", overdueMsg);
        Assert.Contains($"{4_500_000m:N0} đ", overdueMsg);
        Assert.Contains("20/11/2026", overdueMsg);
        Assert.Contains("quá hạn", overdueMsg, StringComparison.OrdinalIgnoreCase);

        var upcoming = MakeReceivable("Đỗ Thu Hà", 2_200_000m, new DateTime(2026, 11, 28, 0, 0, 0, DateTimeKind.Utc), "Open", "CSCA K11");
        var upcomingMsg = DebtReminderHelper.BuildZaloReminderMessage(upcoming, now);
        Assert.Contains("Đỗ Thu Hà", upcomingMsg);
        Assert.Contains("CSCA K11", upcomingMsg);
        Assert.Contains("28/11/2026", upcomingMsg);
        Assert.DoesNotContain("quá hạn", upcomingMsg, StringComparison.OrdinalIgnoreCase);

        var noDue = MakeReceivable("Vũ Quốc Bảo", 1_800_000m, null, "Open", "CSCA K12");
        var noDueMsg = DebtReminderHelper.BuildZaloReminderMessage(noDue, now);
        Assert.Contains("Vũ Quốc Bảo", noDueMsg);
        Assert.DoesNotContain("quá hạn", noDueMsg, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertValidWorkbook(byte[] bytes, string[] expectedSheets, bool expectSumFormula, string? requiredInlineText = null)
    {
        Assert.NotEmpty(bytes);
        using var ms = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(ms, isEditable: false);

        var validator = new OpenXmlValidator();
        var errors = validator.Validate(doc).Select(e => $"{e.Part?.Uri}: {e.Description}").ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        var workbookPart = Assert.IsAssignableFrom<WorkbookPart>(doc.WorkbookPart);
        var sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>() ?? [];
        var sheetNames = sheets.Select(s => s.Name?.Value).ToArray();
        Assert.Equal(expectedSheets, sheetNames);

        var allCells = workbookPart.WorksheetParts
            .SelectMany(p => p.Worksheet?.Descendants<Cell>() ?? [])
            .ToList();

        if (expectSumFormula)
            Assert.Contains(allCells, c => c.CellFormula?.Text?.StartsWith("SUM(", StringComparison.Ordinal) == true);

        if (requiredInlineText is not null)
            Assert.Contains(allCells, c => c.DataType?.Value == CellValues.InlineString && c.InlineString?.Text?.Text == requiredInlineText);
    }

    private static ApiClient.FinanceWorkspaceReportItem CreateSampleWorkspaceReport()
    {
        var months = new[]
        {
            new ApiClient.FinanceMonthlyRowItem(
                new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), "Actual",
                100_000_000m, 60_000_000m, 35_000_000m, 55_000_000m, 20_000_000m, 5_000_000m, 15_000_000m, 125_000_000m),
            new ApiClient.FinanceMonthlyRowItem(
                new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "Forecast",
                125_000_000m, 20_000_000m, 30_000_000m, 18_000_000m, 15_000_000m, 2_000_000m, 15_000_000m, 115_000_000m)
        };
        var receivables = new[]
        {
            new ApiClient.FinanceReceivableItem(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "<script>alert('xss')</script> Nguyễn Văn A", "CSCA <K12>",
                12_000_000m, 7_000_000m, 0m, 5_000_000m,
                new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc), "Open", "Còn nợ")
        };
        var teachers = new[]
        {
            new ApiClient.FinanceTeacherCostItem(
                Guid.NewGuid(), "Cô Lan & Cộng sự",
                18_000_000m, 4_000_000m, 10_000_000m, 2_000_000m, 0m, 2_000_000m, 6_000_000m)
        };
        var movements = new[]
        {
            new ApiClient.FinanceCashMovementItem(Guid.NewGuid(), DateTime.UtcNow, "Income", "Thu học phí lớp CSCA", 7_000_000m, 0m, "CscaTuition", Guid.NewGuid())
        };
        return new ApiClient.FinanceWorkspaceReportItem(
            new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc),
            125_000_000m, 5_000_000m, 6_000_000m, 115_000_000m,
            months, receivables, teachers, [], movements)
        {
            AsOf = new DateTime(2026, 11, 26, 3, 0, 0, DateTimeKind.Utc)
        };
    }

    private static ApiClient.FinanceReceivableItem MakeReceivable(
        string studentName, decimal outstanding, DateTime? dueDate, string status, string? className = "CSCA K01") =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), studentName, className,
            10_000_000m, 10_000_000m - outstanding, 0m, outstanding, dueDate, status, status);
}
