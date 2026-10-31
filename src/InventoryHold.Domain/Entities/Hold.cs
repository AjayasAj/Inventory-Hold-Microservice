using InventoryHold.Contracts;
using InventoryHold.Domain.Exceptions;

namespace InventoryHold.Domain.Entities;

/// <summary>
/// Hold aggregate. Owns the lifecycle rules (Active -> Released | Expired); persistence applies
/// the resulting transitions with a conditional update so concurrent callers cannot both win.
/// </summary>
public sealed record Hold(
    string Id,
    HoldStatus Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    DateTime? ClosedAtUtc,
    IReadOnlyList<HoldLine> Lines)
{
    public static Hold Place(IReadOnlyList<HoldLine> lines, DateTime nowUtc, TimeSpan duration)
    {
        if (lines.Count == 0)
        {
            throw new HoldValidationException("A hold must contain at least one item.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Hold duration must be positive.");
        }

        return new Hold(Guid.NewGuid().ToString("N"), HoldStatus.Active, nowUtc, nowUtc + duration, null, lines);
    }

    public bool IsActive => Status == HoldStatus.Active;

    /// <summary>True when the hold is still marked active but its expiry time has passed.</summary>
    public bool IsDueForExpiry(DateTime nowUtc) => IsActive && ExpiresAtUtc <= nowUtc;

    public void EnsureReleasable(DateTime nowUtc)
    {
        if (!IsActive)
        {
            throw new HoldNotActiveException(Id, Status);
        }

        if (IsDueForExpiry(nowUtc))
        {
            throw new HoldExpiredException(Id);
        }
    }

    public Hold Release(DateTime nowUtc)
    {
        EnsureReleasable(nowUtc);
        return this with { Status = HoldStatus.Released, ClosedAtUtc = nowUtc };
    }

    public Hold Expire(DateTime nowUtc)
    {
        if (!IsDueForExpiry(nowUtc))
        {
            throw new InvalidOperationException($"Hold '{Id}' is not due for expiry.");
        }

        return this with { Status = HoldStatus.Expired, ClosedAtUtc = nowUtc };
    }
}
