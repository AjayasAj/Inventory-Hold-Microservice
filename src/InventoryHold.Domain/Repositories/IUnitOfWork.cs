namespace InventoryHold.Domain.Repositories;

/// <summary>
/// Runs <c>work</c> atomically: every repository call made inside it commits together or not at all.
/// Implementations may retry <c>work</c> on transient conflicts, so it must not have side effects
/// outside the repositories.
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}
