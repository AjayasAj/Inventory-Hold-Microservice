using System.Text.Json;

namespace InventoryHold.Infrastructure;

/// <summary>Wire format for integration events: camelCase JSON, enums as strings (via the contract attributes).</summary>
internal static class EventSerialization
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}
