namespace InternalManagement.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Username { get; }
    Guid? CompanyId { get; }
    Guid? BusinessUnitId { get; }
    IReadOnlyList<string> Permissions { get; }
    bool HasPermission(string permission);
}

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
