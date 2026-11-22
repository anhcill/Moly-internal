using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InternalManagement.Api.Middlewares;

public class GlobalExceptionHandlerMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred while processing request: {Path}", context.Request.Path);
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Response already started; cannot write problem details for {Path}.", context.Request.Path);
                throw;
            }
            await HandleExceptionAsync(context, ex);
        }
    }

    private Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        if (exception is DbUpdateConcurrencyException
            || exception is DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } })
            context.Response.StatusCode = StatusCodes.Status409Conflict;

        var cause = exception.GetBaseException();
        var (errorCode, suggestion) = cause switch
        {
            _ when context.Response.StatusCode == StatusCodes.Status409Conflict =>
                ("DATA_CONFLICT", "Dữ liệu đã thay đổi hoặc giao dịch đang được xử lý. Tải lại và thử lại cùng mã giao dịch để tránh ghi trùng."),
            TimeoutException or TaskCanceledException =>
                ("TIMEOUT", "Yêu cầu quá thời gian. Thử lại; nếu vẫn xảy ra, cung cấp mã yêu cầu cho quản trị viên."),
            HttpRequestException =>
                ("UPSTREAM_CONNECTION_ERROR", "Kiểm tra kết nối tới dịch vụ liên kết rồi thử lại."),
            UnauthorizedAccessException =>
                ("ACCESS_DENIED", "Kiểm tra quyền của tài khoản đối với thao tác này."),
            _ when cause.GetType().Name == "PostgresException" =>
                ("DATABASE_ERROR", "Kiểm tra dữ liệu nhập và trạng thái bản ghi liên quan; cung cấp mã yêu cầu nếu vẫn lỗi."),
            _ =>
                ("UNHANDLED_ERROR", "Cung cấp mã yêu cầu cho quản trị viên để tra log máy chủ.")
        };

        var exposeDetails = _environment.IsDevelopment() || _environment.IsEnvironment("Testing");
        var problemDetails = new ProblemDetails
        {
            Status = context.Response.StatusCode,
            Title = "Không thể hoàn tất thao tác trên máy chủ.",
            Detail = exposeDetails ? exception.Message : suggestion,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["correlationId"] = context.TraceIdentifier;
        problemDetails.Extensions["operation"] = $"{context.Request.Method} {context.Request.Path}";
        problemDetails.Extensions["errorCode"] = errorCode;
        if (exposeDetails)
        {
            problemDetails.Extensions["cause"] = $"{cause.GetType().Name}: {cause.Message}";
        }
        problemDetails.Extensions["suggestion"] = suggestion;

        return context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, JsonOptions));
    }
}
