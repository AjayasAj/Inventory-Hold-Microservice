namespace InventoryHold.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Uri { get; set; } = string.Empty;

    /// <summary>Durable topic exchange; routing keys are <c>hold.created</c>, <c>hold.released</c>, <c>hold.expired</c>.</summary>
    public string Exchange { get; set; } = "inventory.holds";

    /// <summary>Durable queue bound to <c>hold.#</c> so events are retained even before any consumer exists.</summary>
    public string Queue { get; set; } = "inventory.holds.events";
}
