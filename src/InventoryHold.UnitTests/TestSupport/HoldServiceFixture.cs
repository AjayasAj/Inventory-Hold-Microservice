using InventoryHold.Contracts;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;
using InventoryHold.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace InventoryHold.UnitTests.TestSupport;

/// <summary>Shared setup: repositories, outbox and cache are Moq mocks; the unit of work runs inline.</summary>
public abstract class HoldServiceFixture
{
    protected static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    protected Mock<IInventoryRepository> Inventory = null!;
    protected Mock<IHoldRepository> Holds = null!;
    protected Mock<IOutboxRepository> Outbox = null!;
    protected Mock<IInventoryCache> Cache = null!;
    protected InlineUnitOfWork UnitOfWork = null!;
    protected FakeClock Clock = null!;
    protected List<HoldEvent> OutboxEvents = null!;
    protected HoldService Service = null!;

    [SetUp]
    public void SetUpFixture()
    {
        Inventory = new Mock<IInventoryRepository>(MockBehavior.Strict);
        Holds = new Mock<IHoldRepository>(MockBehavior.Strict);
        Outbox = new Mock<IOutboxRepository>(MockBehavior.Strict);
        Cache = new Mock<IInventoryCache>();
        UnitOfWork = new InlineUnitOfWork();
        Clock = new FakeClock(Now);
        OutboxEvents = [];

        Outbox.Setup(x => x.AddAsync(It.IsAny<HoldEvent>(), It.IsAny<CancellationToken>()))
            .Callback<HoldEvent, CancellationToken>((evt, _) => OutboxEvents.Add(evt))
            .Returns(Task.CompletedTask);
        Holds.Setup(x => x.CreateAsync(It.IsAny<Hold>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Inventory.Setup(x => x.RestockAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Service = CreateService(new HoldOptions());
    }

    protected HoldService CreateService(
        HoldOptions options,
        IInventoryRepository? inventory = null,
        IHoldRepository? holds = null) =>
        new(
            inventory ?? Inventory.Object,
            holds ?? Holds.Object,
            Outbox.Object,
            UnitOfWork,
            Cache.Object,
            options,
            Clock,
            NullLogger<HoldService>.Instance);

    /// <summary>Configures a product whose reservation succeeds while enough stock remains.</summary>
    protected void GivenProduct(string id, string name, int stock)
    {
        var product = new Product(id, name, stock);
        Inventory.Setup(x => x.GetProductAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(product);
        Inventory.Setup(x => x.TryReserveAsync(id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, int quantity, CancellationToken _) =>
                quantity <= stock ? product with { AvailableQuantity = stock - quantity } : null);
    }

    protected void GivenStoredHold(Hold hold) =>
        Holds.Setup(x => x.GetAsync(hold.Id, It.IsAny<CancellationToken>())).ReturnsAsync(hold);

    protected void GivenTransitionSucceeds(string holdId, bool succeeds = true) =>
        Holds.Setup(x => x.TryTransitionAsync(
                It.Is<Hold>(h => h.Id == holdId), HoldStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(succeeds);

    protected static Hold ActiveHold(DateTime expiresAtUtc, string id = "hold-1") => new(
        id,
        HoldStatus.Active,
        Now.AddMinutes(-10),
        expiresAtUtc,
        null,
        [new HoldLine("SKU-1", "Mouse", 2), new HoldLine("SKU-2", "Keyboard", 1)]);

    protected void VerifyRestocked(string productId, int quantity) =>
        Inventory.Verify(x => x.RestockAsync(productId, quantity, It.IsAny<CancellationToken>()), Times.Once);

    protected void VerifyNoRestock() =>
        Inventory.Verify(x => x.RestockAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);

    protected void VerifyCacheInvalidated(Func<Times> times) =>
        Cache.Verify(x => x.InvalidateAsync(It.IsAny<CancellationToken>()), times);
}
