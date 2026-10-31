namespace InventoryHold.Contracts;

public sealed record HoldItemRequest(string ProductId, int Quantity);
