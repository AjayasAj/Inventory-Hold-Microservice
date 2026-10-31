using InventoryHold.Contracts;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Services;

internal static class HoldMappings
{
    public static HoldResponse ToResponse(this Hold hold) =>
        new(hold.Id, hold.Status, hold.CreatedAtUtc, hold.ExpiresAtUtc, hold.ClosedAtUtc, hold.ToItems());

    public static HoldEvent ToEvent(this Hold hold, string eventType, DateTime occurredAtUtc) =>
        new(
            Guid.NewGuid().ToString("N"),
            eventType,
            HoldEventTypes.CurrentSchemaVersion,
            occurredAtUtc,
            hold.Id,
            hold.Status,
            hold.CreatedAtUtc,
            hold.ExpiresAtUtc,
            hold.ToItems());

    private static IReadOnlyList<HoldItem> ToItems(this Hold hold) =>
        hold.Lines.Select(line => new HoldItem(line.ProductId, line.ProductName, line.Quantity)).ToArray();
}
