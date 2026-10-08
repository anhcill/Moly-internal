using InternalManagement.Application.Features.Integration.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Opt-in recovery for attendance webhooks acknowledged before immediate LMS
/// projection existed. New webhooks are projected by WebhookProcessor.IngestAsync.
/// </summary>
public sealed class CscaAttendanceInboxReplayWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CscaAttendanceInboxReplayWorker> _logger;

    public CscaAttendanceInboxReplayWorker(IServiceScopeFactory scopeFactory,
        IConfiguration configuration, ILogger<CscaAttendanceInboxReplayWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var section = _configuration.GetSection("Integrations:CscaCourseLms:AttendanceInboxReplay");
            if (section.GetValue<bool>("Enabled"))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<IWebhookProcessor>();
                    var batchSize = Math.Clamp(section.GetValue<int?>("BatchSize") ?? 10, 1, 100);
                    var projected = await processor.ReplayPendingCscaAttendanceAsync(batchSize, stoppingToken);
                    if (projected > 0)
                        _logger.LogInformation("Replayed {Count} pending CSCA LMS attendance inbox events.", projected);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CSCA LMS attendance inbox replay iteration failed.");
                }
            }

            var interval = Math.Clamp(section.GetValue<int?>("PollIntervalSeconds") ?? 30, 5, 300);
            try { await Task.Delay(TimeSpan.FromSeconds(interval), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
