using System;
using Lidgren.Network;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network;

/// <summary>
///     Broad classification of a <see cref="NetMessage"/>. The group is never sent over the wire. Its only
///     functional use is selecting the default <see cref="NetMessage.DeliveryMethod"/>; beyond that it is a
///     label for statistics and for documenting what a message is for.
/// </summary>
/// <remarks>
///     <see cref="Error"/> deliberately means "no group chosen". Every concrete message type must override
///     <see cref="NetMessage.MsgGroup"/> with one of the other values (or override
///     <see cref="NetMessage.DeliveryMethod"/>), otherwise the first attempt to send it fails.
/// </remarks>
public enum MsgGroups : byte
{
    /// <summary>
    ///     The group was never chosen. This is the default of <see cref="NetMessage.MsgGroup"/>, and messages that
    ///     keep it cannot be sent using the default delivery mapping.
    /// </summary>
    Error = 0,

    /// <summary>
    ///     Core engine traffic and general-purpose messages that must arrive, but where relative order between
    ///     messages does not matter.
    /// </summary>
    Core,

    /// <summary>
    ///     Frequent entity and game-state traffic. Loss is tolerable because newer data supersedes older data,
    ///     so these are sent unreliably by default.
    /// </summary>
    Entity,

    /// <summary>
    ///     String table traffic, which must arrive and must keep its order.
    /// </summary>
    String,

    /// <summary>
    ///     Console commands and similar requests that must arrive but need no ordering.
    /// </summary>
    Command,

    /// <summary>
    ///     Networked entity events, which must arrive in the order they were sent.
    /// </summary>
    EntityEvent,
}

/// <summary>
///     Base class of every message that is exchanged between client and server through
///     <see cref="INetManager"/>.
/// </summary>
/// <remarks>
///     <para>
///         A message type is a class that derives from <see cref="NetMessage"/>, has a public parameterless
///         constructor (required by the <c>new()</c> constraint of
///         <see cref="INetManager.RegisterNetMessage{T}"/>), overrides <see cref="MsgGroup"/> and implements
///         <see cref="ReadFromBuffer"/> and <see cref="WriteToBuffer"/>. The class is registered once per
///         net manager; after that instances are created with <c>new</c>, filled in and passed to one of the
///         send methods of the net manager.
///     </para>
///     <para>
///         Wire format: a packet consists of one byte holding the message type id, followed by whatever
///         <see cref="WriteToBuffer"/> appended. The id is resolved from <see cref="MsgName"/> by the net
///         manager and is handled entirely outside this class. Since the id has a single byte, at most 255
///         message types can be registered per manager, including the engine's own.
///     </para>
///     <para>
///         Default delivery method by <see cref="MsgGroup"/>, as returned by <see cref="DeliveryMethod"/>:
///     </para>
///     <list type="table">
///         <listheader>
///             <term>Group</term>
///             <description>Default delivery method</description>
///         </listheader>
///         <item>
///             <term><see cref="MsgGroups.Entity"/></term>
///             <description><see cref="NetDeliveryMethod.Unreliable"/></description>
///         </item>
///         <item>
///             <term><see cref="MsgGroups.Core"/></term>
///             <description><see cref="NetDeliveryMethod.ReliableUnordered"/></description>
///         </item>
///         <item>
///             <term><see cref="MsgGroups.Command"/></term>
///             <description><see cref="NetDeliveryMethod.ReliableUnordered"/></description>
///         </item>
///         <item>
///             <term><see cref="MsgGroups.String"/></term>
///             <description><see cref="NetDeliveryMethod.ReliableOrdered"/></description>
///         </item>
///         <item>
///             <term><see cref="MsgGroups.EntityEvent"/></term>
///             <description><see cref="NetDeliveryMethod.ReliableOrdered"/></description>
///         </item>
///         <item>
///             <term><see cref="MsgGroups.Error"/> or any undefined value</term>
///             <description>throws <see cref="ArgumentOutOfRangeException"/></description>
///         </item>
///     </list>
///     <para>
///         Instances are plain mutable objects without any locking: build and fill a message on one thread and
///         then hand it to a send call. Receive callbacks run on the main thread.
///     </para>
/// </remarks>
public abstract class NetMessage
{
    // Default value for MsgName, captured once at construction so that reading the name stays cheap.
    private readonly string _defaultMsgName;

    /// <summary>
    ///     Initializes the message and records the simple name of its runtime type (the most-derived class,
    ///     without namespace) as the default <see cref="MsgName"/>. For a generic type this is the runtime's
    ///     simple name, including the arity suffix.
    /// </summary>
    protected NetMessage()
    {
        _defaultMsgName = GetType().Name;
    }

    /// <summary>
    ///     The stable string identifier of this message type. It is the key under which the type is
    ///     registered with the net manager, and the name that the server sends to clients in the connection-time
    ///     string table that maps names to one-byte wire ids. Both sides of a connection must therefore compute
    ///     the same name for the same message type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         By default this is the simple name of the most-derived class. The namespace is not included, so
    ///         two message types with the same class name in different namespaces collide: registering the
    ///         second one with the same net manager throws.
    ///     </para>
    ///     <para>
    ///         A derived class may override this to return a fixed string, which then replaces the default
    ///         entirely. The engine's handshake messages do this (returning an empty string) because they are
    ///         read and written directly during login and never go through normal registration.
    ///     </para>
    ///     <para>
    ///         The net manager reads this on a freshly constructed instance during registration, on every send
    ///         to look up the wire id, and when dispatching a locally injected message. It must be cheap,
    ///         deterministic and must not depend on state that is assigned after construction.
    ///     </para>
    /// </remarks>
    public virtual string MsgName => _defaultMsgName;

    /// <summary>
    ///     The classification of this message. It is never written to the wire; the default
    ///     <see cref="DeliveryMethod"/> is computed from it.
    /// </summary>
    /// <remarks>
    ///     The default is <see cref="MsgGroups.Error"/>, which the base constructor does not change. Every
    ///     concrete message type is expected to override this property with a real group. A message that
    ///     does not will serialize successfully, but then fails with an <see cref="ArgumentOutOfRangeException"/>
    ///     when the send path reads <see cref="DeliveryMethod"/>. Registration is not affected, as it only
    ///     needs <see cref="MsgName"/>.
    /// </remarks>
    public virtual MsgGroups MsgGroup => default;

    /// <summary>
    ///     The channel this message was received on. The net manager assigns it on every freshly created
    ///     incoming instance before <see cref="ReadFromBuffer"/> runs, so it is valid inside
    ///     <see cref="ReadFromBuffer"/> and in the receive callback. Handlers use it to find out who sent the
    ///     message and to answer on the same connection.
    /// </summary>
    /// <remarks>
    ///     For messages that are created locally in order to be sent, this stays <see langword="null"/> and the
    ///     send methods ignore it, because the destination is always passed to the send call explicitly. The
    ///     same holds for messages dispatched locally on the client unless the caller assigns a channel.
    ///     The setter is public so that callers and tests can assign it.
    /// </remarks>
    public INetChannel MsgChannel { get; set; } = default!;

    /// <summary>
    ///     Size in bytes of the packet this message was read from or written to. This is purely
    ///     informational and defaults to 0.
    /// </summary>
    /// <remarks>
    ///     The net manager never sets it. Message types that want to report it assign it themselves, typically
    ///     in <see cref="ReadFromBuffer"/> (and, for messages whose delivery decision depends on size, in
    ///     <see cref="WriteToBuffer"/>) from the length of the buffer they were handed. A message that never
    ///     assigns it reports 0.
    /// </remarks>
    public int MsgSize { get; set; }

    /// <summary>
    ///     Reads the payload of this message from <paramref name="buffer"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The net manager calls this exactly once per received data packet, on the thread that pumps
    ///         incoming packets (the main thread, inside the packet-processing call), on a new instance made
    ///         through the public parameterless constructor. At that point the packet has been decrypted, the
    ///         leading type id byte has been consumed and <see cref="MsgChannel"/> has been set, so the buffer
    ///         is positioned at the first payload byte.
    ///     </para>
    ///     <para>
    ///         Implementations must read exactly the data, in the same order and encoding, that
    ///         <see cref="WriteToBuffer"/> wrote. Bytes left unread are not detected.
    ///     </para>
    ///     <para>
    ///         Any exception thrown is treated as a corrupt or hostile packet: the sending channel is
    ///         disconnected with the reason "Failed to deserialize packet." and the message is dropped without
    ///         calling its handler. Implementations should therefore throw on malformed input instead of
    ///         accepting it, and must validate every length read from the wire before allocating, because on a
    ///         server this code processes untrusted client data.
    ///     </para>
    /// </remarks>
    /// <param name="buffer">The incoming packet, positioned at the start of the payload.</param>
    /// <param name="serializer">
    ///     The engine's general serializer, for messages that embed serialized objects.
    /// </param>
    public abstract void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer);

    /// <summary>
    ///     Appends the payload of this message to <paramref name="buffer"/>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The net manager calls this synchronously inside the send call, on the sender's thread, once for
    ///         every recipient. The buffer is a new outgoing packet whose first byte (the message type id) has
    ///         already been written; implementations append only their payload. Apart from optional encryption
    ///         of the whole packet, nothing is added afterwards.
    ///     </para>
    ///     <para>
    ///         A broadcast to N channels calls this N times on the same instance, so the implementation must be
    ///         repeatable and must not consume or alter state in a way that would change a later write.
    ///     </para>
    ///     <para>
    ///         Exceptions propagate to the caller of the send method and nothing is sent. All payload data has
    ///         been copied into the packet by the time the send call returns, so the message may be reused or
    ///         modified afterwards.
    ///     </para>
    /// </remarks>
    /// <param name="buffer">The outgoing packet, with the type id byte already written.</param>
    /// <param name="serializer">
    ///     The engine's general serializer, for messages that embed serialized objects.
    /// </param>
    public abstract void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer);

    /// <summary>
    ///     The Lidgren delivery guarantee used when this message is sent. The net manager reads it once per
    ///     send, after <see cref="WriteToBuffer"/> has run.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The default is a pure function of <see cref="MsgGroup"/>; see the table in the remarks of
    ///         <see cref="NetMessage"/>. <see cref="MsgGroups.Error"/> and any undefined value cause an
    ///         <see cref="ArgumentOutOfRangeException"/> without a parameter name, which surfaces from the send
    ///         call after the message has been serialized.
    ///     </para>
    ///     <para>
    ///         Derived types may override this, and may call <c>base.DeliveryMethod</c> to fall back to the
    ///         group default (for example to switch to reliable delivery only for large messages). Be aware that
    ///         the send path treats <see cref="NetDeliveryMethod.ReliableOrdered"/>,
    ///         <see cref="NetDeliveryMethod.ReliableSequenced"/> and
    ///         <see cref="NetDeliveryMethod.UnreliableSequenced"/> as ordered and encrypts and sends them in
    ///         order per channel, so choosing one of them has consequences beyond the transport guarantee.
    ///     </para>
    ///     <para>
    ///         This may be read from any sending thread and therefore must not have side effects.
    ///     </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The default implementation was used with <see cref="MsgGroups.Error"/> or an undefined group.
    /// </exception>
    public virtual NetDeliveryMethod DeliveryMethod
    {
        get
        {
            switch (MsgGroup)
            {
                case MsgGroups.Entity:
                    return NetDeliveryMethod.Unreliable;

                case MsgGroups.Core:
                case MsgGroups.Command:
                    return NetDeliveryMethod.ReliableUnordered;

                case MsgGroups.String:
                case MsgGroups.EntityEvent:
                    return NetDeliveryMethod.ReliableOrdered;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    /// <summary>
    ///     The Lidgren sequence channel on which this message is sent. It only matters for the ordered and
    ///     sequenced delivery methods, where independent streams use different channel numbers. The net
    ///     manager reads it once per send.
    /// </summary>
    /// <remarks>
    ///     The default is 0 for every message. Channels 16 and above are reserved for the engine's internal use
    ///     (the file transfer messages use 16), so content should only use 0 through 15. Lidgren itself accepts
    ///     channel indexes 0 through 31 per delivery method.
    /// </remarks>
    public virtual int SequenceChannel => 0;
}
