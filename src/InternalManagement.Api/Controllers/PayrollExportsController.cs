using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.HrPayroll.Services;
using Microsoft.AspNetCore.Mvc;

namespace InternalManagement.Api.Controllers;

[ApiController]
[Route("api/v1/payroll-exports")]
public sealed class PayrollExportsController : ControllerBase
{
    private readonly IPayrollExportService _payrollExportService;

    public PayrollExportsController(IPayrollExportService payrollExportService)
    {
        _payrollExportService = payrollExportService;
    }

    /// <summary>Downloads one calculated payroll period as a protected Excel workbook.</summary>
    [HttpGet("periods/{id:guid}/xlsx")]
    [HasPermission(Permissions.PayrollViewAll)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportPeriodXlsx(Guid id, CancellationToken ct = default)
    {
        var result = await _payrollExportService.ExportPeriodXlsxAsync(id, ct);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors.FirstOrDefault() ?? "Không thể xuất Excel bảng lương.");
        }

        var file = result.Value!;
        return File(file.Content, file.ContentType, file.FileName);
    }
}
