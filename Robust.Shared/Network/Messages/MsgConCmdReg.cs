using System;
using System.Collections.Generic;
using Lidgren.Network;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages
{
    /// <summary>
    ///     Exchanges the list of console commands the server makes available.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     This one type serves two roles:
    ///     <list type="bullet">
    ///         <item><description>Request (client to server): the client sends an instance whose
    ///         <see cref="Commands"/> is null, which is serialized as zero commands and means "please send me
    ///         your command list". The server treats any received instance as such a request and ignores its
    ///         content.</description></item>
    ///         <item><description>Response (server to client): the server fills <see cref="Commands"/> with each
    ///         command it offers (name, short description, help text). The client registers a local placeholder
    ///         for each so they can be listed and auto-completed.</description></item>
    ///     </list>
    /// </para>
    /// <para>
    ///     Delivery: belongs to <see cref="MsgGroups.String"/>, so it is sent reliably and ordered on sequence
    ///     channel 0.
    /// </para>
    /// <para>
    ///     Wire layout (after the message id header written by the net manager):
    ///     <list type="number">
    ///         <item><description>Command count: unsigned 16-bit integer, fixed width (2 bytes), little-endian.
    ///         Not a varint. Zero when <see cref="Commands"/> is null; in that case nothing else is
    ///         written.</description></item>
    ///         <item><description>That many entries back to back, each consisting of three Lidgren strings (varint
    ///         UTF-8 byte count followed by the UTF-8 bytes; null or empty is written as a single 0x00 byte and
    ///         read back as the empty string), in this order: <see cref="Command.Name"/>,
    ///         <see cref="Command.Description"/>, <see cref="Command.Help"/>. Note that the description comes
    ///         before the help text.</description></item>
    ///     </list>
    ///     A null list and an empty list both serialize to the two bytes <c>00 00</c>. At most 65535 commands can
    ///     be represented; writing more throws instead of silently wrapping the count. After a read,
    ///     <see cref="Commands"/> is never null.
    /// </para>
    /// </remarks>
    public sealed class MsgConCmdReg : NetMessage
    {
        /// <inheritdoc />
        public override MsgGroups MsgGroup => MsgGroups.String;

        /// <summary>
        ///     The commands being advertised. Null when sending a request; never null after reading.
        /// </summary>
        public List<Command> Commands { get; set; }

        /// <summary>
        ///     Description of a single console command.
        /// </summary>
        public sealed class Command
        {
            /// <summary>
            ///     The name used to invoke the command.
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            ///     Short, one-line description of the command.
            /// </summary>
            public string Description { get; set; }

            /// <summary>
            ///     Longer help or usage text.
            /// </summary>
            public string Help { get; set; }
        }

        /// <inheritdoc />
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var count = (int) buffer.ReadUInt16();
            Commands = new List<Command>(count);

            for (var i = 0; i < count; i++)
            {
                var name = buffer.ReadString();
                var description = buffer.ReadString();
                var help = buffer.ReadString();

                Commands.Add(new Command
                {
                    Name = name,
                    Description = description,
                    Help = help,
                });
            }
        }

        /// <inheritdoc />
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            if (Commands == null)
            {
                buffer.Write((ushort) 0);
                return;
            }

            if (Commands.Count > ushort.MaxValue)
            {
                throw new InvalidOperationException(
                    $"Cannot send {Commands.Count} commands; MsgConCmdReg supports at most {ushort.MaxValue}.");
            }

            buffer.Write((ushort) Commands.Count);

            foreach (var command in Commands)
            {
                buffer.Write(command.Name);
                buffer.Write(command.Description);
                buffer.Write(command.Help);
            }
        }
    }
}
