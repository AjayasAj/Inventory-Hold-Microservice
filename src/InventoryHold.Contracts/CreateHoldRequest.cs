namespace InventoryHold.Contracts;

public sealed record CreateHoldRequest(IReadOnlyList<HoldItemRequest> Items);
