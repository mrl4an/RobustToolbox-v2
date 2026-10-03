using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Robust.Shared.Tests.Input;

/// <summary>
///     Tests for the input command message family declared in <c>InputCmdMessage.cs</c>:
///     <see cref="InputCmdMessage"/> (abstract base), <see cref="StateInputCmdMessage"/>,
///     <see cref="EventInputCmdMessage"/>, <see cref="PointerInputCmdMessage"/>,
///     <see cref="FullInputCmdMessage"/>, <see cref="ClientFullInputCmdMessage"/> and
///     <see cref="IFullInputCmdMessage"/>.
/// </summary>
/// <remarks>
///     Several expectations here protect compatibility rather than "obviously correct" behaviour: the set of
///     <c>[NetSerializable]</c> types feeds the serializer's type list and handshake hash, and
///     <see cref="InputCmdMessage.CompareTo"/> is used to sort buffers of pending commands. Tests that document
///     a quirk of the current implementation say so explicitly in their comments.
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(InputCmdMessage))]
internal sealed class InputCmdMessageTests
{
    // Small helpers so that tests only spell out the values they care about.

    private static GameTick Tick(uint value) => new(value);

    private static KeyFunctionId Func(int id) => new(id);

    /// <summary>
    ///     Creates an <see cref="EventInputCmdMessage"/> (the simplest concrete message) with the given sequence.
    /// </summary>
    private static EventInputCmdMessage EventMessage(uint sequence, uint tick = 1, ushort subTick = 0, int func = 1)
    {
        return new EventInputCmdMessage(Tick(tick), subTick, Func(func)) { InputSequence = sequence };
    }

    private static NetCoordinates SampleNetCoordinates() => new(new NetEntity(4), new Vector2(1.5f, -2.5f));

    private static ScreenCoordinates SampleScreenCoordinates() => new(new Vector2(10f, 20f), WindowId.Main);

    private static EntityCoordinates SampleEntityCoordinates() => new(new EntityUid(6), new Vector2(3f, 4f));

    private static FullInputCmdMessage CreateFull(uint tick = 7, ushort subTick = 2, int func = 3,
        BoundKeyState state = BoundKeyState.Down, NetEntity? uid = null)
    {
        return new FullInputCmdMessage(Tick(tick), subTick, Func(func), state, SampleNetCoordinates(),
            SampleScreenCoordinates(), uid ?? new NetEntity(9));
    }

    // ---------------------------------------------------------------------------------------------------------
    // Base class: constructor and InputSequence
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void Constructor_StoresTickSubTickAndFunctionId()
    {
        var message = new EventInputCmdMessage(Tick(42), 7, Func(13));

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(42)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 7));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(13)));
        });
    }

    [Test]
    public void Constructor_AcceptsExtremeValuesWithoutValidation()
    {
        // The base constructor performs no argument validation; the largest representable values must round-trip.
        var message = new EventInputCmdMessage(GameTick.MaxValue, ushort.MaxValue, Func(int.MaxValue));

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(GameTick.MaxValue));
            Assert.That(message.SubTick, Is.EqualTo(ushort.MaxValue));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(int.MaxValue)));
        });
    }

    [Test]
    public void InputSequence_DefaultsToZero()
    {
        // Zero means "unsequenced": the client only assigns a sequence just before sending, and only when
        // client-side prediction is enabled.
        var message = new EventInputCmdMessage(Tick(1), 0, Func(1));

        Assert.That(message.InputSequence, Is.Zero);
    }

    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(123456u)]
    [TestCase(uint.MaxValue)]
    public void InputSequence_CanBeSet(uint sequence)
    {
        // InputSequence is the only mutable member and is deliberately unchecked.
        var message = new EventInputCmdMessage(Tick(1), 0, Func(1));

        message.InputSequence = sequence;

        Assert.That(message.InputSequence, Is.EqualTo(sequence));
    }

    [Test]
    public void InputSequence_SetterDoesNotAffectOtherMembers()
    {
        var message = new EventInputCmdMessage(Tick(5), 6, Func(7));

        message.InputSequence = 99;

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(5)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 6));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(7)));
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // CompareTo
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void CompareTo_Null_ReturnsOne()
    {
        // Any instance sorts after null.
        var message = EventMessage(0);

        Assert.That(message.CompareTo(null), Is.EqualTo(1));
    }

    [Test]
    public void CompareTo_Null_ReturnsPositiveEvenForHighestSequence()
    {
        var message = EventMessage(uint.MaxValue);

        Assert.That(message.CompareTo(null), Is.Positive);
    }

    [Test]
    public void CompareTo_SameReference_ReturnsZero()
    {
        var message = EventMessage(5);

        Assert.That(message.CompareTo(message), Is.Zero);
    }

    [TestCase(1u, 2u, -1)]
    [TestCase(2u, 1u, 1)]
    [TestCase(3u, 3u, 0)]
    [TestCase(0u, 1u, -1)]
    [TestCase(1u, 0u, 1)]
    [TestCase(0u, 0u, 0)]
    public void CompareTo_OrdersBySequence(uint thisSequence, uint otherSequence, int expectedSign)
    {
        var message = EventMessage(thisSequence);
        var other = EventMessage(otherSequence);

        Assert.That(Math.Sign(message.CompareTo(other)), Is.EqualTo(expectedSign));
    }

    [TestCase(0u, uint.MaxValue, -1)]
    [TestCase(uint.MaxValue, 0u, 1)]
    [TestCase(1u, uint.MaxValue, -1)]
    [TestCase(uint.MaxValue, 1u, 1)]
    [TestCase(uint.MaxValue, uint.MaxValue, 0)]
    [TestCase(2147483647u, 2147483648u, -1)]
    [TestCase(2147483648u, 2147483647u, 1)]
    public void CompareTo_UsesUnsignedComparison(uint thisSequence, uint otherSequence, int expectedSign)
    {
        // uint.MaxValue is the same bit pattern as -1 in a signed 32-bit integer. If the sequence were
        // compared as a signed value, 0 would wrongly sort after uint.MaxValue. The same applies to values
        // across the 2^31 boundary. The sequence must therefore be compared as an unsigned number.
        var message = EventMessage(thisSequence);
        var other = EventMessage(otherSequence);

        Assert.That(Math.Sign(message.CompareTo(other)), Is.EqualTo(expectedSign));
    }

    [Test]
    public void CompareTo_IgnoresTickSubTickAndFunction()
    {
        // Same sequence, but every other value differs: the result is still 0, because only the sequence takes
        // part in the comparison.
        var message = EventMessage(sequence: 10, tick: 1, subTick: 0, func: 1);
        var other = EventMessage(sequence: 10, tick: 999, subTick: 500, func: 77);

        Assert.Multiple(() =>
        {
            Assert.That(message.CompareTo(other), Is.Zero);
            Assert.That(other.CompareTo(message), Is.Zero);
        });
    }

    [Test]
    public void CompareTo_LowerSequenceWinsEvenWithLaterTick()
    {
        // The message with the smaller sequence sorts first although it was created at a later tick and
        // sub-tick and has a larger function id.
        var early = EventMessage(sequence: 1, tick: 500, subTick: 900, func: 50);
        var late = EventMessage(sequence: 2, tick: 1, subTick: 0, func: 1);

        Assert.Multiple(() =>
        {
            Assert.That(early.CompareTo(late), Is.Negative);
            Assert.That(late.CompareTo(early), Is.Positive);
        });
    }

    [Test]
    public void CompareTo_IgnoresRuntimeType()
    {
        // Different concrete types with the same sequence still compare as equal.
        InputCmdMessage state = new StateInputCmdMessage(Tick(1), 0, Func(1), BoundKeyState.Down) { InputSequence = 8 };
        InputCmdMessage full = CreateFull();
        full.InputSequence = 8;

        Assert.That(state.CompareTo(full), Is.Zero);

        full.InputSequence = 9;

        Assert.Multiple(() =>
        {
            Assert.That(state.CompareTo(full), Is.Negative);
            Assert.That(full.CompareTo(state), Is.Positive);
        });
    }

    [Test]
    public void CompareTo_UnsequencedMessageSortsBeforeSequencedOnes()
    {
        var unsequenced = EventMessage(0);
        var sequenced = EventMessage(1);

        Assert.Multiple(() =>
        {
            Assert.That(unsequenced.CompareTo(sequenced), Is.Negative);
            Assert.That(sequenced.CompareTo(unsequenced), Is.Positive);
        });
    }

    [Test]
    public void CompareTo_EqualSequencesDoNotImplyEquality()
    {
        // The ordering is deliberately not consistent with equality: there is no Equals override, so two
        // distinct messages with equal sequences compare as 0 while still being different objects.
        var first = EventMessage(4);
        var second = EventMessage(4);

        Assert.Multiple(() =>
        {
            Assert.That(first.CompareTo(second), Is.Zero);
            Assert.That(first.Equals(second), Is.False);
        });
    }

    [Test]
    public void CompareTo_ImplementsGenericComparableInterface()
    {
        IComparable<InputCmdMessage> comparable = EventMessage(1);

        Assert.That(comparable.CompareTo(EventMessage(2)), Is.Negative);
    }

    [Test]
    public void CompareTo_IsNotVirtual()
    {
        // Subclasses must not be able to change the ordering, which callers rely on when sorting pending commands.
        var method = typeof(InputCmdMessage).GetMethod(nameof(InputCmdMessage.CompareTo));

        Assert.That(method, Is.Not.Null);

        // CompareTo implements IComparable<InputCmdMessage>, so the compiler emits it as "virtual final" even
        // though it is not declared virtual in C#. What matters is that subclasses cannot override it, i.e. the
        // method is either not virtual at all or sealed.
        var canBeOverridden = method!.IsVirtual && !method.IsFinal;
        Assert.That(canBeOverridden, Is.False);
    }

    [Test]
    public void Sort_OrdersMessagesBySequence()
    {
        // The sequences below include 0 and uint.MaxValue so that a signed comparison would fail. The ticks
        // are chosen in the reverse order of the sequences to prove that the tick does not influence sorting.
        // All sequences are distinct, so the result does not depend on whether the sort is stable.
        var messages = new List<InputCmdMessage>
        {
            EventMessage(sequence: 5, tick: 10),
            EventMessage(sequence: 0, tick: 50),
            EventMessage(sequence: uint.MaxValue, tick: 1),
            EventMessage(sequence: 1, tick: 40),
            EventMessage(sequence: 3, tick: 20),
        };

        messages.Sort();

        Assert.That(messages.Select(m => m.InputSequence), Is.EqualTo(new uint[] { 0, 1, 3, 5, uint.MaxValue }));
    }

    [Test]
    public void Sort_WithEqualSequences_KeepsAllMessages()
    {
        // Ties compare as equal, so their relative order is unspecified, but no message may be lost.
        var a = EventMessage(2);
        var b = EventMessage(2);
        var c = EventMessage(1);
        var messages = new List<InputCmdMessage> { a, b, c };

        messages.Sort();

        Assert.Multiple(() =>
        {
            Assert.That(messages, Has.Count.EqualTo(3));
            Assert.That(messages[0], Is.SameAs(c));
            Assert.That(messages, Does.Contain(a));
            Assert.That(messages, Does.Contain(b));
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // ToString
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void ToString_UsesDocumentedLayout()
    {
        // Layout: first three pairs separated by comma+space, then a single space (no comma) before "func".
        // GameTick prints its numeric value and KeyFunctionId prints its integer id.
        var message = new EventInputCmdMessage(Tick(5), 3, Func(7)) { InputSequence = 9 };

        Assert.That(message.ToString(), Is.EqualTo("tick=5, subTick=3, seq=9 func=7"));
    }

    [Test]
    public void ToString_ShowsZeroSequenceByDefault()
    {
        var message = new EventInputCmdMessage(Tick(1), 0, Func(2));

        Assert.That(message.ToString(), Is.EqualTo("tick=1, subTick=0, seq=0 func=2"));
    }

    [Test]
    public void ToString_SequenceAndFunctionAreSeparatedBySpaceOnly()
    {
        var text = new EventInputCmdMessage(Tick(1), 2, Func(3)) { InputSequence = 4 }.ToString();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("tick="));
            Assert.That(text, Does.Contain(", subTick="));
            Assert.That(text, Does.Contain(", seq="));
            Assert.That(text, Does.Contain("seq=4 func="));
            Assert.That(text, Does.Not.Contain(", func="));
        });
    }

    [Test]
    public void ToString_ReflectsLargestSequenceAsUnsigned()
    {
        // The sequence is unsigned, so the maximum prints as 4294967295 rather than -1.
        var text = new EventInputCmdMessage(Tick(1), 0, Func(1)) { InputSequence = uint.MaxValue }.ToString();

        Assert.That(text, Does.Contain("seq=4294967295 "));
    }

    [Test]
    public void ToString_IsInheritedByDerivedTypes()
    {
        // None of the derived message types override ToString, so the base layout applies to all of them.
        var state = new StateInputCmdMessage(Tick(8), 1, Func(2), BoundKeyState.Down) { InputSequence = 3 };

        Assert.That(state.ToString(), Is.EqualTo("tick=8, subTick=1, seq=3 func=2"));
    }

    // ---------------------------------------------------------------------------------------------------------
    // StateInputCmdMessage
    // ---------------------------------------------------------------------------------------------------------

    [TestCase(BoundKeyState.Up)]
    [TestCase(BoundKeyState.Down)]
    public void StateInputCmdMessage_StoresStateAndBaseValues(BoundKeyState state)
    {
        var message = new StateInputCmdMessage(Tick(11), 12, Func(13), state);

        Assert.Multiple(() =>
        {
            Assert.That(message.State, Is.EqualTo(state));
            Assert.That(message.Tick, Is.EqualTo(Tick(11)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 12));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(13)));
            Assert.That(message.InputSequence, Is.Zero);
        });
    }

    [Test]
    public void StateInputCmdMessage_StoresUndefinedStateValueWithoutValidation()
    {
        // The state is a byte-sized enum and the constructor does not validate it, so receivers must sanitize.
        var message = new StateInputCmdMessage(Tick(1), 0, Func(1), (BoundKeyState) 200);

        Assert.That((byte) message.State, Is.EqualTo(200));
    }

    // ---------------------------------------------------------------------------------------------------------
    // EventInputCmdMessage
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void EventInputCmdMessage_StoresBaseValues()
    {
        var message = new EventInputCmdMessage(Tick(21), 22, Func(23));

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(21)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 22));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(23)));
            Assert.That(message.InputSequence, Is.Zero);
        });
    }

    [Test]
    public void EventInputCmdMessage_IsNotSealed()
    {
        // PointerInputCmdMessage and content code derive from it.
        Assert.That(typeof(EventInputCmdMessage).IsSealed, Is.False);
    }

    // ---------------------------------------------------------------------------------------------------------
    // PointerInputCmdMessage
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void PointerInputCmdMessage_FourArgumentConstructor_UsesInvalidUid()
    {
        var coordinates = SampleNetCoordinates();

        var message = new PointerInputCmdMessage(Tick(1), 2, Func(3), coordinates);

        Assert.Multiple(() =>
        {
            Assert.That(message.Uid, Is.EqualTo(NetEntity.Invalid));
            Assert.That(message.Coordinates, Is.EqualTo(coordinates));
            Assert.That(message.Tick, Is.EqualTo(Tick(1)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 2));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(3)));
        });
    }

    [Test]
    public void PointerInputCmdMessage_FiveArgumentConstructor_StoresUid()
    {
        var coordinates = SampleNetCoordinates();
        var uid = new NetEntity(77);

        var message = new PointerInputCmdMessage(Tick(1), 2, Func(3), coordinates, uid);

        Assert.Multiple(() =>
        {
            Assert.That(message.Uid, Is.EqualTo(uid));
            Assert.That(message.Coordinates, Is.EqualTo(coordinates));
            Assert.That(message.Tick, Is.EqualTo(Tick(1)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 2));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(3)));
            Assert.That(message.InputSequence, Is.Zero);
        });
    }

    [Test]
    public void PointerInputCmdMessage_IsAnEventMessage()
    {
        var message = new PointerInputCmdMessage(Tick(1), 0, Func(1), SampleNetCoordinates());

        Assert.That(message, Is.InstanceOf<EventInputCmdMessage>());
    }

    // ---------------------------------------------------------------------------------------------------------
    // FullInputCmdMessage
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void FullInputCmdMessage_UidConstructor_StoresEverything()
    {
        var coordinates = SampleNetCoordinates();
        var screen = SampleScreenCoordinates();
        var uid = new NetEntity(31);

        var message = new FullInputCmdMessage(Tick(7), 8, Func(9), BoundKeyState.Down, coordinates, screen, uid);

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(7)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 8));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(9)));
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Down));
            Assert.That(message.Coordinates, Is.EqualTo(coordinates));
            Assert.That(message.ScreenCoordinates, Is.EqualTo(screen));
            Assert.That(message.Uid, Is.EqualTo(uid));
            Assert.That(message.InputSequence, Is.Zero);
        });
    }

    [Test]
    public void FullInputCmdMessage_SequenceConstructor_StoresEverythingExceptSequenceAndUsesInvalidUid()
    {
        var coordinates = SampleNetCoordinates();
        var screen = SampleScreenCoordinates();

        // The third argument (the int 5) is the input sequence; the overload is selected by its int type.
        var message = new FullInputCmdMessage(Tick(7), 8, 5, Func(9), BoundKeyState.Up, coordinates, screen);

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(7)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 8));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(9)));
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Up));
            Assert.That(message.Coordinates, Is.EqualTo(coordinates));
            Assert.That(message.ScreenCoordinates, Is.EqualTo(screen));
            Assert.That(message.Uid, Is.EqualTo(NetEntity.Invalid));
        });
    }

    [Test]
    public void FullInputCmdMessage_SequenceConstructor_IgnoresInputSequenceArgument()
    {
        // This test documents CURRENT behaviour, which is a known quirk: the int inputSequence parameter is
        // accepted for source compatibility but never stored, so InputSequence stays at 0. The client assigns
        // the real sequence later through the property setter. If this behaviour is ever changed on purpose,
        // update this test together with the code and review the sequencing logic of the client.
        var message = new FullInputCmdMessage(Tick(1), 0, 12345, Func(1), BoundKeyState.Down,
            SampleNetCoordinates(), SampleScreenCoordinates());

        Assert.That(message.InputSequence, Is.Zero);
    }

    [Test]
    public void FullInputCmdMessage_InputSequence_CanBeAssignedAfterConstruction()
    {
        var message = CreateFull();

        message.InputSequence = 55;

        Assert.That(message.InputSequence, Is.EqualTo(55u));
    }

    [Test]
    public void FullInputCmdMessage_Uid_CanBeSetThroughObjectInitializer()
    {
        var message = new FullInputCmdMessage(Tick(1), 0, Func(1), BoundKeyState.Down, SampleNetCoordinates(),
            SampleScreenCoordinates(), NetEntity.Invalid)
        {
            Uid = new NetEntity(88),
        };

        Assert.That(message.Uid, Is.EqualTo(new NetEntity(88)));
    }

    [Test]
    public void FullInputCmdMessage_OnlyUidIsInitOnly_OtherPointerPropertiesAreGetOnly()
    {
        // State, Coordinates and ScreenCoordinates have no setter at all; Uid has an init-only setter.
        Assert.Multiple(() =>
        {
            Assert.That(HasAnySetter(typeof(FullInputCmdMessage), nameof(FullInputCmdMessage.State)), Is.False);
            Assert.That(HasAnySetter(typeof(FullInputCmdMessage), nameof(FullInputCmdMessage.Coordinates)), Is.False);
            Assert.That(HasAnySetter(typeof(FullInputCmdMessage), nameof(FullInputCmdMessage.ScreenCoordinates)), Is.False);
            Assert.That(IsInitOnly(typeof(FullInputCmdMessage), nameof(FullInputCmdMessage.Uid)), Is.True);
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // ClientFullInputCmdMessage
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void ClientFullInputCmdMessage_ShortConstructor_LeavesPointerDataAtDefaults()
    {
        var message = new ClientFullInputCmdMessage(Tick(3), 4, Func(5));

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(3)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 4));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(5)));
            Assert.That(message.InputSequence, Is.Zero);
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Up));
            Assert.That(message.Coordinates, Is.EqualTo(default(EntityCoordinates)));
            Assert.That(message.ScreenCoordinates, Is.EqualTo(default(ScreenCoordinates)));
            Assert.That(message.Uid, Is.EqualTo(default(EntityUid)));
            Assert.That(message.Uid, Is.EqualTo(EntityUid.Invalid));
        });
    }

    [Test]
    public void ClientFullInputCmdMessage_LongConstructor_StoresEverything()
    {
        var coordinates = SampleEntityCoordinates();
        var screen = SampleScreenCoordinates();
        var uid = new EntityUid(15);

        // Note the parameter order of this constructor: coordinates, screenCoordinates, state, uid.
        var message = new ClientFullInputCmdMessage(Tick(3), 4, Func(5), coordinates, screen, BoundKeyState.Down, uid);

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(3)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 4));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(5)));
            Assert.That(message.InputSequence, Is.Zero);
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Down));
            Assert.That(message.Coordinates, Is.EqualTo(coordinates));
            Assert.That(message.ScreenCoordinates, Is.EqualTo(screen));
            Assert.That(message.Uid, Is.EqualTo(uid));
        });
    }

    [Test]
    public void ClientFullInputCmdMessage_ObjectInitializer_SetsInitOnlyProperties()
    {
        // This is how content constructs the message: the short constructor plus an object initializer.
        var coordinates = SampleEntityCoordinates();
        var screen = SampleScreenCoordinates();
        var uid = new EntityUid(15);

        var message = new ClientFullInputCmdMessage(Tick(3), 4, Func(5))
        {
            State = BoundKeyState.Down,
            Coordinates = coordinates,
            ScreenCoordinates = screen,
            Uid = uid,
        };

        Assert.Multiple(() =>
        {
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Down));
            Assert.That(message.Coordinates, Is.EqualTo(coordinates));
            Assert.That(message.ScreenCoordinates, Is.EqualTo(screen));
            Assert.That(message.Uid, Is.EqualTo(uid));
        });
    }

    [Test]
    public void ClientFullInputCmdMessage_AllOwnPropertiesAreInitOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IsInitOnly(typeof(ClientFullInputCmdMessage), nameof(ClientFullInputCmdMessage.State)), Is.True);
            Assert.That(IsInitOnly(typeof(ClientFullInputCmdMessage), nameof(ClientFullInputCmdMessage.Coordinates)), Is.True);
            Assert.That(IsInitOnly(typeof(ClientFullInputCmdMessage), nameof(ClientFullInputCmdMessage.ScreenCoordinates)), Is.True);
            Assert.That(IsInitOnly(typeof(ClientFullInputCmdMessage), nameof(ClientFullInputCmdMessage.Uid)), Is.True);
        });
    }

    [Test]
    public void ClientFullInputCmdMessage_InputSequence_RemainsSettable()
    {
        // InputSequence comes from the base class and must stay a normal (not init-only) setter, because the
        // client's game-state manager assigns it after construction.
        var message = new ClientFullInputCmdMessage(Tick(1), 0, Func(1));

        message.InputSequence = 17;

        Assert.Multiple(() =>
        {
            Assert.That(message.InputSequence, Is.EqualTo(17u));
            Assert.That(IsInitOnly(typeof(InputCmdMessage), nameof(InputCmdMessage.InputSequence)), Is.False);
            Assert.That(typeof(InputCmdMessage).GetProperty(nameof(InputCmdMessage.InputSequence))!.SetMethod,
                Is.Not.Null);
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // Serialization attributes and type shape
    // ---------------------------------------------------------------------------------------------------------

    private static readonly Type[] NetSerializableMessageTypes =
    {
        typeof(InputCmdMessage),
        typeof(StateInputCmdMessage),
        typeof(EventInputCmdMessage),
        typeof(PointerInputCmdMessage),
        typeof(FullInputCmdMessage),
    };

    [Test]
    public void NetSerializableTypes_CarryBothSerializationAttributes([ValueSource(nameof(NetSerializableMessageTypes))] Type type)
    {
        // [NetSerializable] makes the serializer include the type in its type list and handshake hash, so the
        // exact set of marked types must stay stable. [Serializable] is kept alongside it by convention.
        Assert.Multiple(() =>
        {
            Assert.That(Attribute.IsDefined(type, typeof(NetSerializableAttribute), inherit: false), Is.True,
                $"{type.Name} must be [NetSerializable]");
            Assert.That(type.IsSerializable, Is.True, $"{type.Name} must be [Serializable]");
        });
    }

    [Test]
    public void ClientFullInputCmdMessage_IsNotSerializable()
    {
        // It uses local EntityUid/EntityCoordinates values that are meaningless on the wire, and marking it
        // [NetSerializable] would change the serializer's type list and break connection compatibility.
        // Attributes are checked on the type itself (not inherited), although the base class carries both.
        Assert.Multiple(() =>
        {
            Assert.That(Attribute.IsDefined(typeof(ClientFullInputCmdMessage), typeof(NetSerializableAttribute), inherit: false), Is.False);
            Assert.That(typeof(ClientFullInputCmdMessage).IsSerializable, Is.False);
        });
    }

    [Test]
    public void IFullInputCmdMessage_IsNotNetSerializable()
    {
        Assert.That(Attribute.IsDefined(typeof(IFullInputCmdMessage), typeof(NetSerializableAttribute), inherit: false), Is.False);
    }

    [Test]
    public void BaseClass_IsAbstract()
    {
        Assert.That(typeof(InputCmdMessage).IsAbstract, Is.True);
    }

    [Test]
    public void BaseClass_DerivesFromEntityEventArgs()
    {
        // Network events must derive from EntityEventArgs to be raised through the event bus.
        Assert.That(typeof(InputCmdMessage).BaseType, Is.EqualTo(typeof(EntityEventArgs)));
    }

    [Test]
    public void ConcreteTypes_DeriveFromExpectedBases()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(StateInputCmdMessage).BaseType, Is.EqualTo(typeof(InputCmdMessage)));
            Assert.That(typeof(EventInputCmdMessage).BaseType, Is.EqualTo(typeof(InputCmdMessage)));
            Assert.That(typeof(PointerInputCmdMessage).BaseType, Is.EqualTo(typeof(EventInputCmdMessage)));
            Assert.That(typeof(FullInputCmdMessage).BaseType, Is.EqualTo(typeof(InputCmdMessage)));
            Assert.That(typeof(ClientFullInputCmdMessage).BaseType, Is.EqualTo(typeof(InputCmdMessage)));
        });
    }

    [Test]
    public void FullTypes_AreSealedAndDoNotDeriveFromEachOther()
    {
        // Handlers pattern-match on the two concrete types, so they must remain distinct sealed classes.
        Assert.Multiple(() =>
        {
            Assert.That(typeof(FullInputCmdMessage).IsSealed, Is.True);
            Assert.That(typeof(ClientFullInputCmdMessage).IsSealed, Is.True);
            Assert.That(typeof(FullInputCmdMessage).IsAssignableFrom(typeof(ClientFullInputCmdMessage)), Is.False);
            Assert.That(typeof(ClientFullInputCmdMessage).IsAssignableFrom(typeof(FullInputCmdMessage)), Is.False);
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // IFullInputCmdMessage
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void IFullInputCmdMessage_CanBeAssignedFromBothFullTypes()
    {
        IFullInputCmdMessage network = CreateFull();
        IFullInputCmdMessage client = new ClientFullInputCmdMessage(Tick(1), 0, Func(1));

        Assert.Multiple(() =>
        {
            Assert.That(network, Is.InstanceOf<FullInputCmdMessage>());
            Assert.That(client, Is.InstanceOf<ClientFullInputCmdMessage>());
            Assert.That(typeof(IFullInputCmdMessage).IsAssignableFrom(typeof(FullInputCmdMessage)), Is.True);
            Assert.That(typeof(IFullInputCmdMessage).IsAssignableFrom(typeof(ClientFullInputCmdMessage)), Is.True);
        });
    }

    [Test]
    public void IFullInputCmdMessage_ExposesValuesOfNetworkMessage()
    {
        IFullInputCmdMessage message = CreateFull(tick: 70, subTick: 71, func: 72, state: BoundKeyState.Down);

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(70)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 71));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(72)));
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Down));
            Assert.That(message.InputSequence, Is.Zero);
        });
    }

    [Test]
    public void IFullInputCmdMessage_ExposesValuesOfClientMessage()
    {
        IFullInputCmdMessage message = new ClientFullInputCmdMessage(Tick(80), 81, Func(82))
        {
            State = BoundKeyState.Down,
        };

        Assert.Multiple(() =>
        {
            Assert.That(message.Tick, Is.EqualTo(Tick(80)));
            Assert.That(message.SubTick, Is.EqualTo((ushort) 81));
            Assert.That(message.InputFunctionId, Is.EqualTo(Func(82)));
            Assert.That(message.State, Is.EqualTo(BoundKeyState.Down));
            Assert.That(message.InputSequence, Is.Zero);
        });
    }

    [Test]
    public void IFullInputCmdMessage_InputSequenceSetterWritesThroughToConcreteMessage()
    {
        // Input handlers only see the interface, and the client assigns sequences through it.
        var network = CreateFull();
        var client = new ClientFullInputCmdMessage(Tick(1), 0, Func(1));
        IFullInputCmdMessage networkView = network;
        IFullInputCmdMessage clientView = client;

        networkView.InputSequence = 21;
        clientView.InputSequence = 22;

        Assert.Multiple(() =>
        {
            Assert.That(network.InputSequence, Is.EqualTo(21u));
            Assert.That(client.InputSequence, Is.EqualTo(22u));
        });
    }

    [Test]
    public void IFullInputCmdMessage_DeclaresExactlyTheDocumentedMembers()
    {
        var properties = typeof(IFullInputCmdMessage).GetProperties()
            .ToDictionary(p => p.Name, p => p);

        Assert.Multiple(() =>
        {
            Assert.That(properties.Keys, Is.EquivalentTo(new[]
            {
                "Tick", "State", "InputFunctionId", "SubTick", "InputSequence",
            }));

            Assert.That(properties["Tick"].PropertyType, Is.EqualTo(typeof(GameTick)));
            Assert.That(properties["State"].PropertyType, Is.EqualTo(typeof(BoundKeyState)));
            Assert.That(properties["InputFunctionId"].PropertyType, Is.EqualTo(typeof(KeyFunctionId)));
            Assert.That(properties["SubTick"].PropertyType, Is.EqualTo(typeof(ushort)));
            Assert.That(properties["InputSequence"].PropertyType, Is.EqualTo(typeof(uint)));

            // Only InputSequence is settable through the interface.
            Assert.That(properties["Tick"].CanWrite, Is.False);
            Assert.That(properties["State"].CanWrite, Is.False);
            Assert.That(properties["InputFunctionId"].CanWrite, Is.False);
            Assert.That(properties["SubTick"].CanWrite, Is.False);
            Assert.That(properties["InputSequence"].CanWrite, Is.True);
        });
    }

    [Test]
    public void IFullInputCmdMessage_IsAnInterfaceWithoutBaseInterfaces()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(IFullInputCmdMessage).IsInterface, Is.True);
            Assert.That(typeof(IFullInputCmdMessage).GetInterfaces(), Is.Empty);
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // Reflection helpers
    // ---------------------------------------------------------------------------------------------------------

    private static PropertyInfo GetProperty(Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(property, Is.Not.Null, $"{type.Name}.{name} must exist as a public instance property");
        return property!;
    }

    /// <summary>
    ///     True if the property has a setter of any accessibility (init-only setters count).
    /// </summary>
    private static bool HasAnySetter(Type type, string name)
    {
        return GetProperty(type, name).GetSetMethod(nonPublic: true) != null;
    }

    /// <summary>
    ///     True if the property has a setter that is marked with the compiler's <c>IsExternalInit</c> modifier,
    ///     i.e. an <c>init</c> accessor.
    /// </summary>
    private static bool IsInitOnly(Type type, string name)
    {
        var setter = GetProperty(type, name).SetMethod;
        if (setter == null)
            return false;

        return setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));
    }
}
