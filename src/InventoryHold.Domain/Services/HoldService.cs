using InventoryHold.Contracts;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace InventoryHold.Domain.Services;

/// <summary>
/// Orchestrates the hold lifecycle. Every state change (stock movement, hold status, outbox event)
/// runs inside one <see cref="IUnitOfWork"/>, so a failure part-way through leaves nothing behind
/// and no hand-written compensation is needed.
/// </summary>
public sealed class HoldService(
    IInventoryRepository inventory,
    IHoldRepository holds,
    IOutboxRepository outbox,
    IUnitOfWork unitOfWork,
    IInventoryCache cache,
    HoldOptions options,
    TimeProvider clock,
    ILogger<HoldService> logger) : IHoldService
{
    public const int MaxItemsPerHold = 50;
    public const int MaxListLimit = 200;

    private DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    public async Task<HoldResponse> CreateAsync(CreateHoldRequest request, CancellationToken ct)
    {
        var items = ValidateAndNormalize(request);
        var now = UtcNow;

        var hold = await unitOfWork.ExecuteAsync(async token =>
        {
            var lines = new List<HoldLine>(items.Count);
            foreach (var item in items)
            {
                var product = await inventory.TryReserveAsync(item.ProductId, item.Quantity, token)
                    ?? throw await DescribeReservationFailureAsync(item, token);
                lines.Add(new HoldLine(product.Id, product.Name, item.Quantity));
            }

            var placed = Hold.Place(lines, now, options.Duration);
            await holds.CreateAsync(placed, token);
            await outbox.AddAsync(placed.ToEvent(HoldEventTypes.Created, now), token);
            return placed;
        }, ct);

        await cache.InvalidateAsync(ct);
        logger.LogInformation(
            "Hold {HoldId} placed for {LineCount} item(s), expires {ExpiresAtUtc:O}",
            hold.Id, hold.Lines.Count, hold.ExpiresAtUtc);
        return hold.ToResponse();
    }

    public async Task<HoldResponse> GetAsync(string id, CancellationToken ct)
    {
        var hold = await holds.GetAsync(id, ct) ?? throw new HoldNotFoundException(id);
        var (current, _) = await ExpireIfDueAsync(hold, ct);
        return current.ToResponse();
    }

    public async Task<IReadOnlyList<HoldResponse>> ListAsync(HoldStatus? status, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > MaxListLimit)
        {
            throw new HoldValidationException($"limit must be between 1 and {MaxListLimit}.");
        }

        var results = new List<HoldResponse>();
        foreach (var hold in await holds.ListAsync(status, limit, ct))
        {
            var (current, _) = await ExpireIfDueAsync(hold, ct);
            if (status is null || current.Status == status)
            {
                results.Add(current.ToResponse());
            }
        }

        return results;
    }

    public async Task<HoldResponse> ReleaseAsync(string id, CancellationToken ct)
    {
        var now = UtcNow;

        var (hold, expired) = await unitOfWork.ExecuteAsync(async token =>
        {
            var stored = await holds.GetAsync(id, token) ?? throw new HoldNotFoundException(id);

            // An overdue hold is expired (and its stock returned) rather than released, and the
            // caller is told so. This must commit before we throw, so it is not an exception here.
            if (stored.IsDueForExpiry(now))
            {
                return (await CloseAsync(stored, stored.Expire(now), HoldEventTypes.Expired, now, token), true);
            }

            return (await CloseAsync(stored, stored.Release(now), HoldEventTypes.Released, now, token), false);
        }, ct);

        await cache.InvalidateAsync(ct);

        if (expired)
        {
            logger.LogInformation("Hold {HoldId} expired before it could be released", id);
            throw new HoldExpiredException(id);
        }

        logger.LogInformation("Hold {HoldId} released", id);
        return hold.ToResponse();
    }

    public async Task<int> ExpireDueHoldsAsync(int batchSize, CancellationToken ct)
    {
        var expiredCount = 0;
        foreach (var hold in await holds.FindDueForExpiryAsync(UtcNow, batchSize, ct))
        {
            try
            {
                var (_, expiredByUs) = await ExpireIfDueAsync(hold, ct);
                if (expiredByUs)
                {
                    expiredCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad hold must not stop the sweep; it will be retried on the next pass.
                logger.LogError(ex, "Failed to expire hold {HoldId}", hold.Id);
            }
        }

        return expiredCount;
    }

    /// <summary>
    /// Expires the hold if it is overdue. Returns the current state and whether this call performed
    /// the expiry (false if it was not due, or a concurrent caller transitioned it first).
    /// </summary>
    private async Task<(Hold Current, bool ExpiredByUs)> ExpireIfDueAsync(Hold hold, CancellationToken ct)
    {
        var now = UtcNow;
        if (!hold.IsDueForExpiry(now))
        {
            return (hold, false);
        }

        var result = await unitOfWork.ExecuteAsync(async token =>
        {
            var expired = hold.Expire(now);
            if (await TryCloseAsync(hold, expired, HoldEventTypes.Expired, now, token))
            {
                return (expired, true);
            }

            // Lost the race (released or expired elsewhere): report whatever state won.
            return (await holds.GetAsync(hold.Id, token) ?? hold, false);
        }, ct);

        if (result.Item2)
        {
            await cache.InvalidateAsync(ct);
            logger.LogInformation("Hold {HoldId} expired; stock returned", hold.Id);
        }

        return result;
    }

    private async Task<Hold> CloseAsync(Hold stored, Hold closed, string eventType, DateTime now, CancellationToken ct)
    {
        if (await TryCloseAsync(stored, closed, eventType, now, ct))
        {
            return closed;
        }

        var current = await holds.GetAsync(stored.Id, ct);
        throw new HoldNotActiveException(stored.Id, current?.Status ?? stored.Status);
    }

    /// <summary>
    /// Conditional Active -> closed transition. Stock is only returned by the caller that wins the
    /// transition, so concurrent release/expiry can never restock twice.
    /// </summary>
    private async Task<bool> TryCloseAsync(Hold stored, Hold closed, string eventType, DateTime now, CancellationToken ct)
    {
        if (!await holds.TryTransitionAsync(closed, stored.Status, ct))
        {
            return false;
        }

        foreach (var line in closed.Lines)
        {
            await inventory.RestockAsync(line.ProductId, line.Quantity, ct);
        }

        await outbox.AddAsync(closed.ToEvent(eventType, now), ct);
        return true;
    }

    private async Task<DomainException> DescribeReservationFailureAsync(HoldItemRequest item, CancellationToken ct)
    {
        var product = await inventory.GetProductAsync(item.ProductId, ct);
        return product is null
            ? new ProductNotFoundException(item.ProductId)
            : new InsufficientStockException(item.ProductId, item.Quantity, product.AvailableQuantity);
    }

    /// <summary>
    /// Validates the request and returns its items trimmed and ordered by product id, so concurrent
    /// multi-item holds touch products in a consistent order.
    /// </summary>
    private static IReadOnlyList<HoldItemRequest> ValidateAndNormalize(CreateHoldRequest? request)
    {
        if (request?.Items is null || request.Items.Count == 0)
        {
            throw new HoldValidationException("At least one item is required.");
        }

        if (request.Items.Count > MaxItemsPerHold)
        {
            throw new HoldValidationException($"A hold may contain at most {MaxItemsPerHold} items.");
        }

        var normalized = new List<HoldItemRequest>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ProductId))
            {
                throw new HoldValidationException("Each item must have a productId.");
            }

            if (item.Quantity <= 0)
            {
                throw new HoldValidationException($"Quantity for '{item.ProductId}' must be greater than zero.");
            }

            normalized.Add(item with { ProductId = item.ProductId.Trim() });
        }

        var duplicate = normalized
            .GroupBy(item => item.ProductId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new HoldValidationException(
                $"Product '{duplicate.Key}' appears more than once; combine the quantities into one item.");
        }

        return normalized.OrderBy(item => item.ProductId, StringComparer.Ordinal).ToArray();
    }
}
