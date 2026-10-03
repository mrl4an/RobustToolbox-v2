using NUnit.Framework;
using Robust.Shared.Input;

namespace Robust.Shared.Tests.Input;

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(PlayerCommandStates))]
internal sealed class PlayerCommandStatesTests
{
    // BoundKeyFunction converts implicitly from string, so these are plain named functions.
    private static readonly BoundKeyFunction FuncA = "FuncA";
    private static readonly BoundKeyFunction FuncB = "FuncB";

    [Test]
    public void GetState_FunctionNeverSet_IsUp()
    {
        // A missing entry means "not pressed". Consumers rely on a fresh instance reporting Up
        // for every function.
        var states = new PlayerCommandStates();

        Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));
    }

    [Test]
    public void Indexer_FunctionNeverSet_IsUp()
    {
        var states = new PlayerCommandStates();

        Assert.That(states[FuncA], Is.EqualTo(BoundKeyState.Up));
    }

    [Test]
    public void GetState_DoesNotInsertEntryForUnknownFunction()
    {
        // The class exposes no way to enumerate entries, so reading is checked indirectly: read an unknown
        // function (which must stay Up), then set another one, and make sure neither action affects the
        // other function.
        var states = new PlayerCommandStates();

        Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));
        states.SetState(FuncB, BoundKeyState.Down);

        Assert.Multiple(() =>
        {
            Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));
            Assert.That(states.GetState(FuncB), Is.EqualTo(BoundKeyState.Down));
        });
    }

    [Test]
    public void SetState_ThenGetState_ReturnsStoredState()
    {
        var states = new PlayerCommandStates();

        states.SetState(FuncA, BoundKeyState.Down);

        Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Down));
    }

    [Test]
    public void SetState_Overwrites_PreviousState()
    {
        // SetState is an upsert: assigning twice must replace the value instead of failing on a duplicate key.
        var states = new PlayerCommandStates();

        states.SetState(FuncA, BoundKeyState.Down);
        states.SetState(FuncA, BoundKeyState.Up);
        Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));

        states.SetState(FuncA, BoundKeyState.Down);
        Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Down));
    }

    [Test]
    public void SetState_UpOnUnknownFunction_ReadsBackAsUp()
    {
        // Explicitly storing Up for a function that was never set is allowed and gives the same result as
        // never having set it.
        var states = new PlayerCommandStates();

        Assert.DoesNotThrow(() => states.SetState(FuncA, BoundKeyState.Up));

        Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));
    }

    [Test]
    public void IndexerSet_IsEquivalentToSetState()
    {
        var states = new PlayerCommandStates();

        states[FuncA] = BoundKeyState.Down;

        Assert.Multiple(() =>
        {
            Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Down));
            Assert.That(states[FuncA], Is.EqualTo(BoundKeyState.Down));
        });
    }

    [Test]
    public void IndexerGet_IsEquivalentToGetState()
    {
        var states = new PlayerCommandStates();

        states.SetState(FuncA, BoundKeyState.Down);

        Assert.That(states[FuncA], Is.EqualTo(states.GetState(FuncA)));
    }

    [Test]
    public void ThroughInterface_BehavesTheSame()
    {
        // Production code (client and server InputSystem) only sees the interface, so exercise it that way too.
        IPlayerCommandStates states = new PlayerCommandStates();

        states.SetState(FuncA, BoundKeyState.Down);
        states[FuncB] = BoundKeyState.Down;

        Assert.Multiple(() =>
        {
            Assert.That(states[FuncA], Is.EqualTo(BoundKeyState.Down));
            Assert.That(states.GetState(FuncB), Is.EqualTo(BoundKeyState.Down));
        });
    }

    [Test]
    public void DifferentFunctions_AreIndependent()
    {
        var states = new PlayerCommandStates();

        states.SetState(FuncA, BoundKeyState.Down);

        Assert.Multiple(() =>
        {
            Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Down));
            Assert.That(states.GetState(FuncB), Is.EqualTo(BoundKeyState.Up));
        });

        states.SetState(FuncB, BoundKeyState.Down);
        states.SetState(FuncA, BoundKeyState.Up);

        Assert.Multiple(() =>
        {
            Assert.That(states.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));
            Assert.That(states.GetState(FuncB), Is.EqualTo(BoundKeyState.Down));
        });
    }

    [Test]
    public void Functions_WithSameName_ShareOneEntry()
    {
        // Keys use the value equality of BoundKeyFunction, so two separately created functions with the same
        // name refer to the same state.
        var states = new PlayerCommandStates();

        states.SetState(new BoundKeyFunction("Shared"), BoundKeyState.Down);

        Assert.That(states.GetState(new BoundKeyFunction("Shared")), Is.EqualTo(BoundKeyState.Down));
    }

    [Test]
    public void SeparateInstances_DoNotShareState()
    {
        // The server keeps one instance per player session, so states of different players must not leak
        // into each other.
        var first = new PlayerCommandStates();
        var second = new PlayerCommandStates();

        first.SetState(FuncA, BoundKeyState.Down);

        Assert.That(second.GetState(FuncA), Is.EqualTo(BoundKeyState.Up));
    }
}
