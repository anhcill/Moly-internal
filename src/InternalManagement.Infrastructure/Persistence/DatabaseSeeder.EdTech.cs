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

}
