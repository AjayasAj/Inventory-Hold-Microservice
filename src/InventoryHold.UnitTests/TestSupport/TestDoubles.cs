using InventoryHold.Contracts;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;

namespace InventoryHold.UnitTests.TestSupport;

public sealed class FakeClock(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(UtcNow, DateTimeKind.Utc));
}

/// <summary>Runs the work inline. Counts invocations so tests can assert work happened inside a unit of work.</summary>
public sealed class InlineUnitOfWork : IUnitOfWork
{
    private int _executions;

    public int Executions => _executions;

    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        Interlocked.Increment(ref _executions);
        return work(ct);
    }
}

/// <summary>
/// Thread-safe in-memory inventory honouring the repository contract (atomic check-and-decrement),
/// used to exercise the service under real concurrency.
/// </summary>
public sealed class InMemoryInventoryRepository : IInventoryRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Product> _products = new(StringComparer.Ordinal);

    public InMemoryInventoryRepository(params Product[] products)
    {
        foreach (var product in products)
        {
            _products[product.Id] = product;
        }
    }

    public int StockOf(string id)
    {
        lock (_gate)
        {
            return _products[id].AvailableQuantity;
        }
    }

    public Task<IReadOnlyList<Product>> GetInventoryAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Product>>(_products.Values.ToArray());
        }
    }

    public Task<Product?> GetProductAsync(string id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_products.GetValueOrDefault(id));
        }
    }

    public async Task<Product?> TryReserveAsync(string id, int quantity, CancellationToken ct)
    {
        await Task.Yield(); // force interleaving between concurrent callers
        lock (_gate)
        {
            if (!_products.TryGetValue(id, out var product) || product.AvailableQuantity < quantity)
            {
                return null;
            }

            _products[id] = product with { AvailableQuantity = product.AvailableQuantity - quantity };
            return _products[id];
        }
    }

    public async Task RestockAsync(string id, int quantity, CancellationToken ct)
    {
        await Task.Yield();
        lock (_gate)
        {
            var product = _products[id];
            _products[id] = product with { AvailableQuantity = product.AvailableQuantity + quantity };
        }
    }
}

/// <summary>Thread-safe in-memory holds with compare-and-set transitions, mirroring the Mongo implementation.</summary>
public sealed class InMemoryHoldRepository : IHoldRepository
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Hold> _holds = new(StringComparer.Ordinal);

    public Task CreateAsync(Hold hold, CancellationToken ct)
    {
        lock (_gate)
        {
            _holds.Add(hold.Id, hold);
        }

        return Task.CompletedTask;
    }

    public Task<Hold?> GetAsync(string id, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_holds.GetValueOrDefault(id));
        }
    }

    public Task<IReadOnlyList<Hold>> ListAsync(HoldStatus? status, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Hold>>(
                _holds.Values.Where(h => status is null || h.Status == status).Take(limit).ToArray());
        }
    }

    public Task<IReadOnlyList<Hold>> FindDueForExpiryAsync(DateTime nowUtc, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<Hold>>(
                _holds.Values.Where(h => h.IsDueForExpiry(nowUtc)).Take(limit).ToArray());
        }
    }

    public async Task<bool> TryTransitionAsync(Hold updated, HoldStatus expectedStatus, CancellationToken ct)
    {
        await Task.Yield();
        lock (_gate)
        {
            if (!_holds.TryGetValue(updated.Id, out var current) || current.Status != expectedStatus)
            {
                return false;
            }

            _holds[updated.Id] = updated;
            return true;
        }
    }
}
