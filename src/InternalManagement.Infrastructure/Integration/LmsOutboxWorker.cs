using InternalManagement.Application.Features.Integration.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InternalManagement.Infrastructure.Integration;

/// <summary>
/// Optional worker for committed LMS commands. It is disabled by default so a
/// deployment cannot call an external LMS until its credentials and endpoint
/// have been explicitly configured.
/// </summary>
public sealed class LmsOutboxWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LmsOutboxWorker> _logger;

    public LmsOutboxWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<LmsOutboxWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var section = _configuration.GetSection("Integrations:CscaCourseLms:OutboxWorker");
            var enabled = section.GetValue<bool>("Enabled");
            var interval = TimeSpan.FromSeconds(Math.Clamp(section.GetValue<int?>("PollIntervalSeconds") ?? 10, 2, 60));

            if (enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<ILmsOutboxDispatcher>();
                    var batchSize = Math.Clamp(section.GetValue<int?>("BatchSize") ?? 25, 1, 100);
                    var result = await dispatcher.DispatchPendingAsync(batchSize, stoppingToken);
                    if (result.Processed > 0)
                    {
                        _logger.LogInformation(
                            "LMS outbox worker processed {Processed}: {Succeeded} succeeded, {Retrying} retrying, {DeadLettered} dead-lettered.",
                            result.Processed,
                            result.Succeeded,
                            result.Retrying,
                            result.DeadLettered);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LMS outbox worker iteration failed.");
                }
            }

            await Task.Delay(interval, stoppingToken);
        }
    }
}
