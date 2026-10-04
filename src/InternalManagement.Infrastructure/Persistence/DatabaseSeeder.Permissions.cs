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
    private async Task SeedPermissionsAndRolesAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);

        // Seed all permissions
        var existingPermissionCodes = await _context.Permissions.Select(p => p.Code).ToListAsync(ct);
        var missingPermissions = Permissions.All
            .Where(code => !existingPermissionCodes.Contains(code))
            .Select(code =>
            {
                var parts = code.Split('.');
                var category = parts.Length > 1 ? parts[1] : "General";
                var name = parts.Length > 2 ? $"{parts[1]} {parts[2]}" : code;
                return new Permission
                {
                    Code = code,
                    Name = name,
                    Category = category
                };
            })
            .ToList();

        if (missingPermissions.Count > 0)
        {
            _context.Permissions.AddRange(missingPermissions);
            await _context.SaveChangesAsync(ct);
        }

        var allPermissions = await _context.Permissions.ToListAsync(ct);

        // Seed roles and role-permission mappings
        var roleDefinitions = new Dictionary<string, (string Name, string Desc, bool IsSystem, Func<string, bool> PermFilter)>
        {
            ["SuperAdmin"] = ("Admin Tổng", "Toàn quyền trên toàn bộ hệ thống", true, _ => true),
            ["SystemAdmin"] = ("Quản Trị Hệ Thống", "Quản trị tài khoản, role, cấu hình và đồng bộ", true, p =>
                p == Permissions.UsersView || p == Permissions.UsersManage ||
                p == Permissions.RolesView || p == Permissions.RolesManage ||
                p == Permissions.AuditLogsView || p == Permissions.SystemSyncView ||
                p == Permissions.SystemSyncTrigger || p == Permissions.DeadLettersManage),
            ["Director"] = ("Giám Đốc Điều Hành", "Toàn quyền xem báo cáo, duyệt cấp cao", true, p =>
                p.EndsWith(".View") || p.EndsWith(".ViewAll") || p.Contains("Report") || p == Permissions.PayrollApprove),
            ["HrStaff"] = ("Chuyên Viên Nhân Sự", "Quản lý nhân viên, chấm công", false, p =>
                p == Permissions.EmployeesView || p == Permissions.EmployeesManage || p == Permissions.AttendanceImport || p == Permissions.PayrollViewPersonal),
            ["PayrollAccountant"] = ("Kế Toán Tiền Lương", "Tính lương, phát hành bảng lương", false, p =>
                p == Permissions.EmployeesView || p == Permissions.AttendanceImport || p == Permissions.PayrollViewAll || p == Permissions.PayrollCalculate || p == Permissions.PayrollPublish || p == Permissions.PayrollViewPersonal),
            ["ContentEditor"] = ("Biên Tập Viên Nội Dung", "Soạn thảo và xuất bản khóa học, câu hỏi", false, p =>
                p.StartsWith("Permissions.Courses") || p.StartsWith("Permissions.Questions") ||
                p == Permissions.InternalResourcesView || p == Permissions.InternalResourcesManage),
            ["QuestionAuthor"] = ("Người Làm Đề", "Tạo và chỉnh sửa câu hỏi được phân công", false, p =>
                p == Permissions.QuestionsView || p == Permissions.QuestionsManage),
            ["ContentReviewer"] = ("Người Duyệt Nội Dung", "Kiểm tra và xuất bản phiên bản đề", false, p =>
                p == Permissions.CoursesView || p == Permissions.QuestionsView || p == Permissions.QuestionsPublish),
            ["Teacher"] = ("Giáo Viên / Giảng Viên", "Xem lớp và học sinh phụ trách", false, p =>
                p == Permissions.CscaClassesView || p == Permissions.CscaStudentsManage || p == Permissions.PayrollViewPersonal),
            ["TeachingAssistant"] = ("Trợ Giảng", "Xem lớp và học sinh được phân công", false, p =>
                p == Permissions.CscaClassesView || p == Permissions.PayrollViewPersonal),
            ["ClassCoordinator"] = ("Điều Phối Viên Lớp Học", "Quản lý lớp, xếp lịch và phân công nhân sự CSCA", false, p =>
                p.StartsWith("Permissions.Csca") || p.StartsWith("Permissions.Interview")),
            ["CustomerService"] = ("Chăm Sóc Khách Hàng", "Xem học viên và gói dịch vụ", false, p =>
                p == Permissions.EdTechCustomersView || p == Permissions.SubscriptionsManage || p == Permissions.InterviewCustomersView || p == Permissions.InterviewCustomersManage ||
                p == Permissions.InternalCustomersView || p == Permissions.InternalCustomersManage),
            ["WarehouseManager"] = ("Quản Lý Kho Vận", "Nhập xuất tồn kho và kiểm hàng hoàn", false, p =>
                p.StartsWith("Permissions.Products") || p.StartsWith("Permissions.Inventory") || p.StartsWith("Permissions.Materials") || p.StartsWith("Permissions.Manufacturing") || p == Permissions.OrdersView || p == Permissions.ReturnsManage ||
                p == Permissions.InternalResourcesView || p == Permissions.InternalResourcesManage),
            ["OrderManager"] = ("Quản Lý Đơn Hàng", "Xử lý đơn hàng bán lẻ và sàn TMĐT", false, p =>
                p.StartsWith("Permissions.Orders") || p == Permissions.ProductsView || p == Permissions.ReturnsManage ||
                p == Permissions.InternalCustomersView || p == Permissions.InternalCustomersManage),
            ["PaymentAccountant"] = ("Kế Toán Thu Chi", "Phiếu thu chi và dòng tiền", false, p =>
                p == Permissions.PaymentsView || p.StartsWith("Permissions.Finance") || p == Permissions.PricingSimulator || p == Permissions.ChannelFeePolicyManage),
            ["Employee"] = ("Nhân Viên Chuẩn", "Xem phiếu lương cá nhân", false, p =>
                p == Permissions.PayrollViewPersonal)
        };

        foreach (var (roleCode, def) in roleDefinitions)
        {
            var role = await _context.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.CompanyId == company.Id && r.Code == roleCode, ct);

            if (role == null)
            {
                role = new Role
                {
                    CompanyId = company.Id,
                    Code = roleCode,
                    Name = def.Name,
                    Description = def.Desc,
                    IsSystem = def.IsSystem
                };
                _context.Roles.Add(role);
                await _context.SaveChangesAsync(ct);
            }

            var allowedPermissions = allPermissions.Where(p => def.PermFilter(p.Code)).ToList();
            foreach (var perm in allowedPermissions)
            {
                if (!role.RolePermissions.Any(rp => rp.PermissionId == perm.Id))
                {
                    role.RolePermissions.Add(new RolePermission
                    {
                        RoleId = role.Id,
                        PermissionId = perm.Id
                    });
                }
            }
        }

        await _context.SaveChangesAsync(ct);
    }

}
