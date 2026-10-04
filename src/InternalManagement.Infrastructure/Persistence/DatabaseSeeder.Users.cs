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
    private async Task SeedUsersAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var bus = await _context.BusinessUnits.Where(b => b.CompanyId == company.Id).ToDictionaryAsync(b => b.Code, b => b.Id, ct);
        var roles = await _context.Roles.Where(r => r.CompanyId == company.Id).ToDictionaryAsync(r => r.Code, r => r, ct);

        var usersToSeed = new[]
        {
            ("admin", "admin@moli.local", "Admin@123456", "Admin Tổng MOLI", "HQ", "SuperAdmin"),
            ("director", "director@moli.local", "Director@123456", "Giám Đốc MOLI", "HQ", "Director"),
            ("hr_staff", "hr@moli.local", "Hr@123456", "Nhân Viên Nhân Sự", "HQ", "HrStaff"),
            ("payroll_acc", "payroll@moli.local", "Payroll@123456", "Kế Toán Lương", "HQ", "PayrollAccountant"),
            ("content_editor", "editor@moli.local", "Editor@123456", "Biên Tập Viên EdTech", "EDTECH", "ContentEditor"),
            ("question_author", "author@moli.local", "Author@123456", "Người Làm Đề EdTech", "EDTECH", "QuestionAuthor"),
            ("content_reviewer", "reviewer@moli.local", "Reviewer@123456", "Người Duyệt Nội Dung", "EDTECH", "ContentReviewer"),
            ("coordinator", "coordinator@moli.local", "Coordinator@123456", "Điều Phối CSCA & Interview", "CSCA", "ClassCoordinator"),
            ("teacher", "teacher@moli.local", "Teacher@123456", "Giáo Viên CSCA", "CSCA", "Teacher"),
            ("teaching_assistant", "ta@moli.local", "Ta@123456", "Trợ Giảng CSCA", "CSCA", "TeachingAssistant"),
            ("warehouse_mgr", "warehouse@moli.local", "Warehouse@123456", "Thủ Kho Fashion", "FASHION", "WarehouseManager"),
            ("employee", "employee@moli.local", "Employee@123456", "Nhân Viên MOLI", "EDTECH", "Employee")
        };

        foreach (var (username, email, password, fullName, buCode, roleCode) in usersToSeed)
        {
            var user = await _context.Users
                .Include(u => u.UserRoles)
                .FirstOrDefaultAsync(u => u.Username == username, ct);

            if (user == null)
            {
                user = new User
                {
                    CompanyId = company.Id,
                    DefaultBusinessUnitId = bus.TryGetValue(buCode, out var buId) ? buId : null,
                    Username = username,
                    Email = email,
                    PasswordHash = _passwordHasher.HashPassword(password),
                    FullName = fullName,
                    IsActive = true
                };

                _context.Users.Add(user);
            }

            if (roles.TryGetValue(roleCode, out var assignedRole) &&
                !user.UserRoles.Any(ur => ur.RoleId == assignedRole.Id))
            {
                user.UserRoles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = assignedRole.Id
                });
            }
        }

        await _context.SaveChangesAsync(ct);
    }

}
