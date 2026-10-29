using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using InternalManagement.Application.Common.Interfaces;

namespace InternalManagement.Infrastructure.Services;

public class CurrentUserService : ICurrentUserService
{
    private const string BusinessUnitHeader = "X-Business-Unit-ID";
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor? httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var idClaim = _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? _httpContextAccessor?.HttpContext?.User?.FindFirstValue("sub");

            return Guid.TryParse(idClaim, out var id) ? id : null;
        }
    }

    public string? Username =>
        _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.Name)
        ?? _httpContextAccessor?.HttpContext?.User?.FindFirstValue("unique_name")
        ?? "System";

    public Guid? CompanyId
    {
        get
        {
            var companyClaim = _httpContextAccessor?.HttpContext?.User?.FindFirstValue("company_id");
            return Guid.TryParse(companyClaim, out var id) ? id : null;
        }
    }

    public Guid? BusinessUnitId
    {
        get
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            var selectedBusinessUnit = httpContext?.Request.Headers[BusinessUnitHeader].ToString();
            if (Guid.TryParse(selectedBusinessUnit, out var selectedId))
                return selectedId;

            var buClaim = httpContext?.User?.FindFirstValue("bu_id");
            return Guid.TryParse(buClaim, out var id) ? id : null;
        }
    }

    public IReadOnlyList<string> Permissions
    {
        get
        {
            var claims = _httpContextAccessor?.HttpContext?.User?.FindAll("permission");
            return claims?.Select(c => c.Value).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
    }

    public bool HasPermission(string permission)
    {
        if (_httpContextAccessor?.HttpContext?.User?.IsInRole("SuperAdmin") == true)
            return true;

        return Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }
}

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
