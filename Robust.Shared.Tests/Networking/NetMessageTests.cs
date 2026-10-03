using System;
using Lidgren.Network;
using Moq;
using NUnit.Framework;
using Robust.Shared.Analyzers;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Robust.Shared.Tests.Networking;

/// <summary>
///     Tests for <see cref="MsgGroups"/> and the default behaviour of <see cref="NetMessage"/>.
/// </summary>
/// <remarks>
///     <see cref="NetMessage"/> is abstract, so every test uses one of the small private subclasses declared at the
///     bottom of this class. They implement <see cref="NetMessage.ReadFromBuffer"/> and
///     <see cref="NetMessage.WriteToBuffer"/> as no-ops because those methods (the wire format) belong to concrete
///     message types and are not what is under test here.
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(NetMessage))]
internal sealed class NetMessageTests
{
    // ---------------------------------------------------------------------------------------------------------
    // MsgGroups
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void MsgGroups_UnderlyingTypeIsByte()
    {
        Assert.That(Enum.GetUnderlyingType(typeof(MsgGroups)), Is.EqualTo(typeof(byte)));
    }

    [Test]
    [TestCase(MsgGroups.Error, 0)]
    [TestCase(MsgGroups.Core, 1)]
    [TestCase(MsgGroups.Entity, 2)]
    [TestCase(MsgGroups.String, 3)]
    [TestCase(MsgGroups.Command, 4)]
    [TestCase(MsgGroups.EntityEvent, 5)]
    public void MsgGroups_HasExpectedNumericValue(MsgGroups group, int expected)
    {
        // The numeric values are pinned so that an accidental reordering or insertion of a member is noticed.
        Assert.That((byte) group, Is.EqualTo(expected));
    }

    [Test]
    public void MsgGroups_HasExactlySixMembers()
    {
        Assert.That(Enum.GetValues<MsgGroups>(), Has.Length.EqualTo(6));
    }

    [Test]
    public void MsgGroups_DefaultValueIsError()
    {
        // MsgGroup relies on default(MsgGroups) meaning "no group chosen".
        Assert.That(default(MsgGroups), Is.EqualTo(MsgGroups.Error));
    }

    [Test]
    public void MsgGroups_IsNotAFlagsEnum()
    {
        Assert.That(typeof(MsgGroups).IsDefined(typeof(FlagsAttribute), inherit: false), Is.False);
    }

    // ---------------------------------------------------------------------------------------------------------
    // Type shape
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void NetMessage_IsAbstractAndNotSealed()
    {
        Assert.Multiple(() =>
        {
            Assert.That(typeof(NetMessage).IsAbstract, Is.True);
            Assert.That(typeof(NetMessage).IsSealed, Is.False);
        });
    }

    [Test]
    public void NetMessage_OnlyConstructorIsProtected()
    {
        // Derived message types need their own public parameterless constructor to satisfy the new() constraint
        // of INetManager.RegisterNetMessage, so the base constructor does not need to be public.
        var constructors = typeof(NetMessage).GetConstructors(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);

        Assert.That(constructors, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(constructors[0].IsFamily, Is.True);
            Assert.That(constructors[0].GetParameters(), Is.Empty);
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // MsgName
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void MsgName_DefaultsToSimpleTypeNameOfDerivedClass()
    {
        // The default carries no namespace and no declaring-type prefix (the test class is nested).
        var message = new PlainMessage();

        Assert.That(message.MsgName, Is.EqualTo("PlainMessage"));
    }

    [Test]
    public void MsgName_DefaultUsesMostDerivedType()
    {
        // The name comes from the runtime type of the object being constructed, not from the class that
        // declares the property or from an intermediate base class.
        var message = new FurtherDerivedMessage();

        Assert.That(message.MsgName, Is.EqualTo("FurtherDerivedMessage"));
    }

    [Test]
    public void MsgName_DefaultOfGenericTypeIncludesArity()
    {
        // The runtime's simple name of a generic type has a backtick and the number of type parameters.
        var message = new GenericMessage<int>();

        Assert.That(message.MsgName, Is.EqualTo("GenericMessage`1"));
    }

    [Test]
    public void MsgName_DefaultIsTheSameForEveryInstanceOfAType()
    {
        // The name must be deterministic and must not depend on per-instance state.
        var first = new PlainMessage();
        var second = new PlainMessage { MsgSize = 123 };

        Assert.That(second.MsgName, Is.EqualTo(first.MsgName));
    }

    [Test]
    public void MsgName_OverrideReplacesDefault()
    {
        var message = new NamedMessage("CustomName");

        Assert.That(message.MsgName, Is.EqualTo("CustomName"));
    }

    [Test]
    public void MsgName_OverrideMayReturnEmptyString()
    {
        // The engine's handshake messages override the name with an empty string.
        var message = new NamedMessage(string.Empty);

        Assert.That(message.MsgName, Is.EqualTo(string.Empty));
    }

    // ---------------------------------------------------------------------------------------------------------
    // MsgGroup
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void MsgGroup_DefaultsToError()
    {
        // The base class deliberately does not pick a group; every concrete message type must override it.
        var message = new PlainMessage();

        Assert.That(message.MsgGroup, Is.EqualTo(MsgGroups.Error));
    }

    [Test]
    [TestCase(MsgGroups.Core)]
    [TestCase(MsgGroups.Entity)]
    [TestCase(MsgGroups.String)]
    [TestCase(MsgGroups.Command)]
    [TestCase(MsgGroups.EntityEvent)]
    public void MsgGroup_OverrideIsReturned(MsgGroups group)
    {
        var message = new GroupMessage(group);

        Assert.That(message.MsgGroup, Is.EqualTo(group));
    }

    // ---------------------------------------------------------------------------------------------------------
    // DeliveryMethod
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void DeliveryMethod_WithDefaultErrorGroup_Throws()
    {
        // A message type that forgot to override MsgGroup must fail when it is sent, rather than silently
        // getting some arbitrary delivery guarantee.
        var message = new PlainMessage();

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = message.DeliveryMethod);
    }

    [Test]
    public void DeliveryMethod_WithExplicitErrorGroup_Throws()
    {
        var message = new GroupMessage(MsgGroups.Error);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = message.DeliveryMethod);
    }

    [Test]
    [TestCase((byte) 6)]
    [TestCase((byte) 100)]
    [TestCase((byte) 255)]
    public void DeliveryMethod_WithUndefinedGroupValue_Throws(byte rawGroup)
    {
        // Group values that are not named members of the enum are treated like Error.
        var message = new GroupMessage((MsgGroups) rawGroup);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = message.DeliveryMethod);
    }

    [Test]
    [TestCase(MsgGroups.Entity, NetDeliveryMethod.Unreliable)]
    [TestCase(MsgGroups.Core, NetDeliveryMethod.ReliableUnordered)]
    [TestCase(MsgGroups.Command, NetDeliveryMethod.ReliableUnordered)]
    [TestCase(MsgGroups.String, NetDeliveryMethod.ReliableOrdered)]
    [TestCase(MsgGroups.EntityEvent, NetDeliveryMethod.ReliableOrdered)]
    public void DeliveryMethod_DefaultMappingFollowsGroup(MsgGroups group, NetDeliveryMethod expected)
    {
        // Entity traffic is unreliable on purpose: newer data supersedes older data. Core and Command only
        // need to arrive; String and EntityEvent also have to keep their order.
        var message = new GroupMessage(group);

        Assert.That(message.DeliveryMethod, Is.EqualTo(expected));
    }

    [Test]
    public void DeliveryMethod_CanBeOverriddenIndependentlyOfGroup()
    {
        // Overriding the delivery method also makes the Error group harmless, because the group is then not
        // consulted at all.
        var message = new FixedDeliveryMessage(NetDeliveryMethod.ReliableSequenced);

        Assert.That(message.DeliveryMethod, Is.EqualTo(NetDeliveryMethod.ReliableSequenced));
    }

    [Test]
    [TestCase(MsgGroups.Entity, NetDeliveryMethod.Unreliable)]
    [TestCase(MsgGroups.Core, NetDeliveryMethod.ReliableUnordered)]
    [TestCase(MsgGroups.String, NetDeliveryMethod.ReliableOrdered)]
    public void DeliveryMethod_OverrideCanFallBackToBaseImplementation(MsgGroups group, NetDeliveryMethod expected)
    {
        // Derived types may switch to another method for special cases (for example large messages) and
        // otherwise call base.DeliveryMethod to keep the group default.
        var message = new FallbackDeliveryMessage(group, forceReliable: false);

        Assert.That(message.DeliveryMethod, Is.EqualTo(expected));
    }

    [Test]
    public void DeliveryMethod_OverrideCanReplaceBaseImplementationConditionally()
    {
        var message = new FallbackDeliveryMessage(MsgGroups.Entity, forceReliable: true);

        Assert.That(message.DeliveryMethod, Is.EqualTo(NetDeliveryMethod.ReliableUnordered));
    }

    [Test]
    public void DeliveryMethod_ReflectsMsgGroupOverrideOnEveryRead()
    {
        // The default mapping is a pure function of the group that is evaluated on each read.
        var message = new MutableGroupMessage { Group = MsgGroups.Entity };
        var before = message.DeliveryMethod;

        message.Group = MsgGroups.String;
        var after = message.DeliveryMethod;

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(NetDeliveryMethod.Unreliable));
            Assert.That(after, Is.EqualTo(NetDeliveryMethod.ReliableOrdered));
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // SequenceChannel
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void SequenceChannel_DefaultsToZero()
    {
        var message = new PlainMessage();

        Assert.That(message.SequenceChannel, Is.Zero);
    }

    [Test]
    [TestCase(MsgGroups.Error)]
    [TestCase(MsgGroups.Core)]
    [TestCase(MsgGroups.Entity)]
    [TestCase(MsgGroups.String)]
    [TestCase(MsgGroups.Command)]
    [TestCase(MsgGroups.EntityEvent)]
    public void SequenceChannel_DefaultsToZeroForEveryGroup(MsgGroups group)
    {
        // The channel does not depend on the group and reading it never throws, not even for the Error group.
        var message = new GroupMessage(group);

        Assert.That(message.SequenceChannel, Is.Zero);
    }

    [TestCase(1)]
    [TestCase(15)]
    [TestCase(16)]
    public void SequenceChannel_OverrideIsReturned(int channel)
    {
        var message = new SequenceChannelMessage(channel);

        Assert.That(message.SequenceChannel, Is.EqualTo(channel));
    }

    // ---------------------------------------------------------------------------------------------------------
    // MsgSize
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void MsgSize_DefaultsToZero()
    {
        // The net manager never sets the size; only message types that opt in assign it.
        var message = new PlainMessage();

        Assert.That(message.MsgSize, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(1400)]
    [TestCase(int.MaxValue)]
    public void MsgSize_CanBeSet(int size)
    {
        var message = new PlainMessage();

        message.MsgSize = size;

        Assert.That(message.MsgSize, Is.EqualTo(size));
    }

    // ---------------------------------------------------------------------------------------------------------
    // MsgChannel
    // ---------------------------------------------------------------------------------------------------------

    [Test]
    public void MsgChannel_IsNullOnLocallyCreatedMessage()
    {
        // It is declared non-null but really stays null until the net manager assigns it on incoming messages;
        // messages created in order to be sent never get a channel.
        var message = new PlainMessage();

        Assert.That(message.MsgChannel, Is.Null);
    }

    [Test]
    public void MsgChannel_CanBeSet()
    {
        var channel = Mock.Of<INetChannel>();
        var message = new PlainMessage();

        message.MsgChannel = channel;

        Assert.That(message.MsgChannel, Is.SameAs(channel));
    }

    [Test]
    public void MsgChannel_CanBeSetThroughObjectInitializer()
    {
        // Callers assign it with an object initializer, so the setter has to be public.
        var channel = Mock.Of<INetChannel>();

        var message = new PlainMessage { MsgChannel = channel };

        Assert.That(message.MsgChannel, Is.SameAs(channel));
    }

    [Test]
    public void MsgChannel_CanBeReplacedAndCleared()
    {
        var first = Mock.Of<INetChannel>();
        var second = Mock.Of<INetChannel>();
        var message = new PlainMessage { MsgChannel = first };

        message.MsgChannel = second;
        var afterReplace = message.MsgChannel;
        message.MsgChannel = null!;

        Assert.Multiple(() =>
        {
            Assert.That(afterReplace, Is.SameAs(second));
            Assert.That(message.MsgChannel, Is.Null);
        });
    }

    [Test]
    public void MsgChannelAndMsgSize_AreIndependentOfNameAndGroup()
    {
        // Assigning the plain properties must not disturb the values that identify and classify the message.
        var message = new GroupMessage(MsgGroups.Command) { MsgChannel = Mock.Of<INetChannel>(), MsgSize = 77 };

        Assert.Multiple(() =>
        {
            Assert.That(message.MsgName, Is.EqualTo("GroupMessage"));
            Assert.That(message.MsgGroup, Is.EqualTo(MsgGroups.Command));
            Assert.That(message.DeliveryMethod, Is.EqualTo(NetDeliveryMethod.ReliableUnordered));
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // Test message types
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Does not override anything except the two abstract methods, so it exposes every default.
    /// </summary>
    [Virtual]
    private class PlainMessage : NetMessage
    {
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    /// <summary>
    ///     Derives from <see cref="PlainMessage"/> to check that the default name uses the most-derived type.
    /// </summary>
    private sealed class FurtherDerivedMessage : PlainMessage
    {
    }

    private sealed class GenericMessage<T> : NetMessage
    {
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    private sealed class NamedMessage : NetMessage
    {
        private readonly string _name;

        public NamedMessage(string name)
        {
            _name = name;
        }

        public override string MsgName => _name;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    /// <summary>
    ///     A message whose group is chosen by the constructor, so one class covers every group value.
    /// </summary>
    private sealed class GroupMessage : NetMessage
    {
        private readonly MsgGroups _group;

        public GroupMessage(MsgGroups group)
        {
            _group = group;
        }

        public override MsgGroups MsgGroup => _group;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    private sealed class MutableGroupMessage : NetMessage
    {
        public MsgGroups Group { get; set; }

        public override MsgGroups MsgGroup => Group;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    /// <summary>
    ///     Overrides only the delivery method; the group stays at its default (Error).
    /// </summary>
    private sealed class FixedDeliveryMessage : NetMessage
    {
        private readonly NetDeliveryMethod _method;

        public FixedDeliveryMessage(NetDeliveryMethod method)
        {
            _method = method;
        }

        public override NetDeliveryMethod DeliveryMethod => _method;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    /// <summary>
    ///     Mimics a message that forces reliable delivery in a special case and otherwise keeps the group default.
    /// </summary>
    private sealed class FallbackDeliveryMessage : NetMessage
    {
        private readonly MsgGroups _group;
        private readonly bool _forceReliable;

        public FallbackDeliveryMessage(MsgGroups group, bool forceReliable)
        {
            _group = group;
            _forceReliable = forceReliable;
        }

        public override MsgGroups MsgGroup => _group;

        public override NetDeliveryMethod DeliveryMethod =>
            _forceReliable ? NetDeliveryMethod.ReliableUnordered : base.DeliveryMethod;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }

    private sealed class SequenceChannelMessage : NetMessage
    {
        private readonly int _channel;

        public SequenceChannelMessage(int channel)
        {
            _channel = channel;
        }

        public override int SequenceChannel => _channel;

        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
        }
    }
}
