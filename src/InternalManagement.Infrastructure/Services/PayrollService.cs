using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using InternalManagement.Application.Features.Finance.Services;
using InternalManagement.Application.Features.MasterData.Services;
using InternalManagement.Domain.Entities.Documents;
using InternalManagement.Domain.Entities.HrPayroll;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed partial class PayrollService : IPayrollService
{
    private readonly IPayrollDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<PayrollService> _logger;
    private readonly IBusinessDocumentRegistry? _documentRegistry;
    private readonly IFinancePostingService? _financePostingService;

    public PayrollService(
        IPayrollDbContext db,
        ICurrentUserService currentUser,
        ILogger<PayrollService> logger,
        IBusinessDocumentRegistry? documentRegistry = null,
        IFinancePostingService? financePostingService = null)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
        _documentRegistry = documentRegistry;
        _financePostingService = financePostingService;
    }

    public IReadOnlyList<PayrollComponentTypeDto> GetPayrollComponentTypes() =>
    [
        new(PayrollComponentCodes.Allowance, "Trợ cấp", "INCOME", "Khoản trợ cấp được cộng vào tổng thu nhập."),
        new(PayrollComponentCodes.KpiBonus, "Thưởng KPI", "INCOME", "Thưởng theo kết quả đánh giá KPI."),
        new(PayrollComponentCodes.Bonus, "Thưởng khác", "INCOME", "Khoản thưởng khác ngoài KPI."),
        new(PayrollComponentCodes.Overtime, "Tiền làm thêm giờ", "INCOME", "Thu nhập do làm thêm giờ."),
        new(PayrollComponentCodes.HealthInsurance, "Bảo hiểm y tế (BHYT)", "DEDUCTION", "Khoản BHYT khấu trừ vào lương."),
        new(PayrollComponentCodes.Deduction, "Khấu trừ khác", "DEDUCTION", "Khoản khấu trừ khác ngoài BHYT.")
    ];

    private async Task<Guid> GetCompanyIdAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        return companyId.Value;
    }

}
