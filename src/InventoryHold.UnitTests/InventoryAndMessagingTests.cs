using InventoryHold.Contracts;
using InventoryHold.Contracts.Events;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Messaging;
using InventoryHold.Domain.Repositories;
using InventoryHold.Domain.Services;
using InventoryHold.UnitTests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace InventoryHold.UnitTests;

[TestFixture]
public sealed class InventoryServiceTests
{
    private Mock<IInventoryRepository> _repository = null!;
    private Mock<IInventoryCache> _cache = null!;
    private InventoryService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = new Mock<IInventoryRepository>();
        _cache = new Mock<IInventoryCache>();
        _service = new InventoryService(_repository.Object, _cache.Object);
    }

    [Test]
    public async Task GetInventory_CacheHit_DoesNotQueryRepository()
    {
        IReadOnlyList<InventoryResponse> cached = [new InventoryResponse("SKU-1", "Mouse", 8)];
        _cache.Setup(x => x.GetOrCreateAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<InventoryResponse>>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        var result = await _service.GetInventoryAsync(CancellationToken.None);

        Assert.That(result, Is.SameAs(cached));
        _repository.Verify(x => x.GetInventoryAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task GetInventory_CacheMiss_LoadsFromRepositorySortedById()
    {
        _cache.Setup(x => x.GetOrCreateAsync(It.IsAny<Func<CancellationToken, Task<IReadOnlyList<InventoryResponse>>>>(), It.IsAny<CancellationToken>()))
            .Returns((Func<CancellationToken, Task<IReadOnlyList<InventoryResponse>>> factory, CancellationToken ct) => factory(ct));
        _repository.Setup(x => x.GetInventoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Product("SKU-2", "Keyboard", 3), new Product("SKU-1", "Mouse", 8)]);

        var result = await _service.GetInventoryAsync(CancellationToken.None);

        Assert.That(result, Is.EqualTo(new[]
        {
            new InventoryResponse("SKU-1", "Mouse", 8),
            new InventoryResponse("SKU-2", "Keyboard", 3)
        }));
    }
}

[TestFixture]
public sealed class EventRelayServiceTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private Mock<IOutboxRepository> _outbox = null!;
    private Mock<IEventPublisher> _publisher = null!;
    private EventRelayService _relay = null!;

    [SetUp]
    public void SetUp()
    {
        _outbox = new Mock<IOutboxRepository>();
        _publisher = new Mock<IEventPublisher>();
        _relay = new EventRelayService(_outbox.Object, _publisher.Object, new FakeClock(Now), NullLogger<EventRelayService>.Instance);
    }

    [Test]
    public async Task Relay_PublishesPendingEventsInOrderAndMarksEachPublished()
    {
        var created = Event("e1", HoldEventTypes.Created);
        var released = Event("e2", HoldEventTypes.Released);
        _outbox.Setup(x => x.GetPendingAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync([created, released]);
        var published = new List<string>();
        _publisher.Setup(x => x.PublishAsync(It.IsAny<HoldEvent>(), It.IsAny<CancellationToken>()))
            .Callback<HoldEvent, CancellationToken>((evt, _) => published.Add(evt.EventId))
            .Returns(Task.CompletedTask);

        var count = await _relay.RelayPendingAsync(10, CancellationToken.None);

        Assert.That(count, Is.EqualTo(2));
        Assert.That(published, Is.EqualTo(new[] { "e1", "e2" }));
        _outbox.Verify(x => x.MarkPublishedAsync("e1", Now, It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.MarkPublishedAsync("e2", Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Relay_BrokerFailure_RecordsItAndStopsSoLaterEventsAreNotReordered()
    {
        _outbox.Setup(x => x.GetPendingAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Event("e1", HoldEventTypes.Created), Event("e2", HoldEventTypes.Released)]);
        _publisher.Setup(x => x.PublishAsync(It.Is<HoldEvent>(e => e.EventId == "e1"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("broker down"));

        var count = await _relay.RelayPendingAsync(10, CancellationToken.None);

        Assert.That(count, Is.Zero);
        _outbox.Verify(x => x.RecordFailureAsync("e1", "broker down", It.IsAny<CancellationToken>()), Times.Once);
        _outbox.Verify(x => x.MarkPublishedAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisher.Verify(x => x.PublishAsync(It.Is<HoldEvent>(e => e.EventId == "e2"), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static HoldEvent Event(string id, string type) =>
        new(id, type, 1, Now, "hold-1", HoldStatus.Active, Now, Now.AddMinutes(15), [new HoldItem("SKU-1", "Mouse", 1)]);
}
