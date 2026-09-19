using Microsoft.AspNetCore.Authorization;

namespace InternalManagement.Application.Common.Security;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public string Permission { get; }

    public HasPermissionAttribute(string permission) : base(policy: permission)
    {
        Permission = permission;
    }
}
