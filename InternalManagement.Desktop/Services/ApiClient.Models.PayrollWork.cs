namespace InternalManagement.Desktop.Services;

public sealed partial class ApiClient
{
    public sealed record PayrollWorkEntryItem(
        Guid Id,
        Guid PayrollPeriodId,
        Guid EmployeeId,
        string EmployeeCode,
        string EmployeeName,
        string WorkType,
        string ReferenceCode,
        string Title,
        string? EvidenceUrl,
        DateOnly WorkDate,
        decimal Quantity,
        decimal UnitRate,
        decimal Amount,
        bool IsVoided,
        DateTime CreatedAt)
    {
        public string WorkTypeNameVi => WorkType switch
        {
            "QUESTION_POSTED" => "Đề đã đăng",
            "QUESTION_COMPLETED" => "Đề đã hoàn thành",
            "PROJECT" => "Dự án / hạng mục",
            "SALES_COMMISSION" => "Hoa hồng sale",
            "STUDENT_REFERRAL" => "Học viên giới thiệu / tuyển được",
            "MARKETING" => "Marketing",
            _ => "Đầu việc khác"
        };

        public string StatusNameVi => IsVoided ? "Đã hủy" : "Đã cộng lương";
    }

    public sealed record CreatePayrollWorkEntryModel(
        Guid EmployeeId,
        string WorkType,
        string ReferenceCode,
        string Title,
        DateOnly WorkDate,
        decimal Quantity,
        decimal UnitRate,
        string? EvidenceUrl);
}
