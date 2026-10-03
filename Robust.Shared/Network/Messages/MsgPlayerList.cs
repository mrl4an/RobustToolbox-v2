using System;
using System.Collections.Generic;
using Lidgren.Network;
using Robust.Shared.Enums;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    /// <summary>
    ///     A snapshot of all player sessions known to the server.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Sent by the server to a client in answer to <see cref="MsgPlayerListReq"/>. For each session it lists
    ///     the user id, display name and session status; the client applies the list to its local session list.
    ///     Ping and controlled entity are not transmitted and keep their default values after a read.
    /// </para>
    /// <para>
    ///     Delivery: belongs to <see cref="MsgGroups.Core"/>, so it is sent reliably but unordered on sequence
    ///     channel 0.
    /// </para>
    /// <para>
    ///     Wire layout (after the message id header written by the net manager):
    ///     <list type="number">
    ///         <item><description>Entry count: signed 32-bit integer, fixed width (4 bytes), little-endian. Not a
    ///         varint.</description></item>
    ///         <item><description>That many entries back to back, each consisting of:
    ///             <list type="number">
    ///                 <item><description>User id: 16 raw bytes, the standard .NET <see cref="Guid"/> byte-array
    ///                 layout, with no length prefix.</description></item>
    ///                 <item><description>Name: Lidgren string (varint UTF-8 byte count followed by the UTF-8
    ///                 bytes). Null or empty is written as a single 0x00 byte and read back as the empty
    ///                 string.</description></item>
    ///                 <item><description>Status: a single byte holding the <see cref="SessionStatus"/>
    ///                 value. It is not range checked on read.</description></item>
    ///             </list>
    ///         </description></item>
    ///     </list>
    ///     Entries are written and read in list order. Reading throws on a negative count or on truncated data.
    ///     Writing requires <see cref="Plyrs"/> to be non-null and to contain no null entries.
    /// </para>
    /// </remarks>
    public sealed class MsgPlayerList : NetMessage
    {
        /// <summary>
        ///     Upper bound for the capacity pre-allocated when reading, so a bogus count in a malformed packet
        ///     cannot trigger a huge allocation before the buffer runs dry.
        /// </summary>
        private const int MaxPreallocatedEntries = 1024;

        /// <inheritdoc />
        public override MsgGroups MsgGroup => MsgGroups.Core;

        /// <summary>
        ///     The per-player entries. Only user id, name and status are transmitted.
        /// </summary>
        public List<SessionState> Plyrs { get; set; }

        /// <inheritdoc />
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var count = buffer.ReadInt32();
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Negative player count in MsgPlayerList.");

            Plyrs = new List<SessionState>(Math.Min(count, MaxPreallocatedEntries));

            for (var i = 0; i < count; i++)
            {
                var userId = new NetUserId(buffer.ReadGuid());
                var name = buffer.ReadString();
                var status = (SessionStatus) buffer.ReadByte();

                Plyrs.Add(new SessionState
                {
                    UserId = userId,
                    Name = name,
                    Status = status,
                });
            }
        }

        /// <inheritdoc />
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            buffer.Write(Plyrs.Count);

            foreach (var player in Plyrs)
            {
                buffer.Write(player.UserId.UserId);
                buffer.Write(player.Name);
                buffer.Write((byte) player.Status);
            }
        }
    }
}
