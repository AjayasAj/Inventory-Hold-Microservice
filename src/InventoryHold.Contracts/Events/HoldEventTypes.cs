namespace InventoryHold.Contracts.Events;

public static class HoldEventTypes
{
    public const string Created = "HoldCreated";
    public const string Released = "HoldReleased";
    public const string Expired = "HoldExpired";

    public const int CurrentSchemaVersion = 1;

    /// <summary>Topic routing key, e.g. <c>hold.created</c>, so consumers can bind selectively.</summary>
    public static string RoutingKeyFor(string eventType) => eventType switch
    {
        Created => "hold.created",
        Released => "hold.released",
        Expired => "hold.expired",
        _ => throw new ArgumentOutOfRangeException(nameof(eventType), eventType, "Unknown hold event type.")
    };
}
