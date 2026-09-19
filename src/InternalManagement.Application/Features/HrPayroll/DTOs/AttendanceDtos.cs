namespace InternalManagement.Application.Features.HrPayroll.DTOs;

public record AttendanceRecordDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeCode,
    string EmployeeName,
    string? DepartmentName,
    DateOnly Date,
    TimeOnly? CheckInTime,
    TimeOnly? CheckOutTime,
    decimal WorkHours,
    string Status,
    string? ImportBatchId);

public record CreateAttendanceRecordRequest(
    Guid EmployeeId,
    DateOnly Date,
    TimeOnly? CheckInTime,
    TimeOnly? CheckOutTime,
    decimal WorkHours = 8,
    string Status = "Present");

public record AttendanceSummaryDto(
    int TotalRecords,
    int PresentCount,
    int LateCount,
    int AbsentCount,
    int LeaveCount,
    decimal TotalWorkHours);

public record AttendanceImportRowErrorDto(
    int RowNumber,
    string? EmployeeCode,
    string? DateString,
    string ErrorMessage,
    string RawLine);

public record AttendanceImportResultDto(
    string ImportBatchId,
    int TotalRowsProcessed,
    int SuccessCount,
    int ErrorCount,
    IReadOnlyList<AttendanceImportRowErrorDto> Errors);

public record AttendanceImportTemplateFile(
    byte[] Content,
    string ContentType,
    string FileName);
