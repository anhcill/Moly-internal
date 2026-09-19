using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.EdTech.DTOs;
using InternalManagement.Application.Features.EdTech.Services;
using InternalManagement.Domain.Entities.EdTech;
using InternalManagement.Domain.Enums;

namespace InternalManagement.Infrastructure.Services;

public sealed class EdTechService : IEdTechService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<EdTechService> _logger;

    public EdTechService(
        IApplicationDbContext db,
        ICurrentUserService currentUser,
        ILogger<EdTechService> logger)
    {
        _db = db;
        _currentUser = currentUser;
        _logger = logger;
    }

    private async Task<(Guid CompanyId, Guid? BusinessUnitId)> GetContextAsync(CancellationToken ct)
    {
        var companyId = _currentUser.CompanyId;
        if (!companyId.HasValue || companyId.Value == Guid.Empty)
        {
            var company = await _db.Companies.FirstOrDefaultAsync(c => c.Code == "MOLI", ct);
            companyId = company?.Id ?? Guid.Empty;
        }

        var buId = _currentUser.BusinessUnitId;
        if (!buId.HasValue || buId.Value == Guid.Empty)
        {
            var bu = await _db.BusinessUnits.FirstOrDefaultAsync(b => b.CompanyId == companyId && b.Code == "EDTECH", ct);
            buId = bu?.Id;
        }

        return (companyId.Value, buId);
    }

    // ── Courses ──

    public async Task<PaginatedResult<CourseDto>> GetCoursesAsync(
        string? search, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.Courses
            .AsNoTracking()
            .Include(c => c.Modules)
            .Include(c => c.CscaClasses)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.Title.ToLower().Contains(s) || (c.Slug != null && c.Slug.ToLower().Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(c => c.Status == status);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CourseDto
            {
                Id = c.Id,
                CourseSourceId = c.CourseSourceId,
                Title = c.Title,
                Slug = c.Slug,
                Description = c.Description,
                ThumbnailUrl = c.ThumbnailUrl,
                Price = c.Price,
                Status = c.Status,
                Version = c.Version,
                ModuleCount = c.Modules.Count,
                ClassCount = c.CscaClasses.Count(cscaClass => !cscaClass.IsDeleted),
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
                Modules = c.Modules.OrderBy(m => m.OrderIndex).Select(m => new CourseModuleDto
                {
                    Id = m.Id,
                    Title = m.Title,
                    OrderIndex = m.OrderIndex
                }).ToList()
            })
            .ToListAsync(ct);

        return new PaginatedResult<CourseDto>(items, count, pageIndex, pageSize);
    }

    public async Task<Result<CourseDto>> GetCourseByIdAsync(Guid id, CancellationToken ct)
    {
        var course = await _db.Courses
            .AsNoTracking()
            .Include(c => c.Modules)
            .Include(c => c.CscaClasses)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (course == null)
            return Result<CourseDto>.Failure("Không tìm thấy khóa học.");

        var dto = new CourseDto
        {
            Id = course.Id,
            CourseSourceId = course.CourseSourceId,
            Title = course.Title,
            Slug = course.Slug,
            Description = course.Description,
            ThumbnailUrl = course.ThumbnailUrl,
            Price = course.Price,
            Status = course.Status,
            Version = course.Version,
            ModuleCount = course.Modules.Count,
            ClassCount = course.CscaClasses.Count(cscaClass => !cscaClass.IsDeleted),
            CreatedAt = course.CreatedAt,
            UpdatedAt = course.UpdatedAt,
            Modules = course.Modules.OrderBy(m => m.OrderIndex).Select(m => new CourseModuleDto
            {
                Id = m.Id,
                Title = m.Title,
                OrderIndex = m.OrderIndex
            }).ToList()
        };

        return Result<CourseDto>.Success(dto);
    }

    public async Task<Result<CourseDto>> CreateCourseAsync(CreateCourseRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<CourseDto>.Failure("Tiêu đề khóa học là bắt buộc.");

        if (request.Price < 0)
            return Result<CourseDto>.Failure("Học phí không thể là số âm.");

        var (companyId, buId) = await GetContextAsync(ct);

        var course = new Course
        {
            CompanyId = companyId,
            BusinessUnitId = buId,
            CourseSourceId = $"internal_{Guid.NewGuid():N}"[..16],
            Title = request.Title.Trim(),
            Slug = request.Slug ?? request.Title.Trim().ToLowerInvariant().Replace(" ", "-"),
            Description = request.Description,
            ThumbnailUrl = request.ThumbnailUrl,
            Price = request.Price,
            Status = request.Status ?? "Draft",
            Version = 1,
            CreatedBy = _currentUser.Username ?? "System"
        };

        _db.Courses.Add(course);

        if (request.ModuleTitles != null)
        {
            for (int i = 0; i < request.ModuleTitles.Count; i++)
            {
                _db.CourseModules.Add(new CourseModule
                {
                    CourseId = course.Id,
                    Title = request.ModuleTitles[i],
                    OrderIndex = i
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        return await GetCourseByIdAsync(course.Id, ct);
    }

    public async Task<Result<CourseDto>> UpdateCourseAsync(Guid id, UpdateCourseRequest request, CancellationToken ct)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (course == null)
            return Result<CourseDto>.Failure("Không tìm thấy khóa học.");

        if (string.IsNullOrWhiteSpace(request.Title))
            return Result<CourseDto>.Failure("Tiêu đề không được để trống.");

        course.Title = request.Title.Trim();
        course.Slug = request.Slug ?? course.Slug;
        course.Description = request.Description;
        course.ThumbnailUrl = request.ThumbnailUrl;
        course.Price = request.Price;
        course.Status = request.Status;
        course.Version++;
        course.UpdatedAt = DateTime.UtcNow;
        course.UpdatedBy = _currentUser.Username ?? "System";

        await _db.SaveChangesAsync(ct);

        return await GetCourseByIdAsync(course.Id, ct);
    }

    public async Task<Result<bool>> DeleteCourseAsync(Guid id, CancellationToken ct)
    {
        var course = await _db.Courses
            .Include(c => c.CscaClasses)
            .Include(c => c.Modules)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (course == null)
            return Result<bool>.Failure("Không tìm thấy khóa học cần xóa.");

        if (course.CscaClasses.Any(c => !c.IsDeleted))
            return Result<bool>.Failure("Không thể xóa khóa học đang có lớp học hoạt động. Vui lòng chuyển hoặc xóa các lớp học trước.");

        _db.CourseModules.RemoveRange(course.Modules);
        _db.Courses.Remove(course);
        await _db.SaveChangesAsync(ct);

        return Result<bool>.Success(true);
    }

    // ── Questions & Content Publication (Ngày 9 Core) ──

    public async Task<PaginatedResult<QuestionDto>> GetQuestionsAsync(
        string? search, string? difficulty, string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.Questions
            .AsNoTracking()
            .Include(q => q.QuestionBank)
            .Include(q => q.Subject)
            .Include(q => q.Topic)
            .Include(q => q.Tags)
            .Include(q => q.Versions)
                .ThenInclude(v => v.Choices)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(q => q.Versions.Any(v => v.ContentHtml.ToLower().Contains(s))
                                  || q.QuestionBank.Name.ToLower().Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(difficulty))
        {
            query = query.Where(q => q.DifficultyLevel == difficulty);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<QuestionPublicationStatus>(status, true, out var pubStatus))
        {
            query = query.Where(q => q.Versions.Any(v => v.Status == pubStatus));
        }

        var count = await query.CountAsync(ct);
        var questions = await query
            .OrderByDescending(q => q.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = questions.Select(MapQuestionToDto).ToList();

        return new PaginatedResult<QuestionDto>(dtos, count, pageIndex, pageSize);
    }

    public async Task<Result<QuestionDto>> GetQuestionByIdAsync(Guid id, CancellationToken ct)
    {
        var question = await _db.Questions
            .AsNoTracking()
            .Include(q => q.QuestionBank)
            .Include(q => q.Subject)
            .Include(q => q.Topic)
            .Include(q => q.Tags)
            .Include(q => q.Versions)
                .ThenInclude(v => v.Choices)
            .FirstOrDefaultAsync(q => q.Id == id, ct);

        if (question == null)
            return Result<QuestionDto>.Failure("Không tìm thấy câu hỏi.");

        return Result<QuestionDto>.Success(MapQuestionToDto(question));
    }

    public async Task<Result<QuestionDto>> CreateQuestionAsync(CreateQuestionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ContentHtml))
            return Result<QuestionDto>.Failure("Nội dung câu hỏi không được để trống.");

        if (request.Choices == null || request.Choices.Count < 2)
            return Result<QuestionDto>.Failure("Câu hỏi trắc nghiệm phải có ít nhất 2 đáp án lựa chọn.");

        if (!request.Choices.Any(c => c.IsCorrect))
            return Result<QuestionDto>.Failure("Phải có ít nhất 1 đáp án đúng.");

        var (companyId, buId) = await GetContextAsync(ct);

        // BankName is required; do not silently create a synthetic "Default Bank".
        var bankName = request.BankName?.Trim();
        if (string.IsNullOrWhiteSpace(bankName))
            return Result<QuestionDto>.Failure("Ngân hàng câu hỏi là bắt buộc.");
        var bank = await _db.QuestionBanks.FirstOrDefaultAsync(b => b.Name == bankName && b.CompanyId == companyId, ct);
        if (bank == null)
        {
            bank = new QuestionBank
            {
                CompanyId = companyId,
                BusinessUnitId = buId,
                Name = bankName,
                CreatedBy = _currentUser.Username ?? "System"
            };
            _db.QuestionBanks.Add(bank);
            await _db.SaveChangesAsync(ct);
        }

        // Resolve Subject & Topic
        Guid? subjectId = null;
        Guid? topicId = null;

        if (!string.IsNullOrWhiteSpace(request.SubjectName))
        {
            var subject = await _db.Subjects.FirstOrDefaultAsync(s => s.Name == request.SubjectName && s.CompanyId == companyId, ct);
            if (subject == null)
            {
                subject = new Subject
                {
                    CompanyId = companyId,
                    BusinessUnitId = buId,
                    Code = request.SubjectName.ToUpperInvariant().Replace(" ", "_"),
                    Name = request.SubjectName
                };
                _db.Subjects.Add(subject);
                await _db.SaveChangesAsync(ct);
            }
            subjectId = subject.Id;

            if (!string.IsNullOrWhiteSpace(request.TopicName))
            {
                var topic = await _db.Topics.FirstOrDefaultAsync(t => t.SubjectId == subject.Id && t.Name == request.TopicName, ct);
                if (topic == null)
                {
                    topic = new Topic { SubjectId = subject.Id, Name = request.TopicName };
                    _db.Topics.Add(topic);
                    await _db.SaveChangesAsync(ct);
                }
                topicId = topic.Id;
            }
        }

        var question = new Question
        {
            QuestionBankId = bank.Id,
            SubjectId = subjectId,
            TopicId = topicId,
            DifficultyLevel = request.DifficultyLevel ?? "Medium",
            CreatedBy = _currentUser.Username ?? "System"
        };
        _db.Questions.Add(question);
        await _db.SaveChangesAsync(ct);

        var version = new QuestionVersion
        {
            QuestionId = question.Id,
            VersionNumber = 1,
            ContentHtml = request.ContentHtml,
            ExplanationHtml = request.ExplanationHtml,
            Status = QuestionPublicationStatus.Draft,
            CreatedBy = _currentUser.Username ?? "System"
        };
        _db.QuestionVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        question.CurrentVersionId = version.Id;

        foreach (var c in request.Choices)
        {
            _db.QuestionChoices.Add(new QuestionChoice
            {
                QuestionVersionId = version.Id,
                Label = c.Label,
                ContentHtml = c.ContentHtml,
                IsCorrect = c.IsCorrect,
                OrderIndex = c.OrderIndex
            });
        }

        if (request.Tags != null)
        {
            foreach (var t in request.Tags)
            {
                _db.QuestionTags.Add(new QuestionTag
                {
                    QuestionId = question.Id,
                    Tag = t
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        return await GetQuestionByIdAsync(question.Id, ct);
    }

    public async Task<Result<QuestionDto>> CreateQuestionVersionAsync(
        Guid questionId, CreateQuestionVersionRequest request, CancellationToken ct)
    {
        var question = await _db.Questions
            .Include(q => q.Versions)
            .FirstOrDefaultAsync(q => q.Id == questionId, ct);

        if (question == null)
            return Result<QuestionDto>.Failure("Không tìm thấy câu hỏi.");

        if (string.IsNullOrWhiteSpace(request.ContentHtml))
            return Result<QuestionDto>.Failure("Nội dung câu hỏi không được để trống.");

        if (request.Choices == null || request.Choices.Count < 2)
            return Result<QuestionDto>.Failure("Câu hỏi trắc nghiệm phải có ít nhất 2 đáp án.");

        var nextVersionNumber = (question.Versions.Max(v => (int?)v.VersionNumber) ?? 0) + 1;

        var newVersion = new QuestionVersion
        {
            QuestionId = question.Id,
            VersionNumber = nextVersionNumber,
            ContentHtml = request.ContentHtml,
            ExplanationHtml = request.ExplanationHtml,
            Status = QuestionPublicationStatus.Draft,
            CreatedBy = _currentUser.Username ?? "System"
        };

        _db.QuestionVersions.Add(newVersion);
        await _db.SaveChangesAsync(ct);

        question.CurrentVersionId = newVersion.Id;
        question.UpdatedAt = DateTime.UtcNow;
        question.UpdatedBy = _currentUser.Username ?? "System";

        foreach (var c in request.Choices)
        {
            _db.QuestionChoices.Add(new QuestionChoice
            {
                QuestionVersionId = newVersion.Id,
                Label = c.Label,
                ContentHtml = c.ContentHtml,
                IsCorrect = c.IsCorrect,
                OrderIndex = c.OrderIndex
            });
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Tạo phiên bản mới v{Version} cho câu hỏi {QuestionId}", nextVersionNumber, questionId);

        return await GetQuestionByIdAsync(question.Id, ct);
    }

    public async Task<Result<ContentPublicationDto>> PublishQuestionVersionAsync(
        Guid versionId, PublishQuestionVersionRequest request, CancellationToken ct)
    {
        var version = await _db.QuestionVersions
            .Include(v => v.Question)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);

        if (version == null)
            return Result<ContentPublicationDto>.Failure("Không tìm thấy phiên bản câu hỏi.");

        // Update status to Published
        version.Status = QuestionPublicationStatus.Published;

        var publisherName = _currentUser.Username ?? "SystemAdmin";

        var publication = new ContentPublication
        {
            QuestionVersionId = version.Id,
            PublishedBy = publisherName,
            PublishedAt = DateTime.UtcNow,
            Notes = request.Notes ?? "Đã phê duyệt và xuất bản vào ngân hàng đề thi chính thức."
        };

        _db.ContentPublications.Add(publication);

        // Update parent Question current version
        version.Question.CurrentVersionId = version.Id;
        version.Question.UpdatedAt = DateTime.UtcNow;
        version.Question.UpdatedBy = publisherName;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Xuất bản thành công câu hỏi {QuestionId} phiên bản v{VersionNumber} bởi {Publisher}",
            version.QuestionId, version.VersionNumber, publisherName);

        var dto = new ContentPublicationDto
        {
            Id = publication.Id,
            QuestionVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            PublishedBy = publication.PublishedBy,
            PublishedAt = publication.PublishedAt,
            Notes = publication.Notes
        };

        return Result<ContentPublicationDto>.Success(dto);
    }

    // ── Customers, Subscriptions & Payments ──

    public async Task<PaginatedResult<CustomerSummaryDto>> GetCustomersAsync(
        string? search, int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.EdTechCustomers
            .AsNoTracking()
            .Include(c => c.Subscriptions)
            .Include(c => c.Payments)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(c => c.FullName.ToLower().Contains(s)
                                  || c.Email.ToLower().Contains(s)
                                  || (c.PhoneNumber != null && c.PhoneNumber.Contains(s)));
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CustomerSummaryDto
            {
                Id = c.Id,
                SourceId = c.SourceId,
                FullName = c.FullName,
                Email = c.Email,
                PhoneNumber = c.PhoneNumber,
                SubscriptionCount = c.Subscriptions.Count,
                TotalPaidAmount = c.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount),
                CreatedAt = c.CreatedAt
            })
            .ToListAsync(ct);

        return new PaginatedResult<CustomerSummaryDto>(items, count, pageIndex, pageSize);
    }

    public async Task<PaginatedResult<SubscriptionDto>> GetSubscriptionsAsync(
        string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.Subscriptions
            .AsNoTracking()
            .Include(s => s.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<SubscriptionStatus>(status, true, out var subStatus))
        {
            query = query.Where(s => s.Status == subStatus);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(s => s.StartsAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                CustomerId = s.CustomerId,
                CustomerName = s.Customer.FullName,
                PackageName = s.PackageName,
                StartsAt = s.StartsAt,
                ExpiresAt = s.ExpiresAt,
                Status = s.Status.ToString()
            })
            .ToListAsync(ct);

        return new PaginatedResult<SubscriptionDto>(items, count, pageIndex, pageSize);
    }

    public async Task<PaginatedResult<PaymentDto>> GetPaymentsAsync(
        string? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        var query = _db.Payments
            .AsNoTracking()
            .Include(p => p.Customer)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PaymentStatus>(status, true, out var paymentStatus))
        {
            query = query.Where(p => p.Status == paymentStatus);
        }

        var count = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(p => p.PaidAt)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                CustomerId = p.CustomerId,
                CustomerName = p.Customer.FullName,
                SourcePaymentId = p.SourcePaymentId,
                Amount = p.Amount,
                Currency = p.Currency,
                Status = p.Status.ToString(),
                PaidAt = p.PaidAt,
                PaymentMethod = p.PaymentMethod,
                TransactionReference = p.TransactionReference
            })
            .ToListAsync(ct);

        return new PaginatedResult<PaymentDto>(items, count, pageIndex, pageSize);
    }

    private static QuestionDto MapQuestionToDto(Question q)
    {
        var currentVersion = q.Versions.FirstOrDefault(v => v.Id == q.CurrentVersionId)
                             ?? q.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();

        return new QuestionDto
        {
            Id = q.Id,
            BankName = q.QuestionBank.Name,
            SubjectName = q.Subject?.Name,
            TopicName = q.Topic?.Name,
            DifficultyLevel = q.DifficultyLevel,
            CurrentVersionId = q.CurrentVersionId,
            CurrentVersionNumber = currentVersion?.VersionNumber ?? 1,
            Status = (currentVersion?.Status ?? QuestionPublicationStatus.Draft).ToString(),
            ContentHtml = currentVersion?.ContentHtml ?? string.Empty,
            ExplanationHtml = currentVersion?.ExplanationHtml,
            Choices = currentVersion?.Choices.OrderBy(c => c.OrderIndex).Select(c => new QuestionChoiceDto
            {
                Id = c.Id,
                Label = c.Label,
                ContentHtml = c.ContentHtml,
                IsCorrect = c.IsCorrect,
                OrderIndex = c.OrderIndex
            }).ToList() ?? [],
            Tags = q.Tags.Select(t => t.Tag).ToList(),
            Versions = q.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new QuestionVersionSummaryDto
            {
                Id = v.Id,
                VersionNumber = v.VersionNumber,
                Status = v.Status.ToString(),
                CreatedAt = v.CreatedAt,
                CreatedBy = v.CreatedBy
            }).ToList()
        };
    }
}
