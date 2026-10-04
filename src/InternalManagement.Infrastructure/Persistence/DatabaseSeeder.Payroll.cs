using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Security;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence;

public partial class DatabaseSeeder
{
    private async Task SeedPayrollAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);

        // 1. Seed Payroll Policy Version
        if (!await _context.PayrollPolicyVersions.AnyAsync(ct))
        {
            _context.PayrollPolicyVersions.Add(new Domain.Entities.HrPayroll.PayrollPolicyVersion
            {
                CompanyId = company.Id,
                VersionNumber = 1,
                EffectiveDate = new DateOnly(2026, 1, 1),
                ConfigJson = "{\"InsuranceRates\":{\"Social\":0.08,\"Health\":0.015,\"Unemployment\":0.01},\"Allowances\":{\"Lunch\":730000,\"Fuel\":500000,\"Phone\":300000}}",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "system"
            });
            await _context.SaveChangesAsync(ct);
        }

        // 2. Seed Current Month Payroll Period
        var periodName = "Kỳ Lương Tháng 08/2026";
        var period = await _context.PayrollPeriods.FirstOrDefaultAsync(p => p.CompanyId == company.Id && p.Name == periodName, ct);
        if (period == null)
        {
            period = new Domain.Entities.HrPayroll.PayrollPeriod
            {
                CompanyId = company.Id,
                Name = periodName,
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 8, 31),
                Status = Domain.Enums.PayrollStatus.Draft,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "payroll_accountant"
            };
            _context.PayrollPeriods.Add(period);
            await _context.SaveChangesAsync(ct);

            // Seed Adjustments
            var devEmp = await _context.Employees.FirstOrDefaultAsync(e => e.CompanyId == company.Id && e.EmployeeCode == "EMP-DEV01", ct);
            if (devEmp != null)
            {
                _context.PayrollAdjustments.Add(new Domain.Entities.HrPayroll.PayrollAdjustment
                {
                    PayrollPeriodId = period.Id,
                    EmployeeId = devEmp.Id,
                    Type = "Bonus",
                    Amount = 3000000,
                    Reason = "Thưởng hoàn thành kiến trúc Microservices",
                    CreatedBy = "payroll_accountant",
                    CreatedAt = DateTime.UtcNow
                });
            }

            var hrEmp = await _context.Employees.FirstOrDefaultAsync(e => e.CompanyId == company.Id && e.EmployeeCode == "EMP-HR01", ct);
            if (hrEmp != null)
            {
                _context.PayrollAdjustments.Add(new Domain.Entities.HrPayroll.PayrollAdjustment
                {
                    PayrollPeriodId = period.Id,
                    EmployeeId = hrEmp.Id,
                    Type = "Allowance",
                    Amount = 1000000,
                    Reason = "Phụ cấp đi lại & điện thoại",
                    CreatedBy = "payroll_accountant",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync(ct);

            // Seed initial payslips
            var allEmps = await _context.Employees.Where(e => e.CompanyId == company.Id && !e.IsDeleted).ToListAsync(ct);
            decimal totalGross = 0;
            decimal totalNet = 0;

            foreach (var emp in allEmps)
            {
                var baseSal = emp.BaseSalary;
                var bonus = (emp.EmployeeCode == "EMP-DEV01") ? 3000000m : 0m;
                var allowance = (emp.EmployeeCode == "EMP-HR01") ? 1000000m : 0m;
                var gross = baseSal + bonus + allowance;
                var deductions = Math.Round(baseSal * 0.105m, 0); // 10.5% BHXH + BHYT + BHTN
                var net = gross - deductions;

                _context.Payslips.Add(new Domain.Entities.HrPayroll.Payslip
                {
                    PayrollPeriodId = period.Id,
                    EmployeeId = emp.Id,
                    BaseSalary = baseSal,
                    StandardWorkDays = 22.0m,
                    ActualWorkDays = 22.0m,
                    GrossSalary = gross,
                    Allowances = bonus + allowance,
                    Deductions = deductions,
                    NetSalary = net,
                    Status = Domain.Enums.PayrollStatus.Calculated,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "payroll_accountant"
                });

                totalGross += gross;
                totalNet += net;
            }

            period.Status = Domain.Enums.PayrollStatus.Calculated;
            period.TotalGrossAmount = totalGross;
            period.TotalNetAmount = totalNet;
            period.CalculatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Dữ liệu demo dùng để kiểm tra bốn màn hình thuộc mảng Công nghệ - Giáo dục:
    /// khóa học, bài thi & câu hỏi, học viên đồng bộ và bảng lương EdTech.
    /// Các mã nguồn cố định giúp seeder chạy lặp mà không tạo bản ghi trùng.
    /// </summary>
}
