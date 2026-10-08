using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net;
using System.Text.Json;

namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record CreateEmployeeModel(
        string EmployeeCode,
        string FullName,
        string Email,
        string? Phone,
        string? Position,
        decimal BaseSalary,
        Guid? DepartmentId,
        Guid? BusinessUnitId,
        DateTime? JoinedDate,
        string Status,
        int EmploymentType,
        int? PartTimeCalculationMethod,
        decimal? PartTimeUnitRate,
        string? CvUrlOrPath,
        string? ProfessionalSummary,
        string? Skills,
        string? Experience,
        DateTime? StatusChangedAt = null,
        string? StatusReason = null,
        string? BankName = null,
        string? BankAccountNumber = null,
        string? BankAccountHolder = null);

    public sealed record UpdateEmployeeModel(
        string FullName,
        string Email,
        string? Phone,
        string? Position,
        decimal BaseSalary,
        Guid? DepartmentId,
        Guid? BusinessUnitId,
        DateTime? JoinedDate,
        string Status,
        int? EmploymentType,
        int? PartTimeCalculationMethod,
        decimal? PartTimeUnitRate,
        string? CvUrlOrPath,
        string? ProfessionalSummary,
        string? Skills,
        string? Experience,
        DateTime? StatusChangedAt = null,
        string? StatusReason = null,
        string? BankName = null,
        string? BankAccountNumber = null,
        string? BankAccountHolder = null);

    public sealed record EmployeePaymentDetails(
        Guid EmployeeId,
        string? BankName,
        string? BankAccountNumber,
        string? BankAccountHolder);

    public sealed record EmployeeItem(
        Guid Id,
        Guid CompanyId,
        Guid? BusinessUnitId,
        string? BusinessUnitName,
        Guid? DepartmentId,
        string? DepartmentName,
        Guid? UserId,
        string? Username,
        string EmployeeCode,
        string FullName,
        string Email,
        string? Phone,
        string? Position,
        decimal BaseSalary,
        DateTime? JoinedDate,
        string Status,
        DateTime CreatedAt,
        int EmploymentType = 0,
        string EmploymentTypeNameVi = "Toàn thời gian",
        int? PartTimeCalculationMethod = null,
        string? PartTimeCalculationMethodNameVi = null,
        decimal? PartTimeUnitRate = null,
        string? CvUrlOrPath = null,
        string? ProfessionalSummary = null,
        string? Skills = null,
        string? Experience = null,
        DateTime? StatusChangedAt = null,
        string? StatusReason = null)
    {
        public string SalaryMethodDisplay => EmploymentType == 1
            ? $"GV/CTV · {PartTimeCalculationMethodNameVi ?? "Chưa chọn đơn vị tính"}"
            : "Lương tháng";

        public string SalaryDisplay => EmploymentType == 1
            ? PartTimeCalculationMethod == 2
                ? "Theo từng đầu việc"
                : $"{PartTimeUnitRate.GetValueOrDefault():N0} đ/{(PartTimeCalculationMethod == 1 ? "ca" : "giờ")}"
            : $"{BaseSalary:N0} đ/tháng";

        public string CvDisplay => string.IsNullOrWhiteSpace(CvUrlOrPath) ? "Chưa có CV" : CvUrlOrPath;
        public string StatusNameVi => Status.ToUpperInvariant() switch
        {
            "ACTIVE" => "Đang làm việc",
            "INACTIVE" => "Ngừng làm việc",
            "RESIGNED" => "Đã nghỉ việc",
            "PROBATION" => "Đang thử việc",
            "BLACKLISTED" or "BLACKLIST" => "Blacklist",
            "ONLEAVE" or "ON_LEAVE" => "Đang nghỉ phép",
            _ => Status
        };
    }

    public sealed record DepartmentItem(
        Guid Id,
        Guid CompanyId,
        string Code,
        string Name,
        int EmployeeCount,
        DateTime CreatedAt);

    public sealed record AttendanceItem(
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
        string? ImportBatchId)
    {
        public string StatusNameVi => Status.ToUpperInvariant() switch
        {
            "PRESENT" => "Đúng giờ",
            "LATE" => "Đi muộn",
            "ABSENT" => "Vắng mặt",
            "LEAVE" => "Nghỉ phép",
            _ => Status
        };
    }

    public sealed record RecordAttendanceModel(
        Guid EmployeeId,
        DateOnly Date,
        TimeOnly? CheckInTime,
        TimeOnly? CheckOutTime,
        decimal WorkHours,
        string Status);

    public sealed record AttendanceSummaryItem(
        int TotalRecords,
        int PresentCount,
        int LateCount,
        int AbsentCount,
        int LeaveCount,
        decimal TotalWorkHours);

    public sealed record AttendanceImportErrorItem(
        int RowNumber,
        string? EmployeeCode,
        string? DateString,
        string ErrorMessage,
        string RawLine);

    public sealed record AttendanceImportResultItem(
        string ImportBatchId,
        int TotalRowsProcessed,
        int SuccessCount,
        int ErrorCount,
        IReadOnlyList<AttendanceImportErrorItem> Errors);

}
