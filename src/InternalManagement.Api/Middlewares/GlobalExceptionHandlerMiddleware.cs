using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace InternalManagement.Api.Middlewares;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
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
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var cause = exception.GetBaseException();
        var (errorCode, suggestion) = cause switch
        {
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

        var problemDetails = new ProblemDetails
        {
            Status = context.Response.StatusCode,
            Title = "Không thể hoàn tất thao tác trên máy chủ.",
            Detail = exception.Message,
            Instance = context.Request.Path
        };

        problemDetails.Extensions["correlationId"] = context.TraceIdentifier;
        problemDetails.Extensions["operation"] = $"{context.Request.Method} {context.Request.Path}";
        problemDetails.Extensions["errorCode"] = errorCode;
        problemDetails.Extensions["cause"] = $"{cause.GetType().Name}: {cause.Message}";
        problemDetails.Extensions["suggestion"] = suggestion;

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return context.Response.WriteAsync(JsonSerializer.Serialize(problemDetails, jsonOptions));
    }
}
