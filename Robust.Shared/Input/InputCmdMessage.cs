using System;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

namespace Robust.Shared.Input
{
    /// <summary>
    ///     Base class of all messages that describe "the player did something with a key function".
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Messages are created on the client when a bound key function changes state or is triggered,
    ///         serialized and sent to the server by the entity networking layer, and consumed on the server by the
    ///         server input system and by any content system that subscribes to the corresponding network event.
    ///         Of the network-capable subclasses only <see cref="FullInputCmdMessage"/> is currently sent by the engine.
    ///     </para>
    ///     <para>
    ///         Serialization caveat: the network serializer orders the fields declared on each type by name. The
    ///         properties declared here are auto-properties on purpose. Do not rename them, change their types, or add
    ///         extra instance fields to any message type in this file, since that alters the wire format.
    ///     </para>
    /// </remarks>
    [Serializable, NetSerializable]
    public abstract class InputCmdMessage : EntityEventArgs, IComparable<InputCmdMessage>
    {
        /// <summary>
        ///     The simulation tick of the sending client at the moment the command was created.
        /// </summary>
        public GameTick Tick { get; }

        /// <summary>
        ///     How far into <see cref="Tick"/> the command was generated. Zero is the start of the tick and larger
        ///     values are later within the same tick.
        /// </summary>
        public ushort SubTick { get; }

        /// <summary>
        ///     The network id of the <see cref="BoundKeyFunction"/> this command refers to. This is an index into the
        ///     list of all key functions that client and server agree on (see <see cref="BoundKeyMap"/>).
        /// </summary>
        public KeyFunctionId InputFunctionId { get; }

        /// <summary>
        ///     Position of this command in the sending client's stream of input commands.
        /// </summary>
        /// <remarks>
        ///     Zero (the default) means the command is unsequenced. The client assigns increasing values just before
        ///     sending, and only while client-side prediction is enabled. The server reports the highest value it has
        ///     processed back to the client so the client can discard inputs that were already acknowledged.
        ///     This is the only member that can change after construction, and no validation is performed.
        /// </remarks>
        public uint InputSequence { get; set; }

        /// <summary>
        ///     Creates a message with the given timing and function. <see cref="InputSequence"/> starts at zero.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        public InputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId)
        {
            Tick = tick;
            SubTick = subTick;
            InputFunctionId = inputFunctionId;
        }

        /// <summary>
        ///     Orders messages by <see cref="InputSequence"/> alone.
        /// </summary>
        /// <param name="other">The message to compare against, or null.</param>
        /// <returns>
        ///     A positive value if <paramref name="other"/> is null, zero if it is the same instance as this message,
        ///     and otherwise the result of an unsigned comparison of the two sequence numbers.
        /// </returns>
        /// <remarks>
        ///     <see cref="Tick"/>, <see cref="SubTick"/>, <see cref="InputFunctionId"/> and the runtime type do not
        ///     take part in the comparison, so two different messages with the same sequence number compare as equal.
        ///     This ordering is therefore not consistent with equality, which remains reference equality.
        ///     Unsequenced messages (sequence 0) sort before all sequenced ones.
        /// </remarks>
        public int CompareTo(InputCmdMessage? other)
        {
            if (other is null)
                return 1;

            if (ReferenceEquals(this, other))
                return 0;

            return InputSequence.CompareTo(other.InputSequence);
        }

        /// <summary>
        ///     Produces a one-line description of the message for logs.
        /// </summary>
        public override string ToString()
        {
            return $"tick={Tick}, subTick={SubTick}, seq={InputSequence} func={InputFunctionId}";
        }
    }

    /// <summary>
    ///     A command for a key function that has a persistent pressed/released state, carrying no pointer data.
    /// </summary>
    /// <remarks>
    ///     Not produced or consumed by the engine's current input pipeline; it is kept as part of the public API.
    ///     The state is stored as received, so a receiver must not assume it holds a defined enum value.
    /// </remarks>
    [Serializable, NetSerializable]
    public sealed class StateInputCmdMessage : InputCmdMessage
    {
        /// <summary>
        ///     The new state of the key function.
        /// </summary>
        public BoundKeyState State { get; }

        /// <summary>
        ///     Creates a state command.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        /// <param name="state">The new state of the function.</param>
        public StateInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId, BoundKeyState state)
            : base(tick, subTick, inputFunctionId)
        {
            State = state;
        }
    }

    /// <summary>
    ///     A one-shot command for a key function: it has no pressed/released state and carries no pointer data.
    /// </summary>
    /// <remarks>
    ///     Deliberately not sealed; <see cref="PointerInputCmdMessage"/> and content code derive from it.
    ///     Not produced or consumed by the engine's current input pipeline; it is kept as part of the public API.
    /// </remarks>
    [Serializable, NetSerializable, Virtual]
    public class EventInputCmdMessage : InputCmdMessage
    {
        /// <summary>
        ///     Creates an event command.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        public EventInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId)
            : base(tick, subTick, inputFunctionId)
        {
        }
    }

    /// <summary>
    ///     A one-shot command that also reports where the pointer was.
    /// </summary>
    /// <remarks>
    ///     Not produced or consumed by the engine's current input pipeline; it is kept as part of the public API.
    /// </remarks>
    [Serializable, NetSerializable]
    public sealed class PointerInputCmdMessage : EventInputCmdMessage
    {
        /// <summary>
        ///     The pointer position, in network-safe entity-relative coordinates, when the command was created.
        /// </summary>
        public NetCoordinates Coordinates { get; }

        /// <summary>
        ///     The network id of the entity under the pointer, or <see cref="NetEntity.Invalid"/> if there was none.
        /// </summary>
        public NetEntity Uid { get; }

        /// <summary>
        ///     Creates a pointer command with no entity under the pointer.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        /// <param name="coordinates">The pointer position.</param>
        public PointerInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId,
            NetCoordinates coordinates)
            : this(tick, subTick, inputFunctionId, coordinates, NetEntity.Invalid)
        {
        }

        /// <summary>
        ///     Creates a pointer command.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        /// <param name="coordinates">The pointer position.</param>
        /// <param name="uid">The network id of the entity under the pointer, or <see cref="NetEntity.Invalid"/>.</param>
        public PointerInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId,
            NetCoordinates coordinates, NetEntity uid)
            : base(tick, subTick, inputFunctionId)
        {
            Coordinates = coordinates;
            Uid = uid;
        }
    }

    /// <summary>
    ///     The client-local form of a full input command.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It uses local <see cref="EntityUid"/> and <see cref="EntityCoordinates"/> values so the client can
    ///         interact with client-only entities that have no network id. It is converted to a
    ///         <see cref="FullInputCmdMessage"/> when a command has to be sent to the server.
    ///     </para>
    ///     <para>
    ///         This type must never be marked as net-serializable: local ids are not valid on the wire, and adding
    ///         the attribute would change the serializer's type list.
    ///     </para>
    ///     <para>
    ///         The additional properties are init-only and can be set either through the long constructor or with an
    ///         object initializer after the short one.
    ///     </para>
    /// </remarks>
    public sealed class ClientFullInputCmdMessage : InputCmdMessage, IFullInputCmdMessage
    {
        /// <summary>
        ///     The new state of the key function.
        /// </summary>
        public BoundKeyState State { get; init; }

        /// <summary>
        ///     The pointer position in local entity coordinates.
        /// </summary>
        public EntityCoordinates Coordinates { get; init; }

        /// <summary>
        ///     The pointer position on screen, including the window or viewport it belongs to.
        /// </summary>
        public ScreenCoordinates ScreenCoordinates { get; init; }

        /// <summary>
        ///     The local entity under the pointer, or the default (invalid) uid if there was none.
        /// </summary>
        public EntityUid Uid { get; init; }

        /// <summary>
        ///     Creates a command whose state, coordinates and uid are left at their defaults.
        ///     Use an object initializer to set them.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        public ClientFullInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId)
            : base(tick, subTick, inputFunctionId)
        {
        }

        /// <summary>
        ///     Creates a fully populated command. Note that the parameter order differs from
        ///     <see cref="FullInputCmdMessage"/>: the state follows the coordinates.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        /// <param name="coordinates">The pointer position in local entity coordinates.</param>
        /// <param name="screenCoordinates">The pointer position on screen.</param>
        /// <param name="state">The new state of the function.</param>
        /// <param name="uid">The local entity under the pointer, or the default uid.</param>
        public ClientFullInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId,
            EntityCoordinates coordinates, ScreenCoordinates screenCoordinates, BoundKeyState state, EntityUid uid)
            : base(tick, subTick, inputFunctionId)
        {
            State = state;
            Coordinates = coordinates;
            ScreenCoordinates = screenCoordinates;
            Uid = uid;
        }
    }

    /// <summary>
    ///     Read-only view shared by <see cref="ClientFullInputCmdMessage"/> and <see cref="FullInputCmdMessage"/>,
    ///     so that input handlers can be written once for both.
    /// </summary>
    /// <remarks>
    ///     Pointer data is intentionally absent because the two implementations use different (local versus network)
    ///     types for it; handlers that need it pattern-match on the concrete type.
    /// </remarks>
    [NotContentImplementable]
    public interface IFullInputCmdMessage
    {
        /// <summary>
        ///     The client tick at which the command was created.
        /// </summary>
        GameTick Tick { get; }

        /// <summary>
        ///     The new state of the key function.
        /// </summary>
        BoundKeyState State { get; }

        /// <summary>
        ///     The network id of the key function.
        /// </summary>
        KeyFunctionId InputFunctionId { get; }

        /// <summary>
        ///     The offset within <see cref="Tick"/> at which the command was created.
        /// </summary>
        ushort SubTick { get; }

        /// <summary>
        ///     The command's position in the client's input stream; zero if unsequenced.
        /// </summary>
        uint InputSequence { get; set; }
    }

    /// <summary>
    ///     The input command that is actually sent from client to server: the new key state plus pointer data.
    /// </summary>
    /// <remarks>
    ///     It is raised on the server as a network entity event. The server input system and other content systems
    ///     subscribe to it. Do not rename or retype its properties or add stored fields; see
    ///     <see cref="InputCmdMessage"/> for the serialization caveat.
    /// </remarks>
    [Serializable, NetSerializable]
    public sealed class FullInputCmdMessage : InputCmdMessage, IFullInputCmdMessage
    {
        /// <summary>
        ///     The new state of the key function.
        /// </summary>
        public BoundKeyState State { get; }

        /// <summary>
        ///     The pointer position in network-safe entity coordinates.
        /// </summary>
        public NetCoordinates Coordinates { get; }

        /// <summary>
        ///     The pointer position in screen space.
        /// </summary>
        public ScreenCoordinates ScreenCoordinates { get; }

        /// <summary>
        ///     The network id of the entity under the pointer, or <see cref="NetEntity.Invalid"/> if there was none.
        /// </summary>
        public NetEntity Uid { get; init; }

        /// <summary>
        ///     Creates a command with no entity under the pointer.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputSequence">
        ///     Accepted for source compatibility but ignored: it is not stored, and
        ///     <see cref="InputCmdMessage.InputSequence"/> stays 0. The sequence is assigned later through the property.
        /// </param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        /// <param name="state">The new state of the function.</param>
        /// <param name="coordinates">The pointer position in network-safe coordinates.</param>
        /// <param name="screenCoordinates">The pointer position on screen.</param>
        public FullInputCmdMessage(GameTick tick, ushort subTick, int inputSequence, KeyFunctionId inputFunctionId,
            BoundKeyState state, NetCoordinates coordinates, ScreenCoordinates screenCoordinates)
            : this(tick, subTick, inputFunctionId, state, coordinates, screenCoordinates, NetEntity.Invalid)
        {
        }

        /// <summary>
        ///     Creates a command.
        /// </summary>
        /// <param name="tick">The client tick at which the command was created.</param>
        /// <param name="subTick">The offset within that tick.</param>
        /// <param name="inputFunctionId">The network id of the key function.</param>
        /// <param name="state">The new state of the function.</param>
        /// <param name="coordinates">The pointer position in network-safe coordinates.</param>
        /// <param name="screenCoordinates">The pointer position on screen.</param>
        /// <param name="uid">The network id of the entity under the pointer, or <see cref="NetEntity.Invalid"/>.</param>
        public FullInputCmdMessage(GameTick tick, ushort subTick, KeyFunctionId inputFunctionId,
            BoundKeyState state, NetCoordinates coordinates, ScreenCoordinates screenCoordinates, NetEntity uid)
            : base(tick, subTick, inputFunctionId)
        {
            State = state;
            Coordinates = coordinates;
            ScreenCoordinates = screenCoordinates;
            Uid = uid;
        }
    }
}
