namespace InventoryHold.Infrastructure.Workers;

public sealed class WorkerOptions
{
    public const string SectionName = "Workers";

    /// <summary>How often the expiry sweep runs. Bounds how long an expired hold can keep stock reserved.</summary>
    public int ExpirationSweepSeconds { get; set; } = 10;

    public int ExpirationBatchSize { get; set; } = 100;

    public int OutboxPollMilliseconds { get; set; } = 1000;

    public int OutboxBatchSize { get; set; } = 100;
}
