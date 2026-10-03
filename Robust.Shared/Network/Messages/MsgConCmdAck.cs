using System.IO;
using Lidgren.Network;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    public sealed class MsgConCmdAck : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.String;

        public FormattedMessage Text { get; set; }
        public bool Error { get; set; }

        /// <summary>
        ///     Reads the reply text. Wire format: a variable-length int32 byte count, followed by that many
        ///     bytes holding the serialized <see cref="FormattedMessage"/>. <see cref="Error"/> is not on the wire.
        /// </summary>
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var byteCount = buffer.ReadVariableInt32();
            using var stream = RobustMemoryManager.GetMemoryStream(byteCount);
            buffer.ReadAlignedMemory(stream, byteCount);
            Text = serializer.Deserialize<FormattedMessage>(stream);
        }

        /// <summary>
        ///     Writes the reply text: first the byte count as a variable-length int32, then the serialized
        ///     <see cref="FormattedMessage"/> bytes. <see cref="Error"/> is not written.
        /// </summary>
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            var stream = new MemoryStream();
            serializer.Serialize(stream, Text);
            buffer.WriteVariableInt32((int) stream.Length);
            buffer.Write(stream.AsSpan());
        }
    }
}
