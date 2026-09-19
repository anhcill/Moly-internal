using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.EdTech.DTOs;

namespace InternalManagement.Application.Features.EdTech.Services;

public interface IEdTechService
{
    // Courses
    Task<PaginatedResult<CourseDto>> GetCoursesAsync(string? search, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<CourseDto>> GetCourseByIdAsync(Guid id, CancellationToken ct);
    Task<Result<CourseDto>> CreateCourseAsync(CreateCourseRequest request, CancellationToken ct);
    Task<Result<CourseDto>> UpdateCourseAsync(Guid id, UpdateCourseRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteCourseAsync(Guid id, CancellationToken ct);

    // Questions & Versions & Publication (Ngày 9 Core)
    Task<PaginatedResult<QuestionDto>> GetQuestionsAsync(string? search, string? difficulty, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<Result<QuestionDto>> GetQuestionByIdAsync(Guid id, CancellationToken ct);
    Task<Result<QuestionDto>> CreateQuestionAsync(CreateQuestionRequest request, CancellationToken ct);
    Task<Result<QuestionDto>> CreateQuestionVersionAsync(Guid questionId, CreateQuestionVersionRequest request, CancellationToken ct);
    Task<Result<ContentPublicationDto>> PublishQuestionVersionAsync(Guid versionId, PublishQuestionVersionRequest request, CancellationToken ct);

    // Customers, Subscriptions & Payments
    Task<PaginatedResult<CustomerSummaryDto>> GetCustomersAsync(string? search, int pageIndex, int pageSize, CancellationToken ct);
    Task<PaginatedResult<SubscriptionDto>> GetSubscriptionsAsync(string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<PaginatedResult<PaymentDto>> GetPaymentsAsync(string? status, int pageIndex, int pageSize, CancellationToken ct);
}
