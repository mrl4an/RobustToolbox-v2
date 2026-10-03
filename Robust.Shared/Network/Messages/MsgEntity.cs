using System;
using System.IO;
using Lidgren.Network;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    public sealed class MsgEntity : NetMessage
    {
        public override MsgGroups MsgGroup => MsgGroups.EntityEvent;

        public EntityMessageType Type { get; set; }

        public EntityEventArgs SystemMessage { get; set; }
        public EntityUid EntityUid { get; set; }
        public uint NetId { get; set; }
        public uint Sequence { get; set; }
        public GameTick SourceTick { get; set; }

        /// <summary>
        ///     Decodes the message from the wire. The fixed header (type, tick, sequence) is always present;
        ///     the serialized event payload follows only for <see cref="EntityMessageType.SystemMessage"/>.
        /// </summary>
        /// <remarks>
        ///     No validation is done here. Malformed or truncated input surfaces as an exception from Lidgren,
        ///     the memory manager or the serializer, which the net manager handles by dropping the sender.
        ///     Unknown <see cref="Type"/> values are accepted and simply carry no payload.
        /// </remarks>
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            // Fixed header.
            Type = (EntityMessageType) buffer.ReadByte();
            SourceTick = buffer.ReadGameTick();
            Sequence = buffer.ReadUInt32();

            if (Type != EntityMessageType.SystemMessage)
            {
                // Error and unknown types have no body.
                return;
            }

            // Payload: a variable-length byte count, then that many raw serializer bytes.
            var payloadLength = buffer.ReadVariableInt32();
            using var payload = RobustMemoryManager.GetMemoryStream(payloadLength);
            buffer.ReadAlignedMemory(payload, payloadLength);
            SystemMessage = serializer.Deserialize<EntityEventArgs>(payload);
        }

        /// <summary>
        ///     Encodes the message to the wire. Layout: type (1 byte), source tick (uint32), sequence (uint32),
        ///     and for <see cref="EntityMessageType.SystemMessage"/> a variable-int length followed by the
        ///     serialized event bytes. This method does not modify the message.
        /// </summary>
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            // Fixed header.
            buffer.Write((byte) Type);
            buffer.Write(SourceTick);
            buffer.Write(Sequence);

            if (Type != EntityMessageType.SystemMessage)
            {
                // Error and unknown types have no body.
                return;
            }

            // Serialize the event first so its exact length is known for the prefix.
            using var payload = new MemoryStream();
            serializer.Serialize(payload, SystemMessage);

            buffer.WriteVariableInt32((int) payload.Length);
            buffer.Write(payload.AsSpan());
        }

        public override string ToString()
        {
            var timingData = $"T: {SourceTick} S: {Sequence}";
            switch (Type)
            {
                case EntityMessageType.Error:
                    return "MsgEntity Error";
                case EntityMessageType.SystemMessage:
                    return $"MsgEntity Comp, {timingData}, {SystemMessage}";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
