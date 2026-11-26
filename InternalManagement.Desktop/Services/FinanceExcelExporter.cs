using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace InternalManagement.Desktop.Services;

public sealed record ClassTuitionExportRow(
    string StudentCode,
    string StudentName,
    string PhoneNumber,
    string Email,
    decimal TuitionFee,
    decimal DiscountAmount,
    string DiscountNote,
    decimal PayableAmount,
    decimal PaidAmount,
    decimal DebtAmount,
    string JoinedAtFormatted,
    string PaymentStatusLabel,
    string DebtDueDateFormatted,
    string Notes);

/// <summary>
/// Generates accounting-grade multi-sheet .xlsx Excel workbooks using OpenXML
/// with proper VND currency formatting, real SUM formulas, frozen headers,
/// AutoFilter, A4 print fit-to-width setup, and Vietnamese signatory blocks.
/// </summary>
public static class FinanceExcelExporter
{
    private const string DefaultCompany = "MOLY INTERNAL MANAGEMENT";

    private const uint StyleDefault = 0;
    private const uint StyleTitle = 1;
    private const uint StyleMeta = 2;
    private const uint StyleKpiLabel = 3;
    private const uint StyleKpiMoney = 4;
    private const uint StyleHeader = 5;
    private const uint StyleCurrency = 6;
    private const uint StylePercent = 7;
    private const uint StyleTotalLabel = 8;
    private const uint StyleTotalCurrency = 9;
    private const uint StyleText = 10;
    private const uint StyleIntCenter = 11;
    private const uint StyleTextCenter = 12;
    private const uint StyleSigTitle = 13;
    private const uint StyleSigNote = 14;
    private const uint StyleKpiWords = 15;

    public static byte[] BuildFinanceWorkspaceWorkbook(
        ApiClient.FinanceWorkspaceReportItem report,
        IReadOnlyList<ApiClient.CashAccountItem>? cashAccounts = null,
        string companyTitle = DefaultCompany)
    {
        ArgumentNullException.ThrowIfNull(report);
        var vnNow = DateTime.UtcNow.AddHours(7);
        var fromLocal = ToVnTime(report.From);
        var toLocal = ToVnTime(report.To);
        var meta = $"Kỳ báo cáo: {fromLocal:dd/MM/yyyy} – {toLocal:dd/MM/yyyy} · Xuất lúc: {vnNow:dd/MM/yyyy HH:mm} (GMT+7)";

        var sheets = new List<(string Name, Worksheet Sheet)>
        {
            ("Tổng quan & P&L", BuildPnlSheet(report, companyTitle, meta, vnNow)),
            ("Lương & Ứng GV", BuildTeacherSheet(report, companyTitle, meta, vnNow)),
            ("Công nợ Học viên", BuildReceivablesSheet(report, companyTitle, meta, vnNow)),
            ("Sổ Thu Chi", BuildCashMovementsSheet(report, companyTitle, meta, vnNow)),
            ("Kế hoạch Thu Chi", BuildPlansSheet(report, companyTitle, meta, vnNow))
        };

        if (cashAccounts is { Count: > 0 })
        {
            sheets.Add(("Quỹ & Tài khoản", BuildCashAccountsSheet(cashAccounts, companyTitle, meta, vnNow)));
        }

        return CreateWorkbook(sheets);
    }

    public static byte[] BuildCompanyOverviewWorkbook(
        ApiClient.CompanyFinancialOverviewItem overview,
        DateTime? from,
        DateTime? to,
        string companyTitle = DefaultCompany)
    {
        ArgumentNullException.ThrowIfNull(overview);
        var vnNow = DateTime.UtcNow.AddHours(7);
        var period = from.HasValue && to.HasValue
            ? $"{from.Value:dd/MM/yyyy} – {to.Value:dd/MM/yyyy}"
            : "Toàn thời gian";
        var meta = $"Kỳ đối soát: {period} · Xuất lúc: {vnNow:dd/MM/yyyy HH:mm} (GMT+7)";

        var b = new SheetBuilder([6, 28, 19, 19, 19, 19, 13, 22, 12]);
        b.AddTitleBlock("BÁO CÁO TỔNG HỢP TÀI CHÍNH THEO MẢNG HOẠT ĐỘNG", companyTitle, meta);

        b.AddRow([
            TextCell("TỔNG THU", StyleKpiLabel), NumCell(overview.CompanyTotal.TotalIncome, StyleKpiMoney), EmptyCell(),
            TextCell("TỔNG CHI", StyleKpiLabel), NumCell(overview.CompanyTotal.TotalExpense, StyleKpiMoney), EmptyCell(),
            TextCell("LỢI NHUẬN HĐ", StyleKpiLabel), NumCell(overview.CompanyTotal.OperatingProfit, StyleKpiMoney), EmptyCell()
        ], height: 24);
        b.AddMergedNoteRow($"Lợi nhuận hoạt động bằng chữ: {VietnameseNumberToWords.ToVndWords(overview.CompanyTotal.OperatingProfit)}", StyleKpiWords);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["STT", "Mảng hoạt động", "Tổng Thu", "Tổng Chi", "Dòng tiền ròng", "Lợi nhuận HĐ", "Biên lãi", "Trạng thái dữ liệu", "Số GD"]);
        var areas = overview.Areas ?? [];
        for (var i = 0; i < areas.Count; i++)
        {
            var a = areas[i];
            var margin = a.TotalIncome > 0 ? a.OperatingProfit / a.TotalIncome : 0m;
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(a.AreaName, StyleText),
                NumCell(a.TotalIncome, StyleCurrency),
                NumCell(a.TotalExpense, StyleCurrency),
                NumCell(a.NetCashFlow, StyleCurrency),
                NumCell(a.OperatingProfit, StyleCurrency),
                NumCell(margin, StylePercent),
                TextCell(a.ProfitDataStatusName, StyleTextCenter),
                NumCell(a.CashTransactionCount, StyleIntCenter)
            ]);
        }

        b.AddTotalRow(firstDataRow, areas.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [3] = (areas.Sum(x => x.TotalIncome), StyleTotalCurrency),
            [4] = (areas.Sum(x => x.TotalExpense), StyleTotalCurrency),
            [5] = (areas.Sum(x => x.NetCashFlow), StyleTotalCurrency),
            [6] = (areas.Sum(x => x.OperatingProfit), StyleTotalCurrency),
            [9] = (areas.Sum(x => x.CashTransactionCount), StyleIntCenter)
        });

        if (overview.HasUnclassifiedTransactions)
        {
            b.AddMergedNoteRow(
                $"Lưu ý: Còn {overview.UnclassifiedCashFlow.TransactionCount:N0} giao dịch chưa phân loại mảng (Thu: {overview.UnclassifiedCashFlow.TotalIncome:N0} đ · Chi: {overview.UnclassifiedCashFlow.TotalExpense:N0} đ).",
                StyleMeta);
        }

        b.AddSignatureBlock("NGƯỜI LẬP BIỂU", "KẾ TOÁN TRƯỞNG", "GIÁM ĐỐC", vnNow);
        return CreateWorkbook([("Tổng hợp theo mảng", b.Build())]);
    }

    public static byte[] BuildCashAccountsAuditWorkbook(
        IReadOnlyList<ApiClient.CashAccountItem> accounts,
        IReadOnlyList<ApiClient.FinanceAuditEntryItem> auditEntries,
        DateTime? from,
        DateTime? to,
        string companyTitle = DefaultCompany)
    {
        var vnNow = DateTime.UtcNow.AddHours(7);
        var period = from.HasValue || to.HasValue
            ? $"Lọc từ {(from.HasValue ? from.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "đầu")} đến {(to.HasValue ? to.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "hiện tại")}"
            : "Tất cả thời gian";
        var meta = $"{period} · Xuất lúc: {vnNow:dd/MM/yyyy HH:mm} (GMT+7)";

        var sheets = new List<(string Name, Worksheet Sheet)>
        {
            ("Quỹ & Tài khoản", BuildCashAccountsSheet(accounts, companyTitle, meta, vnNow)),
            ("Nhật ký Kiểm toán", BuildAuditSheet(auditEntries, companyTitle, meta, vnNow))
        };
        return CreateWorkbook(sheets);
    }

    public static byte[] BuildClassTuitionWorkbook(
        string classCode,
        string className,
        string courseName,
        IReadOnlyList<ClassTuitionExportRow> students,
        string companyTitle = DefaultCompany)
    {
        var vnNow = DateTime.UtcNow.AddHours(7);
        var meta = $"Lớp: {classCode} – {className} ({courseName}) · Xuất lúc: {vnNow:dd/MM/yyyy HH:mm} (GMT+7)";

        var b = new SheetBuilder([6, 14, 24, 15, 24, 16, 15, 18, 16, 16, 16, 15, 15, 13, 22]);
        b.AddTitleBlock("BẢNG THEO DÕI HỌC PHÍ & CÔNG NỢ LỚP HỌC", companyTitle, meta);

        var totalPayable = students.Sum(x => x.PayableAmount);
        var totalPaid = students.Sum(x => x.PaidAmount);
        var totalDebt = students.Sum(x => x.DebtAmount);
        var rate = totalPayable > 0 ? totalPaid / totalPayable : 0m;

        b.AddRow([
            TextCell("SĨ SỐ", StyleKpiLabel), NumCell(students.Count, StyleIntCenter), EmptyCell(),
            TextCell("CẦN THU", StyleKpiLabel), NumCell(totalPayable, StyleKpiMoney), EmptyCell(),
            TextCell("ĐÃ THU", StyleKpiLabel), NumCell(totalPaid, StyleKpiMoney), EmptyCell(),
            TextCell("CÒN NỢ", StyleKpiLabel), NumCell(totalDebt, StyleKpiMoney), EmptyCell(),
            TextCell("TỶ LỆ THU", StyleKpiLabel), NumCell(rate, StylePercent), EmptyCell()
        ], height: 24);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow([
            "STT", "Mã HV", "Họ và tên", "Điện thoại", "Email", "Học phí gốc", "Giảm giá", "Lý do giảm giá",
            "Cần thu", "Đã đóng", "Còn nợ", "Ngày vào lớp", "Trạng thái", "Hẹn trả nợ", "Ghi chú"
        ]);

        for (var i = 0; i < students.Count; i++)
        {
            var s = students[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(s.StudentCode, StyleTextCenter),
                TextCell(s.StudentName, StyleText),
                TextCell(s.PhoneNumber, StyleTextCenter),
                TextCell(s.Email, StyleText),
                NumCell(s.TuitionFee, StyleCurrency),
                NumCell(s.DiscountAmount, StyleCurrency),
                TextCell(s.DiscountNote, StyleText),
                NumCell(s.PayableAmount, StyleCurrency),
                NumCell(s.PaidAmount, StyleCurrency),
                NumCell(s.DebtAmount, StyleCurrency),
                TextCell(s.JoinedAtFormatted, StyleTextCenter),
                TextCell(s.PaymentStatusLabel, StyleTextCenter),
                TextCell(s.DebtDueDateFormatted, StyleTextCenter),
                TextCell(s.Notes, StyleText)
            ]);
        }

        b.AddTotalRow(firstDataRow, students.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [6] = (students.Sum(x => x.TuitionFee), StyleTotalCurrency),
            [7] = (students.Sum(x => x.DiscountAmount), StyleTotalCurrency),
            [9] = (totalPayable, StyleTotalCurrency),
            [10] = (totalPaid, StyleTotalCurrency),
            [11] = (totalDebt, StyleTotalCurrency)
        });

        b.AddSignatureBlock("NGƯỜI LẬP BIỂU", "KẾ TOÁN", "QUẢN LÝ ĐÀO TẠO", vnNow);
        return CreateWorkbook([("Học phí lớp học", b.Build())]);
    }

    private static Worksheet BuildPnlSheet(
        ApiClient.FinanceWorkspaceReportItem report, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([12, 18, 18, 18, 18, 18, 18, 18, 18]);
        b.AddTitleBlock("BÁO CÁO DÒNG TIỀN & KẾT QUẢ KINH DOANH (P&L)", companyTitle, meta);

        b.AddRow([
            TextCell("TỔNG THU", StyleKpiLabel), NumCell(report.PeriodTotalInflow, StyleKpiMoney), EmptyCell(),
            TextCell("TỔNG CHI", StyleKpiLabel), NumCell(report.PeriodTotalOutflow, StyleKpiMoney), EmptyCell(),
            TextCell("LỢI NHUẬN RÒNG", StyleKpiLabel), NumCell(report.PeriodNetProfit, StyleKpiMoney), NumCell(report.PeriodProfitMarginPercent / 100m, StylePercent)
        ], height: 24);
        b.AddRow([
            TextCell("QUỸ HIỆN TẠI", StyleKpiLabel), NumCell(report.CurrentCash, StyleKpiMoney), EmptyCell(),
            TextCell("CÔNG NỢ HV", StyleKpiLabel), NumCell(report.StudentDebt, StyleKpiMoney), EmptyCell(),
            TextCell("CÒN TRẢ GV", StyleKpiLabel), NumCell(report.TeacherPayable, StyleKpiMoney), EmptyCell()
        ], height: 24);
        b.AddMergedNoteRow($"Lợi nhuận ròng bằng chữ: {VietnameseNumberToWords.ToVndWords(report.PeriodNetProfit)}", StyleKpiWords);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["Tháng", "Loại dữ liệu", "Số dư đầu kỳ", "Tổng Thu", "Tổng Chi", "Lãi / Lỗ tháng", "Thu học viên", "Chi giáo viên", "Số dư cuối kỳ"]);
        foreach (var m in report.Months)
        {
            b.AddRow([
                TextCell(m.MonthLabel, StyleTextCenter),
                TextCell(m.KindLabel, StyleTextCenter),
                NumCell(m.OpeningCash, StyleCurrency),
                NumCell(m.TotalInflow, StyleCurrency),
                NumCell(m.TotalOutflow, StyleCurrency),
                NumCell(m.NetProfit, StyleCurrency),
                NumCell(m.StudentCollections, StyleCurrency),
                NumCell(m.TeacherPayments, StyleCurrency),
                NumCell(m.ClosingCash, StyleCurrency)
            ]);
        }

        b.AddTotalRow(firstDataRow, report.Months.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [4] = (report.PeriodTotalInflow, StyleTotalCurrency),
            [5] = (report.PeriodTotalOutflow, StyleTotalCurrency),
            [6] = (report.PeriodNetProfit, StyleTotalCurrency),
            [7] = (report.Months.Sum(x => x.StudentCollections), StyleTotalCurrency),
            [8] = (report.Months.Sum(x => x.TeacherPayments), StyleTotalCurrency)
        }, label: "TỔNG CỘNG TOÀN KỲ");

        b.AddSignatureBlock("NGƯỜI LẬP BIỂU", "KẾ TOÁN TRƯỞNG", "GIÁM ĐỐC", vnNow);
        return b.Build();
    }

    private static Worksheet BuildTeacherSheet(
        ApiClient.FinanceWorkspaceReportItem report, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([6, 26, 17, 15, 15, 17, 17, 16, 16, 16, 18, 16]);
        b.AddTitleBlock("BẢNG TỔNG HỢP THÙ LAO, TẠM ỨNG & QUYẾT TOÁN GIÁO VIÊN", companyTitle, meta);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow([
            "STT", "Giáo viên", "Công buổi dạy", "Thưởng", "Khấu trừ", "Công thực tế",
            "Đã quyết toán", "Đã ứng", "Đã hoàn ứng", "Ứng còn dư", "Còn phải chuyển", "Trạng thái"
        ]);
        var rows = report.TeacherCosts.OrderByDescending(x => x.UnpaidEarned).ThenBy(x => x.TeacherName).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var t = rows[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(t.TeacherName, StyleText),
                NumCell(t.GrossEarnedAmount, StyleCurrency),
                NumCell(t.BonusAmount, StyleCurrency),
                NumCell(t.DeductionAmount, StyleCurrency),
                NumCell(t.EarnedAmount, StyleCurrency),
                NumCell(t.SettledAmount, StyleCurrency),
                NumCell(t.AdvancesIssued, StyleCurrency),
                NumCell(t.AdvancesReturned, StyleCurrency),
                NumCell(t.RemainingAdvance, StyleCurrency),
                NumCell(t.UnpaidEarned, StyleCurrency),
                TextCell(t.PaymentStatusLabel, StyleTextCenter)
            ]);
        }

        b.AddTotalRow(firstDataRow, rows.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [3] = (rows.Sum(x => x.GrossEarnedAmount), StyleTotalCurrency),
            [4] = (rows.Sum(x => x.BonusAmount), StyleTotalCurrency),
            [5] = (rows.Sum(x => x.DeductionAmount), StyleTotalCurrency),
            [6] = (rows.Sum(x => x.EarnedAmount), StyleTotalCurrency),
            [7] = (rows.Sum(x => x.SettledAmount), StyleTotalCurrency),
            [8] = (rows.Sum(x => x.AdvancesIssued), StyleTotalCurrency),
            [9] = (rows.Sum(x => x.AdvancesReturned), StyleTotalCurrency),
            [10] = (rows.Sum(x => x.RemainingAdvance), StyleTotalCurrency),
            [11] = (rows.Sum(x => x.UnpaidEarned), StyleTotalCurrency)
        });

        b.AddSignatureBlock("NGƯỜI LẬP BIỂU", "KẾ TOÁN", "GIÁM ĐỐC DUYỆT", vnNow);
        return b.Build();
    }

    private static Worksheet BuildReceivablesSheet(
        ApiClient.FinanceWorkspaceReportItem report, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([6, 26, 22, 17, 17, 16, 17, 14, 15, 16]);
        b.AddTitleBlock("BẢNG CHI TIẾT CÔNG NỢ HỌC PHÍ HỌC VIÊN", companyTitle, meta);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["STT", "Học viên", "Lớp học", "Học phí cần thu", "Đã đóng ròng", "Hoàn tiền", "Còn nợ", "Hạn thu", "Trạng thái", "Cảnh báo"]);
        var rows = report.Receivables.OrderByDescending(x => x.OutstandingAmount).ThenBy(x => x.StudentName).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(r.StudentName, StyleText),
                TextCell(r.ClassName ?? "—", StyleText),
                NumCell(r.PayableAmount, StyleCurrency),
                NumCell(r.PaidAmount - r.RefundedAmount, StyleCurrency),
                NumCell(r.RefundedAmount, StyleCurrency),
                NumCell(r.OutstandingAmount, StyleCurrency),
                TextCell(r.LocalDueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—", StyleTextCenter),
                TextCell(r.StatusLabel, StyleTextCenter),
                TextCell(r.CollectionAlert, StyleTextCenter)
            ]);
        }

        b.AddTotalRow(firstDataRow, rows.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [4] = (rows.Sum(x => x.PayableAmount), StyleTotalCurrency),
            [5] = (rows.Sum(x => x.PaidAmount - x.RefundedAmount), StyleTotalCurrency),
            [6] = (rows.Sum(x => x.RefundedAmount), StyleTotalCurrency),
            [7] = (rows.Sum(x => x.OutstandingAmount), StyleTotalCurrency)
        });

        b.AddSignatureBlock("NGƯỜI LẬP BIỂU", "KẾ TOÁN CÔNG NỢ", "GIÁM ĐỐC", vnNow);
        return b.Build();
    }

    private static Worksheet BuildCashMovementsSheet(
        ApiClient.FinanceWorkspaceReportItem report, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([6, 18, 10, 40, 18, 18, 24]);
        b.AddTitleBlock("SỔ NHẬT KÝ THU CHI TIỀN MẶT & NGÂN HÀNG", companyTitle, meta);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["STT", "Thời gian (GMT+7)", "Loại", "Nội dung diễn giải", "Tiền Thu", "Tiền Chi", "Nguồn / Tham chiếu"]);
        var rows = (report.CashMovements ?? []).OrderByDescending(x => x.OccurredAt).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(m.LocalOccurredAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), StyleTextCenter),
                TextCell(m.TypeLabel, StyleTextCenter),
                TextCell(m.Description, StyleText),
                NumCell(m.Inflow, StyleCurrency),
                NumCell(m.Outflow, StyleCurrency),
                TextCell(m.ReferenceLabel, StyleText)
            ]);
        }

        b.AddTotalRow(firstDataRow, rows.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [5] = (rows.Sum(x => x.Inflow), StyleTotalCurrency),
            [6] = (rows.Sum(x => x.Outflow), StyleTotalCurrency)
        });

        b.AddSignatureBlock("NGƯỜI LẬP SỔ", "THỦ QUỸ", "KẾ TOÁN TRƯỞNG", vnNow);
        return b.Build();
    }

    private static Worksheet BuildPlansSheet(
        ApiClient.FinanceWorkspaceReportItem report, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([6, 15, 10, 34, 18, 15, 15, 28]);
        b.AddTitleBlock("KẾ HOẠCH THU CHI DỰ KIẾN", companyTitle, meta);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["STT", "Ngày dự kiến", "Loại", "Nội dung", "Số tiền", "Lặp lại", "Trạng thái", "Ghi chú"]);
        var rows = report.PlannedMovements.OrderBy(x => x.PlannedDate).ToList();
        for (var i = 0; i < rows.Count; i++)
        {
            var p = rows[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(p.LocalPlannedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), StyleTextCenter),
                TextCell(p.TypeLabel, StyleTextCenter),
                TextCell(p.Title, StyleText),
                NumCell(p.Amount, StyleCurrency),
                TextCell(p.RecurrenceLabel, StyleTextCenter),
                TextCell(p.StatusLabel, StyleTextCenter),
                TextCell(p.Notes ?? string.Empty, StyleText)
            ]);
        }

        b.AddTotalRow(firstDataRow, rows.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [5] = (rows.Sum(x => x.Amount), StyleTotalCurrency)
        });

        b.AddSignatureBlock("NGƯỜI LẬP KẾ HOẠCH", "KẾ TOÁN", "GIÁM ĐỐC", vnNow);
        return b.Build();
    }

    private static Worksheet BuildCashAccountsSheet(
        IReadOnlyList<ApiClient.CashAccountItem> accounts, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([6, 14, 28, 22, 22, 20]);
        b.AddTitleBlock("BẢNG TỔNG HỢP SỐ DƯ QUỸ & TÀI KHOẢN NGÂN HÀNG", companyTitle, meta);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["STT", "Mã quỹ", "Tên quỹ / Tài khoản", "Ngân hàng", "Số tài khoản", "Số dư hiện tại"]);
        for (var i = 0; i < accounts.Count; i++)
        {
            var a = accounts[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(a.Code, StyleTextCenter),
                TextCell(a.Name, StyleText),
                TextCell(a.BankName ?? "Tiền mặt / Nội bộ", StyleText),
                TextCell(a.AccountNumber ?? "—", StyleTextCenter),
                NumCell(a.CurrentBalance, StyleCurrency)
            ]);
        }

        b.AddTotalRow(firstDataRow, accounts.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [6] = (accounts.Sum(x => x.CurrentBalance), StyleTotalCurrency)
        });

        b.AddSignatureBlock("THỦ QUỸ", "KẾ TOÁN TRƯỞNG", "GIÁM ĐỐC", vnNow);
        return b.Build();
    }

    private static Worksheet BuildAuditSheet(
        IReadOnlyList<ApiClient.FinanceAuditEntryItem> entries, string companyTitle, string meta, DateTime vnNow)
    {
        var b = new SheetBuilder([6, 18, 22, 18, 12, 18, 36, 22]);
        b.AddTitleBlock("NHẬT KÝ KIỂM TOÁN GIAO DỊCH TÀI CHÍNH", companyTitle, meta);
        b.SkipRow();

        var firstDataRow = b.AddHeaderRow(["STT", "Thời gian (GMT+7)", "Người thao tác", "Nghiệp vụ", "Dòng tiền", "Số tiền", "Nội dung", "Tham chiếu"]);
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            b.AddRow([
                NumCell(i + 1, StyleIntCenter),
                TextCell(e.LocalOccurredAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), StyleTextCenter),
                TextCell(e.Actor, StyleText),
                TextCell(e.ActionLabel, StyleText),
                TextCell(e.IsInflow ? "Thu (+)" : "Chi (-)", StyleTextCenter),
                NumCell(e.IsInflow ? e.Amount : -e.Amount, StyleCurrency),
                TextCell(e.Description, StyleText),
                TextCell(e.ReferenceLabel, StyleText)
            ]);
        }

        b.AddTotalRow(firstDataRow, entries.Count, new Dictionary<int, (decimal Sum, uint Style)>
        {
            [6] = (entries.Sum(x => x.IsInflow ? x.Amount : -x.Amount), StyleTotalCurrency)
        }, label: "BIẾN ĐỘNG RÒNG");

        b.AddSignatureBlock("NGƯỜI XUẤT NHẬT KÝ", "KIỂM SOÁT NỘI BỘ", "GIÁM ĐỐC", vnNow);
        return b.Build();
    }

    private static byte[] CreateWorkbook(IReadOnlyList<(string Name, Worksheet Sheet)> sheets)
    {
        using var stream = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new Workbook();
            wbPart.AddNewPart<WorkbookStylesPart>().Stylesheet = CreateStylesheet();

            var sheetsNode = wbPart.Workbook.AppendChild(new Sheets());
            uint sheetId = 1;
            foreach (var (name, worksheet) in sheets)
            {
                var wsPart = wbPart.AddNewPart<WorksheetPart>();
                wsPart.Worksheet = worksheet;
                sheetsNode.Append(new Sheet
                {
                    Id = wbPart.GetIdOfPart(wsPart),
                    SheetId = sheetId++,
                    Name = SanitizeSheetName(name)
                });
            }

            wbPart.Workbook.CalculationProperties = new CalculationProperties
            {
                CalculationId = 191029U,
                FullCalculationOnLoad = true,
                ForceFullCalculation = true
            };
            wbPart.Workbook.Save();
        }
        return stream.ToArray();
    }

    private static Stylesheet CreateStylesheet()
    {
        var fonts = new Fonts(
            new Font(new FontSize { Val = 10.5D }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 14D }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
            new Font(new Italic(), new FontSize { Val = 10D }, new Color { Rgb = "FF475569" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 9.5D }, new Color { Rgb = "FF1E3A8A" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11D }, new Color { Rgb = "FF065F46" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 10D }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 10.5D }, new Color { Rgb = "FF0F172A" }, new FontName { Val = "Calibri" }));

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            SolidFill("FF1E3A8A"), // 2: Navy
            SolidFill("FFF1F5F9"), // 3: Slate 100
            SolidFill("FFEFF6FF"), // 4: Blue 50
            SolidFill("FFECFDF5"), // 5: Emerald 50
            SolidFill("FFDBEAFE")); // 6: Blue 100

        var borders = new Borders(
            new Border(),
            new Border(
                new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
                new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
                new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
                new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFCBD5E1" } },
                new DiagonalBorder()),
            new Border(
                new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1E3A8A" } },
                new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1E3A8A" } },
                new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF1E3A8A" } },
                new BottomBorder { Style = BorderStyleValues.Medium, Color = new Color { Rgb = "FF0F172A" } },
                new DiagonalBorder()));

        var numberFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164U, FormatCode = "#,##0 \"đ\"" },
            new NumberingFormat { NumberFormatId = 165U, FormatCode = "0.0%" },
            new NumberingFormat { NumberFormatId = 166U, FormatCode = "#,##0" });

        var cellFormats = new CellFormats(
            Fmt(0, 0, 0, HorizontalAlignmentValues.Left),              // 0 Default
            Fmt(1, 2, 0, HorizontalAlignmentValues.Center),            // 1 Title
            Fmt(2, 0, 0, HorizontalAlignmentValues.Left),              // 2 Meta
            Fmt(3, 3, 1, HorizontalAlignmentValues.Left),              // 3 KPI Label
            Fmt(4, 5, 1, HorizontalAlignmentValues.Right, 164U),       // 4 KPI Money
            Fmt(5, 2, 2, HorizontalAlignmentValues.Center),            // 5 Header (White on Navy)
            Fmt(0, 0, 1, HorizontalAlignmentValues.Right, 164U),       // 6 Currency
            Fmt(0, 0, 1, HorizontalAlignmentValues.Right, 165U),       // 7 Percent
            Fmt(6, 4, 1, HorizontalAlignmentValues.Left),              // 8 Total Label
            Fmt(6, 6, 1, HorizontalAlignmentValues.Right, 164U),       // 9 Total Currency
            Fmt(0, 0, 1, HorizontalAlignmentValues.Left),              // 10 Text
            Fmt(0, 0, 1, HorizontalAlignmentValues.Center, 166U),      // 11 Int Center
            Fmt(0, 0, 1, HorizontalAlignmentValues.Center),            // 12 Text Center
            Fmt(6, 0, 0, HorizontalAlignmentValues.Center),            // 13 Sig Title
            Fmt(2, 0, 0, HorizontalAlignmentValues.Center),            // 14 Sig Note
            Fmt(2, 3, 1, HorizontalAlignmentValues.Left));             // 15 KPI Words

        return new Stylesheet(numberFormats, fonts, fills, borders, cellFormats);
    }

    private static Fill SolidFill(string rgb) => new(new PatternFill(new ForegroundColor { Rgb = rgb }) { PatternType = PatternValues.Solid });

    private static CellFormat Fmt(uint fontId, uint fillId, uint borderId, HorizontalAlignmentValues align, uint? numFmtId = null) => new()
    {
        FontId = fontId,
        FillId = fillId,
        BorderId = borderId,
        NumberFormatId = numFmtId ?? 0U,
        ApplyNumberFormat = numFmtId.HasValue,
        ApplyAlignment = true,
        Alignment = new Alignment { Horizontal = align, Vertical = VerticalAlignmentValues.Center, WrapText = true }
    };

    private static Cell TextCell(string? value, uint style) => new()
    {
        StyleIndex = style,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static Cell NumCell(decimal value, uint style) => new()
    {
        StyleIndex = style,
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell FormulaCell(string formula, decimal cachedValue, uint style) => new()
    {
        StyleIndex = style,
        CellFormula = new CellFormula(formula),
        CellValue = new CellValue(cachedValue.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell EmptyCell(uint style = StyleDefault) => style == StyleDefault ? new() : new() { StyleIndex = style };

    private static DateTime ToVnTime(DateTime value) => value.Kind == DateTimeKind.Utc ? value.AddHours(7) : value;

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? ' ' : c).ToArray()).Trim();
        return cleaned.Length > 31 ? cleaned[..31] : (string.IsNullOrEmpty(cleaned) ? "Sheet" : cleaned);
    }

    private static string ColLetter(int oneBasedCol)
    {
        var result = string.Empty;
        var col = oneBasedCol;
        while (col > 0)
        {
            col--;
            result = (char)('A' + (col % 26)) + result;
            col /= 26;
        }
        return result;
    }

    private sealed class SheetBuilder
    {
        private readonly double[] _widths;
        private readonly SheetData _data = new();
        private readonly List<MergeCell> _merges = [];
        private uint _row = 1;
        private uint _headerRow;
        private uint _lastDataRow;

        public SheetBuilder(double[] widths) => _widths = widths;
        private int ColCount => _widths.Length;
        private string LastCol => ColLetter(ColCount);

        public void AddTitleBlock(string title, string company, string meta)
        {
            AddMergedRow(title, StyleTitle, 28);
            AddMergedRow(company, StyleMeta, 19);
            AddMergedRow(meta, StyleMeta, 19);
        }

        public void AddMergedNoteRow(string text, uint style) => AddMergedRow(text, style, 20);
        public void SkipRow() => _row++;

        public uint AddHeaderRow(string[] headers)
        {
            _headerRow = _row;
            AddRow(headers.Select(h => TextCell(h, StyleHeader)).ToArray(), 28);
            return _row; // firstDataRow
        }

        public void AddRow(Cell[] cells, double height = 21)
        {
            var r = new Row { RowIndex = _row, CustomHeight = true, Height = height };
            for (var i = 0; i < cells.Length; i++)
            {
                cells[i].CellReference = $"{ColLetter(i + 1)}{_row}";
                r.Append(cells[i]);
            }
            _data.Append(r);
            _lastDataRow = _row;
            _row++;
        }

        public void AddTotalRow(uint firstDataRow, int rowCount, Dictionary<int, (decimal Sum, uint Style)> sumColumns, string label = "TỔNG CỘNG")
        {
            var lastRow = rowCount > 0 ? firstDataRow + (uint)rowCount - 1 : firstDataRow;
            var cells = new Cell[ColCount];
            var firstSumCol = sumColumns.Count > 0 ? sumColumns.Keys.Min() : ColCount + 1;
            for (var c = 1; c <= ColCount; c++)
            {
                if (c == 1)
                {
                    cells[c - 1] = TextCell(label, StyleTotalLabel);
                }
                else if (sumColumns.TryGetValue(c, out var spec))
                {
                    var col = ColLetter(c);
                    cells[c - 1] = rowCount > 0
                        ? FormulaCell($"SUM({col}{firstDataRow}:{col}{lastRow})", spec.Sum, spec.Style)
                        : NumCell(0, spec.Style);
                }
                else
                {
                    cells[c - 1] = EmptyCell(StyleTotalLabel);
                }
            }

            var totalRowIndex = _row;
            AddRow(cells, 24);
            if (firstSumCol > 2)
                _merges.Add(new MergeCell { Reference = $"A{totalRowIndex}:{ColLetter(firstSumCol - 1)}{totalRowIndex}" });
        }

        public void AddSignatureBlock(string leftRole, string midRole, string rightRole, DateTime vnNow)
        {
            SkipRow();
            var seg = Math.Max(1, ColCount / 3);
            var c1Start = 1;
            var c1End = seg;
            var c2Start = seg + 1;
            var c2End = Math.Min(ColCount - 1, seg * 2);
            var c3Start = Math.Min(ColCount, c2End + 1);
            var c3End = ColCount;

            var dateRow = _row;
            var dateCells = new Cell[ColCount];
            for (var i = 0; i < ColCount; i++) dateCells[i] = EmptyCell();
            dateCells[c3Start - 1] = TextCell($"Ngày {vnNow:dd} tháng {vnNow:MM} năm {vnNow:yyyy}", StyleSigNote);
            AddRow(dateCells, 19);
            if (c3End > c3Start) _merges.Add(new MergeCell { Reference = $"{ColLetter(c3Start)}{dateRow}:{ColLetter(c3End)}{dateRow}" });

            AddThreeSigRow(leftRole, midRole, rightRole, StyleSigTitle, c1Start, c1End, c2Start, c2End, c3Start, c3End);
            AddThreeSigRow("(Ký, ghi rõ họ tên)", "(Ký, ghi rõ họ tên)", "(Ký, đóng dấu)", StyleSigNote, c1Start, c1End, c2Start, c2End, c3Start, c3End);
        }

        private void AddThreeSigRow(string s1, string s2, string s3, uint style, int c1a, int c1b, int c2a, int c2b, int c3a, int c3b)
        {
            var rIdx = _row;
            var cells = new Cell[ColCount];
            for (var i = 0; i < ColCount; i++) cells[i] = EmptyCell();
            cells[c1a - 1] = TextCell(s1, style);
            cells[c2a - 1] = TextCell(s2, style);
            cells[c3a - 1] = TextCell(s3, style);
            AddRow(cells, 20);
            if (c1b > c1a) _merges.Add(new MergeCell { Reference = $"{ColLetter(c1a)}{rIdx}:{ColLetter(c1b)}{rIdx}" });
            if (c2b > c2a) _merges.Add(new MergeCell { Reference = $"{ColLetter(c2a)}{rIdx}:{ColLetter(c2b)}{rIdx}" });
            if (c3b > c3a) _merges.Add(new MergeCell { Reference = $"{ColLetter(c3a)}{rIdx}:{ColLetter(c3b)}{rIdx}" });
        }

        private void AddMergedRow(string text, uint style, double height)
        {
            var rIdx = _row;
            var cells = new Cell[ColCount];
            cells[0] = TextCell(text, style);
            for (var i = 1; i < ColCount; i++) cells[i] = EmptyCell(style);
            AddRow(cells, height);
            if (ColCount > 1) _merges.Add(new MergeCell { Reference = $"A{rIdx}:{LastCol}{rIdx}" });
        }

        public Worksheet Build()
        {
            var cols = new Columns();
            for (var i = 0; i < _widths.Length; i++)
                cols.Append(new Column { Min = (uint)(i + 1), Max = (uint)(i + 1), Width = _widths[i], CustomWidth = true });

            var view = new SheetView { WorkbookViewId = 0U };
            if (_headerRow > 0)
            {
                view.Pane = new Pane
                {
                    VerticalSplit = _headerRow,
                    TopLeftCell = $"A{_headerRow + 1}",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen
                };
            }

            var ws = new Worksheet(
                new SheetProperties(new PageSetupProperties { FitToPage = true }),
                new SheetViews(view),
                cols,
                _data);

            if (_headerRow > 0)
                ws.Append(new AutoFilter { Reference = $"A{_headerRow}:{LastCol}{Math.Max(_headerRow, _lastDataRow)}" });

            if (_merges.Count > 0)
                ws.Append(new MergeCells(_merges));

            ws.Append(new PageMargins { Left = 0.3D, Right = 0.3D, Top = 0.45D, Bottom = 0.45D, Header = 0.2D, Footer = 0.2D });
            ws.Append(new PageSetup { Orientation = OrientationValues.Landscape, PaperSize = 9U, FitToWidth = 1U, FitToHeight = 0U });
            return ws;
        }
    }
}
