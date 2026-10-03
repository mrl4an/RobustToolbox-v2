using Lidgren.Network;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    /// <summary>
    ///     Carries a single console command line, in either direction.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Client to server: asks the server to execute the command on behalf of the sending player.
    ///     Server to client: asks the client to execute the command locally.
    ///     The text is the raw command line (command name followed by its arguments).
    /// </para>
    /// <para>
    ///     Delivery: despite the name, this message belongs to <see cref="MsgGroups.Core"/> rather than
    ///     <see cref="MsgGroups.Command"/>, so it is sent reliably but unordered on sequence channel 0. Commands
    ///     sent back to back may therefore be received in a different order.
    /// </para>
    /// <para>
    ///     Wire layout (after the message id header written by the net manager):
    ///     <list type="number">
    ///         <item><description><see cref="Text"/>: Lidgren string, i.e. an unsigned base-128 varint holding the
    ///         UTF-8 byte count, followed by that many UTF-8 bytes. A null or empty string is written as a single
    ///         0x00 byte and is read back as the empty string.</description></item>
    ///     </list>
    ///     No length limit or content validation is applied here.
    /// </para>
    /// </remarks>
    public sealed class MsgConCmd : NetMessage
    {
        /// <inheritdoc />
        public override MsgGroups MsgGroup => MsgGroups.Core;

        /// <summary>
        ///     The raw command line to execute.
        /// </summary>
        public string Text { get; set; }

        /// <inheritdoc />
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            Text = buffer.ReadString();
        }

        /// <inheritdoc />
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write(Text);
        }
    }
}
