using InventoryHold.Contracts;

namespace InventoryHold.Domain.Exceptions;

/// <summary>
/// Base type for expected business-rule failures. The Web API maps each subtype to a specific
/// HTTP status; anything that is not a <see cref="DomainException"/> is treated as a 500.
/// </summary>
public abstract class DomainException(string code, string message) : Exception(message)
{
    /// <summary>Stable machine-readable error code returned to clients.</summary>
    public string Code { get; } = code;
}

public sealed class HoldValidationException(string message)
    : DomainException("validation_failed", message);

public sealed class ProductNotFoundException(string productId)
    : DomainException("product_not_found", $"Product '{productId}' does not exist.")
{
    public string ProductId { get; } = productId;
}

public sealed class InsufficientStockException(string productId, int requested, int available)
    : DomainException(
        "insufficient_stock",
        $"Insufficient stock for '{productId}': requested {requested}, available {available}.")
{
    public string ProductId { get; } = productId;
    public int Requested { get; } = requested;
    public int Available { get; } = available;
}

public sealed class HoldNotFoundException(string holdId)
    : DomainException("hold_not_found", $"Hold '{holdId}' was not found.")
{
    public string HoldId { get; } = holdId;
}

public sealed class HoldNotActiveException(string holdId, HoldStatus status)
    : DomainException("hold_not_active", $"Hold '{holdId}' is already {status.ToString().ToLowerInvariant()}.")
{
    public string HoldId { get; } = holdId;
    public HoldStatus Status { get; } = status;
}

public sealed class HoldExpiredException(string holdId)
    : DomainException("hold_expired", $"Hold '{holdId}' has expired; its inventory has been returned to stock.")
{
    public string HoldId { get; } = holdId;
}
