using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternalManagement.Application.Common.Models;
using InternalManagement.Application.Common.Security;
using InternalManagement.Application.Features.EdTech.DTOs;
using InternalManagement.Application.Features.EdTech.Services;

namespace InternalManagement.Api.Controllers;

[Route("api/v1/[controller]")]
public class EdTechController : BaseApiController
{
    private readonly IEdTechService _edTechService;

    public EdTechController(IEdTechService edTechService)
    {
        _edTechService = edTechService;
    }

    // ── Courses Endpoints ──

    [HttpGet("courses")]
    [HasPermission(Permissions.CoursesView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<CourseDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCourses(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _edTechService.GetCoursesAsync(search, status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<CourseDto>>.Ok(result, "Lấy danh sách khóa học thành công."));
    }

    [HttpGet("courses/{id:guid}")]
    [HasPermission(Permissions.CoursesView)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCourseById(Guid id, CancellationToken ct)
    {
        var result = await _edTechService.GetCourseByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<CourseDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy khóa học."));

        return Ok(ApiResponse<CourseDto>.Ok(result.Value!, "Lấy chi tiết khóa học thành công."));
    }

    [HttpPost("courses")]
    [HasPermission(Permissions.CoursesManage)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateCourse([FromBody] CreateCourseRequest request, CancellationToken ct)
    {
        var result = await _edTechService.CreateCourseAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CourseDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo khóa học thất bại."));

        return CreatedAtAction(nameof(GetCourseById), new { id = result.Value!.Id }, ApiResponse<CourseDto>.Ok(result.Value, "Tạo khóa học thành công."));
    }

    [HttpPut("courses/{id:guid}")]
    [HasPermission(Permissions.CoursesManage)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CourseDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateCourse(Guid id, [FromBody] UpdateCourseRequest request, CancellationToken ct)
    {
        var result = await _edTechService.UpdateCourseAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<CourseDto>.Fail(result.Errors.FirstOrDefault() ?? "Cập nhật khóa học thất bại."));

        return Ok(ApiResponse<CourseDto>.Ok(result.Value!, "Cập nhật khóa học thành công."));
    }

    [HttpDelete("courses/{id:guid}")]
    [HasPermission(Permissions.CoursesManage)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteCourse(Guid id, CancellationToken ct)
    {
        var result = await _edTechService.DeleteCourseAsync(id, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<bool>.Fail(result.Errors.FirstOrDefault() ?? "Xóa khóa học thất bại."));

        return Ok(ApiResponse<bool>.Ok(true, "Xóa khóa học thành công."));
    }

    // ── Questions & Versioning & Publication Endpoints (Ngày 9 Core) ──

    [HttpGet("questions")]
    [HasPermission(Permissions.QuestionsView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<QuestionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetQuestions(
        [FromQuery] string? search,
        [FromQuery] string? difficulty,
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _edTechService.GetQuestionsAsync(search, difficulty, status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<QuestionDto>>.Ok(result, "Lấy ngân hàng câu hỏi thành công."));
    }

    [HttpGet("questions/{id:guid}")]
    [HasPermission(Permissions.QuestionsView)]
    [ProducesResponseType(typeof(ApiResponse<QuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QuestionDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetQuestionById(Guid id, CancellationToken ct)
    {
        var result = await _edTechService.GetQuestionByIdAsync(id, ct);
        if (!result.Succeeded)
            return NotFound(ApiResponse<QuestionDto>.Fail(result.Errors.FirstOrDefault() ?? "Không tìm thấy câu hỏi."));

        return Ok(ApiResponse<QuestionDto>.Ok(result.Value!, "Lấy chi tiết câu hỏi thành công."));
    }

    [HttpPost("questions")]
    [HasPermission(Permissions.QuestionsManage)]
    [ProducesResponseType(typeof(ApiResponse<QuestionDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<QuestionDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateQuestion([FromBody] CreateQuestionRequest request, CancellationToken ct)
    {
        var result = await _edTechService.CreateQuestionAsync(request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<QuestionDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo câu hỏi thất bại."));

        return CreatedAtAction(nameof(GetQuestionById), new { id = result.Value!.Id }, ApiResponse<QuestionDto>.Ok(result.Value, "Tạo câu hỏi mới thành công."));
    }

    [HttpPost("questions/{id:guid}/versions")]
    [HasPermission(Permissions.QuestionsManage)]
    [ProducesResponseType(typeof(ApiResponse<QuestionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<QuestionDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateQuestionVersion(Guid id, [FromBody] CreateQuestionVersionRequest request, CancellationToken ct)
    {
        var result = await _edTechService.CreateQuestionVersionAsync(id, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<QuestionDto>.Fail(result.Errors.FirstOrDefault() ?? "Tạo phiên bản câu hỏi mới thất bại."));

        return Ok(ApiResponse<QuestionDto>.Ok(result.Value!, "Tạo phiên bản câu hỏi mới thành công."));
    }

    [HttpPost("questions/versions/{versionId:guid}/publish")]
    [HasPermission(Permissions.QuestionsPublish)]
    [ProducesResponseType(typeof(ApiResponse<ContentPublicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ContentPublicationDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PublishQuestionVersion(Guid versionId, [FromBody] PublishQuestionVersionRequest request, CancellationToken ct)
    {
        var result = await _edTechService.PublishQuestionVersionAsync(versionId, request, ct);
        if (!result.Succeeded)
            return BadRequest(ApiResponse<ContentPublicationDto>.Fail(result.Errors.FirstOrDefault() ?? "Xuất bản câu hỏi thất bại."));

        return Ok(ApiResponse<ContentPublicationDto>.Ok(result.Value!, "Xuất bản phiên bản câu hỏi thành công."));
    }

    // ── Customers, Subscriptions & Payments Endpoints ──

    [HttpGet("customers")]
    [HasPermission(Permissions.EdTechCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<CustomerSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] string? search,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _edTechService.GetCustomersAsync(search, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<CustomerSummaryDto>>.Ok(result, "Lấy danh sách học viên thành công."));
    }

    [HttpGet("subscriptions")]
    [HasPermission(Permissions.EdTechCustomersView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<SubscriptionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _edTechService.GetSubscriptionsAsync(status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<SubscriptionDto>>.Ok(result, "Lấy danh sách gói thuê bao thành công."));
    }

    [HttpGet("payments")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType(typeof(ApiResponse<PaginatedResult<PaymentDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPayments(
        [FromQuery] string? status,
        [FromQuery] int pageIndex = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await _edTechService.GetPaymentsAsync(status, pageIndex, pageSize, ct);
        return Ok(ApiResponse<PaginatedResult<PaymentDto>>.Ok(result, "Lấy danh sách thanh toán thành công."));
    }
}
