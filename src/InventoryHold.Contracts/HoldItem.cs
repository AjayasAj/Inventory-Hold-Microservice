namespace InventoryHold.Contracts;

public sealed record HoldItem(string ProductId, string Name, int Quantity);
