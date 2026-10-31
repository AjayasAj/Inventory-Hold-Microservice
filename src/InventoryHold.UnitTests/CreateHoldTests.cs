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
public sealed class CreateHoldTests : HoldServiceFixture
{
    private static IEnumerable<TestCaseData> InvalidRequests()
    {
        yield return new TestCaseData(new CreateHoldRequest(null!)).SetName("Items null");
        yield return new TestCaseData(new CreateHoldRequest([])).SetName("Items empty");
        yield return new TestCaseData(new CreateHoldRequest([new HoldItemRequest("SKU-1", 0)])).SetName("Zero quantity");
        yield return new TestCaseData(new CreateHoldRequest([new HoldItemRequest("SKU-1", -3)])).SetName("Negative quantity");
        yield return new TestCaseData(new CreateHoldRequest([new HoldItemRequest("  ", 1)])).SetName("Blank productId");
        yield return new TestCaseData(new CreateHoldRequest([new HoldItemRequest("SKU-1", 1), new HoldItemRequest(" SKU-1 ", 2)]))
            .SetName("Duplicate productId after trimming");
        yield return new TestCaseData(new CreateHoldRequest(
                Enumerable.Range(0, HoldService.MaxItemsPerHold + 1).Select(i => new HoldItemRequest($"SKU-{i}", 1)).ToArray()))
            .SetName("Too many items");
    }

    [TestCaseSource(nameof(InvalidRequests))]
    public void Create_InvalidRequest_ThrowsValidationWithoutTouchingStorage(CreateHoldRequest request)
    {
        Assert.ThrowsAsync<HoldValidationException>(() => Service.CreateAsync(request, CancellationToken.None));

        Assert.That(UnitOfWork.Executions, Is.Zero, "validation must happen before any unit of work starts");
        Inventory.VerifyNoOtherCalls();
        Holds.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Create_ValidRequest_ReservesPersistsAndQueuesHoldCreatedAtomically()
    {
        GivenProduct("SKU-1", "Mouse", 10);
        GivenProduct("SKU-2", "Keyboard", 5);

        var result = await Service.CreateAsync(
            new CreateHoldRequest([new HoldItemRequest("SKU-1", 3), new HoldItemRequest("SKU-2", 1)]),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HoldStatus.Active));
            Assert.That(result.CreatedAtUtc, Is.EqualTo(Now));
            Assert.That(result.ExpiresAtUtc, Is.EqualTo(Now.AddMinutes(15)), "default duration is 15 minutes");
            Assert.That(result.Items, Is.EqualTo(new[] { new HoldItem("SKU-1", "Mouse", 3), new HoldItem("SKU-2", "Keyboard", 1) }));
            Assert.That(UnitOfWork.Executions, Is.EqualTo(1));
        });

        Holds.Verify(x => x.CreateAsync(It.Is<Hold>(h => h.Id == result.Id), It.IsAny<CancellationToken>()), Times.Once);
        var evt = OutboxEvents.Single();
        Assert.Multiple(() =>
        {
            Assert.That(evt.EventType, Is.EqualTo(HoldEventTypes.Created));
            Assert.That(evt.HoldId, Is.EqualTo(result.Id));
            Assert.That(evt.Items, Has.Count.EqualTo(2));
            Assert.That(evt.SchemaVersion, Is.EqualTo(HoldEventTypes.CurrentSchemaVersion));
        });
        VerifyCacheInvalidated(Times.Once);
    }

    [Test]
    public async Task Create_UsesConfiguredHoldDuration()
    {
        GivenProduct("SKU-1", "Mouse", 10);
        var service = CreateService(new HoldOptions { DurationMinutes = 2 });

        var result = await service.CreateAsync(new CreateHoldRequest([new HoldItemRequest("SKU-1", 1)]), CancellationToken.None);

        Assert.That(result.ExpiresAtUtc - result.CreatedAtUtc, Is.EqualTo(TimeSpan.FromMinutes(2)));
    }

    [Test]
    public void Create_UnknownProduct_ThrowsProductNotFound()
    {
        Inventory.Setup(x => x.TryReserveAsync("NOPE", 1, It.IsAny<CancellationToken>())).ReturnsAsync((Product?)null);
        Inventory.Setup(x => x.GetProductAsync("NOPE", It.IsAny<CancellationToken>())).ReturnsAsync((Product?)null);

        var ex = Assert.ThrowsAsync<ProductNotFoundException>(() =>
            Service.CreateAsync(new CreateHoldRequest([new HoldItemRequest("NOPE", 1)]), CancellationToken.None));

        Assert.That(ex!.ProductId, Is.EqualTo("NOPE"));
    }

    [Test]
    public void Create_InsufficientStock_ThrowsWithRequestedAndAvailable()
    {
        GivenProduct("SKU-1", "Mouse", 4);

        var ex = Assert.ThrowsAsync<InsufficientStockException>(() =>
            Service.CreateAsync(new CreateHoldRequest([new HoldItemRequest("SKU-1", 5)]), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Requested, Is.EqualTo(5));
            Assert.That(ex.Available, Is.EqualTo(4));
        });
        Holds.Verify(x => x.CreateAsync(It.IsAny<Hold>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void Create_WhenLaterItemFails_AbortsUnitOfWorkWithNothingPersistedOrPublished()
    {
        GivenProduct("SKU-1", "Mouse", 10);
        GivenProduct("SKU-2", "Keyboard", 0);

        Assert.ThrowsAsync<InsufficientStockException>(() => Service.CreateAsync(
            new CreateHoldRequest([new HoldItemRequest("SKU-1", 2), new HoldItemRequest("SKU-2", 1)]),
            CancellationToken.None));

        // The SKU-1 reservation is undone by the transaction rollback, not by compensating writes.
        Assert.That(UnitOfWork.Executions, Is.EqualTo(1));
        VerifyNoRestock();
        Holds.Verify(x => x.CreateAsync(It.IsAny<Hold>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.That(OutboxEvents, Is.Empty);
        VerifyCacheInvalidated(Times.Never);
    }

    [Test]
    public async Task Create_ReservesItemsInProductIdOrder()
    {
        var order = new List<string>();
        foreach (var id in new[] { "SKU-3", "SKU-1", "SKU-2" })
        {
            GivenProduct(id, id, 10);
            Inventory.Setup(x => x.TryReserveAsync(id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Callback(() => order.Add(id))
                .ReturnsAsync(new Product(id, id, 9));
        }

        await Service.CreateAsync(
            new CreateHoldRequest([new("SKU-3", 1), new("SKU-1", 1), new("SKU-2", 1)]),
            CancellationToken.None);

        Assert.That(order, Is.EqualTo(new[] { "SKU-1", "SKU-2", "SKU-3" }));
    }

    [Test]
    public async Task Create_ConcurrentRequestsForLastUnits_NeverOversell()
    {
        const int stock = 10;
        const int buyers = 50;
        var inventory = new InMemoryInventoryRepository(new Product("SKU-1", "Mouse", stock));
        var service = CreateService(new HoldOptions(), inventory, new InMemoryHoldRepository());

        var attempts = await Task.WhenAll(Enumerable.Range(0, buyers).Select(_ => Task.Run(async () =>
        {
            try
            {
                await service.CreateAsync(new CreateHoldRequest([new HoldItemRequest("SKU-1", 1)]), CancellationToken.None);
                return true;
            }
            catch (InsufficientStockException)
            {
                return false;
            }
        })));

        Assert.Multiple(() =>
        {
            Assert.That(attempts.Count(placed => placed), Is.EqualTo(stock));
            Assert.That(attempts.Count(placed => !placed), Is.EqualTo(buyers - stock));
            Assert.That(inventory.StockOf("SKU-1"), Is.Zero);
        });
    }
}
