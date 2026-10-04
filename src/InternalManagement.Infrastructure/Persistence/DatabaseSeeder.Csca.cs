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

}
