using Lidgren.Network;
using Robust.Shared.Serialization;

namespace Robust.Shared.Network.Messages
{
    /// <summary>
    ///     Asks the server to send the current player list.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Sent by the client to the server during connection setup. The server replies with
    ///     <see cref="MsgPlayerList"/>, either immediately or once the client's initial resources have been sent.
    ///     The message carries no data; the server only needs to know which channel it arrived on.
    /// </para>
    /// <para>
    ///     Delivery: belongs to <see cref="MsgGroups.Core"/>, so it is sent reliably but unordered on sequence
    ///     channel 0.
    /// </para>
    /// <para>
    ///     Wire layout: empty. Only the message id header written by the net manager appears on the wire; this
    ///     class neither reads nor writes any payload bytes.
    /// </para>
    /// </remarks>
    public sealed class MsgPlayerListReq : NetMessage
    {
        /// <inheritdoc />
        public override MsgGroups MsgGroup => MsgGroups.Core;

        /// <inheritdoc />
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            // No payload.
        }

        /// <inheritdoc />
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            // No payload.
        }
    }
}
