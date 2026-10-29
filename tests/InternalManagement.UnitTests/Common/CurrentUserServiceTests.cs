using System.Security.Claims;
using FluentAssertions;
using InternalManagement.Infrastructure.Services;
using Microsoft.AspNetCore.Http;

namespace InternalManagement.UnitTests.Common;

public class CurrentUserServiceTests
{
    [Fact]
    public void BusinessUnitId_UsesSelectedBusinessUnitHeader_WhenPresent()
    {
        var claimBusinessUnitId = Guid.NewGuid();
        var selectedBusinessUnitId = Guid.NewGuid();
        var context = CreateContext(claimBusinessUnitId);
        context.Request.Headers["X-Business-Unit-ID"] = selectedBusinessUnitId.ToString();

        var service = new CurrentUserService(new HttpContextAccessor { HttpContext = context });

        service.BusinessUnitId.Should().Be(selectedBusinessUnitId);
    }

    [Fact]
    public void BusinessUnitId_FallsBackToClaim_WhenHeaderIsMissingOrInvalid()
    {
        var claimBusinessUnitId = Guid.NewGuid();
        var context = CreateContext(claimBusinessUnitId);
        context.Request.Headers["X-Business-Unit-ID"] = "not-a-guid";

        var service = new CurrentUserService(new HttpContextAccessor { HttpContext = context });

        service.BusinessUnitId.Should().Be(claimBusinessUnitId);
    }

    private static DefaultHttpContext CreateContext(Guid businessUnitId)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("bu_id", businessUnitId.ToString())],
            authenticationType: "Test"));
        return context;
    }
}
