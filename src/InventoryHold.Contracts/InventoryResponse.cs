namespace InventoryHold.Contracts;

public sealed record InventoryResponse(string ProductId, string Name, int AvailableQuantity);
