using InventoryHold.Contracts;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using InventoryHold.Domain.Services;
using InventoryHold.UnitTests.TestSupport;
using Moq;
using NUnit.Framework;

namespace InventoryHold.UnitTests;

[TestFixture]
public sealed class HoldLifecycleTests : HoldServiceFixture
{
    // ---- GET ----

    [Test]
    public void Get_MissingHold_ThrowsHoldNotFound()
    {
        Holds.Setup(x => x.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Hold?)null);

        Assert.ThrowsAsync<HoldNotFoundException>(() => Service.GetAsync("missing", CancellationToken.None));
    }

    [Test]
    public async Task Get_ActiveHold_IsReturnedUnchanged()
    {
        var hold = ActiveHold(Now.AddMinutes(5));
        GivenStoredHold(hold);

        var result = await Service.GetAsync(hold.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(HoldStatus.Active));
        Holds.Verify(x => x.TryTransitionAsync(It.IsAny<Hold>(), It.IsAny<HoldStatus>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Get_OverdueHold_IsExpiredRestockedAndAnnounced()
    {
        var hold = ActiveHold(Now.AddSeconds(-1));
        GivenStoredHold(hold);
        GivenTransitionSucceeds(hold.Id);

        var result = await Service.GetAsync(hold.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HoldStatus.Expired));
            Assert.That(result.ClosedAtUtc, Is.EqualTo(Now));
            Assert.That(OutboxEvents.Single().EventType, Is.EqualTo(HoldEventTypes.Expired));
        });
        VerifyRestocked("SKU-1", 2);
        VerifyRestocked("SKU-2", 1);
        VerifyCacheInvalidated(Times.Once);
    }

    [Test]
    public async Task Get_ExpiryRaceLost_ReturnsWinningStateWithoutRestockingTwice()
    {
        var hold = ActiveHold(Now.AddMinutes(-1));
        Holds.SetupSequence(x => x.GetAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold)
            .ReturnsAsync(hold with { Status = HoldStatus.Released, ClosedAtUtc = Now });
        GivenTransitionSucceeds(hold.Id, succeeds: false);

        var result = await Service.GetAsync(hold.Id, CancellationToken.None);

        Assert.That(result.Status, Is.EqualTo(HoldStatus.Released));
        VerifyNoRestock();
        Assert.That(OutboxEvents, Is.Empty);
    }

    // ---- LIST ----

    [TestCase(0)]
    [TestCase(HoldService.MaxListLimit + 1)]
    public void List_LimitOutOfRange_ThrowsValidation(int limit)
    {
        Assert.ThrowsAsync<HoldValidationException>(() => Service.ListAsync(null, limit, CancellationToken.None));
    }

    [Test]
    public async Task List_ActiveFilter_ExcludesHoldsThatExpireWhileListing()
    {
        var live = ActiveHold(Now.AddMinutes(5), "live");
        var overdue = ActiveHold(Now.AddMinutes(-1), "overdue");
        Holds.Setup(x => x.ListAsync(HoldStatus.Active, 50, It.IsAny<CancellationToken>())).ReturnsAsync([live, overdue]);
        GivenTransitionSucceeds("overdue");

        var result = await Service.ListAsync(HoldStatus.Active, 50, CancellationToken.None);

        Assert.That(result.Select(h => h.Id), Is.EqualTo(new[] { "live" }));
        Assert.That(OutboxEvents.Single().HoldId, Is.EqualTo("overdue"));
    }

    // ---- RELEASE ----

    [Test]
    public void Release_MissingHold_ThrowsHoldNotFound()
    {
        Holds.Setup(x => x.GetAsync("missing", It.IsAny<CancellationToken>())).ReturnsAsync((Hold?)null);

        Assert.ThrowsAsync<HoldNotFoundException>(() => Service.ReleaseAsync("missing", CancellationToken.None));
        VerifyNoRestock();
    }

    [Test]
    public async Task Release_ActiveHold_RestocksEveryLineAndQueuesHoldReleased()
    {
        var hold = ActiveHold(Now.AddMinutes(5));
        GivenStoredHold(hold);
        GivenTransitionSucceeds(hold.Id);

        var result = await Service.ReleaseAsync(hold.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HoldStatus.Released));
            Assert.That(result.ClosedAtUtc, Is.EqualTo(Now));
            Assert.That(OutboxEvents.Single().EventType, Is.EqualTo(HoldEventTypes.Released));
            Assert.That(OutboxEvents.Single().Status, Is.EqualTo(HoldStatus.Released));
        });
        Holds.Verify(x => x.TryTransitionAsync(
            It.Is<Hold>(h => h.Status == HoldStatus.Released), HoldStatus.Active, It.IsAny<CancellationToken>()), Times.Once);
        VerifyRestocked("SKU-1", 2);
        VerifyRestocked("SKU-2", 1);
        VerifyCacheInvalidated(Times.Once);
    }

    [TestCase(HoldStatus.Released)]
    [TestCase(HoldStatus.Expired)]
    public void Release_HoldAlreadyClosed_ThrowsNotActiveAndRestocksNothing(HoldStatus status)
    {
        var hold = ActiveHold(Now.AddMinutes(5)) with { Status = status, ClosedAtUtc = Now.AddMinutes(-1) };
        GivenStoredHold(hold);

        var ex = Assert.ThrowsAsync<HoldNotActiveException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        Assert.That(ex!.Status, Is.EqualTo(status));
        VerifyNoRestock();
        Assert.That(OutboxEvents, Is.Empty);
    }

    [Test]
    public void Release_OverdueHold_ExpiresItInsteadAndReportsGone()
    {
        var hold = ActiveHold(Now.AddMinutes(-1));
        GivenStoredHold(hold);
        GivenTransitionSucceeds(hold.Id);

        Assert.ThrowsAsync<HoldExpiredException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        // The expiry is committed (stock back, HoldExpired queued) before the 410 is reported.
        Holds.Verify(x => x.TryTransitionAsync(
            It.Is<Hold>(h => h.Status == HoldStatus.Expired), HoldStatus.Active, It.IsAny<CancellationToken>()), Times.Once);
        VerifyRestocked("SKU-1", 2);
        Assert.That(OutboxEvents.Single().EventType, Is.EqualTo(HoldEventTypes.Expired));
    }

    [Test]
    public void Release_LosesRaceToConcurrentRelease_ThrowsNotActiveWithoutRestocking()
    {
        var hold = ActiveHold(Now.AddMinutes(5));
        Holds.SetupSequence(x => x.GetAsync(hold.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(hold)
            .ReturnsAsync(hold with { Status = HoldStatus.Released });
        GivenTransitionSucceeds(hold.Id, succeeds: false);

        var ex = Assert.ThrowsAsync<HoldNotActiveException>(() => Service.ReleaseAsync(hold.Id, CancellationToken.None));

        Assert.That(ex!.Status, Is.EqualTo(HoldStatus.Released));
        VerifyNoRestock();
        Assert.That(OutboxEvents, Is.Empty);
    }

    [Test]
    public async Task Release_ConcurrentCallers_ExactlyOneWinsAndStockIsRestoredOnce()
    {
        var inventory = new InMemoryInventoryRepository(new Product("SKU-1", "Mouse", 10));
        var holds = new InMemoryHoldRepository();
        var service = CreateService(new HoldOptions(), inventory, holds);
        var hold = await service.CreateAsync(new CreateHoldRequest([new HoldItemRequest("SKU-1", 4)]), CancellationToken.None);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(async () =>
        {
            try
            {
                await service.ReleaseAsync(hold.Id, CancellationToken.None);
                return true;
            }
            catch (HoldNotActiveException)
            {
                return false;
            }
        })));

        Assert.Multiple(() =>
        {
            Assert.That(outcomes.Count(released => released), Is.EqualTo(1));
            Assert.That(inventory.StockOf("SKU-1"), Is.EqualTo(10), "stock must be restored exactly once");
            Assert.That(OutboxEvents.Count(e => e.EventType == HoldEventTypes.Released), Is.EqualTo(1));
        });
    }

    // ---- EXPIRY SWEEP ----

    [Test]
    public async Task ExpireDueHolds_ExpiresEachOverdueHoldAndCountsOnlyItsOwnWins()
    {
        var first = ActiveHold(Now.AddMinutes(-2), "first");
        var second = ActiveHold(Now.AddMinutes(-1), "second");
        Holds.Setup(x => x.FindDueForExpiryAsync(Now, 100, It.IsAny<CancellationToken>())).ReturnsAsync([first, second]);
        GivenTransitionSucceeds("first");
        GivenTransitionSucceeds("second", succeeds: false); // a reader expired it first
        GivenStoredHold(second with { Status = HoldStatus.Expired });

        var expired = await Service.ExpireDueHoldsAsync(100, CancellationToken.None);

        Assert.That(expired, Is.EqualTo(1));
        Assert.That(OutboxEvents.Select(e => e.HoldId), Is.EqualTo(new[] { "first" }));
    }

    [Test]
    public async Task ExpireDueHolds_OneFailureDoesNotStopTheSweep()
    {
        var broken = ActiveHold(Now.AddMinutes(-2), "broken");
        var healthy = ActiveHold(Now.AddMinutes(-1), "healthy");
        Holds.Setup(x => x.FindDueForExpiryAsync(Now, 100, It.IsAny<CancellationToken>())).ReturnsAsync([broken, healthy]);
        Holds.Setup(x => x.TryTransitionAsync(It.Is<Hold>(h => h.Id == "broken"), HoldStatus.Active, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db timeout"));
        GivenTransitionSucceeds("healthy");

        var expired = await Service.ExpireDueHoldsAsync(100, CancellationToken.None);

        Assert.That(expired, Is.EqualTo(1));
        Assert.That(OutboxEvents.Single().HoldId, Is.EqualTo("healthy"));
    }
}
