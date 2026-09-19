using System.Text.Json;
using InternalManagement.Application.Features.Integration.Interfaces;
using InternalManagement.Application.Features.Integration.Models;

namespace InternalManagement.Infrastructure.Integration.Connectors;

/// <summary>
/// Mock connector giả lập Website EdTech API.
/// Trả mock data có cấu trúc giống API thật.
/// Khi có API thật, thay bằng HttpClient call.
/// </summary>
public sealed class WebsiteEdtechConnector : IExternalConnector
{
    public string SourceSystem => "WEBSITE_EDTECH";

    public IReadOnlyList<string> SupportedEntityTypes { get; } =
        ["Courses", "Questions", "Customers", "Subscriptions", "Payments"];

    public Task<SyncPage> PullAsync(string entityType, SyncCursor cursor, CancellationToken ct)
    {
        var items = entityType switch
        {
            "Courses" => GenerateCourses(),
            "Questions" => GenerateQuestions(),
            "Customers" => GenerateCustomers(),
            "Subscriptions" => GenerateSubscriptions(),
            "Payments" => GeneratePayments(),
            _ => []
        };

        return Task.FromResult(new SyncPage
        {
            Items = items,
            HasMore = false,
            NextCursor = new SyncCursor { UpdatedSince = DateTime.UtcNow },
            TotalAvailable = items.Count
        });
    }

    public Task<ConnectorHealth> CheckHealthAsync(CancellationToken ct)
    {
        return Task.FromResult(new ConnectorHealth
        {
            SourceSystem = SourceSystem,
            IsHealthy = true,
            Message = "Mock EdTech connector — healthy"
        });
    }

    // ── Mock Data Generators ──

    private static List<JsonElement> GenerateCourses()
    {
        var modules1 = new[] { new { Title = "Giới thiệu C#", OrderIndex = 0 }, new { Title = "Biến và kiểu dữ liệu", OrderIndex = 1 }, new { Title = "Câu lệnh điều kiện", OrderIndex = 2 } };
        var modules2 = new[] { new { Title = "Thiết kế REST API", OrderIndex = 0 }, new { Title = "Entity Framework Core", OrderIndex = 1 } };
        var modulesEmpty = Array.Empty<object>();

        object[] courses =
        [
            new { Id = "course_001", Title = "Lập trình C# cơ bản", Slug = "lap-trinh-csharp-co-ban",
                Description = "Khóa học lập trình C# từ zero đến hero", ThumbnailUrl = "https://cdn.moli.vn/csharp.jpg",
                Price = 299000m, Status = "Published", Version = 1, UpdatedAt = DateTime.UtcNow,
                Modules = modules1.Cast<object>().ToArray() },

            new { Id = "course_002", Title = "ASP.NET Core Web API", Slug = "aspnet-core-web-api",
                Description = "Xây dựng REST API chuyên nghiệp", ThumbnailUrl = "https://cdn.moli.vn/aspnet.jpg",
                Price = 499000m, Status = "Published", Version = 2, UpdatedAt = DateTime.UtcNow,
                Modules = modules2.Cast<object>().ToArray() },

            new { Id = "course_003", Title = "React Frontend", Slug = "react-frontend",
                Description = "Phát triển giao diện web với React", ThumbnailUrl = "https://cdn.moli.vn/react.jpg",
                Price = 399000m, Status = "Published", Version = 1, UpdatedAt = DateTime.UtcNow,
                Modules = modulesEmpty }
        ];

        return courses.Select(c => JsonSerializer.SerializeToElement(c, JsonOpts)).ToList();
    }

    private static List<JsonElement> GenerateCustomers()
    {
        object[] customers =
        [
            new { Id = "cust_001", FullName = "Nguyễn Văn An", Email = "an.nguyen@gmail.com", PhoneNumber = "0901234567", UpdatedAt = DateTime.UtcNow },
            new { Id = "cust_002", FullName = "Trần Thị Bình", Email = "binh.tran@gmail.com", PhoneNumber = "0912345678", UpdatedAt = DateTime.UtcNow },
            new { Id = "cust_003", FullName = "Lê Hoàng Cường", Email = "cuong.le@yahoo.com", PhoneNumber = "0923456789", UpdatedAt = DateTime.UtcNow },
            new { Id = "cust_004", FullName = "Phạm Minh Dương", Email = "duong.pham@outlook.com", PhoneNumber = "", UpdatedAt = DateTime.UtcNow },
        ];

        return customers.Select(c => JsonSerializer.SerializeToElement(c, JsonOpts)).ToList();
    }

    private static List<JsonElement> GeneratePayments()
    {
        var payments = new[]
        {
            new { Id = "pay_001", CustomerId = "cust_001", Amount = 299000m, Currency = "VND", Status = "Paid",
                PaidAt = DateTime.UtcNow.AddDays(-5), PaymentMethod = "MoMo", TransactionReference = "MOMO_TXN_001" },
            new { Id = "pay_002", CustomerId = "cust_002", Amount = 499000m, Currency = "VND", Status = "Paid",
                PaidAt = DateTime.UtcNow.AddDays(-3), PaymentMethod = "BankTransfer", TransactionReference = "VCB_TXN_002" },
            new { Id = "pay_003", CustomerId = "cust_003", Amount = 399000m, Currency = "VND", Status = "Pending",
                PaidAt = DateTime.UtcNow.AddDays(-1), PaymentMethod = "ZaloPay", TransactionReference = "ZALO_TXN_003" },
        };

        return payments.Select(p => JsonSerializer.SerializeToElement(p, JsonOpts)).ToList();
    }

    private static List<JsonElement> GenerateSubscriptions()
    {
        var subs = new[]
        {
            new { Id = "sub_001", CustomerId = "cust_001", PackageName = "Premium 6 tháng",
                StartsAt = DateTime.UtcNow.AddMonths(-2), ExpiresAt = DateTime.UtcNow.AddMonths(4), Status = "Active" },
            new { Id = "sub_002", CustomerId = "cust_002", PackageName = "Premium 12 tháng",
                StartsAt = DateTime.UtcNow.AddMonths(-1), ExpiresAt = DateTime.UtcNow.AddMonths(11), Status = "Active" },
        };

        return subs.Select(s => JsonSerializer.SerializeToElement(s, JsonOpts)).ToList();
    }

    private static List<JsonElement> GenerateQuestions()
    {
        var questions = new[]
        {
            new { Id = "q_001", BankName = "C# Fundamentals", Subject = "Lập trình", Topic = "C# Basics",
                DifficultyLevel = "Easy", ContentHtml = "<p>Kiểu dữ liệu <code>int</code> trong C# có kích thước bao nhiêu byte?</p>",
                ExplanationHtml = "<p><code>int</code> = 4 bytes = 32 bits</p>",
                Choices = new[] {
                    new { Label = "A", ContentHtml = "2 bytes", IsCorrect = false, OrderIndex = 0 },
                    new { Label = "B", ContentHtml = "4 bytes", IsCorrect = true, OrderIndex = 1 },
                    new { Label = "C", ContentHtml = "8 bytes", IsCorrect = false, OrderIndex = 2 },
                    new { Label = "D", ContentHtml = "16 bytes", IsCorrect = false, OrderIndex = 3 }
                },
                Tags = new[] { "csharp", "data-types", "fundamentals" }
            },
            new { Id = "q_002", BankName = "C# Fundamentals", Subject = "Lập trình", Topic = "OOP",
                DifficultyLevel = "Medium", ContentHtml = "<p>Tính chất nào sau đây KHÔNG phải của OOP?</p>",
                ExplanationHtml = "<p>Compilation không phải tính chất OOP</p>",
                Choices = new[] {
                    new { Label = "A", ContentHtml = "Encapsulation", IsCorrect = false, OrderIndex = 0 },
                    new { Label = "B", ContentHtml = "Compilation", IsCorrect = true, OrderIndex = 1 },
                    new { Label = "C", ContentHtml = "Inheritance", IsCorrect = false, OrderIndex = 2 },
                    new { Label = "D", ContentHtml = "Polymorphism", IsCorrect = false, OrderIndex = 3 }
                },
                Tags = new[] { "oop", "concepts" }
            }
        };

        return questions.Select(q => JsonSerializer.SerializeToElement(q, JsonOpts)).ToList();
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
