using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Domain.Entities.HrPayroll;

namespace InternalManagement.Infrastructure.Services;

public sealed class AttendanceService : IAttendanceService
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "yyyy/MM/dd"];
    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss", "H:mm", "h:mm tt", "hh:mm tt"];
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AttendanceService> _logger;

    public AttendanceService(IApplicationDbContext db, ICurrentUserService currentUser, ILogger<AttendanceService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    private async Task<Guid> GetCompanyIdAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
            companyId = (await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct))?.Id ?? Guid.Empty;
        return companyId.Value;
    }

    public async Task<PaginatedResult<AttendanceRecordDto>> GetAttendanceRecordsAsync(
        DateOnly? fromDate, DateOnly? toDate, Guid? employeeId, Guid? departmentId, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var scopedUnitIds = await ResolveBusinessUnitIdsAsync(companyId, businessUnitId, businessSegment, ct);
        var query = _db.AttendanceRecords.AsNoTracking().Where(a => a.CompanyId == companyId)
            .Include(a => a.Employee).ThenInclude(e => e.Department).AsQueryable();

        if (scopedUnitIds != null)
            query = query.Where(a => a.Employee.BusinessUnitId.HasValue && scopedUnitIds.Contains(a.Employee.BusinessUnitId.Value));
        if (fromDate.HasValue) query = query.Where(a => a.Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(a => a.Date <= toDate.Value);
        if (employeeId is { } employeeFilterId && employeeFilterId != Guid.Empty) query = query.Where(a => a.EmployeeId == employeeFilterId);
        if (departmentId is { } departmentFilterId && departmentFilterId != Guid.Empty) query = query.Where(a => a.Employee.DepartmentId == departmentFilterId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(a => a.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.Date).ThenBy(a => a.Employee.EmployeeCode)
            .Skip((pageIndex - 1) * pageSize).Take(pageSize)
            .Select(a => new AttendanceRecordDto(a.Id, a.EmployeeId, a.Employee.EmployeeCode, a.Employee.FullName,
                a.Employee.Department != null ? a.Employee.Department.Name : null, a.Date, a.CheckInTime, a.CheckOutTime,
                a.WorkHours, a.Status, a.ImportBatchId))
            .ToListAsync(ct);
        return new PaginatedResult<AttendanceRecordDto>(items, total, pageIndex, pageSize);
    }

    public async Task<Result<AttendanceSummaryDto>> GetAttendanceSummaryAsync(
        DateOnly? fromDate, DateOnly? toDate, Guid? departmentId, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null)
    {
        var companyId = await GetCompanyIdAsync(ct);
        var scopedUnitIds = await ResolveBusinessUnitIdsAsync(companyId, businessUnitId, businessSegment, ct);
        var query = _db.AttendanceRecords.AsNoTracking().Where(a => a.CompanyId == companyId).Include(a => a.Employee).AsQueryable();
        if (scopedUnitIds != null)
            query = query.Where(a => a.Employee.BusinessUnitId.HasValue && scopedUnitIds.Contains(a.Employee.BusinessUnitId.Value));
        if (fromDate.HasValue) query = query.Where(a => a.Date >= fromDate.Value);
        if (toDate.HasValue) query = query.Where(a => a.Date <= toDate.Value);
        if (departmentId is { } id && id != Guid.Empty) query = query.Where(a => a.Employee.DepartmentId == id);

        var records = await query.ToListAsync(ct);
        return Result<AttendanceSummaryDto>.Success(new AttendanceSummaryDto(
            records.Count, records.Count(a => a.Status == "Present"), records.Count(a => a.Status == "Late"),
            records.Count(a => a.Status == "Absent"), records.Count(a => a.Status == "Leave"), records.Sum(a => a.WorkHours)));
    }

    public async Task<Result<AttendanceRecordDto>> RecordAttendanceAsync(CreateAttendanceRecordRequest request, CancellationToken ct)
    {
        if (request.WorkHours is < 0 or > 24)
            return Result<AttendanceRecordDto>.Failure("Số giờ làm việc phải nằm trong khoảng từ 0 đến 24 giờ.");
        if (request.CheckInTime.HasValue && request.CheckOutTime.HasValue && request.CheckOutTime <= request.CheckInTime)
            return Result<AttendanceRecordDto>.Failure("Giờ ra phải sau giờ vào.");

        var companyId = await GetCompanyIdAsync(ct);
        var employee = await _db.Employees.Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId && e.CompanyId == companyId && !e.IsDeleted, ct);
        if (employee == null) return Result<AttendanceRecordDto>.Failure("Không tìm thấy nhân viên.");

        var scopedUnitIds = await ResolveBusinessUnitIdsAsync(companyId, null, null, ct);
        if (scopedUnitIds != null && (!employee.BusinessUnitId.HasValue || !scopedUnitIds.Contains(employee.BusinessUnitId.Value)))
            return Result<AttendanceRecordDto>.Failure("Bạn chỉ có thể chấm công cho nhân sự thuộc đơn vị kinh doanh được cấp quyền.");

        var status = NormalizeStatus(request.Status) ?? "Present";
        var record = await _db.AttendanceRecords.FirstOrDefaultAsync(a =>
            a.CompanyId == companyId && a.EmployeeId == request.EmployeeId && a.Date == request.Date, ct);
        if (record == null)
        {
            record = new AttendanceRecord
            {
                CompanyId = companyId, BusinessUnitId = employee.BusinessUnitId, EmployeeId = employee.Id, Date = request.Date,
                CheckInTime = request.CheckInTime, CheckOutTime = request.CheckOutTime, WorkHours = request.WorkHours, Status = status
            };
            _db.AttendanceRecords.Add(record);
        }
        else
        {
            record.CheckInTime = request.CheckInTime;
            record.CheckOutTime = request.CheckOutTime;
            record.WorkHours = request.WorkHours;
            record.Status = status;
        }

        await _db.SaveChangesAsync(ct);
        return Result<AttendanceRecordDto>.Success(new AttendanceRecordDto(record.Id, employee.Id, employee.EmployeeCode,
            employee.FullName, employee.Department?.Name, record.Date, record.CheckInTime, record.CheckOutTime,
            record.WorkHours, record.Status, record.ImportBatchId));
    }

    public async Task<Result<AttendanceImportResultDto>> ImportAttendanceAsync(
        Stream fileStream, string fileName, CancellationToken ct, Guid? businessUnitId = null, string? businessSegment = null)
    {
        var parsed = await ParseImportRowsAsync(fileStream, fileName, ct);
        if (!parsed.Succeeded)
            return Result<AttendanceImportResultDto>.Failure(parsed.Errors.FirstOrDefault() ?? "Không thể đọc file chấm công.");

        var companyId = await GetCompanyIdAsync(ct);
        var scopedUnitIds = await ResolveBusinessUnitIdsAsync(companyId, businessUnitId, businessSegment, ct);
        if ((businessUnitId.HasValue || !string.IsNullOrWhiteSpace(businessSegment)) && scopedUnitIds is { Count: 0 })
            return Result<AttendanceImportResultDto>.Failure("Không tìm thấy đơn vị kinh doanh hợp lệ cho mảng đang import.");

        var employees = await _db.Employees.AsNoTracking().Where(e => e.CompanyId == companyId && !e.IsDeleted)
            .ToDictionaryAsync(e => e.EmployeeCode.ToUpperInvariant(), e => e, ct);
        var batchId = $"ATT-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        var errors = new List<AttendanceImportRowErrorDto>();
        var recordsToSave = new List<AttendanceRecord>();
        var seenEmployeeDates = new HashSet<(Guid EmployeeId, DateOnly Date)>();

        foreach (var row in parsed.Value!)
        {
            if (row.Values.All(string.IsNullOrWhiteSpace)) continue;
            var employeeCode = GetCell(row, 0).Trim();
            var dateText = GetCell(row, 1).Trim();
            if (string.IsNullOrWhiteSpace(employeeCode) || string.IsNullOrWhiteSpace(dateText))
            {
                errors.Add(RowError(row, employeeCode, dateText, "Dòng thiếu cột bắt buộc: Mã NV và Ngày."));
                continue;
            }
            if (!employees.TryGetValue(employeeCode.ToUpperInvariant(), out var employee))
            {
                errors.Add(RowError(row, employeeCode, dateText, $"Mã nhân viên '{employeeCode}' không tồn tại trong hệ thống."));
                continue;
            }
            if (scopedUnitIds != null && (!employee.BusinessUnitId.HasValue || !scopedUnitIds.Contains(employee.BusinessUnitId.Value)))
            {
                errors.Add(RowError(row, employeeCode, dateText, $"Nhân viên '{employeeCode}' không thuộc mảng hoặc đơn vị kinh doanh đang import."));
                continue;
            }
            if (!TryParseDate(dateText, out var date))
            {
                errors.Add(RowError(row, employeeCode, dateText, $"Định dạng ngày '{dateText}' không hợp lệ. Hỗ trợ: yyyy-MM-dd, dd/MM/yyyy."));
                continue;
            }
            if (!seenEmployeeDates.Add((employee.Id, date)))
            {
                errors.Add(RowError(row, employeeCode, dateText, "File có nhiều hơn một dòng cho cùng một nhân viên và ngày. Hãy giữ lại một dòng duy nhất."));
                continue;
            }

            var checkInText = GetCell(row, 2).Trim();
            var checkOutText = GetCell(row, 3).Trim();
            var hoursText = GetCell(row, 4).Trim();
            var statusText = GetCell(row, 5).Trim();
            if (!TryParseTime(checkInText, out var checkIn))
            {
                errors.Add(RowError(row, employeeCode, dateText, $"Định dạng giờ vào '{checkInText}' không hợp lệ. Hỗ trợ: HH:mm (ví dụ 08:30)."));
                continue;
            }
            if (!TryParseTime(checkOutText, out var checkOut))
            {
                errors.Add(RowError(row, employeeCode, dateText, $"Định dạng giờ ra '{checkOutText}' không hợp lệ. Hỗ trợ: HH:mm (ví dụ 17:30)."));
                continue;
            }
            if (checkIn.HasValue && checkOut.HasValue && checkOut <= checkIn)
            {
                errors.Add(RowError(row, employeeCode, dateText, "Giờ ra phải sau giờ vào."));
                continue;
            }
            var status = NormalizeStatus(statusText);
            if (status == null)
            {
                errors.Add(RowError(row, employeeCode, dateText, "Trạng thái không hợp lệ. Dùng: Present/Có mặt, Late/Muộn, Absent/Vắng hoặc Leave/Nghỉ phép."));
                continue;
            }

            decimal workHours;
            if (!string.IsNullOrWhiteSpace(hoursText))
            {
                if (!TryParseDecimal(hoursText, out workHours) || workHours is < 0 or > 24)
                {
                    errors.Add(RowError(row, employeeCode, dateText, $"Số giờ làm việc '{hoursText}' không hợp lệ (từ 0 đến 24 giờ)."));
                    continue;
                }
            }
            else if (status is "Absent" or "Leave") workHours = 0;
            else if (checkIn.HasValue && checkOut.HasValue)
            {
                var duration = (decimal)(checkOut.Value.ToTimeSpan() - checkIn.Value.ToTimeSpan()).TotalHours;
                workHours = Math.Round(duration > 5 ? duration - 1 : duration, 1);
            }
            else workHours = 8;

            recordsToSave.Add(new AttendanceRecord
            {
                CompanyId = companyId, BusinessUnitId = employee.BusinessUnitId, EmployeeId = employee.Id, Date = date,
                CheckInTime = checkIn, CheckOutTime = checkOut, WorkHours = workHours, Status = status, ImportBatchId = batchId
            });
        }

        if (recordsToSave.Count > 0)
        {
            var employeeIds = recordsToSave.Select(record => record.EmployeeId).Distinct().ToList();
            var firstDate = recordsToSave.Min(record => record.Date);
            var lastDate = recordsToSave.Max(record => record.Date);
            var existing = await _db.AttendanceRecords.Where(record => record.CompanyId == companyId &&
                    employeeIds.Contains(record.EmployeeId) && record.Date >= firstDate && record.Date <= lastDate)
                .ToListAsync(ct);
            var existingByKey = existing.ToDictionary(record => (record.EmployeeId, record.Date));
            foreach (var record in recordsToSave)
            {
                if (existingByKey.TryGetValue((record.EmployeeId, record.Date), out var current))
                {
                    current.CheckInTime = record.CheckInTime;
                    current.CheckOutTime = record.CheckOutTime;
                    current.WorkHours = record.WorkHours;
                    current.Status = record.Status;
                    current.ImportBatchId = record.ImportBatchId;
                }
                else _db.AttendanceRecords.Add(record);
            }
            await _db.SaveChangesAsync(ct);
        }

        _logger.LogInformation("Imported {Count} attendance records from {FileName} in batch {BatchId}; {Errors} error rows.",
            recordsToSave.Count, fileName, batchId, errors.Count);
        return Result<AttendanceImportResultDto>.Success(new AttendanceImportResultDto(
            batchId, parsed.Value!.Count, recordsToSave.Count, errors.Count, errors));
    }

    public Task<AttendanceImportTemplateFile> CreateImportTemplateAsync(CancellationToken ct)
    {
        _ = ct;
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = new Stylesheet(
                new Fonts(new Font(), new Font(new Bold())) { Count = 2U },
                new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })) { Count = 2U },
                new Borders(new Border()) { Count = 1U }, new CellStyleFormats(new CellFormat()) { Count = 1U },
                new CellFormats(new CellFormat(), new CellFormat { FontId = 1U, ApplyFont = true }) { Count = 2U });
            stylesPart.Stylesheet.Save();

            var inputPart = workbookPart.AddNewPart<WorksheetPart>();
            inputPart.Worksheet = new Worksheet(
                new SheetViews(new SheetView { WorkbookViewId = 0U }),
                new Columns(new Column { Min = 1U, Max = 1U, Width = 16D, CustomWidth = true },
                    new Column { Min = 2U, Max = 2U, Width = 14D, CustomWidth = true },
                    new Column { Min = 3U, Max = 4U, Width = 13D, CustomWidth = true },
                    new Column { Min = 5U, Max = 5U, Width = 12D, CustomWidth = true },
                    new Column { Min = 6U, Max = 6U, Width = 18D, CustomWidth = true }),
                new SheetData(new Row(CreateTextCell("Mã NV", 1), CreateTextCell("Ngày", 1), CreateTextCell("Giờ vào", 1),
                    CreateTextCell("Giờ ra", 1), CreateTextCell("Số giờ", 1), CreateTextCell("Trạng thái", 1)) { RowIndex = 1U }),
                new AutoFilter { Reference = "A1:F1" });
            inputPart.Worksheet.Save();

            var guidePart = workbookPart.AddNewPart<WorksheetPart>();
            guidePart.Worksheet = new Worksheet(new SheetData(
                new Row(CreateTextCell("HƯỚNG DẪN NHẬP CHẤM CÔNG", 1)) { RowIndex = 1U },
                new Row(CreateTextCell("Điền dữ liệu từ dòng 2 của sheet 'Nhập chấm công'. Không đổi tên sáu cột tiêu đề.", 0)) { RowIndex = 2U },
                new Row(CreateTextCell("Ngày: yyyy-MM-dd hoặc dd/MM/yyyy. Giờ: HH:mm. Số giờ để trống sẽ tự tính từ giờ vào/ra; Vắng/Nghỉ phép mặc định 0 giờ.", 0)) { RowIndex = 3U },
                new Row(CreateTextCell("Trạng thái: Present/Có mặt, Late/Muộn, Absent/Vắng, Leave/Nghỉ phép.", 0)) { RowIndex = 4U }));
            guidePart.Worksheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(inputPart), SheetId = 1U, Name = "Nhập chấm công" });
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(guidePart), SheetId = 2U, Name = "Hướng dẫn" });
            workbookPart.Workbook.Save();
        }
        return Task.FromResult(new AttendanceImportTemplateFile(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "mau-nhap-cham-cong.xlsx"));
    }

    private async Task<Result<IReadOnlyList<ImportedAttendanceRow>>> ParseImportRowsAsync(Stream stream, string fileName, CancellationToken ct)
    {
        try
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".xlsx") return Result<IReadOnlyList<ImportedAttendanceRow>>.Success(ParseXlsxRows(stream));
            if (extension is ".csv" or ".txt" or "") return Result<IReadOnlyList<ImportedAttendanceRow>>.Success(await ParseDelimitedRowsAsync(stream, ct));
            return Result<IReadOnlyList<ImportedAttendanceRow>>.Failure("Chỉ hỗ trợ file .xlsx, .csv hoặc .txt.");
        }
        catch (OpenXmlPackageException)
        {
            return Result<IReadOnlyList<ImportedAttendanceRow>>.Failure("File Excel không hợp lệ hoặc bị hỏng. Hãy tải mẫu Excel mới rồi nhập lại.");
        }
        catch (Exception ex) when (ex is InvalidDataException or FormatException)
        {
            return Result<IReadOnlyList<ImportedAttendanceRow>>.Failure("Không thể đọc cấu trúc file chấm công. Hãy dùng mẫu Excel hoặc file CSV UTF-8.");
        }
    }

    private static IReadOnlyList<ImportedAttendanceRow> ParseXlsxRows(Stream stream)
    {
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbookPart = document.WorkbookPart ?? throw new InvalidDataException("Workbook không có dữ liệu.");
        var sheets = workbookPart.Workbook.Sheets ?? throw new InvalidDataException("Workbook không có sheet nhập liệu.");
        var sheet = sheets.Elements<Sheet>().FirstOrDefault()
                    ?? throw new InvalidDataException("Workbook không có sheet nhập liệu.");
        var relationshipId = sheet.Id?.Value ?? throw new InvalidDataException("Sheet nhập liệu không hợp lệ.");
        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(relationshipId);
        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var rows = (worksheetPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>() ?? Enumerable.Empty<Row>())
            .Select((row, index) => new RawImportRow((int)(row.RowIndex?.Value ?? (uint)(index + 1)), ReadWorksheetRow(row, sharedStrings)))
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value))).ToList();
        return ConvertRows(rows);
    }

    private static async Task<IReadOnlyList<ImportedAttendanceRow>> ParseDelimitedRowsAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(ct);
        var delimiter = DetectDelimiter(content);
        var rows = new List<RawImportRow>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var rowNumber = 1;
        var inQuotes = false;
        for (var i = 0; i < content.Length; i++)
        {
            var character = content[i];
            if (character == '"')
            {
                if (inQuotes && i + 1 < content.Length && content[i + 1] == '"') { cell.Append('"'); i++; }
                else inQuotes = !inQuotes;
            }
            else if (!inQuotes && character == delimiter) { cells.Add(cell.ToString().Trim()); cell.Clear(); }
            else if (!inQuotes && (character == '\r' || character == '\n'))
            {
                if (character == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                cells.Add(cell.ToString().Trim()); cell.Clear();
                rows.Add(new RawImportRow(rowNumber++, cells.ToArray())); cells.Clear();
            }
            else cell.Append(character);
        }
        if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString().Trim()); rows.Add(new RawImportRow(rowNumber, cells.ToArray())); }
        return ConvertRows(rows.Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value))).ToList());
    }

    private static IReadOnlyList<ImportedAttendanceRow> ConvertRows(IReadOnlyList<RawImportRow> rows)
    {
        if (rows.Count == 0) return [];
        var mapping = TryReadHeader(rows[0].Values);
        return rows.Skip(mapping == null ? 0 : 1).Select(row => new ImportedAttendanceRow(row.RowNumber,
            mapping == null ? row.Values : new[] { ReadAt(row.Values, mapping.EmployeeCode), ReadAt(row.Values, mapping.Date),
                ReadAt(row.Values, mapping.CheckIn), ReadAt(row.Values, mapping.CheckOut), ReadAt(row.Values, mapping.WorkHours), ReadAt(row.Values, mapping.Status) },
            string.Join(" | ", row.Values))).ToList();
    }

    private static AttendanceColumnMapping? TryReadHeader(IReadOnlyList<string> headers)
    {
        var employeeCode = FindHeader(headers, "MANV", "EMPLOYEECODE", "EMPLOYEEID", "MACONGNHANVIEN");
        var date = FindHeader(headers, "NGAY", "DATE", "WORKDATE");
        return employeeCode < 0 || date < 0 ? null : new AttendanceColumnMapping(employeeCode, date,
            FindHeader(headers, "GIOVAO", "CHECKIN", "CHECKINTIME"), FindHeader(headers, "GIORA", "CHECKOUT", "CHECKOUTTIME"),
            FindHeader(headers, "SOGIO", "GIOCONG", "WORKHOURS", "HOURS"), FindHeader(headers, "TRANGTHAI", "STATUS"));
    }

    private static int FindHeader(IReadOnlyList<string> headers, params string[] candidates) => headers.Select((header, index) => new { Header = NormalizeHeader(header), Index = index })
        .Where(item => candidates.Contains(item.Header, StringComparer.Ordinal)).Select(item => item.Index).DefaultIfEmpty(-1).First();

    private static string NormalizeHeader(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character)) result.Append(char.ToUpperInvariant(character));
        return result.ToString();
    }

    private static IReadOnlyList<string> ReadWorksheetRow(Row row, SharedStringTable? sharedStrings)
    {
        var values = new List<string>();
        var currentColumn = 0;
        foreach (var cell in row.Elements<Cell>())
        {
            var column = GetColumnIndex(cell.CellReference?.Value) ?? currentColumn;
            while (values.Count <= column) values.Add(string.Empty);
            values[column] = GetCellText(cell, sharedStrings);
            currentColumn = column + 1;
        }
        return values;
    }

    private static string GetCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        var value = cell.CellValue?.InnerText ?? cell.InnerText;
        if (cell.DataType?.Value == CellValues.SharedString && int.TryParse(value, out var index) && sharedStrings != null)
            return sharedStrings.ElementAt(index).InnerText;
        return cell.DataType?.Value == CellValues.InlineString ? cell.InlineString?.InnerText ?? string.Empty : value;
    }

    private static int? GetColumnIndex(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var value = 0;
        foreach (var character in reference)
        {
            if (!char.IsLetter(character)) break;
            value = value * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return value == 0 ? null : value - 1;
    }

    private static char DetectDelimiter(string content)
    {
        var firstLine = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return new[] { ',', ';', '\t', '|' }.OrderByDescending(candidate => firstLine.Count(character => character == candidate)).FirstOrDefault(',');
    }

    private static bool TryParseDate(string value, out DateOnly date)
    {
        if (DateOnly.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
            DateOnly.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out date)) return true;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 60000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }
        date = default;
        return false;
    }

    private static bool TryParseTime(string value, out TimeOnly? time)
    {
        if (string.IsNullOrWhiteSpace(value)) { time = null; return true; }
        if (TimeOnly.TryParseExact(value, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            TimeOnly.TryParse(value, CultureInfo.GetCultureInfo("vi-VN"), DateTimeStyles.None, out parsed)) { time = parsed; return true; }
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is >= 0 and < 1)
        {
            time = TimeOnly.FromTimeSpan(TimeSpan.FromDays(serial));
            return true;
        }
        time = null;
        return false;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        // Excel commonly stores decimals with a dot; parsing it first as vi-VN would read "8.0" as eighty.
        if (value.Contains('.', StringComparison.Ordinal) &&
            decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result)) return true;
        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out result) ||
               decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
    private static string? NormalizeStatus(string? value) => string.IsNullOrWhiteSpace(value) ? "Present" : NormalizeHeader(value) switch
    {
        "PRESENT" or "COMAT" or "DILAM" => "Present", "LATE" or "MUON" => "Late", "ABSENT" or "VANG" => "Absent", "LEAVE" or "NGHIPHEP" => "Leave", _ => null
    };
    private static string GetCell(ImportedAttendanceRow row, int index) => ReadAt(row.Values, index);
    private static string ReadAt(IReadOnlyList<string> values, int index) => index >= 0 && index < values.Count ? values[index] : string.Empty;
    private static AttendanceImportRowErrorDto RowError(ImportedAttendanceRow row, string? employeeCode, string? date, string message) => new(row.RowNumber, employeeCode, date, message, row.RawLine);
    private static Cell CreateTextCell(string value, uint styleIndex) => new() { DataType = CellValues.InlineString, StyleIndex = styleIndex, InlineString = new InlineString(new Text(value)) };

    private async Task<List<Guid>?> ResolveBusinessUnitIdsAsync(Guid companyId, Guid? businessUnitId, string? segment, CancellationToken ct)
    {
        var units = _db.BusinessUnits.AsNoTracking().Where(unit => unit.CompanyId == companyId && !unit.IsDeleted && unit.IsActive);
        List<Guid>? requestedScope = null;
        if (businessUnitId is { } requestedId && requestedId != Guid.Empty)
            requestedScope = await units.Where(unit => unit.Id == requestedId).Select(unit => unit.Id).ToListAsync(ct);
        else if (!string.IsNullOrWhiteSpace(segment))
        {
            var normalized = segment.Trim().ToUpperInvariant().Replace('-', '_').Replace(' ', '_');
            if (normalized is "FASHION" or "THOI_TRANG") units = units.Where(unit => unit.Code.ToUpper() == "FASHION");
            else if (normalized is "TECH_EDUCATION" or "TECHNOLOGY_EDUCATION" or "CONG_NGHE_GIAO_DUC" or "MOLY")
                units = units.Where(unit => unit.Code.ToUpper() == "EDTECH" || unit.Code.ToUpper() == "CSCA" || unit.Code.ToUpper() == "INTERVIEW");
            else units = units.Where(unit => unit.Code.ToUpper() == normalized);
            requestedScope = await units.Select(unit => unit.Id).ToListAsync(ct);
        }

        // Company administrators intentionally work across all business units. A unit default is their
        // landing context, not a data restriction. Segment/request scope still applies when supplied.
        if (_currentUser.HasPermission(Permissions.UsersManage)) return requestedScope;
        if (_currentUser.BusinessUnitId is not { } currentUnitId || currentUnitId == Guid.Empty) return requestedScope;
        var validCurrentUnit = await _db.BusinessUnits.AsNoTracking().AnyAsync(unit => unit.Id == currentUnitId && unit.CompanyId == companyId && !unit.IsDeleted && unit.IsActive, ct);
        if (!validCurrentUnit) return [];
        if (requestedScope == null) return [currentUnitId];
        return requestedScope.Contains(currentUnitId) ? [currentUnitId] : [];
    }

    private sealed record RawImportRow(int RowNumber, IReadOnlyList<string> Values);
    private sealed record ImportedAttendanceRow(int RowNumber, IReadOnlyList<string> Values, string RawLine);
    private sealed record AttendanceColumnMapping(int EmployeeCode, int Date, int CheckIn, int CheckOut, int WorkHours, int Status);
}
