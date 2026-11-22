using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace InternalManagement.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class HealthController : BaseApiController
{
    private readonly HealthCheckService _healthCheckService;

    public HealthController(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken);
        var isAuthenticated = User.Identity?.IsAuthenticated == true;

        object result = isAuthenticated
            ? new
            {
                Status = report.Status.ToString(),
                TotalDuration = report.TotalDuration.TotalMilliseconds,
                Entries = report.Entries.Select(e => new
                {
                    Key = e.Key,
                    Status = e.Value.Status.ToString(),
                    Description = e.Value.Description,
                    Duration = e.Value.Duration.TotalMilliseconds,
                    Data = e.Value.Data
                })
            }
            : new
            {
                Status = report.Status.ToString(),
                TotalDuration = report.TotalDuration.TotalMilliseconds
            };

        return report.Status == HealthStatus.Healthy
            ? Ok(result)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, result);
    }
}
