namespace InventoryHold.Domain.Entities;

public sealed record Product(string Id, string Name, int AvailableQuantity);
