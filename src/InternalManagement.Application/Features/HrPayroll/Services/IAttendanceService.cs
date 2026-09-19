using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;

namespace InternalManagement.Application.Features.HrPayroll.Services;

public interface IAttendanceService
{
    Task<PaginatedResult<AttendanceRecordDto>> GetAttendanceRecordsAsync(
        DateOnly? fromDate, DateOnly? toDate, Guid? employeeId, Guid? departmentId, string? status, int pageIndex, int pageSize, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null);

    Task<Result<AttendanceSummaryDto>> GetAttendanceSummaryAsync(
        DateOnly? fromDate, DateOnly? toDate, Guid? departmentId, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null);

    Task<Result<AttendanceRecordDto>> RecordAttendanceAsync(
        CreateAttendanceRecordRequest request, CancellationToken ct);

    Task<Result<AttendanceImportResultDto>> ImportAttendanceAsync(
        Stream fileStream, string fileName, CancellationToken ct,
        Guid? businessUnitId = null, string? businessSegment = null);

    Task<AttendanceImportTemplateFile> CreateImportTemplateAsync(CancellationToken ct);
}
