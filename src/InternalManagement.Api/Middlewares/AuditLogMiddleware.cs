using InternalManagement.Application.Common.Interfaces;
using InternalManagement.Domain.Entities.Identity;

namespace InternalManagement.Api.Middlewares;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditLogMiddleware> _logger;

    public AuditLogMiddleware(RequestDelegate next, ILogger<AuditLogMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IServiceProvider serviceProvider)
    {
        await _next(context);

        // Audit state-modifying actions if request was successful
        var method = context.Request.Method;
        var isMutation = method is "POST" or "PUT" or "DELETE" or "PATCH";
        var isSuccess = context.Response.StatusCode is >= 200 and < 300;
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip health checks
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (isMutation && isSuccess)
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var currentUserService = scope.ServiceProvider.GetRequiredService<ICurrentUserService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

                var action = method switch
                {
                    "POST" => "CREATE",
                    "PUT" => "UPDATE",
                    "PATCH" => "PARTIAL_UPDATE",
                    "DELETE" => "DELETE",
                    _ => method
                };

                var entityName = path.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "Resource";

                var auditLog = new AuditLog
                {
                    UserId = currentUserService.UserId,
                    CompanyId = currentUserService.CompanyId ?? Guid.Empty,
                    BusinessUnitId = currentUserService.BusinessUnitId,
                    Action = action,
                    EntityName = entityName,
                    EntityId = path,
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    CreatedAt = DateTime.UtcNow
                };

                dbContext.AuditLogs.Add(auditLog);
                await dbContext.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to record audit log for request: {Path}", path);
            }
        }
    }
}
