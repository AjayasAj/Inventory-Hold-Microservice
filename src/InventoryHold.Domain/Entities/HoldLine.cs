namespace InventoryHold.Domain.Entities;

/// <summary>A product quantity reserved by a hold. Product name is snapshotted at reservation time.</summary>
public sealed record HoldLine(string ProductId, string ProductName, int Quantity);
