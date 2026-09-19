using System.Security.Claims;

namespace InternalManagement.Api.Middlewares;

/// <summary>
/// Validates optional scope headers against the authenticated token.
/// Controllers/services still have to apply the same scope to their queries.
/// </summary>
public sealed class TenantScopeMiddleware
{
    private const string CompanyHeader = "X-Company-ID";
    private const string BusinessUnitHeader = "X-Business-Unit-ID";
    private readonly RequestDelegate _next;

    public TenantScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !IsSuperAdmin(context.User))
        {
            if (!MatchesClaim(context, CompanyHeader, "company_id") ||
                !MatchesClaim(context, BusinessUnitHeader, "bu_id"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    message = "Phạm vi công ty/business unit không khớp với tài khoản."
                });
                return;
            }
        }

        await _next(context);
    }

    private static bool MatchesClaim(HttpContext context, string headerName, string claimType)
    {
        if (!context.Request.Headers.TryGetValue(headerName, out var headerValue) ||
            string.IsNullOrWhiteSpace(headerValue))
        {
            return true;
        }

        var claimValue = context.User.FindFirstValue(claimType);
        return Guid.TryParse(headerValue.ToString(), out var requestedId) &&
               Guid.TryParse(claimValue, out var tokenId) &&
               requestedId == tokenId;
    }

    private static bool IsSuperAdmin(ClaimsPrincipal user) => user.IsInRole("SuperAdmin");
}
