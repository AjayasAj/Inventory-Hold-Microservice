using InventoryHold.Contracts;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Exceptions;
using NUnit.Framework;

namespace InventoryHold.UnitTests;

[TestFixture]
public sealed class HoldEntityTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly HoldLine[] Lines = [new("SKU-1", "Mouse", 1)];

    [Test]
    public void Place_WithoutLines_ThrowsValidation() =>
        Assert.Throws<HoldValidationException>(() => Hold.Place([], Now, TimeSpan.FromMinutes(1)));

    [Test]
    public void Place_CreatesActiveHoldExpiringAfterDuration()
    {
        var hold = Hold.Place(Lines, Now, TimeSpan.FromMinutes(15));

        Assert.Multiple(() =>
        {
            Assert.That(hold.Status, Is.EqualTo(HoldStatus.Active));
            Assert.That(hold.ExpiresAtUtc, Is.EqualTo(Now.AddMinutes(15)));
            Assert.That(hold.ClosedAtUtc, Is.Null);
            Assert.That(hold.Id, Is.Not.Empty);
        });
    }

    [TestCase(-1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public void IsDueForExpiry_IsInclusiveOfTheExpiryInstant(int secondsAfterExpiry, bool expected)
    {
        var hold = Hold.Place(Lines, Now, TimeSpan.FromMinutes(1));

        Assert.That(hold.IsDueForExpiry(hold.ExpiresAtUtc.AddSeconds(secondsAfterExpiry)), Is.EqualTo(expected));
    }

    [Test]
    public void Release_ClosedHold_ThrowsNotActive()
    {
        var released = Hold.Place(Lines, Now, TimeSpan.FromMinutes(1)).Release(Now);

        Assert.Throws<HoldNotActiveException>(() => released.Release(Now));
    }

    [Test]
    public void Release_OverdueHold_ThrowsExpired()
    {
        var hold = Hold.Place(Lines, Now, TimeSpan.FromMinutes(1));

        Assert.Throws<HoldExpiredException>(() => hold.Release(Now.AddMinutes(2)));
    }

    [Test]
    public void Expire_HoldNotYetDue_IsRejected()
    {
        var hold = Hold.Place(Lines, Now, TimeSpan.FromMinutes(1));

        Assert.Throws<InvalidOperationException>(() => hold.Expire(Now));
    }
}
