using InventoryHold.Domain.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventoryHold.Infrastructure.Workers;

/// <summary>
/// Proactively expires overdue holds so their stock returns even if nobody reads them again.
/// Reads also expire lazily; both paths use the same conditional transition, so running several
/// instances of this worker is safe (each hold is expired, restocked and announced exactly once).
/// </summary>
public sealed class HoldExpirationWorker(
    IHoldService holds,
    WorkerOptions options,
    ILogger<HoldExpirationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.ExpirationSweepSeconds));
        do
        {
            try
            {
                int expired;
                do
                {
                    expired = await holds.ExpireDueHoldsAsync(options.ExpirationBatchSize, stoppingToken);
                    if (expired > 0)
                    {
                        logger.LogInformation("Expiry sweep expired {Count} hold(s)", expired);
                    }
                }
                while (expired == options.ExpirationBatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Expiry sweep failed; retrying next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
