using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Features.CscaInterview.DTOs;

namespace InternalManagement.Application.Features.CscaInterview.Services;

public interface ICscaService
{
    // Class Management
    Task<PaginatedResult<CscaClassDto>> GetClassesAsync(string? search, string? batch, string? status, int pageIndex, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<CscaStudentDirectoryDto>> GetStudentDirectoryAsync(string? search, CancellationToken ct);
    Task<Result<CscaClassDetailDto>> GetClassByIdAsync(Guid id, CancellationToken ct);
    Task<Result<CscaClassDto>> CreateClassAsync(CreateCscaClassRequest request, CancellationToken ct);
    Task<Result<CscaClassDto>> UpdateClassAsync(Guid id, UpdateCscaClassRequest request, CancellationToken ct);
    Task<Result<bool>> DeleteClassAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<CscaScheduleDto>> GetSchedulesAsync(Guid classId, CancellationToken ct);
    Task<Result<CscaScheduleDto>> AddScheduleAsync(Guid classId, CreateCscaScheduleRequest request, CancellationToken ct);
    Task<Result<CscaScheduleDto>> UpdateScheduleAsync(Guid classId, Guid scheduleId, UpdateCscaScheduleRequest request, CancellationToken ct);
    Task<Result<bool>> RemoveScheduleAsync(Guid classId, Guid scheduleId, CancellationToken ct);

    // Classroom catalog & dated lessons
    Task<IReadOnlyList<CscaClassroomDto>> GetClassroomsAsync(bool includeInactive, CancellationToken ct);
    Task<Result<CscaClassroomDto>> CreateClassroomAsync(CreateCscaClassroomRequest request, CancellationToken ct);
    Task<Result<CscaClassroomDto>> UpdateClassroomAsync(Guid classroomId, UpdateCscaClassroomRequest request, CancellationToken ct);
    Task<Result<bool>> RemoveClassroomAsync(Guid classroomId, CancellationToken ct);
    Task<IReadOnlyList<CscaLessonSessionDto>> GetLessonSessionsAsync(Guid classId, DateOnly? fromDate, DateOnly? toDate, CancellationToken ct);
    Task<Result<CscaLessonSessionDto>> CreateLessonSessionAsync(Guid classId, CreateCscaLessonSessionRequest request, CancellationToken ct);
    Task<Result<CscaLessonSessionDto>> UpdateLessonSessionAsync(Guid classId, Guid sessionId, UpdateCscaLessonSessionRequest request, CancellationToken ct);
    Task<Result<bool>> RemoveLessonSessionAsync(Guid classId, Guid sessionId, CancellationToken ct);
    Task<Result<int>> GenerateLessonSessionsAsync(Guid classId, GenerateCscaLessonSessionsRequest request, CancellationToken ct);
    Task<Result<IReadOnlyList<CscaLessonAttendanceDto>>> GetLessonAttendanceAsync(Guid classId, Guid sessionId, CancellationToken ct);
    Task<Result<CscaLessonAttendanceDto>> UpsertLessonAttendanceAsync(Guid classId, Guid sessionId, UpsertCscaLessonAttendanceRequest request, CancellationToken ct);

    // Student Enrollment
    Task<Result<CscaStudentDto>> EnrollStudentAsync(Guid classId, EnrollStudentRequest request, CancellationToken ct);
    Task<Result<CscaStudentDto>> UpdateStudentPaymentAsync(Guid classId, Guid studentId, UpdateStudentPaymentRequest request, CancellationToken ct);
    Task<Result<bool>> RemoveStudentAsync(Guid classId, Guid studentId, CancellationToken ct);

    // Staff Assignment
    Task<Result<CscaStaffDto>> AssignStaffAsync(Guid classId, AssignStaffRequest request, CancellationToken ct);
    Task<Result<CscaStaffDto>> UpdateStaffAsync(Guid classId, Guid staffId, UpdateCscaStaffRequest request, CancellationToken ct);
    Task<Result<bool>> RemoveStaffAsync(Guid classId, Guid staffId, CancellationToken ct);

    // Financial Calculation
    Task<Result<ClassFinancialSummaryDto>> GetClassFinancialSummaryAsync(Guid classId, CancellationToken ct);
}
