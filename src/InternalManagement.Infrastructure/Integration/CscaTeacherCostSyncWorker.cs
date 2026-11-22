using InternalManagement.Application.Features.HrPayroll.DTOs;
using InternalManagement.Application.Features.HrPayroll.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Periodic background worker that pulls completed & projected teacher teaching costs
/// from MolyCourse LMS into local CscaTeacherCostSnapshots so Course, Payroll, and Finance
/// stay synchronized automatically without manual intervention.
/// </summary>
public sealed class CscaTeacherCostSyncWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CscaTeacherCostSyncWorker> _logger;

    public CscaTeacherCostSyncWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CscaTeacherCostSyncWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var section = _configuration.GetSection("Integrations:CscaCourseLms:TeacherCostSyncWorker");
            var enabled = section.GetValue<bool>("Enabled");
            var baseUrl = _configuration["Integrations:CscaCourseLms:BaseUrl"];
            var intervalSeconds = Math.Clamp(section.GetValue<int?>("PollIntervalSeconds") ?? 900, 30, 3600);
            var lookbackDays = Math.Clamp(section.GetValue<int?>("LookbackDays") ?? 45, 1, 365);
            var includeProjected = section.GetValue<bool?>("IncludeProjected") ?? true;

            if (enabled && !string.IsNullOrWhiteSpace(baseUrl))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ICscaTeacherCostSnapshotService>();
                    var fromUtc = DateTime.UtcNow.Date.AddDays(-lookbackDays);
                    var toUtc = DateTime.UtcNow.Date.AddDays(60);

                    var result = await service.SyncAsync(
                        new SyncCscaTeacherCostsRequest(
                            From: fromUtc,
                            To: toUtc,
                            IncludeProjected: includeProjected),
                        stoppingToken);

                    if (result.Succeeded && result.Value is { } sync)
                    {
                        _logger.LogInformation(
                            "CSCA teacher cost worker synced {Received} rows ({Inserted} inserted, {Updated} updated, {Unmapped} unmapped teachers).",
                            sync.Received,
                            sync.Inserted,
                            sync.Updated,
                            sync.UnmappedTeacherCount);
                    }
                    else if (!result.Succeeded)
                    {
                        _logger.LogWarning(
                            "CSCA teacher cost worker sync returned error: {Error}",
                            result.Errors.FirstOrDefault());
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CSCA teacher cost sync worker iteration failed.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
