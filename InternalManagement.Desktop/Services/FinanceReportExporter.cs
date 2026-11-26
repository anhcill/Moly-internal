using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace InternalManagement.Desktop.Services;

public static class FinanceReportExporter
{
    private const string DefaultCompanyTitle = "MOLY INTERNAL MANAGEMENT";
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public static string BuildMonthlyPnlHtml(
        ApiClient.FinanceWorkspaceReportItem report,
        string? companyTitle = DefaultCompanyTitle)
    {
        ArgumentNullException.ThrowIfNull(report);

        var company = string.IsNullOrWhiteSpace(companyTitle) ? DefaultCompanyTitle : companyTitle.Trim();
        var asOf = report.AsOf == default ? DateTime.Now : ToDisplayTime(report.AsOf);
        var fromLocal = ToDisplayTime(report.From);
        var toLocal = ToDisplayTime(report.To);
        var docCode = $"BC-TC-{asOf:yyyyMMdd-HHmm}";
        var periodMeta = $"Số hiệu: <strong>{Enc(docCode)}</strong> &nbsp;|&nbsp; Kỳ báo cáo: {FormatDate(fromLocal)} – {FormatDate(toLocal)} &nbsp;|&nbsp; Cập nhật: {FormatDateTime(asOf)}";

        var body = new StringBuilder();

        if (report.Warnings is { Count: > 0 })
        {
            body.AppendLine("<div class=\"alert-box\"><strong>Lưu ý kiểm soát:</strong><ul>");
            foreach (var warning in report.Warnings)
                body.Append("<li>").Append(Enc(warning)).AppendLine("</li>");
            body.AppendLine("</ul></div>");
        }

        var profitClass = report.PeriodNetProfit >= 0 ? "val-pos" : "val-neg";
        var marginText = report.PeriodProfitMarginPercent.ToString("0.0", CultureInfo.InvariantCulture);

        body.AppendLine("<section class=\"kpi-grid\">");
        AppendKpiCard(body, "Tổng Thu", Money(report.PeriodTotalInflow), $"Quỹ hiện tại: {Money(report.CurrentCash)}", "val-pos");
        AppendKpiCard(body, "Tổng Chi", Money(report.PeriodTotalOutflow), $"Dự báo cuối kỳ: {Money(report.ForecastClosingCash)}", "val-neg");
        AppendKpiCard(body, "Lợi nhuận ròng", SignedMoney(report.PeriodNetProfit), $"Biên lãi: {marginText}%", profitClass);
        AppendKpiCard(body, "Công nợ & Còn trả GV", Money(report.StudentDebt), $"Còn trả GV: {Money(report.TeacherPayable)}", "val-warn");
        body.AppendLine("</section>");

        body.Append("<div class=\"words-line\"><strong>Lợi nhuận ròng toàn kỳ (bằng chữ):</strong> <em>")
            .Append(Enc(VietnameseNumberToWords.ToVndWords(report.PeriodNetProfit)))
            .AppendLine("</em></div>");

        if (report.Months.Count > 0)
        {
            body.AppendLine("<section class=\"section\">");
            body.AppendLine("<h2>Biểu đồ Thu – Chi theo tháng</h2>");
            body.AppendLine(BuildMonthlySvgChart(report.Months));
            body.AppendLine("</section>");
        }

        body.AppendLine("<section class=\"section\">");
        body.AppendLine("<h2>1. Dòng tiền &amp; Lãi/Lỗ theo tháng</h2>");
        body.AppendLine("<table><thead><tr>");
        body.AppendLine("<th>Tháng</th><th>Loại</th><th class=\"num\">Đầu kỳ</th><th class=\"num\">Tổng Thu</th><th class=\"num\">Tổng Chi</th><th class=\"num\">Lãi/Lỗ</th><th class=\"num\">Cuối kỳ</th>");
        body.AppendLine("</tr></thead><tbody>");

        if (report.Months.Count == 0)
        {
            body.AppendLine("<tr><td colspan=\"7\" class=\"empty\">Chưa có dữ liệu dòng tiền trong kỳ báo cáo.</td></tr>");
        }
        else
        {
            foreach (var month in report.Months)
            {
                var rowProfitClass = month.IsProfitable ? "num val-pos" : "num val-neg";
                body.Append("<tr>")
                    .Append("<td class=\"bold\">").Append(Enc(month.MonthLabel)).Append("</td>")
                    .Append("<td><span class=\"badge\">").Append(Enc(month.KindLabel)).Append("</span></td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(month.OpeningCash))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(month.TotalInflow))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(month.TotalOutflow))).Append("</td>")
                    .Append("<td class=\"").Append(rowProfitClass).Append("\">").Append(Enc(SignedMoney(month.NetProfit))).Append("</td>")
                    .Append("<td class=\"num bold\">").Append(Enc(Money(month.ClosingCash))).Append("</td>")
                    .AppendLine("</tr>");
            }
        }

        body.AppendLine("</tbody><tfoot><tr>");
        body.Append("<td colspan=\"3\">Tổng cộng toàn kỳ</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(report.PeriodTotalInflow))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(report.PeriodTotalOutflow))).Append("</td>")
            .Append("<td class=\"num ").Append(profitClass).Append("\">").Append(Enc(SignedMoney(report.PeriodNetProfit))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(report.ForecastClosingCash))).Append("</td>");
        body.AppendLine("</tr></tfoot></table></section>");

        var activeReceivables = report.Receivables
            .Where(r => !string.Equals(r.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) && r.OutstandingAmount > 0)
            .OrderByDescending(r => r.OutstandingAmount)
            .ToList();
        const int maxDetailRows = 15;
        var shownReceivables = activeReceivables.Take(maxDetailRows).ToList();
        var restReceivables = activeReceivables.Skip(maxDetailRows).ToList();

        body.AppendLine("<section class=\"section\">");
        body.Append("<h2>2. Công nợ học viên chưa thu (").Append(activeReceivables.Count).AppendLine(" học viên)</h2>");
        body.AppendLine("<table><thead><tr>");
        body.AppendLine("<th>Học viên</th><th>Lớp học</th><th class=\"num\">Học phí</th><th class=\"num\">Đã đóng ròng</th><th class=\"num\">Còn nợ</th><th>Hạn thu</th><th>Trạng thái</th>");
        body.AppendLine("</tr></thead><tbody>");

        if (activeReceivables.Count == 0)
        {
            body.AppendLine("<tr><td colspan=\"7\" class=\"empty\">Không có công nợ học viên tồn đọng.</td></tr>");
        }
        else
        {
            foreach (var r in shownReceivables)
            {
                var netPaid = r.PaidAmount - r.RefundedAmount;
                var dueText = r.LocalDueDate.HasValue ? FormatDate(r.LocalDueDate.Value) : "—";
                body.Append("<tr>")
                    .Append("<td class=\"bold\">").Append(Enc(r.StudentName)).Append("</td>")
                    .Append("<td>").Append(Enc(string.IsNullOrWhiteSpace(r.ClassName) ? "—" : r.ClassName)).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(r.PayableAmount))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(netPaid))).Append("</td>")
                    .Append("<td class=\"num val-neg bold\">").Append(Enc(Money(r.OutstandingAmount))).Append("</td>")
                    .Append("<td>").Append(Enc(dueText)).Append("</td>")
                    .Append("<td><span class=\"badge\">").Append(Enc(r.StatusLabel)).Append("</span></td>")
                    .AppendLine("</tr>");
            }
            if (restReceivables.Count > 0)
            {
                body.Append("<tr class=\"row-subtle\"><td colspan=\"2\"><em>Còn lại ").Append(restReceivables.Count).Append(" học viên khác (xem đầy đủ trong file Excel)</em></td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(restReceivables.Sum(x => x.PayableAmount)))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(restReceivables.Sum(x => x.PaidAmount - x.RefundedAmount)))).Append("</td>")
                    .Append("<td class=\"num val-neg bold\">").Append(Enc(Money(restReceivables.Sum(x => x.OutstandingAmount)))).Append("</td>")
                    .AppendLine("<td colspan=\"2\">—</td></tr>");
            }
        }
        body.AppendLine("</tbody><tfoot><tr>");
        body.Append("<td colspan=\"2\">Tổng công nợ toàn danh sách</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(activeReceivables.Sum(x => x.PayableAmount)))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(activeReceivables.Sum(x => x.PaidAmount - x.RefundedAmount)))).Append("</td>")
            .Append("<td class=\"num val-neg\">").Append(Enc(Money(activeReceivables.Sum(x => x.OutstandingAmount)))).Append("</td>")
            .AppendLine("<td colspan=\"2\"></td></tr></tfoot></table></section>");

        var allTeachers = report.TeacherCosts
            .OrderByDescending(t => t.UnpaidEarned)
            .ThenByDescending(t => t.EarnedAmount)
            .ToList();
        var shownTeachers = allTeachers.Take(maxDetailRows).ToList();
        var restTeachers = allTeachers.Skip(maxDetailRows).ToList();

        body.AppendLine("<section class=\"section\">");
        body.Append("<h2>3. Tổng hợp thù lao &amp; tạm ứng giáo viên (").Append(allTeachers.Count).AppendLine(" giáo viên)</h2>");
        body.AppendLine("<table><thead><tr>");
        body.AppendLine("<th>Giáo viên</th><th class=\"num\">Công thực tế ròng</th><th class=\"num\">Đã quyết toán</th><th class=\"num\">Ứng còn dư</th><th class=\"num\">Còn phải chuyển</th><th>Trạng thái</th>");
        body.AppendLine("</tr></thead><tbody>");

        if (allTeachers.Count == 0)
        {
            body.AppendLine("<tr><td colspan=\"6\" class=\"empty\">Chưa có dữ liệu thù lao giáo viên trong kỳ.</td></tr>");
        }
        else
        {
            foreach (var t in shownTeachers)
            {
                body.Append("<tr>")
                    .Append("<td class=\"bold\">").Append(Enc(t.TeacherName)).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(t.EarnedAmount))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(t.SettledAmount))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(t.RemainingAdvance))).Append("</td>")
                    .Append("<td class=\"num val-warn bold\">").Append(Enc(Money(t.UnpaidEarned))).Append("</td>")
                    .Append("<td><span class=\"badge\">").Append(Enc(t.PaymentStatusLabel)).Append("</span></td>")
                    .AppendLine("</tr>");
            }
            if (restTeachers.Count > 0)
            {
                body.Append("<tr class=\"row-subtle\"><td><em>Còn lại ").Append(restTeachers.Count).Append(" giáo viên khác</em></td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(restTeachers.Sum(x => x.EarnedAmount)))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(restTeachers.Sum(x => x.SettledAmount)))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(restTeachers.Sum(x => x.RemainingAdvance)))).Append("</td>")
                    .Append("<td class=\"num val-warn bold\">").Append(Enc(Money(restTeachers.Sum(x => x.UnpaidEarned)))).Append("</td>")
                    .AppendLine("<td>—</td></tr>");
            }
        }
        body.AppendLine("</tbody><tfoot><tr>");
        body.Append("<td>Tổng cộng toàn danh sách</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(allTeachers.Sum(x => x.EarnedAmount)))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(allTeachers.Sum(x => x.SettledAmount)))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(allTeachers.Sum(x => x.RemainingAdvance)))).Append("</td>")
            .Append("<td class=\"num val-warn\">").Append(Enc(Money(allTeachers.Sum(x => x.UnpaidEarned)))).Append("</td>")
            .AppendLine("<td></td></tr></tfoot></table></section>");

        return BuildDocument(
            "Báo cáo Tài chính & Lãi/Lỗ",
            company,
            "BÁO CÁO DÒNG TIỀN & KẾT QUẢ KINH DOANH (P&L)",
            periodMeta,
            body.ToString(),
            asOf,
            ("Người lập biểu", "(Ký, ghi rõ họ tên)"),
            ("Kế toán trưởng", "(Ký, ghi rõ họ tên)"),
            ("Giám đốc", "(Ký, đóng dấu)"));
    }

    public static string BuildTeacherStatementHtml(ApiClient.FinanceTeacherCostItem teacher, DateTime? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(teacher);

        var reportDate = asOf.HasValue ? ToDisplayTime(asOf.Value) : DateTime.Now;
        var docCode = $"PL-{reportDate:yyyyMMdd}-{Math.Abs(teacher.EmployeeId.GetHashCode()) % 10000:D4}";
        var lmsSuffix = teacher.TeacherLmsId.HasValue ? $" &nbsp;|&nbsp; Mã LMS: #{teacher.TeacherLmsId.Value}" : string.Empty;
        var periodMeta = $"Số phiếu: <strong>{Enc(docCode)}</strong> &nbsp;|&nbsp; Ngày lập: {FormatDateTime(reportDate)} &nbsp;|&nbsp; Trạng thái: {Enc(teacher.PaymentStatusLabel)}{lmsSuffix}";

        var body = new StringBuilder();

        body.AppendLine("<section class=\"highlight-banner\">");
        body.Append("<div><div class=\"hl-label\">Giáo viên</div><div class=\"hl-name\">")
            .Append(Enc(teacher.TeacherName))
            .AppendLine("</div></div>");
        body.Append("<div class=\"hl-right\"><div class=\"hl-label\">Tiền mặt còn phải chuyển</div><div class=\"hl-amount\">")
            .Append(Enc(Money(teacher.UnpaidEarned)))
            .AppendLine("</div></div>");
        body.AppendLine("</section>");

        body.Append("<div class=\"words-line\"><strong>Số tiền còn chuyển (bằng chữ):</strong> <em>")
            .Append(Enc(VietnameseNumberToWords.ToVndWords(teacher.UnpaidEarned)))
            .AppendLine("</em></div>");

        body.AppendLine("<section class=\"section\">");
        body.AppendLine("<h2>1. Bảng quyết toán thù lao &amp; tạm ứng</h2>");
        body.AppendLine("<table class=\"kv-table\"><thead><tr><th>Khoản mục</th><th class=\"num\">Số tiền</th></tr></thead><tbody>");
        AppendKvRow(body, "Tên giáo viên", Enc(teacher.TeacherName), isMoney: false);
        AppendKvRow(body, "Công buổi đã dạy", Money(teacher.GrossEarnedAmount));
        AppendKvRow(body, "Thưởng", Money(teacher.BonusAmount), valueClass: teacher.BonusAmount > 0 ? "val-pos" : null);
        AppendKvRow(body, "Khấu trừ", Money(teacher.DeductionAmount), valueClass: teacher.DeductionAmount > 0 ? "val-neg" : null);
        AppendKvRow(body, "Công thực tế ròng", Money(teacher.EarnedAmount), rowClass: "row-subtle", valueClass: "bold");
        AppendKvRow(body, "Đã quyết toán", Money(teacher.SettledAmount));
        AppendKvRow(body, "Đã ứng", Money(teacher.AdvancesIssued));
        AppendKvRow(body, "Đã hoàn", Money(teacher.AdvancesReturned));
        if (teacher.AdvancesApplied > 0)
            AppendKvRow(body, "Đã cấn trừ ứng", Money(teacher.AdvancesApplied));
        AppendKvRow(body, "Ứng còn dư", Money(teacher.RemainingAdvance), valueClass: teacher.RemainingAdvance > 0 ? "val-warn bold" : null);
        AppendKvRow(body, "Tiền mặt còn phải chuyển", Money(teacher.UnpaidEarned), rowClass: "row-highlight", valueClass: "val-pos bold");
        body.AppendLine("</tbody></table></section>");

        body.AppendLine("<section class=\"section\">");
        body.AppendLine("<h2>2. Sao kê chi tiết các khoản tạm ứng</h2>");
        if (teacher.Advances is { Count: > 0 })
        {
            body.AppendLine("<table><thead><tr>");
            body.AppendLine("<th>Ngày ứng</th><th>Diễn giải</th><th class=\"num\">Số tiền ứng</th><th class=\"num\">Đã hoàn</th><th class=\"num\">Đã cấn trừ</th><th class=\"num\">Còn dư</th>");
            body.AppendLine("</tr></thead><tbody>");
            foreach (var adv in teacher.Advances)
            {
                body.Append("<tr>")
                    .Append("<td>").Append(Enc(FormatDate(ToDisplayTime(adv.IssuedAt)))).Append("</td>")
                    .Append("<td>").Append(Enc(string.IsNullOrWhiteSpace(adv.Description) ? "Tạm ứng thù lao" : adv.Description)).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(adv.Amount))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(adv.ReturnedAmount))).Append("</td>")
                    .Append("<td class=\"num\">").Append(Enc(Money(adv.AppliedAmount))).Append("</td>")
                    .Append("<td class=\"num bold\">").Append(Enc(Money(adv.RemainingAmount))).Append("</td>")
                    .AppendLine("</tr>");
            }
            body.AppendLine("</tbody><tfoot><tr>");
            body.Append("<td colspan=\"2\">Tổng tạm ứng</td>")
                .Append("<td class=\"num\">").Append(Enc(Money(teacher.Advances.Sum(x => x.Amount)))).Append("</td>")
                .Append("<td class=\"num\">").Append(Enc(Money(teacher.Advances.Sum(x => x.ReturnedAmount)))).Append("</td>")
                .Append("<td class=\"num\">").Append(Enc(Money(teacher.Advances.Sum(x => x.AppliedAmount)))).Append("</td>")
                .Append("<td class=\"num\">").Append(Enc(Money(teacher.Advances.Sum(x => x.RemainingAmount)))).Append("</td>");
            body.AppendLine("</tr></tfoot></table>");
        }
        else
        {
            body.AppendLine("<p class=\"empty-note\">Không phát sinh khoản tạm ứng nào.</p>");
        }
        body.AppendLine("</section>");

        return BuildDocument(
            $"Quyết toán thù lao - {teacher.TeacherName}",
            DefaultCompanyTitle,
            "PHIẾU QUYẾT TOÁN THÙ LAO & SAO KÊ TẠM ỨNG GIÁO VIÊN",
            periodMeta,
            body.ToString(),
            reportDate,
            ("Giáo viên xác nhận", "(Ký, ghi rõ họ tên)"),
            ("Kế toán", "(Ký, ghi rõ họ tên)"),
            ("Giám đốc duyệt", "(Ký, đóng dấu)"));
    }

    public static string BuildStudentReceiptHtml(ApiClient.FinanceReceivableItem receivable, DateTime? asOf = null)
    {
        ArgumentNullException.ThrowIfNull(receivable);

        var reportDate = asOf.HasValue ? ToDisplayTime(asOf.Value) : DateTime.Now;
        var docCode = $"PT-{reportDate:yyyyMMdd}-{Math.Abs(receivable.Id.GetHashCode()) % 10000:D4}";
        var netPaid = receivable.PaidAmount - receivable.RefundedAmount;
        var dueText = receivable.LocalDueDate.HasValue ? FormatDate(receivable.LocalDueDate.Value) : "Không giới hạn";
        var classLabel = string.IsNullOrWhiteSpace(receivable.ClassName) ? "—" : receivable.ClassName;
        var periodMeta = $"Số phiếu: <strong>{Enc(docCode)}</strong> &nbsp;|&nbsp; Ngày lập: {FormatDateTime(reportDate)} &nbsp;|&nbsp; Trạng thái: {Enc(receivable.StatusLabel)}";

        var body = new StringBuilder();

        var debtAccent = receivable.OutstandingAmount > 0 ? "hl-debt" : "hl-paid";
        body.Append("<section class=\"highlight-banner ").Append(debtAccent).AppendLine("\">");
        body.Append("<div><div class=\"hl-label\">Học viên</div><div class=\"hl-name\">")
            .Append(Enc(receivable.StudentName))
            .Append("</div><div class=\"hl-sub\">Lớp học: ").Append(Enc(classLabel)).AppendLine("</div></div>");
        body.Append("<div class=\"hl-right\"><div class=\"hl-label\">Còn nợ</div><div class=\"hl-amount\">")
            .Append(Enc(Money(receivable.OutstandingAmount)))
            .AppendLine("</div></div>");
        body.AppendLine("</section>");

        body.Append("<div class=\"words-line\"><strong>Đã thu ròng (bằng chữ):</strong> <em>")
            .Append(Enc(VietnameseNumberToWords.ToVndWords(netPaid)))
            .Append("</em>");
        if (receivable.OutstandingAmount > 0)
        {
            body.Append("<br/><strong>Số tiền còn nợ (bằng chữ):</strong> <em>")
                .Append(Enc(VietnameseNumberToWords.ToVndWords(receivable.OutstandingAmount)))
                .Append("</em>");
        }
        body.AppendLine("</div>");

        body.AppendLine("<section class=\"section\">");
        body.AppendLine("<h2>Chi tiết công nợ học phí</h2>");
        body.AppendLine("<table class=\"kv-table\"><thead><tr><th>Thông tin</th><th class=\"num\">Giá trị</th></tr></thead><tbody>");
        AppendKvRow(body, "Học viên", Enc(receivable.StudentName), isMoney: false);
        AppendKvRow(body, "Lớp học", Enc(classLabel), isMoney: false);
        AppendKvRow(body, "Học phí cần thu", Money(receivable.PayableAmount));
        AppendKvRow(body, "Đã đóng ròng", Money(netPaid), valueClass: "val-pos bold");
        AppendKvRow(body, "Hoàn tiền", Money(receivable.RefundedAmount));
        AppendKvRow(
            body,
            "Còn nợ",
            Money(receivable.OutstandingAmount),
            rowClass: "row-highlight",
            valueClass: receivable.OutstandingAmount > 0 ? "val-neg bold" : "val-pos bold");
        AppendKvRow(body, "Hạn thu", Enc(dueText), isMoney: false);

        var statusHtml = Enc(receivable.StatusLabel);
        if (!string.IsNullOrWhiteSpace(receivable.CollectionAlert))
            statusHtml += $" <span class=\"badge badge-warn\">{Enc(receivable.CollectionAlert)}</span>";
        AppendKvRow(body, "Trạng thái", statusHtml, isMoney: false, rawHtml: true);
        body.AppendLine("</tbody></table></section>");

        return BuildDocument(
            $"Phiếu thu học phí - {receivable.StudentName}",
            DefaultCompanyTitle,
            "PHIẾU THU / SAO KÊ HỌC PHÍ HỌC VIÊN",
            periodMeta,
            body.ToString(),
            reportDate,
            ("Người nộp tiền", "(Ký, ghi rõ họ tên)"),
            ("Người lập phiếu", "(Ký, ghi rõ họ tên)"),
            ("Thủ quỹ / Kế toán", "(Ký, đóng dấu)"));
    }

    public static string BuildCompanyOverviewHtml(
        ApiClient.CompanyFinancialOverviewItem overview, DateTime? from, DateTime? to, string? companyTitle = DefaultCompanyTitle)
    {
        ArgumentNullException.ThrowIfNull(overview);
        var now = DateTime.Now;
        var company = string.IsNullOrWhiteSpace(companyTitle) ? DefaultCompanyTitle : companyTitle.Trim();
        var periodText = from.HasValue && to.HasValue ? $"{FormatDate(from.Value)} – {FormatDate(to.Value)}" : "Toàn thời gian";
        var periodMeta = $"Số hiệu: <strong>BC-MANG-{now:yyyyMMdd-HHmm}</strong> &nbsp;|&nbsp; Kỳ đối soát: {periodText} &nbsp;|&nbsp; Lập lúc: {FormatDateTime(now)}";

        var body = new StringBuilder();
        var profitClass = overview.CompanyTotal.OperatingProfit >= 0 ? "val-pos" : "val-neg";
        body.AppendLine("<section class=\"kpi-grid\">");
        AppendKpiCard(body, "Thực thu toàn công ty", Money(overview.CompanyTotal.TotalIncome), "Tổng thu hợp nhất", "val-pos");
        AppendKpiCard(body, "Thực chi toàn công ty", Money(overview.CompanyTotal.TotalExpense), "Tổng chi hợp nhất", "val-neg");
        AppendKpiCard(body, "Dòng tiền ròng", SignedMoney(overview.CompanyTotal.NetCashFlow), "Thu – Chi thực tế", overview.CompanyTotal.NetCashFlow >= 0 ? "val-pos" : "val-neg");
        AppendKpiCard(body, "Lợi nhuận hoạt động", SignedMoney(overview.CompanyTotal.OperatingProfit), $"{overview.Areas.Count} mảng hoạt động", profitClass);
        body.AppendLine("</section>");

        body.Append("<div class=\"words-line\"><strong>Lợi nhuận hoạt động (bằng chữ):</strong> <em>")
            .Append(Enc(VietnameseNumberToWords.ToVndWords(overview.CompanyTotal.OperatingProfit)))
            .AppendLine("</em></div>");

        body.AppendLine("<section class=\"section\"><h2>Chi tiết kết quả theo từng mảng</h2>");
        body.AppendLine("<table><thead><tr><th>Mảng hoạt động</th><th class=\"num\">Tổng Thu</th><th class=\"num\">Tổng Chi</th><th class=\"num\">Dòng tiền ròng</th><th class=\"num\">Lợi nhuận HĐ</th><th>Trạng thái</th><th class=\"num\">Số GD</th></tr></thead><tbody>");
        foreach (var a in overview.Areas)
        {
            body.Append("<tr>")
                .Append("<td class=\"bold\">").Append(Enc(a.AreaName)).Append("</td>")
                .Append("<td class=\"num val-pos\">").Append(Enc(Money(a.TotalIncome))).Append("</td>")
                .Append("<td class=\"num val-neg\">").Append(Enc(Money(a.TotalExpense))).Append("</td>")
                .Append("<td class=\"num bold\">").Append(Enc(SignedMoney(a.NetCashFlow))).Append("</td>")
                .Append("<td class=\"num bold\">").Append(Enc(SignedMoney(a.OperatingProfit))).Append("</td>")
                .Append("<td><span class=\"badge\">").Append(Enc(a.ProfitDataStatusName)).Append("</span></td>")
                .Append("<td class=\"num\">").Append(a.CashTransactionCount.ToString("N0", Vi)).Append("</td></tr>");
        }
        body.AppendLine("</tbody><tfoot><tr>")
            .Append("<td>Tổng hợp toàn công ty</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(overview.CompanyTotal.TotalIncome))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(Money(overview.CompanyTotal.TotalExpense))).Append("</td>")
            .Append("<td class=\"num\">").Append(Enc(SignedMoney(overview.CompanyTotal.NetCashFlow))).Append("</td>")
            .Append("<td class=\"num ").Append(profitClass).Append("\">").Append(Enc(SignedMoney(overview.CompanyTotal.OperatingProfit))).Append("</td>")
            .Append("<td></td><td class=\"num\">").Append(overview.Areas.Sum(x => x.CashTransactionCount).ToString("N0", Vi)).Append("</td>")
            .AppendLine("</tr></tfoot></table></section>");

        if (overview.HasUnclassifiedTransactions)
        {
            body.Append("<div class=\"alert-box\"><strong>Lưu ý giao dịch chưa phân loại:</strong> Còn ")
                .Append(overview.UnclassifiedCashFlow.TransactionCount.ToString("N0", Vi))
                .Append(" giao dịch chưa gán mảng (Thu: ").Append(Enc(Money(overview.UnclassifiedCashFlow.TotalIncome)))
                .Append(" · Chi: ").Append(Enc(Money(overview.UnclassifiedCashFlow.TotalExpense))).AppendLine(").</div>");
        }

        return BuildDocument("Báo cáo tài chính theo mảng", company, "BÁO CÁO TỔNG HỢP TÀI CHÍNH THEO MẢNG", periodMeta, body.ToString(), now,
            ("Người lập biểu", "(Ký, ghi rõ họ tên)"), ("Kế toán trưởng", "(Ký, ghi rõ họ tên)"), ("Giám đốc", "(Ký, đóng dấu)"));
    }

    public static string BuildCashAccountsAuditHtml(
        IReadOnlyList<ApiClient.CashAccountItem> accounts,
        IReadOnlyList<ApiClient.FinanceAuditEntryItem> entries,
        DateTime? from, DateTime? to, string? companyTitle = DefaultCompanyTitle)
    {
        var now = DateTime.Now;
        var company = string.IsNullOrWhiteSpace(companyTitle) ? DefaultCompanyTitle : companyTitle.Trim();
        var periodText = from.HasValue || to.HasValue
            ? $"{(from.HasValue ? FormatDate(from.Value) : "Đầu kỳ")} – {(to.HasValue ? FormatDate(to.Value) : "Hiện tại")}"
            : "Tất cả thời gian";
        var periodMeta = $"Số hiệu: <strong>BC-QUY-{now:yyyyMMdd-HHmm}</strong> &nbsp;|&nbsp; Phạm vi lọc: {periodText} &nbsp;|&nbsp; Lập lúc: {FormatDateTime(now)}";
        var totalBalance = accounts.Sum(x => x.CurrentBalance);

        var body = new StringBuilder();
        body.Append("<div class=\"words-line\"><strong>Tổng số dư toàn bộ quỹ &amp; ngân hàng:</strong> <strong class=\"val-pos\">")
            .Append(Enc(Money(totalBalance))).Append("</strong> — <em>").Append(Enc(VietnameseNumberToWords.ToVndWords(totalBalance))).AppendLine("</em></div>");

        body.AppendLine("<section class=\"section\"><h2>1. Số dư các Quỹ &amp; Tài khoản ngân hàng</h2>");
        body.AppendLine("<table><thead><tr><th>Mã quỹ</th><th>Tên quỹ / Tài khoản</th><th>Ngân hàng</th><th>Số tài khoản</th><th class=\"num\">Số dư hiện tại</th></tr></thead><tbody>");
        if (accounts.Count == 0)
            body.AppendLine("<tr><td colspan=\"5\" class=\"empty\">Chưa có tài khoản quỹ nào.</td></tr>");
        else
            foreach (var a in accounts)
            {
                body.Append("<tr><td class=\"bold\">").Append(Enc(a.Code)).Append("</td><td>").Append(Enc(a.Name))
                    .Append("</td><td>").Append(Enc(string.IsNullOrWhiteSpace(a.BankName) ? "Tiền mặt / Nội bộ" : a.BankName))
                    .Append("</td><td>").Append(Enc(string.IsNullOrWhiteSpace(a.AccountNumber) ? "—" : a.AccountNumber))
                    .Append("</td><td class=\"num bold ").Append(a.CurrentBalance >= 0 ? "val-pos" : "val-neg").Append("\">").Append(Enc(Money(a.CurrentBalance))).AppendLine("</td></tr>");
            }
        body.Append("</tbody><tfoot><tr><td colspan=\"4\">Tổng số dư quỹ</td><td class=\"num\">").Append(Enc(Money(totalBalance))).AppendLine("</td></tr></tfoot></table></section>");

        body.Append("<section class=\"section\"><h2>2. Nhật ký kiểm toán giao dịch (").Append(entries.Count).AppendLine(" bản ghi)</h2>");
        body.AppendLine("<table><thead><tr><th>Thời gian</th><th>Người thao tác</th><th>Nghiệp vụ</th><th class=\"num\">Số tiền</th><th>Nội dung</th></tr></thead><tbody>");
        if (entries.Count == 0)
            body.AppendLine("<tr><td colspan=\"5\" class=\"empty\">Không có bản ghi kiểm toán trong phạm vi lọc.</td></tr>");
        else
            foreach (var e in entries.Take(100))
            {
                body.Append("<tr><td>").Append(Enc(FormatDateTime(e.LocalOccurredAt))).Append("</td><td class=\"bold\">").Append(Enc(e.Actor))
                    .Append("</td><td>").Append(Enc(e.ActionLabel))
                    .Append("</td><td class=\"num bold ").Append(e.IsInflow ? "val-pos" : "val-neg").Append("\">").Append(Enc(e.SignedAmountDisplay))
                    .Append("</td><td>").Append(Enc(e.Description)).AppendLine("</td></tr>");
            }
        body.AppendLine("</tbody></table></section>");

        return BuildDocument("Báo cáo Quỹ & Kiểm toán", company, "BÁO CÁO SỐ DƯ QUỸ & NHẬT KÝ KIỂM TOÁN", periodMeta, body.ToString(), now,
            ("Thủ quỹ", "(Ký, ghi rõ họ tên)"), ("Kế toán trưởng", "(Ký, ghi rõ họ tên)"), ("Giám đốc", "(Ký, đóng dấu)"));
    }

    public static async Task<string> SaveAndOpenHtmlReportAsync(string fileNamePrefix, string htmlContent)
    {
        var prefix = SanitizePrefix(fileNamePrefix);
        var reportsDir = Path.Combine(Path.GetTempPath(), "MOLY_Reports");
        Directory.CreateDirectory(reportsDir);

        var filePath = Path.Combine(reportsDir, $"{prefix}-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        await File.WriteAllTextAsync(filePath, htmlContent ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
            .ConfigureAwait(false);

        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        return filePath;
    }

    private static string BuildMonthlySvgChart(IReadOnlyList<ApiClient.FinanceMonthlyRowItem> months)
    {
        const double width = 740, height = 165, padL = 56, padR = 16, padT = 18, padB = 28;
        var plotW = width - padL - padR;
        var plotH = height - padT - padB;
        var maxVal = months.SelectMany(m => new[] { m.TotalInflow, m.TotalOutflow }).DefaultIfEmpty(1m).Max();
        if (maxVal <= 0) maxVal = 1m;

        var sb = new StringBuilder();
        sb.Append(FormattableString.Invariant($"<svg viewBox=\"0 0 {width} {height}\" width=\"100%\" style=\"display:block;border:1px solid #e2e8f0;border-radius:8px;background:#f8fafc;\">"));
        for (var i = 0; i <= 3; i++)
        {
            var y = padT + plotH * i / 3.0;
            var val = maxVal * (3 - i) / 3m;
            sb.Append(FormattableString.Invariant($"<line x1=\"{padL:0.#}\" y1=\"{y:0.#}\" x2=\"{width - padR:0.#}\" y2=\"{y:0.#}\" stroke=\"#e2e8f0\" stroke-width=\"1\" />"));
            sb.Append(FormattableString.Invariant($"<text x=\"{padL - 6:0.#}\" y=\"{y + 3:0.#}\" text-anchor=\"end\" font-size=\"9.5\" fill=\"#64748b\">{Enc(CompactMoney(val))}</text>"));
        }

        var slot = plotW / Math.Max(1, months.Count);
        var barW = Math.Clamp(slot * 0.28, 6, 22);
        for (var i = 0; i < months.Count; i++)
        {
            var m = months[i];
            var cx = padL + slot * (i + 0.5);
            var hIn = (double)(m.TotalInflow / maxVal) * plotH;
            var hOut = (double)(m.TotalOutflow / maxVal) * plotH;
            sb.Append(FormattableString.Invariant($"<rect x=\"{cx - barW - 1.5:0.#}\" y=\"{padT + plotH - hIn:0.#}\" width=\"{barW:0.#}\" height=\"{Math.Max(1, hIn):0.#}\" rx=\"2.5\" fill=\"#10b981\" />"));
            sb.Append(FormattableString.Invariant($"<rect x=\"{cx + 1.5:0.#}\" y=\"{padT + plotH - hOut:0.#}\" width=\"{barW:0.#}\" height=\"{Math.Max(1, hOut):0.#}\" rx=\"2.5\" fill=\"#ef4444\" />"));
            sb.Append(FormattableString.Invariant($"<text x=\"{cx:0.#}\" y=\"{height - 9:0.#}\" text-anchor=\"middle\" font-size=\"10\" font-weight=\"600\" fill=\"#334155\">{Enc(m.MonthLabel)}</text>"));
        }

        sb.Append("<rect x=\"570\" y=\"5\" width=\"9\" height=\"9\" rx=\"2\" fill=\"#10b981\" /><text x=\"583\" y=\"13\" font-size=\"10\" fill=\"#334155\">Tổng Thu</text>");
        sb.Append("<rect x=\"648\" y=\"5\" width=\"9\" height=\"9\" rx=\"2\" fill=\"#ef4444\" /><text x=\"661\" y=\"13\" font-size=\"10\" fill=\"#334155\">Tổng Chi</text>");
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static string BuildDocument(
        string pageTitle, string companyTitle, string docSubtitle, string periodMeta, string bodyHtml, DateTime docDate,
        (string Title, string Note) sig1, (string Title, string Note) sig2, (string Title, string Note) sig3) => $$"""
<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>{{Enc(pageTitle)}}</title>
<style>
  @page { size: A4; margin: 12mm; }
  * { box-sizing: border-box; }
  body { margin: 0; padding: 20px; background: #f1f5f9; color: #0f172a; font-family: 'Segoe UI', Arial, sans-serif; font-size: 12.5px; line-height: 1.48; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
  .print-bar { max-width: 820px; margin: 0 auto 12px; display: flex; justify-content: space-between; align-items: center; gap: 12px; background: #fff; border: 1px solid #cbd5e1; border-radius: 10px; padding: 10px 14px; }
  .print-hint { font-size: 12px; color: #475569; }
  .print-btn { background: #1e3a8a; color: #fff; border: none; border-radius: 8px; padding: 9px 18px; font-size: 13px; font-weight: 600; cursor: pointer; }
  .print-btn:hover { background: #1d4ed8; }
  .sheet { max-width: 820px; margin: 0 auto; background: #fff; border: 1px solid #e2e8f0; border-radius: 10px; padding: 26px 30px; box-shadow: 0 8px 24px rgba(15,23,42,.06); }
  .header { display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 2px solid #0f172a; padding-bottom: 12px; margin-bottom: 16px; gap: 16px; }
  .brand { font-size: 11.5px; font-weight: 700; letter-spacing: .08em; color: #1d4ed8; text-transform: uppercase; }
  .doc-title { margin: 4px 0 6px; font-size: 18px; font-weight: 800; color: #0f172a; }
  .meta { font-size: 11.5px; color: #475569; }
  .logo-badge { background: #1e3a8a; color: #fff; font-weight: 800; font-size: 14px; letter-spacing: .12em; padding: 8px 14px; border-radius: 8px; }
  .alert-box { background: #fef3c7; border: 1px solid #f59e0b; color: #92400e; border-radius: 8px; padding: 9px 13px; margin-bottom: 14px; font-size: 12px; }
  .alert-box ul { margin: 4px 0 0 18px; padding: 0; }
  .words-line { background: #f8fafc; border-left: 3px solid #1d4ed8; padding: 8px 12px; border-radius: 0 6px 6px 0; margin-bottom: 16px; font-size: 12px; color: #1e293b; }
  .kpi-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 10px; margin-bottom: 14px; }
  .kpi-card { border: 1px solid #e2e8f0; border-radius: 8px; padding: 10px 11px; background: #f8fafc; }
  .kpi-label { font-size: 10.5px; text-transform: uppercase; letter-spacing: .04em; color: #64748b; font-weight: 700; }
  .kpi-value { font-size: 15.5px; font-weight: 800; margin: 4px 0 2px; }
  .kpi-sub { font-size: 11px; color: #475569; }
  .highlight-banner { display: flex; justify-content: space-between; align-items: center; padding: 14px 18px; border-radius: 10px; background: #eff6ff; border: 1px solid #bfdbfe; margin-bottom: 12px; gap: 16px; }
  .highlight-banner.hl-debt { background: #fef2f2; border-color: #fecaca; }
  .highlight-banner.hl-paid { background: #ecfdf5; border-color: #a7f3d0; }
  .hl-label { font-size: 10.5px; text-transform: uppercase; letter-spacing: .05em; color: #475569; font-weight: 700; }
  .hl-name { font-size: 17px; font-weight: 800; color: #0f172a; margin-top: 2px; }
  .hl-sub { font-size: 12px; color: #475569; margin-top: 2px; }
  .hl-right { text-align: right; }
  .hl-amount { font-size: 21px; font-weight: 800; color: #1d4ed8; margin-top: 2px; }
  .hl-debt .hl-amount { color: #b91c1c; }
  .hl-paid .hl-amount { color: #047857; }
  .section { margin-bottom: 16px; }
  .section h2 { font-size: 13.5px; font-weight: 700; color: #0f172a; margin: 0 0 8px; padding-left: 8px; border-left: 3px solid #1e3a8a; }
  table { width: 100%; border-collapse: collapse; font-size: 11.8px; }
  th, td { padding: 6.5px 8px; border-bottom: 1px solid #e2e8f0; text-align: left; vertical-align: middle; }
  thead th { background: #1e3a8a; color: #fff; font-weight: 700; }
  tfoot td { background: #eff6ff; font-weight: 700; border-top: 2px solid #1e3a8a; }
  .num { text-align: right; font-variant-numeric: tabular-nums; white-space: nowrap; }
  .bold { font-weight: 700; }
  .val-pos { color: #047857; }
  .val-neg { color: #b91c1c; }
  .val-warn { color: #b45309; }
  .row-subtle td { background: #f8fafc; }
  .row-highlight td { background: #eff6ff; font-size: 12.8px; border-top: 1px solid #93c5fd; border-bottom: 2px solid #1d4ed8; }
  .badge { display: inline-block; padding: 2px 7px; border-radius: 999px; font-size: 10.5px; font-weight: 600; background: #e2e8f0; color: #334155; }
  .badge-warn { background: #fee2e2; color: #b91c1c; }
  .empty, .empty-note { text-align: center; color: #64748b; font-style: italic; padding: 12px; }
  .sig-date { text-align: right; font-style: italic; font-size: 11.5px; color: #475569; margin: 22px 18px 8px 0; }
  .signatures { display: grid; grid-template-columns: repeat(3, 1fr); gap: 16px; text-align: center; page-break-inside: avoid; }
  .sig-title { font-weight: 700; font-size: 12px; color: #0f172a; text-transform: uppercase; }
  .sig-note { font-size: 11px; color: #64748b; font-style: italic; margin-top: 2px; }
  .sig-space { height: 60px; }
  @media print { body { background: #fff; padding: 0; } .print-bar { display: none !important; } .sheet { box-shadow: none; border: none; max-width: none; padding: 0; } thead { display: table-header-group; } tr { page-break-inside: avoid; } }
</style>
</head>
<body>
<div class="print-bar">
  <span class="print-hint">Mẹo: Bấm <strong>In / Lưu PDF</strong> → chọn đích <strong>Save as PDF</strong> để xuất file PDF chuẩn A4.</span>
  <button type="button" class="print-btn" onclick="window.print()">In / Lưu PDF (Ctrl+P)</button>
</div>
<main class="sheet">
  <header class="header">
    <div>
      <div class="brand">{{Enc(companyTitle)}}</div>
      <h1 class="doc-title">{{Enc(docSubtitle)}}</h1>
      <div class="meta">{{periodMeta}}</div>
    </div>
    <div class="logo-badge">MOLY</div>
  </header>
  {{bodyHtml}}
  <div class="sig-date">Ngày {{docDate.Day:D2}} tháng {{docDate.Month:D2}} năm {{docDate.Year}}</div>
  <section class="signatures">
    <div><div class="sig-title">{{Enc(sig1.Title)}}</div><div class="sig-note">{{Enc(sig1.Note)}}</div><div class="sig-space"></div></div>
    <div><div class="sig-title">{{Enc(sig2.Title)}}</div><div class="sig-note">{{Enc(sig2.Note)}}</div><div class="sig-space"></div></div>
    <div><div class="sig-title">{{Enc(sig3.Title)}}</div><div class="sig-note">{{Enc(sig3.Note)}}</div><div class="sig-space"></div></div>
  </section>
</main>
</body>
</html>
""";

    private static void AppendKpiCard(StringBuilder sb, string label, string value, string subText, string valueClass) =>
        sb.Append("<div class=\"kpi-card\"><div class=\"kpi-label\">").Append(Enc(label))
            .Append("</div><div class=\"kpi-value ").Append(valueClass).Append("\">").Append(Enc(value))
            .Append("</div><div class=\"kpi-sub\">").Append(Enc(subText)).AppendLine("</div></div>");

    private static void AppendKvRow(
        StringBuilder sb, string label, string value, bool isMoney = true, string? rowClass = null, string? valueClass = null, bool rawHtml = false)
    {
        var trAttr = string.IsNullOrEmpty(rowClass) ? string.Empty : $" class=\"{rowClass}\"";
        var tdClass = string.IsNullOrEmpty(valueClass) ? "num" : $"num {valueClass}";
        var content = rawHtml || !isMoney ? value : Enc(value);
        sb.Append("<tr").Append(trAttr).Append("><td>").Append(Enc(label)).Append("</td><td class=\"").Append(tdClass).Append("\">")
            .Append(content).AppendLine("</td></tr>");
    }

    private static string SanitizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return "moly-report";
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(prefix.Trim().Select(c => invalid.Contains(c) ? '-' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "moly-report" : cleaned;
    }

    private static DateTime ToDisplayTime(DateTime value) => value.Kind == DateTimeKind.Utc ? value.AddHours(7) : value;

    private static string Enc(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var encoded = WebUtility.HtmlEncode(value);
        return Regex.Replace(encoded, @"&#(19[2-9]|2[0-4]\d|25[0-5]);", match =>
        {
            var ch = (char)int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            return char.IsLetter(ch) ? ch.ToString() : match.Value;
        });
    }

    private static string Money(decimal value) => string.Format(Vi, "{0:N0} đ", value);
    private static string SignedMoney(decimal value) => value >= 0 ? string.Format(Vi, "+{0:N0} đ", value) : string.Format(Vi, "{0:N0} đ", value);
    private static string CompactMoney(decimal value) => value >= 1_000_000m ? $"{value / 1_000_000m:0.#}tr" : value >= 1_000m ? $"{value / 1_000m:0.#}k" : $"{value:0}";
    private static string FormatDate(DateTime value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string FormatDateTime(DateTime value) => value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
