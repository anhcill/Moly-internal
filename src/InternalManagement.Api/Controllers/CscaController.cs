using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.CscaInterview.DTOs;
using InternalManagement.Application.Features.CscaInterview.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class CscaController : BaseApiController
{
    private readonly ICscaService _cscaService;
    private readonly ICscaOnlineService _cscaOnlineService;

    public CscaController(ICscaService cscaService, ICscaOnlineService cscaOnlineService)
    {
        _cscaService = cscaService;
        _cscaOnlineService = cscaOnlineService;
    }

    [HttpGet("online/materials")]
    [HasPermission(Permissions.CscaClassesView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CscaOnlineMaterialDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOnlineMaterials(
        [FromQuery] string? search,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _cscaOnlineService.GetMaterialsAsync(search, pageSize, ct);
            return Ok(ApiResponse<IReadOnlyList<CscaOnlineMaterialDto>>.Ok(result, "Lấy tài liệu CSCA online thành công."));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse<IReadOnlyList<CscaOnlineMaterialDto>>.Fail($"Không thể kết nối tài liệu CSCA online: {ex.Message}"));
        }
    }

    [HttpGet("online/vocabulary")]
    [HasPermission(Permissions.CscaClassesView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CscaOnlineVocabularyDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOnlineVocabulary(
        [FromQuery] string? search,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _cscaOnlineService.GetVocabularyAsync(search, pageSize, ct);
            return Ok(ApiResponse<IReadOnlyList<CscaOnlineVocabularyDto>>.Ok(result, "Lấy từ vựng CSCA online thành công."));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse<IReadOnlyList<CscaOnlineVocabularyDto>>.Fail($"Không thể kết nối từ vựng CSCA online: {ex.Message}"));
        }
    }

    [HttpGet("online/posts")]
    [HasPermission(Permissions.CscaClassesView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CscaOnlinePostDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOnlinePosts(
        [FromQuery] string? search,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _cscaOnlineService.GetPostsAsync(search, pageSize, ct);
            return Ok(ApiResponse<IReadOnlyList<CscaOnlinePostDto>>.Ok(result, "Lấy bài viết cộng đồng CSCA online thành công."));
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                ApiResponse<IReadOnlyList<CscaOnlinePostDto>>.Fail($"Không thể kết nối cộng đồng CSCA online: {ex.Message}"));
        }
    }

    [HttpGet("classes")]
    [HasPermission(Permissions.CscaClassesView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<CscaClassDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetClasses(
        [FromQuery] string? search,
        [FromQuery] string? batch,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _cscaService.GetClassesAsync(search, batch, status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<CscaClassDto>>.Ok(result, "Lấy danh sách lớp học CSCA thành công."));
    }

    [HttpGet("students")]
    [HasPermission(Permissions.EdTechCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CscaStudentDirectoryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStudentDirectory([FromQuery] string? search, CancellationToken ct = default)
    {
        var result = await _cscaService.GetStudentDirectoryAsync(search, ct);
        return Ok(ApiResponse<IReadOnlyList<CscaStudentDirectoryDto>>.Ok(result, "Lấy danh sách học viên từ lớp học thành công."));
    }

    [HttpGet("classes/{id:guid}")]
    [HasPermission(Permissions.CscaClassesView)]
    [ProducesResponseType(typeof(ApiResponse<CscaClassDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaClassDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassById(Guid id, CancellationToken ct)
    {
        var result = await _cscaService.GetClassByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<CscaClassDetailDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy lớp học."));

        return Ok(ApiResponse<CscaClassDetailDto>.Ok(result.Value!, "Lấy chi tiết lớp học thành công."));
    }

    [HttpPost("classes")]
    [HasPermission(Permissions.CscaClassesManage)]
    [ProducesResponseType(typeof(ApiResponse<CscaClassDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<CscaClassDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateClass([FromBody] CreateCscaClassRequest request, CancellationToken ct)
    {
        var result = await _cscaService.CreateClassAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaClassDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo lớp học thất bại."));

        return CreatedAtAction(nameof(GetClassById), new { id = result.Value!.Id }, ApiResponse<CscaClassDto>.Ok(result.Value, "Tạo lớp học CSCA thành công."));
    }

    [HttpPut("classes/{id:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    [ProducesResponseType(typeof(ApiResponse<CscaClassDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaClassDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateClass(Guid id, [FromBody] UpdateCscaClassRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpdateClassAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaClassDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật lớp học thất bại."));

        return Ok(ApiResponse<CscaClassDto>.Ok(result.Value!, "Cập nhật thông tin lớp học thành công."));
    }

    [HttpDelete("classes/{id:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteClass(Guid id, CancellationToken ct)
    {
        var result = await _cscaService.DeleteClassAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa lớp học thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Xóa lớp học CSCA thành công."));
    }

    [HttpGet("classes/{id:guid}/schedules")]
    [HasPermission(Permissions.CscaClassesView)]
    public async Task<IActionResult> GetSchedules(Guid id, CancellationToken ct)
    {
        var result = await _cscaService.GetSchedulesAsync(id, ct);
        return Ok(ApiResponse<IReadOnlyList<CscaScheduleDto>>.Ok(result, "Lấy lịch học thành công."));
    }

    [HttpPost("classes/{id:guid}/schedules")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> AddSchedule(Guid id, [FromBody] CreateCscaScheduleRequest request, CancellationToken ct)
    {
        var result = await _cscaService.AddScheduleAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaScheduleDto>.Fail(result.Errors.FirstOrDefault() ?? "Thêm lịch học thất bại."));
        return Ok(ApiResponse<CscaScheduleDto>.Ok(result.Value!, "Thêm lịch học thành công."));
    }

    [HttpPut("classes/{id:guid}/schedules/{scheduleId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> UpdateSchedule(Guid id, Guid scheduleId, [FromBody] UpdateCscaScheduleRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpdateScheduleAsync(id, scheduleId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaScheduleDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật lịch học thất bại."));
        return Ok(ApiResponse<CscaScheduleDto>.Ok(result.Value!, "Cập nhật lịch học thành công."));
    }

    [HttpDelete("classes/{id:guid}/schedules/{scheduleId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> RemoveSchedule(Guid id, Guid scheduleId, CancellationToken ct)
    {
        var result = await _cscaService.RemoveScheduleAsync(id, scheduleId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa lịch học thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Xóa lịch học thành công."));
    }

    [HttpGet("classrooms")]
    [HasPermission(Permissions.CscaClassesView)]
    public async Task<IActionResult> GetClassrooms([FromQuery] bool includeInactive = false, CancellationToken ct = default)
    {
        var result = await _cscaService.GetClassroomsAsync(includeInactive, ct);
        return Ok(ApiResponse<IReadOnlyList<CscaClassroomDto>>.Ok(result, "Lấy danh mục phòng học thành công."));
    }

    [HttpPost("classrooms")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> CreateClassroom([FromBody] CreateCscaClassroomRequest request, CancellationToken ct)
    {
        var result = await _cscaService.CreateClassroomAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaClassroomDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo phòng học thất bại."));
        return Ok(ApiResponse<CscaClassroomDto>.Ok(result.Value!, "Tạo phòng học thành công."));
    }

    [HttpPut("classrooms/{classroomId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> UpdateClassroom(Guid classroomId, [FromBody] UpdateCscaClassroomRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpdateClassroomAsync(classroomId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaClassroomDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật phòng học thất bại."));
        return Ok(ApiResponse<CscaClassroomDto>.Ok(result.Value!, "Cập nhật phòng học thành công."));
    }

    [HttpDelete("classrooms/{classroomId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> RemoveClassroom(Guid classroomId, CancellationToken ct)
    {
        var result = await _cscaService.RemoveClassroomAsync(classroomId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa phòng học thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Xóa phòng học thành công."));
    }

    [HttpGet("classes/{id:guid}/sessions")]
    [HasPermission(Permissions.CscaClassesView)]
    public async Task<IActionResult> GetLessonSessions(Guid id, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate, CancellationToken ct)
    {
        var result = await _cscaService.GetLessonSessionsAsync(id, fromDate, toDate, ct);
        return Ok(ApiResponse<IReadOnlyList<CscaLessonSessionDto>>.Ok(result, "Lấy danh sách buổi học thành công."));
    }

    [HttpPost("classes/{id:guid}/sessions")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> CreateLessonSession(Guid id, [FromBody] CreateCscaLessonSessionRequest request, CancellationToken ct)
    {
        var result = await _cscaService.CreateLessonSessionAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaLessonSessionDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo buổi học thất bại."));
        return Ok(ApiResponse<CscaLessonSessionDto>.Ok(result.Value!, "Tạo buổi học thành công."));
    }

    [HttpPut("classes/{id:guid}/sessions/{sessionId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> UpdateLessonSession(Guid id, Guid sessionId, [FromBody] UpdateCscaLessonSessionRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpdateLessonSessionAsync(id, sessionId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaLessonSessionDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật buổi học thất bại."));
        return Ok(ApiResponse<CscaLessonSessionDto>.Ok(result.Value!, "Cập nhật buổi học thành công."));
    }

    [HttpDelete("classes/{id:guid}/sessions/{sessionId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> RemoveLessonSession(Guid id, Guid sessionId, CancellationToken ct)
    {
        var result = await _cscaService.RemoveLessonSessionAsync(id, sessionId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa buổi học thất bại."));
        return Ok(ApiResponse<bool>.Ok(true, "Xóa buổi học thành công."));
    }

    [HttpPost("classes/{id:guid}/sessions/generate")]
    [HasPermission(Permissions.CscaClassesManage)]
    public async Task<IActionResult> GenerateLessonSessions(Guid id, [FromBody] GenerateCscaLessonSessionsRequest request, CancellationToken ct)
    {
        var result = await _cscaService.GenerateLessonSessionsAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<int>.Fail(result.Errors.FirstOrDefault() ?? "Sinh buổi học thất bại."));
        return Ok(ApiResponse<int>.Ok(result.Value!, $"Đã tạo {result.Value} buổi học từ lịch tuần."));
    }

    [HttpGet("classes/{id:guid}/sessions/{sessionId:guid}/attendance")]
    [HasPermission(Permissions.CscaClassesView)]
    public async Task<IActionResult> GetLessonAttendance(Guid id, Guid sessionId, CancellationToken ct)
    {
        var result = await _cscaService.GetLessonAttendanceAsync(id, sessionId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<IReadOnlyList<CscaLessonAttendanceDto>>.Fail(result.Errors.FirstOrDefault() ?? "Lấy điểm danh thất bại."));
        return Ok(ApiResponse<IReadOnlyList<CscaLessonAttendanceDto>>.Ok(result.Value!, "Lấy điểm danh buổi học thành công."));
    }

    [HttpPut("classes/{id:guid}/sessions/{sessionId:guid}/attendance")]
    [HasPermission(Permissions.CscaStudentsManage)]
    public async Task<IActionResult> UpsertLessonAttendance(Guid id, Guid sessionId, [FromBody] UpsertCscaLessonAttendanceRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpsertLessonAttendanceAsync(id, sessionId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaLessonAttendanceDto>.Fail(result.Errors.FirstOrDefault() ?? "Lưu điểm danh thất bại."));
        return Ok(ApiResponse<CscaLessonAttendanceDto>.Ok(result.Value!, "Đã lưu điểm danh học viên."));
    }

    [HttpPost("classes/{id:guid}/students")]
    [HasPermission(Permissions.CscaStudentsManage)]
    [ProducesResponseType(typeof(ApiResponse<CscaStudentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<CscaStudentDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EnrollStudent(Guid id, [FromBody] EnrollStudentRequest request, CancellationToken ct)
    {
        var result = await _cscaService.EnrollStudentAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaStudentDto>.Fail(result.Errors.FirstOrDefault() ?? "Thêm học viên thất bại."));

        return Ok(ApiResponse<CscaStudentDto>.Ok(result.Value!, "Ghi danh học viên vào lớp thành công."));
    }

    [HttpPut("classes/{id:guid}/students/{studentId:guid}/payment")]
    [HasPermission(Permissions.CscaStudentsManage)]
    [ProducesResponseType(typeof(ApiResponse<CscaStudentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaStudentDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStudentPayment(
        Guid id, Guid studentId, [FromBody] UpdateStudentPaymentRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpdateStudentPaymentAsync(id, studentId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaStudentDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật học phí thất bại."));

        return Ok(ApiResponse<CscaStudentDto>.Ok(result.Value!, "Cập nhật trạng thái đóng học phí thành công."));
    }

    [HttpDelete("classes/{id:guid}/students/{studentId:guid}")]
    [HasPermission(Permissions.CscaStudentsManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveStudent(Guid id, Guid studentId, CancellationToken ct)
    {
        var result = await _cscaService.RemoveStudentAsync(id, studentId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa học viên thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Xóa học viên khỏi lớp thành công."));
    }

    [HttpPost("classes/{id:guid}/staff")]
    [HasPermission(Permissions.CscaClassesManage)]
    [ProducesResponseType(typeof(ApiResponse<CscaStaffDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaStaffDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AssignStaff(Guid id, [FromBody] AssignStaffRequest request, CancellationToken ct)
    {
        var result = await _cscaService.AssignStaffAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaStaffDto>.Fail(result.Errors.FirstOrDefault() ?? "Phân công nhân sự thất bại."));

        return Ok(ApiResponse<CscaStaffDto>.Ok(result.Value!, "Phân công nhân sự/giảng viên thành công."));
    }

    [HttpPut("classes/{id:guid}/staff/{staffId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    [ProducesResponseType(typeof(ApiResponse<CscaStaffDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CscaStaffDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateStaff(Guid id, Guid staffId, [FromBody] UpdateCscaStaffRequest request, CancellationToken ct)
    {
        var result = await _cscaService.UpdateStaffAsync(id, staffId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CscaStaffDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật phân công thất bại."));

        return Ok(ApiResponse<CscaStaffDto>.Ok(result.Value!, "Cập nhật phân công nhân sự/giảng viên thành công."));
    }

    [HttpDelete("classes/{id:guid}/staff/{staffId:guid}")]
    [HasPermission(Permissions.CscaClassesManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveStaff(Guid id, Guid staffId, CancellationToken ct)
    {
        var result = await _cscaService.RemoveStaffAsync(id, staffId, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Hủy phân công nhân sự thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Hủy phân công nhân sự thành công."));
    }

    [HttpGet("classes/{id:guid}/financial-summary")]
    [HasPermission(Permissions.CscaClassesView)]
    [ProducesResponseType(typeof(ApiResponse<ClassFinancialSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ClassFinancialSummaryDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetClassFinancialSummary(Guid id, CancellationToken ct)
    {
        var result = await _cscaService.GetClassFinancialSummaryAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<ClassFinancialSummaryDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy dữ liệu tài chính lớp học."));

        return Ok(ApiResponse<ClassFinancialSummaryDto>.Ok(result.Value!, "Lấy báo cáo tài chính lớp học thành công."));
    }
}
