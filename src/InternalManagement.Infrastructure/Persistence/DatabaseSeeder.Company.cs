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
    private async Task SeedCompanyAndBusinessUnitsAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
        if (company == null)
        {
            company = new Company
            {
                Code = "MOLI",
                Name = "MOLI Group",
                TaxNumber = "0109998888",
                Address = "Hà Nội, Việt Nam",
                IsActive = true
            };
            _context.Companies.Add(company);
            await _context.SaveChangesAsync(ct);
        }

        var bus = new[]
        {
            ("EDTECH", "MOLI EdTech", "Nền tảng học tập & thi trực tuyến EdTech"),
            ("CSCA", "MOLI CSCA Academy", "Học viện đào tạo CSCA Online"),
            ("INTERVIEW", "MOLI Mock Interview", "Dịch vụ phỏng vấn thử & hướng nghiệp"),
            ("FASHION", "MOLI Fashion & Retail", "Thời trang & Bán lẻ đa kênh"),
            ("HQ", "MOLI Headquarter", "Trụ sở chính & Vận hành chung")
        };

        foreach (var (code, name, desc) in bus)
        {
            if (!await _context.BusinessUnits.AnyAsync(b => b.CompanyId == company.Id && b.Code == code, ct))
            {
                _context.BusinessUnits.Add(new BusinessUnit
                {
                    CompanyId = company.Id,
                    Code = code,
                    Name = name,
                    Description = desc,
                    IsActive = true
                });
            }
        }

        var depts = new[]
        {
            ("BOD", "Ban Giám Đốc"),
            ("TECH", "Phòng Công Nghệ"),
            ("HR", "Phòng Nhân Sự"),
            ("ACC", "Phòng Kế Toán & Tài Chính"),
            ("ACAD", "Phòng Học Thuật & Đào Tạo"),
            ("WH", "Phòng Kho Vận & Chuỗi Cung Ứng"),
            ("CS", "Phòng Chăm Sóc Khách Hàng")
        };

        foreach (var (code, name) in depts)
        {
            if (!await _context.Departments.AnyAsync(d => d.CompanyId == company.Id && d.Code == code, ct))
            {
                _context.Departments.Add(new Department
                {
                    CompanyId = company.Id,
                    Code = code,
                    Name = name
                });
            }
        }

        await _context.SaveChangesAsync(ct);
    }

}
