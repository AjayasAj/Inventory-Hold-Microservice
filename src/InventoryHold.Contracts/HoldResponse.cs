namespace InventoryHold.Contracts;

public sealed record HoldResponse(
    string Id,
    HoldStatus Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ClosedAtUtc,
    IReadOnlyList<HoldItem> Items);
