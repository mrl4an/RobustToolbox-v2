using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Lidgren.Network;
using Robust.Shared.Log;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Robust.Shared.Network
{
    /// <summary>
    ///     Raised on the client when string table data has arrived after the table was (re)initialised.
    /// </summary>
    /// <remarks>
    ///     Never raised on the server. See <see cref="StringTable"/> for the exact triggering rules.
    /// </remarks>
    public delegate void InitCallback();

    /// <summary>
    ///     Raised on the client with the entries that were just received from the server, or that were seeded locally
    ///     by <see cref="StringTable.Reset"/>.
    /// </summary>
    /// <param name="entries">
    ///     The complete entry array of the table message, including entries that turned out to be no-ops
    ///     and entries that were removals (empty name).
    /// </param>
    /// <remarks>
    ///     Never raised on the server. See <see cref="StringTable"/> for the exact triggering rules.
    /// </remarks>
    public delegate void StringTableUpdateCallback(MsgStringTableEntries.Entry[] entries);

    /// <summary>
    ///     The mapping between numeric network message ids and message names.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     Every network message type has a name (<see cref="NetMessage.MsgName"/>, by default the simple CLR type
    ///     name). On the wire an ordinary message is prefixed with a single byte id instead of that name. This table
    ///     is where the id and the name are associated with each other.
    /// </para>
    /// <para>
    ///     Roles: the server owns the table. It hands out an id every time a message type is registered
    ///     (<see cref="AddString"/>) and tells connected clients about it. A client never assigns ids; it only
    ///     merges the (id, name) pairs it receives from the server into its own copy
    ///     and lets the net manager rebuild its dispatch cache through the update callback.
    /// </para>
    /// <para>
    ///     Bootstrapping: the only id fixed by convention is 0, which on both sides always means
    ///     <c>MsgStringTableEntries</c>. That is what allows the very first table message to be understood
    ///     before the client knows any other id.
    /// </para>
    /// <para>
    ///     Handshake ordering: the server sends the whole table to a channel (<see cref="SendFullTable"/>) as the
    ///     first thing after the channel is created, before the serializer and transfer handshakes are awaited.
    ///     The table message is flagged as a handshake message so a client accepts it while its connection is still
    ///     handshaking. Names registered later are broadcast as single-entry updates, but only to channels that have
    ///     completed their handshake.
    /// </para>
    /// <para>
    ///     Thread-safety: none. All state is plain unsynchronised fields and every member is expected to be called
    ///     from the main/network-poll thread. <see cref="Strings"/> exposes the live dictionary.
    /// </para>
    /// <para>
    ///     Several behaviours below look like bugs but are deliberate: they are kept so the observable ids and
    ///     the interaction with older clients and servers stay identical. Do not "fix" them casually.
    ///     <list type="bullet">
    ///         <item><description>On a server the first ordinary message gets id 2, because id 1 is consumed by
    ///         the registration performed inside <see cref="Initialize"/> before <see cref="Reset"/> installs id 0,
    ///         and the id counter is never rewound.</description></item>
    ///         <item><description>The update callback is only raised if the init callback is non-null.</description></item>
    ///         <item><description>After a <see cref="Reset"/> the init callback is raised for every received table
    ///         message, not just the first one.</description></item>
    ///         <item><description><see cref="AddString"/> performs no capacity check even though only ids 0..255
    ///         are usable on the wire; the consumers deal with larger ids.</description></item>
    ///         <item><description>A conflicting id while merging a received batch throws and abandons the rest of
    ///         that batch; entries applied before the failure stay applied.</description></item>
    ///         <item><description>Removals and renames received by a client are not propagated to the net
    ///         manager's dispatch cache.</description></item>
    ///         <item><description>A name added in the window between a channel being sent the full table and
    ///         that channel completing its handshake is not delivered to that channel.</description></item>
    ///     </list>
    /// </para>
    /// </remarks>
    internal sealed class StringTable
    {
        /// <summary>
        ///     The id that always maps to <see cref="BootstrapName"/>.
        /// </summary>
        private const int BootstrapId = 0;

        /// <summary>
        ///     Name stored under <see cref="BootstrapId"/>; identical to the CLR name of the table message.
        /// </summary>
        private const string BootstrapName = "MsgStringTableEntries";

        private readonly INetManager _network;
        private readonly Dictionary<int, string> _strings;

        // Last id handed out by AddString. It only ever moves forward.
        private int _lastId;

        private InitCallback? _initCallback;
        private StringTableUpdateCallback? _updateCallback;
        private bool _initialized;

        /// <summary>
        ///     The live id to name map. This is the real backing dictionary, not a copy.
        /// </summary>
        internal Dictionary<int, string> Strings => _strings;

        /// <summary>
        ///     Logger used for the informational lines written when tables are sent or received.
        ///     Must be assigned by the owner before <see cref="SendFullTable"/> or a table message is received.
        /// </summary>
        public ISawmill Sawmill = default!;

        /// <summary>
        ///     Sentinel id: what <see cref="AddString"/> returns on a client.
        /// </summary>
        public static int InvalidStringId => -1;

        /// <summary>
        ///     Creates an empty table. Id 0 is not present until <see cref="Initialize"/> or <see cref="Reset"/> runs.
        /// </summary>
        /// <param name="network">The net manager that owns this table.</param>
        public StringTable(INetManager network)
        {
            _network = network;
            _strings = new Dictionary<int, string>();
        }

        /// <summary>
        ///     Registers the table message with the net manager and puts the table into its starting state.
        /// </summary>
        /// <param name="callback">Client only: see <see cref="InitCallback"/>.</param>
        /// <param name="updateCallback">
        ///     Client only: see <see cref="StringTableUpdateCallback"/>. Only used if <paramref name="callback"/>
        ///     is also supplied.
        /// </param>
        /// <remarks>
        ///     The registration happens before <see cref="Reset"/>, and <see cref="Reset"/> does not rewind the id
        ///     counter. On a server the registration therefore consumes id 1 and the first ordinary message ends up
        ///     with id 2. This is intentional, see the remarks of the class.
        ///     The message is accepted by clients only (servers get no receive callback) and may be received
        ///     before the connection handshake has completed.
        /// </remarks>
        public void Initialize(InitCallback? callback = null, StringTableUpdateCallback? updateCallback = null)
        {
            DebugTools.Assert(!_initialized, "StringTable has already been initialized.");

            _initCallback = callback;
            _updateCallback = updateCallback;

            _network.RegisterNetMessage<MsgStringTableEntries>(
                HandleTableMessage,
                NetMessageAccept.Client | NetMessageAccept.Handshake);

            Reset();

            _initialized = true;
        }

        /// <summary>
        ///     Clears the table and installs the bootstrap entry (id 0) again.
        /// </summary>
        /// <remarks>
        ///     The id counter is deliberately not rewound, so ids are never recycled across resets.
        ///     The initialised flag is cleared; nothing sets it again until <see cref="Initialize"/>
        ///     runs, which means every table message received afterwards raises the init callback.
        ///     On a client the update callback is invoked with the bootstrap entry, which is how the net manager
        ///     learns that id 0 is the table message. The net manager must clear its own dispatch cache
        ///     before calling this, otherwise the re-seeded entry would be lost.
        /// </remarks>
        public void Reset()
        {
            _strings.Clear();
            _initialized = false;

            if (TryFindStringId(BootstrapName, out _))
                return;

            _strings.Add(BootstrapId, BootstrapName);

            if (_network.IsClient)
            {
                _updateCallback?.Invoke(new[]
                {
                    new MsgStringTableEntries.Entry
                    {
                        Id = BootstrapId,
                        String = BootstrapName
                    }
                });
            }
        }

        /// <summary>
        ///     Assigns an id to a name (server) and announces it to connected clients.
        /// </summary>
        /// <param name="str">The message name. Must not be null.</param>
        /// <returns>
        ///     The id of the name; the existing one if the name is already present.
        ///     <see cref="InvalidStringId"/> on a client, where nothing is changed.
        /// </returns>
        /// <remarks>
        ///     The counter is incremented before use and keeps incrementing until it hits an id that is not
        ///     occupied (for example by <see cref="AddStringFixed"/>). Ids are never reused. There is intentionally
        ///     no limit check here: ids above 255 do not fit the one byte packet header, and the consumers
        ///     are responsible for coping with that.
        /// </remarks>
        public int AddString(string str)
        {
            if (_network.IsClient)
                return InvalidStringId;

            if (TryFindStringId(str, out var existingId))
                return existingId;

            do
            {
                _lastId++;
            } while (_strings.ContainsKey(_lastId));

            _strings.Add(_lastId, str);
            BroadcastEntry(_lastId, str);

            return _lastId;
        }

        /// <summary>
        ///     Assigns a specific id to a name (server only, for special cases).
        /// </summary>
        /// <param name="id">The id to use.</param>
        /// <param name="str">The name.</param>
        /// <exception cref="ArgumentException">
        ///     <paramref name="id"/> is already used by a different name. If the name was previously registered
        ///     under another id, that old mapping has already been removed when this is thrown.
        /// </exception>
        /// <remarks>
        ///     Does nothing on a client, or if the exact (id, name) pair already exists.
        ///     The id counter is not touched; <see cref="AddString"/> skips ids that are taken this way.
        /// </remarks>
        public void AddStringFixed(int id, string str)
        {
            DebugTools.Assert(_network != null);

            if (_network.IsClient)
                return;

            if (TryFindStringId(str, out var existingId))
            {
                if (existingId == id)
                    return;

                _strings.Remove(existingId);
            }

            _strings.Add(id, str);
            BroadcastEntry(id, str);
        }

        /// <summary>
        ///     Looks up the name for an id.
        /// </summary>
        /// <returns>The name, or null if the id is unknown.</returns>
        public string? GetString(int id)
        {
            return _strings.TryGetValue(id, out var str) ? str : null;
        }

        /// <summary>
        ///     Looks up the name for an id.
        /// </summary>
        /// <returns>True if the id is known; <paramref name="str"/> is non-null in that case.</returns>
        public bool TryGetString(int id, [NotNullWhen(true)] out string? str)
        {
            return _strings.TryGetValue(id, out str);
        }

        /// <summary>
        ///     Finds the id of a name, using an ordinal case-sensitive comparison.
        /// </summary>
        /// <param name="str">The name to search for.</param>
        /// <param name="id">
        ///     The id of the first match in enumeration order. If nothing matches this is 0, not
        ///     <see cref="InvalidStringId"/>, so always check the return value.
        /// </param>
        /// <returns>True if the name is in the table.</returns>
        /// <remarks>This is a linear scan; there is no reverse index.</remarks>
        public bool TryFindStringId(string str, out int id)
        {
            foreach (var (key, value) in _strings)
            {
                if (value == str)
                {
                    id = key;
                    return true;
                }
            }

            id = 0;
            return false;
        }

        /// <summary>
        ///     Sends the entire table, as one message, to a single channel. Server only; does nothing on a client.
        /// </summary>
        /// <param name="channel">The channel to send to.</param>
        /// <remarks>
        ///     The message holds every entry, including id 0, and is not split into chunks.
        ///     <see cref="Sawmill"/> must have been assigned.
        /// </remarks>
        public void SendFullTable(INetChannel channel)
        {
            if (_network.IsClient)
                return;

            var message = new MsgStringTableEntries();
            message.Entries = new MsgStringTableEntries.Entry[_strings.Count];

            var index = 0;
            foreach (var (id, name) in _strings)
            {
                message.Entries[index++] = new MsgStringTableEntries.Entry
                {
                    Id = id,
                    String = name
                };
            }

            Sawmill.Info($"Sending string table to {channel.RemoteEndPoint.Address}.");

            _network.ServerSendMessage(message, channel);
        }

        /// <summary>
        ///     Sends a single new entry to every connected client that finished its handshake.
        /// </summary>
        /// <remarks>
        ///     Registrations done before the network peer exists produce no traffic.
        ///     Channels that are still handshaking are skipped by the net manager; they get the full table
        ///     instead.
        /// </remarks>
        private void BroadcastEntry(int id, string name)
        {
            if (_network.IsClient)
                return;

            if (!_network.IsRunning)
                return;

            var message = new MsgStringTableEntries();
            message.Entries = new[]
            {
                new MsgStringTableEntries.Entry
                {
                    Id = id,
                    String = name
                }
            };

            _network.ServerSendToAll(message);
        }

        /// <summary>
        ///     Client side receive handler for the table message. The table is merged, not replaced.
        /// </summary>
        /// <remarks>
        ///     An empty or null name removes the entry with that id. A name that already exists under another
        ///     id is moved to the new id. A conflicting id throws <see cref="ArgumentException"/>, which
        ///     abandons the rest of the batch and skips both callbacks. There is no validation of the id range.
        /// </remarks>
        private void HandleTableMessage(MsgStringTableEntries message)
        {
            DebugTools.Assert(_network.IsClient);

            Sawmill.Info("Received string table update from server.");

            foreach (var entry in message.Entries)
            {
                if (string.IsNullOrEmpty(entry.String))
                {
                    _strings.Remove(entry.Id);
                    continue;
                }

                if (TryFindStringId(entry.String, out var existingId))
                {
                    if (existingId == entry.Id)
                        continue;

                    _strings.Remove(existingId);
                }

                _strings.Add(entry.Id, entry.String);
            }

            // The update callback is intentionally gated on the init callback existing.
            if (_initCallback == null)
                return;

            if (_network.IsClient && !_initialized)
                _initCallback();

            _updateCallback?.Invoke(message.Entries);
        }
    }

#nullable disable

    /// <summary>
    ///     Carries string table entries (id and message name pairs) from the server to a client.
    /// </summary>
    /// <remarks>
    /// <para>
    ///     This message always has id 0 on both sides; see <see cref="StringTable"/>. It is group
    ///     <see cref="MsgGroups.String"/> (reliable, ordered), uses sequence channel 0, and is registered so that only
    ///     clients process it and so that it may arrive during the handshake. A server that receives it has no
    ///     handler and disconnects the sender.
    /// </para>
    /// <para>
    ///     An entry with a null or empty name tells the client to remove that id.
    /// </para>
    /// <para>
    ///     Wire layout, after the one byte message id (0) written by the net manager:
    ///     <list type="table">
    ///         <listheader>
    ///             <term>Field</term>
    ///             <description>Encoding</description>
    ///         </listheader>
    ///         <item>
    ///             <term>Entry count N</term>
    ///             <description>Fixed 4 bytes, little-endian. Written as signed, read as unsigned.</description>
    ///         </item>
    ///         <item>
    ///             <term>Entry id (repeated N times)</term>
    ///             <description>Lidgren variable-length signed int: zigzag, then base-128 varint, 1 to 5 bytes.
    ///             Small non-negative ids below 64 take one byte (twice the value).</description>
    ///         </item>
    ///         <item>
    ///             <term>Entry name (repeated N times, directly after its id)</term>
    ///             <description>Lidgren string: base-128 varint UTF-8 byte length, followed by the UTF-8 bytes.
    ///             Null and empty are both a single 0x00 byte.</description>
    ///         </item>
    ///     </list>
    ///     There are no other fields and no padding. Example for the single entry (0, "MsgStringTableEntries"):
    ///     <c>00 | 01 00 00 00 | 00 | 15 "MsgStringTableEntries"</c>.
    /// </para>
    /// <para>
    ///     Reading allocates the entry array from the declared count before reading any entry, so a hostile count
    ///     fails with an exception, which the net manager treats as a deserialization failure and
    ///     disconnects the peer. No ids are validated here.
    /// </para>
    /// </remarks>
    public sealed class MsgStringTableEntries : NetMessage
    {
        /// <inheritdoc />
        public override MsgGroups MsgGroup => MsgGroups.String;

        /// <summary>
        ///     The entries carried by this message. Null until assigned or read.
        /// </summary>
        public Entry[] Entries { get; set; }

        /// <inheritdoc />
        public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
        {
            var count = buffer.ReadUInt32();
            Entries = new Entry[count];

            for (var i = 0; i < Entries.Length; i++)
            {
                var id = buffer.ReadVariableInt32();
                var str = buffer.ReadString();

                Entries[i] = new Entry
                {
                    Id = id,
                    String = str
                };
            }
        }

        /// <inheritdoc />
        public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
        {
            if (Entries == null)
                throw new InvalidOperationException("Entries is null.");

            buffer.Write(Entries.Length);

            foreach (var entry in Entries)
            {
                buffer.WriteVariableInt32(entry.Id);
                buffer.Write(entry.String);
            }
        }

        /// <summary>
        ///     A single (id, name) pair of the string table.
        /// </summary>
        public struct Entry
        {
            /// <summary>
            ///     The message name. Null or empty means "no name": a receiving client removes the id.
            ///     On the wire null and empty are the same, and a null is never recovered when reading.
            /// </summary>
            public string String { get; set; }

            /// <summary>
            ///     The id the server assigned to the name (0 for this message itself).
            /// </summary>
            public int Id { get; set; }
        }
    }
}
