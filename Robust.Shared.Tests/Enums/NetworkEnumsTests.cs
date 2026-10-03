using System;
using NUnit.Framework;
using Robust.Shared.Enums;

namespace Robust.Shared.Tests.Enums;

/// <summary>
///     Both enums tested here are sent over the network as a single raw byte (<c>(byte)value</c>), so their
///     numeric values and underlying type are part of the wire format. Changing any of them silently breaks
///     compatibility between clients and servers of different builds; these tests exist to make such a change
///     a deliberate decision instead of an accident (for example by reordering or inserting a member).
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class NetworkEnumsTests
{
    // PlacementManagerMessage is the discriminator byte at the start of the placement network message.

    [Test]
    public void PlacementManagerMessage_UnderlyingTypeIsByte()
    {
        // The message writer emits exactly one byte, so widening the enum would change the wire format.
        Assert.That(Enum.GetUnderlyingType(typeof(PlacementManagerMessage)), Is.EqualTo(typeof(byte)));
    }

    [Test]
    [TestCase(PlacementManagerMessage.StartPlacement, 0)]
    [TestCase(PlacementManagerMessage.CancelPlacement, 1)]
    [TestCase(PlacementManagerMessage.PlacementFailed, 2)]
    [TestCase(PlacementManagerMessage.RequestPlacement, 3)]
    [TestCase(PlacementManagerMessage.RequestEntRemove, 4)]
    [TestCase(PlacementManagerMessage.RequestRectRemove, 5)]
    public void PlacementManagerMessage_HasExpectedWireValue(PlacementManagerMessage message, int expected)
    {
        Assert.That((byte) message, Is.EqualTo(expected));
    }

    [Test]
    public void PlacementManagerMessage_RoundTripsThroughByte()
    {
        // Mirrors what the network layer does: write (byte)value, then read the byte and cast it back.
        foreach (var message in Enum.GetValues<PlacementManagerMessage>())
        {
            var wire = (byte) message;
            Assert.That((PlacementManagerMessage) wire, Is.EqualTo(message));
        }
    }

    // SessionStatus is sent per player in the player list message and is part of the serialized session state.

    [Test]
    public void SessionStatus_UnderlyingTypeIsByte()
    {
        // The player list message writes the status as one byte per player.
        Assert.That(Enum.GetUnderlyingType(typeof(SessionStatus)), Is.EqualTo(typeof(byte)));
    }

    [Test]
    [TestCase(SessionStatus.Zombie, 0)]
    [TestCase(SessionStatus.Connecting, 1)]
    [TestCase(SessionStatus.Connected, 2)]
    [TestCase(SessionStatus.InGame, 3)]
    [TestCase(SessionStatus.Disconnected, 4)]
    public void SessionStatus_HasExpectedWireValue(SessionStatus status, int expected)
    {
        Assert.That((byte) status, Is.EqualTo(expected));
    }

    [Test]
    public void SessionStatus_ZombieIsTheDefaultValue()
    {
        // A zeroed value (for example an uninitialised field) must read as Zombie, i.e. "not a live connection".
        Assert.That(default(SessionStatus), Is.EqualTo(SessionStatus.Zombie));
    }

    [Test]
    public void SessionStatus_RoundTripsThroughByte()
    {
        foreach (var status in Enum.GetValues<SessionStatus>())
        {
            var wire = (byte) status;
            Assert.That((SessionStatus) wire, Is.EqualTo(status));
        }
    }

    [Test]
    public void SessionStatus_NumericOrderIsLifecycleOrder()
    {
        // Callers compare statuses relationally, for example "status >= Disconnected" to decide that a session
        // is unusable and "status > Connecting" to decide that the client console is available. That makes the
        // order Zombie < Connecting < Connected < InGame < Disconnected part of the contract.
        Assert.Multiple(() =>
        {
            Assert.That(SessionStatus.Zombie, Is.LessThan(SessionStatus.Connecting));
            Assert.That(SessionStatus.Connecting, Is.LessThan(SessionStatus.Connected));
            Assert.That(SessionStatus.Connected, Is.LessThan(SessionStatus.InGame));
            Assert.That(SessionStatus.InGame, Is.LessThan(SessionStatus.Disconnected));
        });
    }

    [Test]
    public void SessionStatus_RelationalChecksUsedByCallersHold()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SessionStatus.Disconnected, Is.GreaterThanOrEqualTo(SessionStatus.Disconnected));
            Assert.That(SessionStatus.InGame, Is.LessThan(SessionStatus.Disconnected));
            Assert.That(SessionStatus.Connecting, Is.Not.GreaterThan(SessionStatus.Connecting));
            Assert.That(SessionStatus.Connected, Is.GreaterThan(SessionStatus.Connecting));
            Assert.That(SessionStatus.InGame, Is.GreaterThan(SessionStatus.Connecting));
        });
    }
}
