using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

/// <summary>
/// Generates the payroll workbook in-process. This keeps sensitive salary data
/// behind the existing RBAC request instead of writing temporary public files.
/// </summary>
public sealed class PayrollExcelExportService : IPayrollExportService
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public PayrollExcelExportService(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<Result<PayrollExportFile>> ExportPeriodXlsxAsync(Guid payrollPeriodId, CancellationToken ct)
    {
        var companyId = await ResolveCompanyIdAsync(ct);
        var period = await _db.PayrollPeriods
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Payslips)
                .ThenInclude(item => item.Employee)
                    .ThenInclude(item => item.Department)
            .FirstOrDefaultAsync(item => item.Id == payrollPeriodId && item.CompanyId == companyId, ct);
        if (period is null)
        {
            return Result<PayrollExportFile>.Failure("Không tìm thấy kỳ lương cần xuất.");
        }

        if (period.Status is PayrollStatus.Draft or PayrollStatus.Cancelled)
        {
            return Result<PayrollExportFile>.Failure("Chỉ có thể xuất Excel cho kỳ lương đã tính và chưa bị hủy.");
        }

        if (_currentUser.BusinessUnitId.HasValue &&
            period.BusinessUnitId != _currentUser.BusinessUnitId.Value)
        {
            // Do not reveal a period outside the caller's assigned business unit.
            return Result<PayrollExportFile>.Failure("Bạn không có quyền xuất kỳ lương của mảng này.");
        }

        var companyName = await _db.Companies.AsNoTracking()
            .Where(company => company.Id == companyId)
            .Select(company => company.Name)
            .FirstOrDefaultAsync(ct) ?? "MOLY";
        var businessUnitName = period.BusinessUnitId.HasValue
            ? await _db.BusinessUnits.AsNoTracking()
                .Where(unit => unit.Id == period.BusinessUnitId.Value && unit.CompanyId == companyId)
                .Select(unit => unit.Name)
                .FirstOrDefaultAsync(ct)
            : "Toàn công ty";

        var rows = period.Payslips
            .OrderBy(item => item.Employee.EmployeeCode)
            .Select(item => new PayrollRow(
                item.Employee.EmployeeCode,
                item.Employee.FullName,
                item.Employee.Department?.Name ?? "Chưa phân phòng ban",
                item.Employee.Position ?? string.Empty,
                item.EmploymentType == EmploymentType.PART_TIME ? "Bán thời gian" : "Toàn thời gian",
                item.BaseSalary,
                item.StandardWorkDays,
                item.ActualWorkDays,
                item.ActualWorkHours,
                item.TotalIncome != 0 ? item.TotalIncome : item.GrossSalary,
                item.TotalDeductions != 0 ? item.TotalDeductions : item.Deductions,
                item.NetSalary,
                GetStatusLabel(item.Status),
                item.PublishedAt))
            .ToList();

        var workbook = CreateWorkbook(period, companyName, businessUnitName ?? "Không xác định", rows);
        var now = DateTime.UtcNow;
        _db.AuditLogs.Add(new AuditLog
        {
            UserId = _currentUser.UserId,
            CompanyId = companyId,
            BusinessUnitId = period.BusinessUnitId,
            Action = "Export",
            EntityName = nameof(PayrollPeriod),
            EntityId = period.Id.ToString(),
            NewValues = $"{{\"Format\":\"xlsx\",\"RecordCount\":{rows.Count},\"ExportedAt\":\"{now:O}\"}}",
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct);

        return Result<PayrollExportFile>.Success(new PayrollExportFile(
            workbook,
            XlsxContentType,
            $"bang-luong-{SanitizeFileName(period.Name)}-{period.StartDate:yyyyMMdd}-{period.EndDate:yyyyMMdd}.xlsx"));
    }

    private async Task<Guid> ResolveCompanyIdAsync(CancellationToken ct)
    {
        if (_currentUser.CompanyId.HasValue && _currentUser.CompanyId.Value != Guid.Empty)
        {
            return _currentUser.CompanyId.Value;
        }

        return await _db.Companies.AsNoTracking()
            .Where(company => company.Code == "MOLI")
            .Select(company => company.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static byte[] CreateWorkbook(
        PayrollPeriod period,
        string companyName,
        string businessUnitName,
        IReadOnlyList<PayrollRow> rows)
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            workbookPart.AddNewPart<WorkbookStylesPart>().Stylesheet = CreateStylesheet();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = CreateWorksheet(period, companyName, businessUnitName, rows);

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1U,
                Name = "Bảng lương"
            });
            workbookPart.Workbook.CalculationProperties = new CalculationProperties
            {
                CalculationId = 191029U,
                FullCalculationOnLoad = true,
                ForceFullCalculation = true
            };
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Worksheet CreateWorksheet(
        PayrollPeriod period,
        string companyName,
        string businessUnitName,
        IReadOnlyList<PayrollRow> rows)
    {
        const uint titleStyle = 1;
        const uint metadataStyle = 2;
        const uint summaryLabelStyle = 3;
        const uint summaryCurrencyStyle = 4;
        const uint headerStyle = 5;
        const uint currencyStyle = 6;
        const uint decimalStyle = 7;
        const uint totalLabelStyle = 8;
        const uint totalCurrencyStyle = 9;
        const uint dateStyle = 10;
        const uint textStyle = 11;

        var mainSheetView = new SheetView
        {
            WorkbookViewId = 0U,
            Pane = new Pane
            {
                VerticalSplit = 6D,
                TopLeftCell = "A7",
                ActivePane = PaneValues.BottomLeft,
                State = PaneStateValues.Frozen
            }
        };
        mainSheetView.Append(new Selection
        {
            Pane = PaneValues.BottomLeft,
            ActiveCell = "A7",
            SequenceOfReferences = new ListValue<StringValue> { InnerText = "A7" }
        });
        var sheetViews = new SheetViews(mainSheetView);
        var columns = new Columns(
            Column(1, 1, 7), Column(2, 2, 15), Column(3, 3, 28), Column(4, 4, 22), Column(5, 5, 20),
            Column(6, 6, 16), Column(7, 7, 18), Column(8, 10, 14), Column(11, 13, 19), Column(14, 14, 16), Column(15, 15, 16));
        var sheetData = new SheetData();

        sheetData.Append(
            Row(1, CellText("BẢNG LƯƠNG", titleStyle)),
            Row(2, CellText(companyName + " · " + businessUnitName, metadataStyle)),
            Row(3, CellText($"Kỳ lương: {period.Name} · {period.StartDate:dd/MM/yyyy} - {period.EndDate:dd/MM/yyyy} · Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm}", metadataStyle)));

        var firstDataRow = 7;
        var lastDataRow = firstDataRow + rows.Count - 1;
        var grossColumn = "K";
        var deductionColumn = "L";
        var netColumn = "M";
        sheetData.Append(Row(4,
            CellText("SỐ PHIẾU", summaryLabelStyle), CellNumber(rows.Count, decimalStyle), CellEmpty(),
            CellText("TỔNG GROSS", summaryLabelStyle), FormulaCurrency($"SUM({grossColumn}{firstDataRow}:{grossColumn}{Math.Max(firstDataRow, lastDataRow)})", rows.Sum(row => row.Gross), summaryCurrencyStyle), CellEmpty(),
            CellText("TỔNG KHẤU TRỪ", summaryLabelStyle), FormulaCurrency($"SUM({deductionColumn}{firstDataRow}:{deductionColumn}{Math.Max(firstDataRow, lastDataRow)})", rows.Sum(row => row.Deductions), summaryCurrencyStyle), CellEmpty(),
            CellText("TỔNG THỰC LĨNH", summaryLabelStyle), FormulaCurrency($"SUM({netColumn}{firstDataRow}:{netColumn}{Math.Max(firstDataRow, lastDataRow)})", rows.Sum(row => row.Net), summaryCurrencyStyle)));

        var headers = new[]
        {
            "STT", "Mã NV", "Họ và tên", "Phòng ban", "Chức vụ", "Loại HĐ", "Lương cơ bản", "Công chuẩn", "Công thực tế", "Giờ thực tế", "Thu nhập Gross", "Khấu trừ", "Thực lĩnh Net", "Trạng thái", "Phát hành"
        };
        sheetData.Append(Row(6, headers.Select(header => CellText(header, headerStyle)).ToArray()));

        for (var index = 0; index < rows.Count; index++)
        {
            var item = rows[index];
            sheetData.Append(Row((uint)(firstDataRow + index),
                CellNumber(index + 1, decimalStyle),
                CellText(item.EmployeeCode, textStyle),
                CellText(item.EmployeeName, textStyle),
                CellText(item.DepartmentName, textStyle),
                CellText(item.Position, textStyle),
                CellText(item.EmploymentType, textStyle),
                CellNumber(item.BaseSalary, currencyStyle),
                CellNumber(item.StandardWorkDays, decimalStyle),
                CellNumber(item.ActualWorkDays, decimalStyle),
                CellNumber(item.ActualWorkHours, decimalStyle),
                CellNumber(item.Gross, currencyStyle),
                CellNumber(item.Deductions, currencyStyle),
                CellNumber(item.Net, currencyStyle),
                CellText(item.Status, textStyle),
                item.PublishedAt.HasValue ? CellDate(item.PublishedAt.Value, dateStyle) : CellText("Chưa phát hành", textStyle)));
        }

        var totalRow = Math.Max(firstDataRow, lastDataRow) + 1;
        sheetData.Append(Row((uint)totalRow,
            CellText("TỔNG CỘNG", totalLabelStyle), CellEmpty(), CellEmpty(), CellEmpty(), CellEmpty(), CellEmpty(), CellEmpty(), CellEmpty(), CellEmpty(), CellEmpty(),
            FormulaCurrency($"SUM({grossColumn}{firstDataRow}:{grossColumn}{Math.Max(firstDataRow, lastDataRow)})", rows.Sum(row => row.Gross), totalCurrencyStyle),
            FormulaCurrency($"SUM({deductionColumn}{firstDataRow}:{deductionColumn}{Math.Max(firstDataRow, lastDataRow)})", rows.Sum(row => row.Deductions), totalCurrencyStyle),
            FormulaCurrency($"SUM({netColumn}{firstDataRow}:{netColumn}{Math.Max(firstDataRow, lastDataRow)})", rows.Sum(row => row.Net), totalCurrencyStyle),
            CellEmpty(), CellEmpty()));

        var worksheet = new Worksheet(sheetViews, columns, sheetData);
        worksheet.Append(new MergeCells(
            new MergeCell { Reference = "A1:O1" },
            new MergeCell { Reference = "A2:O2" },
            new MergeCell { Reference = "A3:O3" }));
        worksheet.Append(new AutoFilter { Reference = $"A6:O{Math.Max(6, lastDataRow)}" });
        worksheet.Append(new PageMargins { Left = 0.3D, Right = 0.3D, Top = 0.5D, Bottom = 0.5D, Header = 0.2D, Footer = 0.2D });
        return worksheet;
    }

    private static Stylesheet CreateStylesheet()
    {
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11D }, new FontName { Val = "Aptos" }),
            new Font(new Bold(), new FontSize { Val = 16D }, new Color { Rgb = "FFFFFFFF" }, new FontName { Val = "Aptos Display" }),
            new Font(new Color { Rgb = "FF475569" }, new FontSize { Val = 10D }, new FontName { Val = "Aptos" }),
            new Font(new Bold(), new Color { Rgb = "FF1E3A8A" }, new FontSize { Val = 10D }, new FontName { Val = "Aptos" }),
            new Font(new Bold(), new Color { Rgb = "FF065F46" }, new FontSize { Val = 12D }, new FontName { Val = "Aptos" }),
            new Font(new Bold(), new Color { Rgb = "FFFFFFFF" }, new FontSize { Val = 10D }, new FontName { Val = "Aptos" }),
            new Font(new Bold(), new Color { Rgb = "FF0F172A" }, new FontSize { Val = 10D }, new FontName { Val = "Aptos" }));
        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            SolidFill("FF1E3A8A"), SolidFill("FFF1F5F9"), SolidFill("FFEFF6FF"), SolidFill("FFECFDF5"), SolidFill("FFDBEAFE"));
        var borders = new Borders(
            new Border(),
            new Border(
                new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE2E8F0" } },
                new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE2E8F0" } },
                new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE2E8F0" } },
                new BottomBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FFE2E8F0" } },
                new DiagonalBorder()),
            new Border(
                new LeftBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF93C5FD" } },
                new RightBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF93C5FD" } },
                new TopBorder { Style = BorderStyleValues.Thin, Color = new Color { Rgb = "FF93C5FD" } },
                new BottomBorder { Style = BorderStyleValues.Medium, Color = new Color { Rgb = "FF1D4ED8" } },
                new DiagonalBorder()));
        var numberFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164U, FormatCode = "#,##0 \"đ\"" },
            new NumberingFormat { NumberFormatId = 165U, FormatCode = "0.00" },
            new NumberingFormat { NumberFormatId = 166U, FormatCode = "dd/mm/yyyy hh:mm" });
        var cellFormats = new CellFormats(
            CellFormat(0, 0, 0, HorizontalAlignmentValues.Left),
            CellFormat(1, 2, 0, HorizontalAlignmentValues.Center),
            CellFormat(2, 0, 0, HorizontalAlignmentValues.Left),
            CellFormat(3, 3, 1, HorizontalAlignmentValues.Left),
            CellFormat(4, 5, 1, HorizontalAlignmentValues.Right, 164U),
            CellFormat(5, 6, 2, HorizontalAlignmentValues.Center),
            CellFormat(0, 0, 1, HorizontalAlignmentValues.Right, 164U),
            CellFormat(0, 0, 1, HorizontalAlignmentValues.Right, 165U),
            CellFormat(6, 4, 1, HorizontalAlignmentValues.Left),
            CellFormat(4, 6, 1, HorizontalAlignmentValues.Right, 164U),
            CellFormat(0, 0, 1, HorizontalAlignmentValues.Center, 166U),
            CellFormat(0, 0, 1, HorizontalAlignmentValues.Left));
        return new Stylesheet(numberFormats, fonts, fills, borders, cellFormats);
    }

    private static Fill SolidFill(string rgb) => new(new PatternFill(new ForegroundColor { Rgb = rgb }) { PatternType = PatternValues.Solid });

    private static CellFormat CellFormat(uint fontId, uint fillId, uint borderId, HorizontalAlignmentValues alignment, uint? numberFormatId = null) =>
        new()
        {
            FontId = fontId,
            FillId = fillId,
            BorderId = borderId,
            NumberFormatId = numberFormatId ?? 0U,
            ApplyNumberFormat = numberFormatId.HasValue,
            ApplyAlignment = true,
            Alignment = new Alignment { Horizontal = alignment, Vertical = VerticalAlignmentValues.Center, WrapText = true }
        };

    private static Column Column(uint min, uint max, double width) => new() { Min = min, Max = max, Width = width, CustomWidth = true };

    private static Row Row(uint rowIndex, params Cell[] cells) => new(cells) { RowIndex = rowIndex, CustomHeight = true, Height = rowIndex == 6 ? 30D : 22D };

    private static Cell CellText(string? value, uint styleIndex) => new()
    {
        StyleIndex = styleIndex,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(value ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve })
    };

    private static Cell CellNumber(decimal value, uint styleIndex) => new()
    {
        StyleIndex = styleIndex,
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell FormulaCurrency(string formula, decimal value, uint styleIndex) => new()
    {
        StyleIndex = styleIndex,
        CellFormula = new CellFormula(formula),
        CellValue = new CellValue(value.ToString(CultureInfo.InvariantCulture))
    };

    private static Cell CellDate(DateTime value, uint styleIndex) => new()
    {
        StyleIndex = styleIndex,
        DataType = CellValues.Number,
        CellValue = new CellValue(value.ToOADate().ToString(CultureInfo.InvariantCulture))
    };

    private static Cell CellEmpty() => new();

    private static string GetStatusLabel(PayrollStatus status) => status switch
    {
        PayrollStatus.Calculated => "Đã tính",
        PayrollStatus.Reviewing => "Chờ duyệt",
        PayrollStatus.Approved => "Đã duyệt",
        PayrollStatus.Paid => "Đã chi",
        PayrollStatus.Published => "Đã phát hành",
        PayrollStatus.Cancelled => "Đã hủy",
        _ => "Bản nháp"
    };

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var value = new string(name.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(value) ? "ky-luong" : value;
    }

    private sealed record PayrollRow(
        string EmployeeCode,
        string EmployeeName,
        string DepartmentName,
        string Position,
        string EmploymentType,
        decimal BaseSalary,
        decimal StandardWorkDays,
        decimal ActualWorkDays,
        decimal ActualWorkHours,
        decimal Gross,
        decimal Deductions,
        decimal Net,
        string Status,
        DateTime? PublishedAt);
}
