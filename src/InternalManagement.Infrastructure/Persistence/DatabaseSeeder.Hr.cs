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

}
