using System.Text.Json.Serialization;

namespace InventoryHold.Contracts;

/// <summary>Lifecycle state of a hold. Serialized as a string in API responses and events.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HoldStatus>))]
public enum HoldStatus
{
    Active,
    Released,
    Expired
}
