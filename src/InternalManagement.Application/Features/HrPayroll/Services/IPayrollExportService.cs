using InternalManagement.Application.Common.Models;

namespace InternalManagement.Application.Features.HrPayroll.Services;

/// <summary>
/// Creates downloadable payroll reports. The contract deliberately returns bytes
/// rather than exposing a storage URL: payroll data must stay inside the
/// authenticated request and must not become a public file by accident.
/// </summary>
public interface IPayrollExportService
{
    Task<Result<PayrollExportFile>> ExportPeriodXlsxAsync(Guid payrollPeriodId, CancellationToken ct);
}

public sealed record PayrollExportFile(
    byte[] Content,
    string ContentType,
    string FileName);
