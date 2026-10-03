using Lidgren.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    /// <summary>
    ///     Tells the server which game state tick the client has most recently received and applied.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Sent by the client to the server after it processes a game state. The server uses the acknowledged tick
    ///     to decide what it can delta-compress future states against.
    /// </para>
    /// <para>
    ///     Delivery: belongs to <see cref="MsgGroups.Entity"/>, so it is sent unreliably on sequence channel 0. An
    ///     individual acknowledgement may be lost or arrive out of order.
    /// </para>
    /// <para>
    ///     Wire layout (after the message id header written by the net manager):
    ///     <list type="number">
    ///         <item><description><see cref="Sequence"/>: unsigned 32-bit integer, fixed width (4 bytes),
    ///         little-endian. Not a varint.</description></item>
    ///     </list>
    ///     The payload is always exactly 4 bytes. No validation is performed on the tick value.
    /// </para>
    /// </remarks>
    public sealed class MsgStateAck : NetMessage
    {
        /// <inheritdoc />
        public override MsgGroups MsgGroup => MsgGroups.Entity;

        /// <summary>
        ///     The tick of the game state being acknowledged.
        /// </summary>
        public GameTick Sequence { get; set; }

        /// <inheritdoc />
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            Sequence = new GameTick(buffer.ReadUInt32());
        }

        /// <inheritdoc />
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write(Sequence.Value);
        }
    }
}
