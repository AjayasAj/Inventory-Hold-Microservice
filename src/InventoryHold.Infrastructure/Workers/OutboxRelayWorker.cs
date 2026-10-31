using InventoryHold.Domain.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventoryHold.Infrastructure.Workers;

public sealed class OutboxRelayWorker(
    EventRelayService relay,
    WorkerOptions options,
    ILogger<OutboxRelayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(options.OutboxPollMilliseconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await relay.RelayPendingAsync(options.OutboxBatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Outbox relay failed; retrying in {Interval}", interval);
            }

            // Drain a backlog without waiting; otherwise poll.
            if (published < options.OutboxBatchSize)
            {
                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
