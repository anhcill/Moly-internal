using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Security;
using InternalManagement.Domain.Entities.Identity;
using InternalManagement.Domain.Entities.Fashion;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Persistence;

public class DatabaseSeeder : IDatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        ApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await SeedCompanyAndBusinessUnitsAsync(cancellationToken);
            await SeedPermissionsAndRolesAsync(cancellationToken);
            await SeedUsersAsync(cancellationToken);
            await SeedCscaAndInterviewAsync(cancellationToken);
            await SeedEmployeesAndAttendanceAsync(cancellationToken);
            await SeedPayrollAsync(cancellationToken);
            await SeedEdTechDemoDataAsync(cancellationToken);
            await SeedFashionAndInventoryAsync(cancellationToken);
            await SeedManufacturingCostingAsync(cancellationToken);
            _logger.LogInformation("Database seeded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

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

    private async Task SeedCscaAndInterviewAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var cscaBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "CSCA", ct);
        var interviewBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "INTERVIEW", ct);
        var acadDept = await _context.Departments.FirstOrDefaultAsync(d => d.CompanyId == company.Id && d.Code == "ACAD", ct);

        // Seed Employees for teaching if not exist
        var teacherUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == "teacher", ct);
        var teacherEmp = await _context.Employees.FirstOrDefaultAsync(e => e.CompanyId == company.Id && (e.EmployeeCode == "EMP-TCH01" || (teacherUser != null && e.UserId == teacherUser.Id)), ct);
        if (teacherEmp == null)
        {
            teacherEmp = new Domain.Entities.HrPayroll.Employee
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                DepartmentId = acadDept?.Id,
                UserId = teacherUser?.Id,
                EmployeeCode = "EMP-TCH01",
                FullName = "Nguyễn Văn Thầy (Giảng Viên Cao Cấp)",
                Email = "teacher@moli.local",
                Phone = "0988111222",
                Position = "Giảng Viên Trưởng",
                BaseSalary = 15000000,
                JoinedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "Active"
            };
            _context.Employees.Add(teacherEmp);
            await _context.SaveChangesAsync(ct);
        }

        var taUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == "teaching_assistant", ct);
        var taEmp = await _context.Employees.FirstOrDefaultAsync(e => e.CompanyId == company.Id && (e.EmployeeCode == "EMP-TA01" || (taUser != null && e.UserId == taUser.Id)), ct);
        if (taEmp == null)
        {
            taEmp = new Domain.Entities.HrPayroll.Employee
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                DepartmentId = acadDept?.Id,
                UserId = taUser?.Id,
                EmployeeCode = "EMP-TA01",
                FullName = "Trần Thị Trợ Giảng (Mentor CSCA)",
                Email = "ta@moli.local",
                Phone = "0977333444",
                Position = "Trợ Giảng",
                BaseSalary = 8000000,
                JoinedDate = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = "Active"
            };
            _context.Employees.Add(taEmp);
            await _context.SaveChangesAsync(ct);
        }

        // Seed CSCA Class 1
        var cscaCourse = await _context.Courses.FirstOrDefaultAsync(c =>
            c.CompanyId == company.Id && c.CourseSourceId == "CSCA-PROGRAM-001", ct);
        if (cscaCourse == null)
        {
            cscaCourse = new Domain.Entities.EdTech.Course
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                CourseSourceId = "CSCA-PROGRAM-001",
                Title = "Chương trình đào tạo CSCA",
                Slug = "chuong-trinh-dao-tao-csca",
                Description = "Khóa học cha dùng để quản lý các lớp CSCA.",
                Price = 0,
                Status = "Published",
                CreatedBy = "admin"
            };
            _context.Courses.Add(cscaCourse);
            await _context.SaveChangesAsync(ct);
        }

        var class1 = await _context.CscaClasses.FirstOrDefaultAsync(c => c.CompanyId == company.Id && c.Code == "CSCA-2026-K01", ct);
        if (class1 != null && class1.CourseId == Guid.Empty)
        {
            class1.CourseId = cscaCourse.Id;
            await _context.SaveChangesAsync(ct);
        }
        if (class1 == null)
        {
            class1 = new Domain.Entities.CscaInterview.CscaClass
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                CourseId = cscaCourse.Id,
                Code = "CSCA-2026-K01",
                Name = "CSCA Foundation K01 — Lập Trình Cơ Bản",
                Batch = "Đợt 1 - Xuân 2026",
                Schedule = "T3 - T5 (19h30 - 21h30)",
                TuitionFee = 4500000,
                StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 5, 31, 0, 0, 0, DateTimeKind.Utc),
                Status = "Active",
                CreatedBy = "admin"
            };
            _context.CscaClasses.Add(class1);
            await _context.SaveChangesAsync(ct);

            // Students
            _context.CscaClassStudents.AddRange(
                new Domain.Entities.CscaInterview.CscaClassStudent
                {
                    ClassId = class1.Id,
                    StudentName = "Nguyễn Hoàng Nam",
                    Email = "nam.nguyen@gmail.com",
                    PhoneNumber = "0912111333",
                    PaidAmount = 4500000,
                    PaymentStatus = Domain.Enums.PaymentStatus.Paid,
                    Notes = "Đã thanh toán đủ qua chuyển khoản"
                },
                new Domain.Entities.CscaInterview.CscaClassStudent
                {
                    ClassId = class1.Id,
                    StudentName = "Lê Bảo Ngọc",
                    Email = "ngoc.le@gmail.com",
                    PhoneNumber = "0988444555",
                    PaidAmount = 4500000,
                    PaymentStatus = Domain.Enums.PaymentStatus.Paid,
                    Notes = "Đã thanh toán đủ qua thẻ tín dụng"
                },
                new Domain.Entities.CscaInterview.CscaClassStudent
                {
                    ClassId = class1.Id,
                    StudentName = "Vũ Minh Quân",
                    Email = "quan.vu@gmail.com",
                    PhoneNumber = "0909666777",
                    PaidAmount = 2000000,
                    PaymentStatus = Domain.Enums.PaymentStatus.Partial,
                    Notes = "Đã cọc 2.000.000 đ, còn nợ 2.500.000 đ"
                }
            );

            // Staff
            _context.CscaClassStaffs.AddRange(
                new Domain.Entities.CscaInterview.CscaClassStaff
                {
                    ClassId = class1.Id,
                    EmployeeId = teacherEmp.Id,
                    RoleInClass = "Teacher",
                    CompensationRate = 3500000,
                    Notes = "Phụ trách giảng dạy chính 24 buổi"
                },
                new Domain.Entities.CscaInterview.CscaClassStaff
                {
                    ClassId = class1.Id,
                    EmployeeId = taEmp.Id,
                    RoleInClass = "TeachingAssistant",
                    CompensationRate = 1200000,
                    Notes = "Chấm bài tập và hỗ trợ lab"
                }
            );

            // Profit Allocation
            _context.ProfitAllocations.Add(new Domain.Entities.CscaInterview.ProfitAllocation
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                ReferenceType = "CscaClass",
                ReferenceId = class1.Id,
                IncomeAmount = 11000000,
                ExpenseAmount = 4700000,
                AllocatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
        }

        // Seed CSCA Class 2
        var class2 = await _context.CscaClasses.FirstOrDefaultAsync(c => c.CompanyId == company.Id && c.Code == "CSCA-2026-K02", ct);
        if (class2 != null && class2.CourseId == Guid.Empty)
        {
            class2.CourseId = cscaCourse.Id;
            await _context.SaveChangesAsync(ct);
        }
        if (class2 == null)
        {
            class2 = new Domain.Entities.CscaInterview.CscaClass
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                CourseId = cscaCourse.Id,
                Code = "CSCA-2026-K02",
                Name = "CSCA Advanced K02 — Cấu Trúc Dữ Liệu & Giải Thuật",
                Batch = "Đợt 1 - Xuân 2026",
                Schedule = "T7 - CN (09h00 - 11h00)",
                TuitionFee = 6000000,
                StartDate = new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = "Active",
                CreatedBy = "admin"
            };
            _context.CscaClasses.Add(class2);
            await _context.SaveChangesAsync(ct);

            // Students
            _context.CscaClassStudents.AddRange(
                new Domain.Entities.CscaInterview.CscaClassStudent
                {
                    ClassId = class2.Id,
                    StudentName = "Phạm Hải Đăng",
                    Email = "dang.pham@gmail.com",
                    PhoneNumber = "0933777888",
                    PaidAmount = 6000000,
                    PaymentStatus = Domain.Enums.PaymentStatus.Paid,
                    Notes = "Đã thanh toán 100%"
                },
                new Domain.Entities.CscaInterview.CscaClassStudent
                {
                    ClassId = class2.Id,
                    StudentName = "Đỗ Gia Huy",
                    Email = "huy.do@gmail.com",
                    PhoneNumber = "0944888999",
                    PaidAmount = 6000000,
                    PaymentStatus = Domain.Enums.PaymentStatus.Paid,
                    Notes = "Đã thanh toán 100%"
                }
            );

            // Staff
            _context.CscaClassStaffs.Add(new Domain.Entities.CscaInterview.CscaClassStaff
            {
                ClassId = class2.Id,
                EmployeeId = teacherEmp.Id,
                RoleInClass = "Teacher",
                CompensationRate = 4000000,
                Notes = "Giảng dạy chuyên sâu Thuật toán"
            });

            // Profit Allocation
            _context.ProfitAllocations.Add(new Domain.Entities.CscaInterview.ProfitAllocation
            {
                CompanyId = company.Id,
                BusinessUnitId = cscaBu?.Id,
                ReferenceType = "CscaClass",
                ReferenceId = class2.Id,
                IncomeAmount = 12000000,
                ExpenseAmount = 4000000,
                AllocatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync(ct);
        }

        // Seed Interview Customers
        if (!await _context.InterviewCustomers.AnyAsync(ct))
        {
            var customer1 = new Domain.Entities.CscaInterview.InterviewCustomer
            {
                CompanyId = company.Id,
                BusinessUnitId = interviewBu?.Id,
                SourceSystem = "WEBSITE_INTERVIEW",
                SourceId = "INT_2026_001",
                FullName = "Đặng Quốc Hưng",
                Email = "hung.dang@gmail.com",
                Phone = "0912345678",
                PackageName = "Mock Interview FAANG Standard",
                SessionCount = 2,
                PaidAmount = 2500000,
                Status = Domain.Enums.PaymentStatus.Paid,
                CreatedBy = "admin"
            };

            var customer2 = new Domain.Entities.CscaInterview.InterviewCustomer
            {
                CompanyId = company.Id,
                BusinessUnitId = interviewBu?.Id,
                SourceSystem = "WEBSITE_INTERVIEW",
                SourceId = "INT_2026_002",
                FullName = "Trương Mỹ Linh",
                Email = "linh.truong@gmail.com",
                Phone = "0987654321",
                PackageName = "System Design & Leadership VIP",
                SessionCount = 4,
                PaidAmount = 5000000,
                Status = Domain.Enums.PaymentStatus.Paid,
                CreatedBy = "admin"
            };

            var customer3 = new Domain.Entities.CscaInterview.InterviewCustomer
            {
                CompanyId = company.Id,
                BusinessUnitId = interviewBu?.Id,
                SourceSystem = "WEBSITE_INTERVIEW",
                SourceId = "INT_2026_003",
                FullName = "Hoàng Đức Anh",
                Email = "anh.hoang@gmail.com",
                Phone = "0901122334",
                PackageName = "Review CV & Mock 1 Buổi",
                SessionCount = 1,
                PaidAmount = 1200000,
                Status = Domain.Enums.PaymentStatus.Paid,
                CreatedBy = "admin"
            };

            _context.InterviewCustomers.AddRange(customer1, customer2, customer3);
            await _context.SaveChangesAsync(ct);

            _context.ProfitAllocations.AddRange(
                new Domain.Entities.CscaInterview.ProfitAllocation
                {
                    CompanyId = company.Id,
                    BusinessUnitId = interviewBu?.Id,
                    ReferenceType = "InterviewCustomer",
                    ReferenceId = customer1.Id,
                    IncomeAmount = 2500000,
                    ExpenseAmount = 0,
                    AllocatedAt = DateTime.UtcNow
                },
                new Domain.Entities.CscaInterview.ProfitAllocation
                {
                    CompanyId = company.Id,
                    BusinessUnitId = interviewBu?.Id,
                    ReferenceType = "InterviewCustomer",
                    ReferenceId = customer2.Id,
                    IncomeAmount = 5000000,
                    ExpenseAmount = 0,
                    AllocatedAt = DateTime.UtcNow
                },
                new Domain.Entities.CscaInterview.ProfitAllocation
                {
                    CompanyId = company.Id,
                    BusinessUnitId = interviewBu?.Id,
                    ReferenceType = "InterviewCustomer",
                    ReferenceId = customer3.Id,
                    IncomeAmount = 1200000,
                    ExpenseAmount = 0,
                    AllocatedAt = DateTime.UtcNow
                }
            );

            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task SeedEmployeesAndAttendanceAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var techDept = await _context.Departments.FirstOrDefaultAsync(d => d.CompanyId == company.Id && d.Code == "TECH", ct);
        var hrDept = await _context.Departments.FirstOrDefaultAsync(d => d.CompanyId == company.Id && d.Code == "HR", ct);
        var accDept = await _context.Departments.FirstOrDefaultAsync(d => d.CompanyId == company.Id && d.Code == "ACC", ct);
        var csDept = await _context.Departments.FirstOrDefaultAsync(d => d.CompanyId == company.Id && d.Code == "CS", ct);
        var whDept = await _context.Departments.FirstOrDefaultAsync(d => d.CompanyId == company.Id && d.Code == "WH", ct);

        var edtechBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "EDTECH", ct);
        var fashionBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "FASHION", ct);
        var hqBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "HQ", ct);

        var employeesToSeed = new (string Code, string Name, string Email, string Phone, string Pos, decimal Sal, Guid? DeptId, Guid? BuId, string RoleUser)[]
        {
            ("EMP-DEV01", "Lê Hoàng Long", "long.le@moli.local", "0908112233", "Tech Lead / Solution Architect", 25000000, techDept?.Id, edtechBu?.Id, "admin"),
            ("EMP-HR01", "Nguyễn Thu Trang", "trang.nguyen@moli.local", "0918223344", "Chuyên viên Tuyển dụng & C&B", 12000000, hrDept?.Id, hqBu?.Id, "hr_staff"),
            ("EMP-ACC01", "Phạm Quỳnh Chi", "chi.pham@moli.local", "0928334455", "Kế toán Tổng hợp & Thu chi", 14000000, accDept?.Id, hqBu?.Id, "payroll_accountant"),
            ("EMP-CS01", "Vũ Hoàng My", "my.vu@moli.local", "0938445566", "Trưởng nhóm Chăm sóc Khách hàng", 10000000, csDept?.Id, edtechBu?.Id, "customer_service"),
            ("EMP-WH01", "Bùi Văn Nam", "nam.bui@moli.local", "0948556677", "Thủ kho & Điều vận Thời trang", 9500000, whDept?.Id, fashionBu?.Id, "warehouse_manager")
        };

        foreach (var empDef in employeesToSeed)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == empDef.RoleUser, ct);
            var emp = await _context.Employees.FirstOrDefaultAsync(e => e.CompanyId == company.Id && (e.EmployeeCode == empDef.Code || (user != null && e.UserId == user.Id)), ct);
            if (emp == null)
            {
                emp = new Domain.Entities.HrPayroll.Employee
                {
                    CompanyId = company.Id,
                    BusinessUnitId = empDef.BuId,
                    DepartmentId = empDef.DeptId,
                    UserId = user?.Id,
                    EmployeeCode = empDef.Code,
                    FullName = empDef.Name,
                    Email = empDef.Email,
                    Phone = empDef.Phone,
                    Position = empDef.Pos,
                    BaseSalary = empDef.Sal,
                    JoinedDate = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    Status = "Active"
                };
                _context.Employees.Add(emp);
            }
        }
        await _context.SaveChangesAsync(ct);

        // Seed Attendance Records for the current week if none exist
        if (!await _context.AttendanceRecords.AnyAsync(ct))
        {
            var allEmps = await _context.Employees.Where(e => e.CompanyId == company.Id).ToListAsync(ct);
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var batchId = $"ATT-SEED-{today:yyyyMMdd}";

            foreach (var emp in allEmps)
            {
                for (int i = 4; i >= 0; i--)
                {
                    var date = today.AddDays(-i);
                    // Skip weekends
                    if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday) continue;

                    var isLate = (emp.EmployeeCode.GetHashCode() + i) % 7 == 0;
                    _context.AttendanceRecords.Add(new Domain.Entities.HrPayroll.AttendanceRecord
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = emp.BusinessUnitId,
                        EmployeeId = emp.Id,
                        Date = date,
                        CheckInTime = isLate ? new TimeOnly(8, 45) : new TimeOnly(8, 15),
                        CheckOutTime = new TimeOnly(17, 30),
                        WorkHours = isLate ? 7.75m : 8.0m,
                        Status = isLate ? "Late" : "Present",
                        ImportBatchId = batchId
                    });
                }
            }
            await _context.SaveChangesAsync(ct);
        }
    }

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
    private async Task SeedEdTechDemoDataAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var edtechBu = await _context.BusinessUnits.FirstAsync(
            b => b.CompanyId == company.Id && b.Code == "EDTECH", ct);

        const string demoSourceSystem = "DEMO_SEED";
        const string demoCreatedBy = "demo_seed";

        // 1. Khóa học mẫu với các module bài học.
        const string courseSourceId = "DEMO-EDTECH-CSHARP-001";
        var course = await _context.Courses
            .Include(c => c.Modules)
            .FirstOrDefaultAsync(c => c.CourseSourceId == courseSourceId, ct);

        if (course == null)
        {
            course = new Domain.Entities.EdTech.Course
            {
                CompanyId = company.Id,
                BusinessUnitId = edtechBu.Id,
                CourseSourceId = courseSourceId,
                Title = "Lập trình C# thực chiến cho người mới",
                Slug = "lap-trinh-csharp-thuc-chien-cho-nguoi-moi",
                Description = "Khóa học mẫu giúp kiểm tra luồng quản lý khóa học, module và học phí trên EdTech.",
                ThumbnailUrl = "https://images.unsplash.com/photo-1516321318423-f06f85e504b3?w=800",
                Price = 1490000,
                Status = "Published",
                Version = 1,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = demoCreatedBy
            };
            _context.Courses.Add(course);
            await _context.SaveChangesAsync(ct);
        }

        var moduleTitles = new[]
        {
            "Làm quen C# và .NET",
            "Biến, kiểu dữ liệu và câu lệnh điều kiện",
            "Lập trình hướng đối tượng",
            "Xây dựng API đầu tiên với ASP.NET Core"
        };

        foreach (var (title, index) in moduleTitles.Select((title, index) => (title, index)))
        {
            if (!course.Modules.Any(m => m.OrderIndex == index))
            {
                _context.CourseModules.Add(new Domain.Entities.EdTech.CourseModule
                {
                    CourseId = course.Id,
                    Title = title,
                    OrderIndex = index
                });
            }
        }
        await _context.SaveChangesAsync(ct);

        // 2. Ngân hàng đề và bốn câu hỏi mẫu đã xuất bản.
        var questionBank = await _context.QuestionBanks.FirstOrDefaultAsync(
            b => b.CompanyId == company.Id && b.Name == "Ngân hàng đề C# cơ bản · Dữ liệu mẫu", ct);
        if (questionBank == null)
        {
            questionBank = new Domain.Entities.EdTech.QuestionBank
            {
                CompanyId = company.Id,
                BusinessUnitId = edtechBu.Id,
                Name = "Ngân hàng đề C# cơ bản · Dữ liệu mẫu",
                Description = "Bộ câu hỏi demo để kiểm tra tìm kiếm, độ khó, đáp án và quy trình xuất bản.",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = demoCreatedBy
            };
            _context.QuestionBanks.Add(questionBank);
            await _context.SaveChangesAsync(ct);
        }

        var subject = await _context.Subjects.FirstOrDefaultAsync(
            s => s.CompanyId == company.Id && s.Code == "CSHARP", ct);
        if (subject == null)
        {
            subject = new Domain.Entities.EdTech.Subject
            {
                CompanyId = company.Id,
                BusinessUnitId = edtechBu.Id,
                Code = "CSHARP",
                Name = "Lập trình C#"
            };
            _context.Subjects.Add(subject);
            await _context.SaveChangesAsync(ct);
        }

        var topicDefinitions = new[]
        {
            (Name: "Cú pháp cơ bản", Code: "CSHARP-BASIC"),
            (Name: "Lập trình hướng đối tượng", Code: "CSHARP-OOP"),
            (Name: "ASP.NET Core", Code: "CSHARP-ASPNET")
        };

        var topics = new Dictionary<string, Domain.Entities.EdTech.Topic>();
        foreach (var topicDefinition in topicDefinitions)
        {
            var topic = await _context.Topics.FirstOrDefaultAsync(
                t => t.SubjectId == subject.Id && t.Name == topicDefinition.Name, ct);
            if (topic == null)
            {
                topic = new Domain.Entities.EdTech.Topic
                {
                    SubjectId = subject.Id,
                    Name = topicDefinition.Name
                };
                _context.Topics.Add(topic);
                await _context.SaveChangesAsync(ct);
            }
            topics[topicDefinition.Code] = topic;
        }

        var questionDefinitions = new[]
        {
            new
            {
                SourceId = "DEMO-Q-CSHARP-001",
                TopicCode = "CSHARP-BASIC",
                Difficulty = "Easy",
                Content = "Từ khóa nào dùng để khai báo một biến hằng số trong C#?",
                Explanation = "Từ khóa const tạo ra một giá trị không thể thay đổi sau khi biên dịch.",
                Correct = "B",
                Tags = new[] { "C#", "Cơ bản", "Biến" },
                Choices = new[]
                {
                    (Label: "A", Content: "static", IsCorrect: false),
                    (Label: "B", Content: "const", IsCorrect: true),
                    (Label: "C", Content: "readonly", IsCorrect: false),
                    (Label: "D", Content: "fixed", IsCorrect: false)
                }
            },
            new
            {
                SourceId = "DEMO-Q-CSHARP-002",
                TopicCode = "CSHARP-BASIC",
                Difficulty = "Medium",
                Content = "Kiểu dữ liệu nào phù hợp để lưu một giá trị đúng hoặc sai?",
                Explanation = "bool chỉ nhận một trong hai giá trị true hoặc false.",
                Correct = "C",
                Tags = new[] { "C#", "Kiểu dữ liệu" },
                Choices = new[]
                {
                    (Label: "A", Content: "int", IsCorrect: false),
                    (Label: "B", Content: "string", IsCorrect: false),
                    (Label: "C", Content: "bool", IsCorrect: true),
                    (Label: "D", Content: "decimal", IsCorrect: false)
                }
            },
            new
            {
                SourceId = "DEMO-Q-CSHARP-003",
                TopicCode = "CSHARP-OOP",
                Difficulty = "Medium",
                Content = "Tính chất nào cho phép lớp con sử dụng lại hành vi của lớp cha?",
                Explanation = "Inheritance (kế thừa) cho phép lớp con kế thừa thành viên từ lớp cha.",
                Correct = "A",
                Tags = new[] { "C#", "OOP", "Kế thừa" },
                Choices = new[]
                {
                    (Label: "A", Content: "Kế thừa", IsCorrect: true),
                    (Label: "B", Content: "Đóng gói", IsCorrect: false),
                    (Label: "C", Content: "Nạp chồng", IsCorrect: false),
                    (Label: "D", Content: "Ép kiểu", IsCorrect: false)
                }
            },
            new
            {
                SourceId = "DEMO-Q-CSHARP-004",
                TopicCode = "CSHARP-ASPNET",
                Difficulty = "Hard",
                Content = "Trong ASP.NET Core, thành phần nào dùng để đăng ký dịch vụ vào Dependency Injection container?",
                Explanation = "Các dịch vụ thường được đăng ký thông qua IServiceCollection trong phần cấu hình ứng dụng.",
                Correct = "D",
                Tags = new[] { "ASP.NET Core", "Dependency Injection", "Web API" },
                Choices = new[]
                {
                    (Label: "A", Content: "IHostEnvironment", IsCorrect: false),
                    (Label: "B", Content: "IConfiguration", IsCorrect: false),
                    (Label: "C", Content: "IWebHost", IsCorrect: false),
                    (Label: "D", Content: "IServiceCollection", IsCorrect: true)
                }
            }
        };

        foreach (var questionDefinition in questionDefinitions)
        {
            var question = await _context.Questions
                .Include(q => q.Versions)
                    .ThenInclude(v => v.Choices)
                .Include(q => q.Tags)
                .FirstOrDefaultAsync(q => q.SourceSystem == demoSourceSystem && q.SourceId == questionDefinition.SourceId, ct);

            if (question != null)
            {
                continue;
            }

            var questionVersion = new Domain.Entities.EdTech.QuestionVersion
            {
                VersionNumber = 1,
                ContentHtml = questionDefinition.Content,
                ExplanationHtml = questionDefinition.Explanation,
                Status = Domain.Enums.QuestionPublicationStatus.Published,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = demoCreatedBy
            };

            question = new Domain.Entities.EdTech.Question
            {
                SourceSystem = demoSourceSystem,
                SourceId = questionDefinition.SourceId,
                QuestionBankId = questionBank.Id,
                SubjectId = subject.Id,
                TopicId = topics[questionDefinition.TopicCode].Id,
                DifficultyLevel = questionDefinition.Difficulty,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = demoCreatedBy,
                Versions = new List<Domain.Entities.EdTech.QuestionVersion> { questionVersion }
            };

            _context.Questions.Add(question);
            await _context.SaveChangesAsync(ct);

            question.CurrentVersionId = questionVersion.Id;
            _context.QuestionChoices.AddRange(questionDefinition.Choices.Select((choice, index) =>
                new Domain.Entities.EdTech.QuestionChoice
                {
                    QuestionVersionId = questionVersion.Id,
                    Label = choice.Label,
                    ContentHtml = choice.Content,
                    IsCorrect = choice.IsCorrect,
                    OrderIndex = index
                }));
            _context.QuestionTags.AddRange(questionDefinition.Tags.Select(tag =>
                new Domain.Entities.EdTech.QuestionTag
                {
                    QuestionId = question.Id,
                    Tag = tag
                }));
            _context.ContentPublications.Add(new Domain.Entities.EdTech.ContentPublication
            {
                QuestionVersionId = questionVersion.Id,
                PublishedBy = demoCreatedBy,
                PublishedAt = DateTime.UtcNow,
                Notes = "Dữ liệu mẫu đã được xuất bản để kiểm tra giao diện."
            });
            await _context.SaveChangesAsync(ct);
        }

        // 3. Học viên đồng bộ từ nguồn CSCA_MOLI_STUDIO.
        var customerDefinitions = new[]
        {
            new
            {
                SourceId = "DEMO-STUDENT-001",
                FullName = "Nguyễn Minh Anh",
                Email = "minhanh.demo@gmail.com",
                Phone = "0901000001",
                Package = "C# Foundation - Gói 6 tháng",
                StartsAt = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc),
                PaymentId = "DEMO-PAYMENT-001",
                Amount = 1490000m,
                PaymentMethod = "BankTransfer"
            },
            new
            {
                SourceId = "DEMO-STUDENT-002",
                FullName = "Trần Gia Huy",
                Email = "giahuy.demo@gmail.com",
                Phone = "0901000002",
                Package = "C# Foundation - Gói 6 tháng",
                StartsAt = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2027, 1, 31, 23, 59, 59, DateTimeKind.Utc),
                PaymentId = "DEMO-PAYMENT-002",
                Amount = 1490000m,
                PaymentMethod = "Momo"
            },
            new
            {
                SourceId = "DEMO-STUDENT-003",
                FullName = "Lê Khánh Linh",
                Email = "khanhlinh.demo@gmail.com",
                Phone = "0901000003",
                Package = "ASP.NET Core nâng cao - Gói 3 tháng",
                StartsAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc),
                PaymentId = "DEMO-PAYMENT-003",
                Amount = 2490000m,
                PaymentMethod = "Card"
            },
            new
            {
                SourceId = "DEMO-STUDENT-004",
                FullName = "Phạm Đức Minh",
                Email = "ducminh.demo@gmail.com",
                Phone = "0901000004",
                Package = "Lộ trình .NET Backend",
                StartsAt = new DateTime(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2027, 2, 15, 23, 59, 59, DateTimeKind.Utc),
                PaymentId = "DEMO-PAYMENT-004",
                Amount = 3990000m,
                PaymentMethod = "BankTransfer"
            }
        };

        foreach (var customerDefinition in customerDefinitions)
        {
            var customer = await _context.EdTechCustomers.FirstOrDefaultAsync(
                c => c.SourceSystem == "CSCA_MOLI_STUDIO" && c.SourceId == customerDefinition.SourceId, ct);
            if (customer == null)
            {
                customer = new Domain.Entities.EdTech.EdTechCustomer
                {
                    CompanyId = company.Id,
                    BusinessUnitId = edtechBu.Id,
                    SourceSystem = "CSCA_MOLI_STUDIO",
                    SourceId = customerDefinition.SourceId,
                    FullName = customerDefinition.FullName,
                    Email = customerDefinition.Email,
                    PhoneNumber = customerDefinition.Phone,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = demoCreatedBy
                };
                _context.EdTechCustomers.Add(customer);
                await _context.SaveChangesAsync(ct);
            }

            var subscriptionExists = await _context.Subscriptions.AnyAsync(
                s => s.CustomerId == customer.Id && s.PackageName == customerDefinition.Package, ct);
            if (!subscriptionExists)
            {
                _context.Subscriptions.Add(new Domain.Entities.EdTech.Subscription
                {
                    CompanyId = company.Id,
                    BusinessUnitId = edtechBu.Id,
                    CustomerId = customer.Id,
                    PackageName = customerDefinition.Package,
                    StartsAt = customerDefinition.StartsAt,
                    ExpiresAt = customerDefinition.ExpiresAt,
                    Status = customerDefinition.ExpiresAt < DateTime.UtcNow
                        ? Domain.Enums.SubscriptionStatus.Expired
                        : Domain.Enums.SubscriptionStatus.Active
                });
            }

            var paymentExists = await _context.Payments.AnyAsync(
                p => p.SourceSystem == "CSCA_MOLI_STUDIO" && p.SourcePaymentId == customerDefinition.PaymentId, ct);
            if (!paymentExists)
            {
                _context.Payments.Add(new Domain.Entities.EdTech.Payment
                {
                    CompanyId = company.Id,
                    BusinessUnitId = edtechBu.Id,
                    CustomerId = customer.Id,
                    SourceSystem = "CSCA_MOLI_STUDIO",
                    SourcePaymentId = customerDefinition.PaymentId,
                    Amount = customerDefinition.Amount,
                    Currency = "VND",
                    Status = Domain.Enums.PaymentStatus.Paid,
                    PaidAt = customerDefinition.StartsAt.AddDays(-1),
                    PaymentMethod = customerDefinition.PaymentMethod,
                    TransactionReference = $"DEMO-TXN-{customerDefinition.SourceId[^3..]}",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = demoCreatedBy
                });
            }

            await _context.SaveChangesAsync(ct);
        }

        // 4. Một kỳ lương riêng thuộc EDTECH để bộ lọc "Công nghệ - Giáo dục" hiển thị dữ liệu.
        const string educationPayrollName = "Kỳ Lương EdTech Tháng 08/2026";
        var educationPayroll = await _context.PayrollPeriods.FirstOrDefaultAsync(
            p => p.CompanyId == company.Id && p.Name == educationPayrollName, ct);
        if (educationPayroll == null)
        {
            educationPayroll = new Domain.Entities.HrPayroll.PayrollPeriod
            {
                CompanyId = company.Id,
                BusinessUnitId = edtechBu.Id,
                Name = educationPayrollName,
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 8, 31),
                Status = Domain.Enums.PayrollStatus.Calculated,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = demoCreatedBy
            };
            _context.PayrollPeriods.Add(educationPayroll);
            await _context.SaveChangesAsync(ct);

            var educationEmployees = await _context.Employees
                .Where(e => e.CompanyId == company.Id && e.BusinessUnitId == edtechBu.Id && !e.IsDeleted)
                .OrderBy(e => e.EmployeeCode)
                .ToListAsync(ct);

            decimal totalGross = 0;
            decimal totalNet = 0;
            foreach (var employee in educationEmployees)
            {
                var allowance = employee.EmployeeCode == "EMP-DEV01" ? 2000000m : 800000m;
                var gross = employee.BaseSalary + allowance;
                var insurance = Math.Round(employee.BaseSalary * 0.105m, 0);
                var net = gross - insurance;

                _context.Payslips.Add(new Domain.Entities.HrPayroll.Payslip
                {
                    PayrollPeriodId = educationPayroll.Id,
                    EmployeeId = employee.Id,
                    BaseSalary = employee.BaseSalary,
                    StandardWorkDays = 22,
                    ActualWorkDays = 22,
                    ActualWorkHours = 176,
                    ActualShifts = 22,
                    GrossSalary = gross,
                    Allowances = allowance,
                    TotalIncome = gross,
                    HealthInsurance = insurance,
                    Deductions = insurance,
                    TotalDeductions = insurance,
                    NetSalary = net,
                    EmploymentType = employee.EmploymentType,
                    PartTimeCalculationMethod = employee.PartTimeCalculationMethod,
                    PartTimeUnitRate = employee.PartTimeUnitRate,
                    Status = Domain.Enums.PayrollStatus.Calculated,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = demoCreatedBy
                });

                totalGross += gross;
                totalNet += net;
            }

            educationPayroll.TotalGrossAmount = totalGross;
            educationPayroll.TotalNetAmount = totalNet;
            educationPayroll.CalculatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    private async Task SeedFashionAndInventoryAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var fashionBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "FASHION", ct);
        var fashionWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.CompanyId == company.Id && w.Code == "FASHION-MAIN", ct);
        if (fashionWarehouse == null)
        {
            fashionWarehouse = new Domain.Entities.Fashion.Warehouse
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "FASHION-MAIN",
                Name = "Kho Thời trang chính",
                IsActive = true,
                IsDefault = true
            };
            _context.Warehouses.Add(fashionWarehouse);
            await _context.SaveChangesAsync(ct);
        }

        // 1. Seed Suppliers if none exist
        var sup1 = await _context.Suppliers.FirstOrDefaultAsync(s => s.CompanyId == company.Id && (s.Code == "SUP-001" || s.Code == "SUP-AODAI-01"), ct);
        if (sup1 == null)
        {
            sup1 = new Domain.Entities.Fashion.Supplier
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "SUP-AODAI-01",
                Name = "Xưởng Dệt & May Lụa Áo Dài Vạn Phúc",
                ContactName = "Nguyễn Văn Hùng",
                Phone = "0988112233",
                Email = "hung.nguyen@luavanphuc.vn",
                Address = "Làng lụa Vạn Phúc, Hà Đông, Hà Nội"
            };
            _context.Suppliers.Add(sup1);
        }

        var sup2 = await _context.Suppliers.FirstOrDefaultAsync(s => s.CompanyId == company.Id && (s.Code == "SUP-002" || s.Code == "SUP-AODAI-02"), ct);
        if (sup2 == null)
        {
            sup2 = new Domain.Entities.Fashion.Supplier
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "SUP-AODAI-02",
                Name = "Xưởng May Đo & Thêu Tay Áo Dài Huế",
                ContactName = "Trần Thị Mai",
                Phone = "0977888999",
                Email = "mai.tran@aodaihue.vn",
                Address = "Phường Phú Hội, TP. Huế"
            };
            _context.Suppliers.Add(sup2);
        }
        await _context.SaveChangesAsync(ct);

        // 2. Seed Products & Variants (Áo Dài MOLY)
        var prodAodaiLua = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.CompanyId == company.Id && (p.Code == "PROD-AODAI-LUA" || p.Code == "PROD-TSHIRT"), ct);
        if (prodAodaiLua == null)
        {
            prodAodaiLua = new Domain.Entities.Fashion.Product
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "PROD-AODAI-LUA",
                Name = "Áo Dài Truyền Thống Lụa Tơ Tằm MOLY",
                Category = "Áo Dài Truyền Thống",
                Description = "Áo dài lụa tơ tằm mềm mại thoáng mát, may thủ công, kiểu dáng thướt tha truyền thống",
                IsActive = true,
                Variants = new List<Domain.Entities.Fashion.ProductVariant>
                {
                    new() { Sku = "AD-LUA-DO-M", Barcode = "8938500101", Color = "Đỏ Tươi", Size = "M", CostPrice = 250000, SellingPrice = 690000, IsActive = true },
                    new() { Sku = "AD-LUA-DO-L", Barcode = "8938500102", Color = "Đỏ Tươi", Size = "L", CostPrice = 250000, SellingPrice = 690000, IsActive = true },
                    new() { Sku = "AD-LUA-XANH-M", Barcode = "8938500103", Color = "Xanh Cốm", Size = "M", CostPrice = 250000, SellingPrice = 690000, IsActive = true },
                    new() { Sku = "AD-LUA-XANH-L", Barcode = "8938500104", Color = "Xanh Cốm", Size = "L", CostPrice = 250000, SellingPrice = 690000, IsActive = true }
                }
            };
            _context.Products.Add(prodAodaiLua);
        }

        var prodAodaiCactan = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.CompanyId == company.Id && (p.Code == "PROD-AODAI-CACTAN" || p.Code == "PROD-POLO"), ct);
        if (prodAodaiCactan == null)
        {
            prodAodaiCactan = new Domain.Entities.Fashion.Product
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "PROD-AODAI-CACTAN",
                Name = "Áo Dài Cách Tân Thêu Hoa Mai MOLY",
                Category = "Áo Dài Cách Tân",
                Description = "Áo dài cách tân trẻ trung thêu tay hoa mai thủ công, phù hợp đi tiệc, sự kiện, chụp ảnh",
                IsActive = true,
                Variants = new List<Domain.Entities.Fashion.ProductVariant>
                {
                    new() { Sku = "AD-CT-HONG-M", Barcode = "8938500201", Color = "Hồng Pastel", Size = "M", CostPrice = 320000, SellingPrice = 850000, IsActive = true },
                    new() { Sku = "AD-CT-HONG-L", Barcode = "8938500202", Color = "Hồng Pastel", Size = "L", CostPrice = 320000, SellingPrice = 850000, IsActive = true }
                }
            };
            _context.Products.Add(prodAodaiCactan);
        }

        var prodAodaiNhung = await _context.Products.Include(p => p.Variants).FirstOrDefaultAsync(p => p.CompanyId == company.Id && (p.Code == "PROD-AODAI-NHUNG" || p.Code == "PROD-HOODIE"), ct);
        if (prodAodaiNhung == null)
        {
            prodAodaiNhung = new Domain.Entities.Fashion.Product
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                Code = "PROD-AODAI-NHUNG",
                Name = "Áo Dài Nhung Gấm Đính Ngọc Trai Mùa Lễ Hội",
                Category = "Áo Dài Nhung Gấm",
                Description = "Áo dài chất liệu nhung gấm sang trọng quý phái, cổ áo đính ngọc trai cao cấp",
                IsActive = true,
                Variants = new List<Domain.Entities.Fashion.ProductVariant>
                {
                    new() { Sku = "AD-NHUNG-DO-L", Barcode = "8938500301", Color = "Đỏ Mận", Size = "L", CostPrice = 450000, SellingPrice = 1250000, IsActive = true }
                }
            };
            _context.Products.Add(prodAodaiNhung);
        }
        await _context.SaveChangesAsync(ct);

        // 3. Seed Sample Purchase Receipt & Inventory Movements & Balances if none exist
        if (!await _context.PurchaseReceipts.AnyAsync(ct))
        {
            var variants = await _context.ProductVariants.Where(v => v.Product.CompanyId == company.Id).ToListAsync(ct);
            if (variants.Count >= 3)
            {
                var v1 = variants[0];
                var v2 = variants[1];
                var v3 = variants[2];

                var receipt = new Domain.Entities.Fashion.PurchaseReceipt
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    WarehouseId = fashionWarehouse.Id,
                    SupplierId = sup1.Id,
                    ReceiptNumber = "PR-20260819-001",
                    Status = "Completed",
                    ReceivedAt = DateTime.UtcNow.AddDays(-2),
                    Notes = "Nhập kho đợt 1 bộ sưu tập Áo Dài MOLY",
                    TotalAmount = (30 * v1.CostPrice) + (30 * v2.CostPrice) + (20 * v3.CostPrice),
                    Items = new List<Domain.Entities.Fashion.PurchaseReceiptItem>
                    {
                        new() { ProductVariantId = v1.Id, Quantity = 30, UnitPrice = v1.CostPrice },
                        new() { ProductVariantId = v2.Id, Quantity = 30, UnitPrice = v2.CostPrice },
                        new() { ProductVariantId = v3.Id, Quantity = 20, UnitPrice = v3.CostPrice }
                    }
                };
                _context.PurchaseReceipts.Add(receipt);
                await _context.SaveChangesAsync(ct);

                // Movements
                _context.InventoryMovements.AddRange(
                    new Domain.Entities.Fashion.InventoryMovement
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v1.Id,
                        MovementType = Domain.Enums.InventoryMovementType.PurchaseReceipt,
                        QuantityDelta = 30,
                        UnitCost = v1.CostPrice,
                        ReferenceType = "PurchaseReceipt",
                        ReferenceId = receipt.Id,
                        MovementDate = DateTime.UtcNow.AddDays(-2),
                        Notes = "Nhập kho theo phiếu PR-20260819-001"
                    },
                    new Domain.Entities.Fashion.InventoryMovement
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v2.Id,
                        MovementType = Domain.Enums.InventoryMovementType.PurchaseReceipt,
                        QuantityDelta = 30,
                        UnitCost = v2.CostPrice,
                        ReferenceType = "PurchaseReceipt",
                        ReferenceId = receipt.Id,
                        MovementDate = DateTime.UtcNow.AddDays(-2),
                        Notes = "Nhập kho theo phiếu PR-20260819-001"
                    },
                    new Domain.Entities.Fashion.InventoryMovement
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v3.Id,
                        MovementType = Domain.Enums.InventoryMovementType.PurchaseReceipt,
                        QuantityDelta = 20,
                        UnitCost = v3.CostPrice,
                        ReferenceType = "PurchaseReceipt",
                        ReferenceId = receipt.Id,
                        MovementDate = DateTime.UtcNow.AddDays(-2),
                        Notes = "Nhập kho theo phiếu PR-20260819-001"
                    }
                );

                // Balances
                _context.InventoryBalances.AddRange(
                    new Domain.Entities.Fashion.InventoryBalance
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v1.Id,
                        OnHandQuantity = 30,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    },
                    new Domain.Entities.Fashion.InventoryBalance
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v2.Id,
                        OnHandQuantity = 30,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    },
                    new Domain.Entities.Fashion.InventoryBalance
                    {
                        CompanyId = company.Id,
                        BusinessUnitId = fashionBu?.Id,
                        WarehouseId = fashionWarehouse.Id,
                        ProductVariantId = v3.Id,
                        OnHandQuantity = 20,
                        ReservedQuantity = 0,
                        LastUpdated = DateTime.UtcNow
                    }
                );

                await _context.SaveChangesAsync(ct);
            }
        }
    }

    private async Task SeedManufacturingCostingAsync(CancellationToken ct)
    {
        var company = await _context.Companies.FirstAsync(c => c.Code == "MOLI", ct);
        var fashionBu = await _context.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == company.Id && b.Code == "FASHION", ct);
        var fashionWarehouse = await _context.Warehouses.FirstAsync(w => w.CompanyId == company.Id && w.Code == "FASHION-MAIN", ct);
        var product = await _context.Products.Include(x => x.Variants)
            .FirstAsync(x => x.CompanyId == company.Id && x.Code == "PROD-AODAI-LUA", ct);
        var supplier = await _context.Suppliers.FirstAsync(x => x.CompanyId == company.Id && x.Code == "SUP-AODAI-01", ct);

        var materialDefinitions = new[]
        {
            (Code: "MAT-VAI-LUA", Name: "Vải lụa tơ tằm", Category: "Vải", Unit: "m", Quantity: 500m, UnitCost: 150000m),
            (Code: "MAT-VAI-LOT", Name: "Vải lót", Category: "Vải lót", Unit: "m", Quantity: 300m, UnitCost: 50000m),
            (Code: "MAT-REN", Name: "Ren trang trí", Category: "Phụ liệu", Unit: "m", Quantity: 200m, UnitCost: 20000m),
            (Code: "MAT-CHI", Name: "Chỉ may", Category: "Phụ liệu", Unit: "cuộn", Quantity: 100m, UnitCost: 10000m),
            (Code: "MAT-NUT", Name: "Nút áo dài", Category: "Phụ liệu", Unit: "cái", Quantity: 1000m, UnitCost: 2000m),
            (Code: "MAT-DA", Name: "Đá đính áo", Category: "Phụ liệu", Unit: "viên", Quantity: 5000m, UnitCost: 1000m)
        };

        var materials = new Dictionary<string, Material>();
        foreach (var definition in materialDefinitions)
        {
            var material = await _context.Materials.FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.Code == definition.Code, ct);
            if (material == null)
            {
                material = new Material
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    Code = definition.Code,
                    Name = definition.Name,
                    Category = definition.Category,
                    Unit = definition.Unit,
                    IsActive = true
                };
                _context.Materials.Add(material);
                await _context.SaveChangesAsync(ct);
            }
            materials[definition.Code] = material;

            if (!await _context.MaterialLots.AnyAsync(x => x.CompanyId == company.Id && x.MaterialId == material.Id, ct))
            {
                var lot = new MaterialLot
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    MaterialId = material.Id,
                    SupplierId = supplier.Id,
                    LotNumber = $"LOT-SEED-{definition.Code}",
                    QuantityReceived = definition.Quantity,
                    QuantityRemaining = definition.Quantity,
                    UnitCost = definition.UnitCost,
                    Currency = "VND",
                    ReceivedAt = DateTime.UtcNow.AddDays(-5)
                };
                material.QuantityOnHand += definition.Quantity;
                _context.MaterialLots.Add(lot);
                _context.MaterialMovements.Add(new MaterialMovement
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    MaterialId = material.Id,
                    MaterialLotId = lot.Id,
                    MovementType = MaterialMovementType.PurchaseReceipt,
                    QuantityDelta = definition.Quantity,
                    UnitCost = definition.UnitCost,
                    ReferenceType = "MaterialReceipt",
                    ReferenceId = lot.Id,
                    MovementDate = lot.ReceivedAt,
                    Notes = "Seed lô nguyên vật liệu cho costing áo dài"
                });
            }
        }
        await _context.SaveChangesAsync(ct);

        var bom = await _context.Boms.Include(x => x.Items).FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.ProductId == product.Id && x.Status == "Approved", ct);
        if (bom == null)
        {
            bom = new Bom
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                ProductId = product.Id,
                Code = "BOM-AD-LUA",
                VersionNumber = 1,
                Status = "Approved",
                EffectiveFrom = DateTime.UtcNow.AddDays(-5),
                IsActive = true,
                Items = new List<BomItem>
                {
                    new() { MaterialId = materials["MAT-VAI-LUA"].Id, Size = "M", Quantity = 3.0m, WastePercent = 0, Unit = "m", Sequence = 1 },
                    new() { MaterialId = materials["MAT-VAI-LUA"].Id, Size = "L", Quantity = 3.2m, WastePercent = 0, Unit = "m", Sequence = 2 },
                    new() { MaterialId = materials["MAT-VAI-LOT"].Id, Size = "M", Quantity = 1.5m, WastePercent = 0, Unit = "m", Sequence = 3 },
                    new() { MaterialId = materials["MAT-VAI-LOT"].Id, Size = "L", Quantity = 1.6m, WastePercent = 0, Unit = "m", Sequence = 4 },
                    new() { MaterialId = materials["MAT-REN"].Id, Size = null, Quantity = 2.0m, WastePercent = 0, Unit = "m", Sequence = 5 },
                    new() { MaterialId = materials["MAT-CHI"].Id, Size = null, Quantity = 1.0m, WastePercent = 0, Unit = "cuộn", Sequence = 6 },
                    new() { MaterialId = materials["MAT-NUT"].Id, Size = null, Quantity = 10m, WastePercent = 0, Unit = "cái", Sequence = 7 },
                    new() { MaterialId = materials["MAT-DA"].Id, Size = null, Quantity = 50m, WastePercent = 0, Unit = "viên", Sequence = 8 }
                }
            };
            _context.Boms.Add(bom);
            await _context.SaveChangesAsync(ct);
        }

        var policyDefinitions = new[]
        {
            (Channel: "STORE", Platform: 0m, Affiliate: 0m, Payment: 0m, Fixed: 0m, Tax: 0m, Shipping: 0m),
            (Channel: "FACEBOOK", Platform: 0m, Affiliate: 0m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "WEBSITE_AODAI", Platform: 0m, Affiliate: 0m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "ZALO", Platform: 0m, Affiliate: 0m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "SHOPEE", Platform: 0.10m, Affiliate: 0.10m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m),
            (Channel: "TIKTOK", Platform: 0.10m, Affiliate: 0.10m, Payment: 0.02m, Fixed: 0m, Tax: 0.01m, Shipping: 30000m)
        };
        foreach (var definition in policyDefinitions)
        {
            if (!await _context.ChannelFeePolicies.AnyAsync(x => x.CompanyId == company.Id && x.Channel == definition.Channel && x.IsActive, ct))
            {
                _context.ChannelFeePolicies.Add(new ChannelFeePolicy
                {
                    CompanyId = company.Id,
                    BusinessUnitId = fashionBu?.Id,
                    Code = $"{definition.Channel}-V1",
                    Channel = definition.Channel,
                    VersionNumber = 1,
                    PlatformFeeRate = definition.Platform,
                    AffiliateFeeRate = definition.Affiliate,
                    PaymentFeeRate = definition.Payment,
                    FixedPaymentFee = definition.Fixed,
                    TaxRate = definition.Tax,
                    DefaultShippingSubsidy = definition.Shipping,
                    EffectiveFrom = DateTime.UtcNow.AddDays(-30),
                    IsActive = true
                });
            }
        }
        await _context.SaveChangesAsync(ct);

        if (!await _context.ProductionOrders.AnyAsync(x => x.CompanyId == company.Id && x.OrderNumber == "SX-SEED-AD-LUA-001", ct))
        {
            var variantM = product.Variants.First(x => x.Sku == "AD-LUA-DO-M");
            var variantL = product.Variants.First(x => x.Sku == "AD-LUA-DO-L");
            var outputQuantityM = 2;
            var outputQuantityL = 1;
            var materialUsages = new (string Code, decimal Quantity)[]
            {
                ("MAT-VAI-LUA", 9.2m), ("MAT-VAI-LOT", 4.6m), ("MAT-REN", 6m),
                ("MAT-CHI", 3m), ("MAT-NUT", 30m), ("MAT-DA", 150m)
            };
            var materialCost = 0m;
            var order = new ProductionOrder
            {
                CompanyId = company.Id,
                BusinessUnitId = fashionBu?.Id,
                OrderNumber = "SX-SEED-AD-LUA-001",
                ProductId = product.Id,
                BomId = bom.Id,
                Status = ProductionOrderStatus.Closed,
                CostStatus = CostStatus.Actual,
                PlannedQuantity = 3,
                GoodQuantity = 3,
                StandardCost = 0,
                ReleasedAt = DateTime.UtcNow.AddDays(-3),
                CompletedAt = DateTime.UtcNow.AddDays(-2),
                ClosedAt = DateTime.UtcNow.AddDays(-2),
                Notes = "Seed batch để kiểm thử actual costing áo dài theo size"
            };
            order.Outputs.Add(new ProductionOrderOutput { ProductionOrderId = order.Id, ProductVariantId = variantM.Id, PlannedQuantity = outputQuantityM, GoodQuantity = outputQuantityM, UnitCost = 1196666.67m });
            order.Outputs.Add(new ProductionOrderOutput { ProductionOrderId = order.Id, ProductVariantId = variantL.Id, PlannedQuantity = outputQuantityL, GoodQuantity = outputQuantityL, UnitCost = 1196666.67m });
            foreach (var usage in materialUsages)
            {
                var material = materials[usage.Code];
                var lot = await _context.MaterialLots.FirstAsync(x => x.MaterialId == material.Id && x.QuantityRemaining >= usage.Quantity, ct);
                lot.QuantityRemaining -= usage.Quantity;
                material.QuantityOnHand -= usage.Quantity;
                materialCost += usage.Quantity * lot.UnitCost;
                order.Materials.Add(new ProductionOrderMaterial { ProductionOrderId = order.Id, MaterialId = material.Id, MaterialLotId = lot.Id, ActualQuantity = usage.Quantity, UnitCost = lot.UnitCost, TotalCost = usage.Quantity * lot.UnitCost });
                _context.MaterialMovements.Add(new MaterialMovement { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, MaterialId = material.Id, MaterialLotId = lot.Id, MovementType = MaterialMovementType.ProductionConsumption, QuantityDelta = -usage.Quantity, UnitCost = lot.UnitCost, ReferenceType = "ProductionOrder", ReferenceId = order.Id, MovementDate = order.CompletedAt.Value, Notes = "Seed xuất NVL cho actual costing" });
            }
            var labor = 3 * (50000m + 200000m + 100000m + 20000m + 20000m);
            var outside = 0m;
            var overhead = 300000m;
            var scrap = 150000m;
            order.ActualMaterialCost = materialCost;
            order.ActualLaborCost = labor;
            order.ActualOutsideProcessingCost = outside;
            order.ActualOverheadCost = overhead;
            order.ActualScrapReworkCost = scrap;
            order.ActualTotalCost = materialCost + labor + outside + overhead + scrap;
            order.ActualUnitCost = decimal.Round(order.ActualTotalCost / 3m, 2);
            order.StandardCost = order.ActualUnitCost;
            foreach (var output in order.Outputs) output.UnitCost = order.ActualUnitCost;

            _context.ProductionOrders.Add(order);
            foreach (var variant in new[] { variantM, variantL })
            {
                variant.CostPrice = order.ActualUnitCost;
                variant.CostStatus = CostStatus.Actual;
                variant.LastActualCostAt = order.ClosedAt;
            }
            _context.InventoryMovements.AddRange(
                new InventoryMovement { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, WarehouseId = fashionWarehouse.Id, ProductVariantId = variantM.Id, MovementType = InventoryMovementType.ProductionOutput, QuantityDelta = outputQuantityM, UnitCost = order.ActualUnitCost, ReferenceType = "ProductionOrder", ReferenceId = order.Id, MovementDate = order.CompletedAt.Value, Notes = "Seed thành phẩm tốt actual cost" },
                new InventoryMovement { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, WarehouseId = fashionWarehouse.Id, ProductVariantId = variantL.Id, MovementType = InventoryMovementType.ProductionOutput, QuantityDelta = outputQuantityL, UnitCost = order.ActualUnitCost, ReferenceType = "ProductionOrder", ReferenceId = order.Id, MovementDate = order.CompletedAt.Value, Notes = "Seed thành phẩm tốt actual cost" });

            foreach (var output in order.Outputs)
            {
                var balance = await _context.InventoryBalances.FirstOrDefaultAsync(x => x.CompanyId == company.Id && x.BusinessUnitId == fashionBu!.Id && x.WarehouseId == fashionWarehouse.Id && x.ProductVariantId == output.ProductVariantId, ct);
                if (balance == null)
                {
                    _context.InventoryBalances.Add(new InventoryBalance { CompanyId = company.Id, BusinessUnitId = fashionBu?.Id, WarehouseId = fashionWarehouse.Id, ProductVariantId = output.ProductVariantId, OnHandQuantity = output.GoodQuantity, ReservedQuantity = 0, LastUpdated = DateTime.UtcNow });
                }
                else
                {
                    balance.OnHandQuantity += output.GoodQuantity;
                    balance.LastUpdated = DateTime.UtcNow;
                }
            }
            await _context.SaveChangesAsync(ct);
        }
    }
}
