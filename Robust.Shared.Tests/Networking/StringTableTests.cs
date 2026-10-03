using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Entry = Robust.Shared.Network.MsgStringTableEntries.Entry;

namespace Robust.Shared.Tests.Networking;

/// <summary>
///     Behavioural tests for <see cref="StringTable"/>, the id to name map that lets every network packet start
///     with a one byte message id instead of the message name.
/// </summary>
/// <remarks>
///     <para>
///         Several expectations below look odd on purpose. The table has a number of quirks (the server skipping
///         id 1, the client's update callback being gated on the init callback, ...) that are kept so that ids and
///         client behaviour stay identical between engine builds. Those tests have "Quirk" in their name and a
///         comment that explains why the behaviour is pinned. If one of them fails after an intentional change,
///         update it deliberately and think about older peers first.
///     </para>
///     <para>
///         The table is driven through <see cref="FakeNetManager"/>, a minimal hand written
///         <see cref="INetManager"/>. The real net manager needs sockets and a lot of configuration, none of which
///         matters here. The fake implements only what the table calls; everything else throws
///         <see cref="NotImplementedException"/> so that a new dependency of the table on the net manager shows up
///         as a loud test failure instead of being silently ignored.
///     </para>
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(StringTable))]
internal sealed class StringTableTests
{
    // The name that is permanently bound to id 0 on both sides. It equals the CLR type name of the table message.
    private const string TableMessageName = "MsgStringTableEntries";

    // A sawmill that has no handlers, so everything the table logs is discarded. The table only needs a non-null
    // sawmill for SendFullTable and for receiving.
    private static readonly ISawmill TestSawmill = new LogManager().GetSawmill("StringTableTests");

    // ------------------------------------------------------------------------------------------------------------
    // Test infrastructure
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     A minimal <see cref="INetManager"/> that records what the <see cref="StringTable"/> asks of it.
    /// </summary>
    /// <remarks>
    ///     <see cref="INetManager"/> is marked <see cref="Robust.Shared.Analyzers.NotContentImplementableAttribute"/>
    ///     because the engine may add members at any time. That is acceptable here: this is an engine test and
    ///     adding a member to the interface is expected to break this fake at compile time.
    /// </remarks>
    private sealed class FakeNetManager : INetManager
    {
        /// <summary>
        ///     The table that owns this net manager. The real net manager asks the string table for an id whenever a
        ///     message type is registered, and the server-side id numbering quirk only shows up because of that
        ///     call, so the fake reproduces exactly that one interaction.
        /// </summary>
        public StringTable? Table { get; set; }

        public bool IsServer { get; set; }

        // The interface documents IsClient as the exact negation of IsServer.
        public bool IsClient => !IsServer;

        public bool IsRunning { get; set; }

        /// <summary>How often <see cref="RegisterNetMessage{T}"/> was called.</summary>
        public int RegisterCalls { get; private set; }

        /// <summary>The message type of the last registration.</summary>
        public Type? RegisteredType { get; private set; }

        /// <summary>The receive callback passed with the last registration.</summary>
        public Delegate? RegisteredCallback { get; private set; }

        /// <summary>The accept mask passed with the last registration.</summary>
        public NetMessageAccept RegisteredAccept { get; private set; }

        /// <summary>Messages passed to <see cref="ServerSendToAll"/>, in call order.</summary>
        public List<NetMessage> SentToAll { get; } = new();

        /// <summary>Messages passed to <see cref="ServerSendMessage"/>, with their recipient, in call order.</summary>
        public List<(NetMessage Message, INetChannel Recipient)> SentToChannel { get; } = new();

        /// <summary>
        ///     Hands <paramref name="message"/> to the callback that the table registered, like the net manager's
        ///     packet dispatcher does for a table message that arrived from the server.
        /// </summary>
        public void Deliver(MsgStringTableEntries message)
        {
            var callback = (ProcessMessage<MsgStringTableEntries>?) RegisteredCallback;
            Assert.That(callback, Is.Not.Null, "The table did not register a receive callback.");
            callback!(message);
        }

        public void RegisterNetMessage<T>(ProcessMessage<T>? rxCallback = null,
            NetMessageAccept accept = NetMessageAccept.Both)
            where T : NetMessage, new()
        {
            RegisterCalls++;
            RegisteredType = typeof(T);
            RegisteredCallback = rxCallback;
            RegisteredAccept = accept;

            // The real net manager adds the message name to the string table. The returned id is not needed.
            Table?.AddString(new T().MsgName);
        }

        public void ServerSendToAll(NetMessage message)
        {
            SentToAll.Add(message);
        }

        public void ServerSendMessage(NetMessage message, INetChannel recipient)
        {
            SentToChannel.Add((message, recipient));
        }

        // Everything below is not used by the string table.

        public bool IsConnected => throw new NotImplementedException();
        public NetworkStats Statistics => throw new NotImplementedException();
        public IEnumerable<INetChannel> Channels => throw new NotImplementedException();
        public int ChannelCount => throw new NotImplementedException();
        public int Port => throw new NotImplementedException();
        public IReadOnlyDictionary<Type, long> MessageBandwidthUsage => throw new NotImplementedException();

        public void ResetBandwidthMetrics() => throw new NotImplementedException();
        public void Initialize(bool isServer) => throw new NotImplementedException();
        public void StartServer() => throw new NotImplementedException();
        public void Shutdown(string reason) => throw new NotImplementedException();
        public void ProcessPackets() => throw new NotImplementedException();
        public void ServerSendToMany(NetMessage message, List<INetChannel> recipients) => throw new NotImplementedException();
        public void ClientSendMessage(NetMessage message) => throw new NotImplementedException();
        public T CreateNetMessage<T>() where T : NetMessage, new() => throw new NotImplementedException();

        public event Func<NetConnectingArgs, Task> Connecting
        {
            add => throw new NotImplementedException();
            remove => throw new NotImplementedException();
        }

        public event EventHandler<NetChannelArgs> Connected
        {
            add => throw new NotImplementedException();
            remove => throw new NotImplementedException();
        }

        public event EventHandler<NetDisconnectedArgs> Disconnect
        {
            add => throw new NotImplementedException();
            remove => throw new NotImplementedException();
        }
    }

    /// <summary>
    ///     A table that has been initialised against a <see cref="FakeNetManager"/>, together with a log of the
    ///     callbacks the table raised.
    /// </summary>
    private sealed class Harness
    {
        public FakeNetManager Net { get; }
        public StringTable Table { get; }

        /// <summary>"init" and "update", in the order the callbacks were raised.</summary>
        public List<string> Calls { get; } = new();

        /// <summary>The argument of every update callback invocation.</summary>
        public List<Entry[]> Updates { get; } = new();

        public int InitCalls => Calls.Count(c => c == "init");

        private Harness(bool server, bool withInitCallback, bool withUpdateCallback)
        {
            Net = new FakeNetManager { IsServer = server };
            Table = new StringTable(Net) { Sawmill = TestSawmill };
            Net.Table = Table;

            InitCallback? init = null;
            if (withInitCallback)
                init = () => Calls.Add("init");

            StringTableUpdateCallback? update = null;
            if (withUpdateCallback)
                update = entries =>
                {
                    Calls.Add("update");
                    Updates.Add(entries);
                };

            // Initialize is called while IsRunning is still false, like the real manager does at startup,
            // so it never produces network traffic.
            Table.Initialize(init, update);
        }

        /// <summary>A server-side table. Callbacks are supplied but must never be raised on a server.</summary>
        public static Harness Server() => new(true, true, true);

        /// <summary>A client-side table initialised the way the net manager does it: both callbacks.</summary>
        public static Harness Client(bool withInitCallback = true, bool withUpdateCallback = true)
            => new(false, withInitCallback, withUpdateCallback);

        /// <summary>
        ///     Forgets the callbacks raised so far. On a client, initialisation itself raises the update callback
        ///     once (it seeds id 0), which most tests are not interested in.
        /// </summary>
        public void ClearLog()
        {
            Calls.Clear();
            Updates.Clear();
        }
    }

    private static Entry MakeEntry(int id, string? name)
    {
        return new Entry { Id = id, String = name! };
    }

    private static MsgStringTableEntries MakeMessage(params Entry[] entries)
    {
        return new MsgStringTableEntries { Entries = entries };
    }

    private static MsgStringTableEntries Sent(NetMessage message)
    {
        Assert.That(message, Is.TypeOf<MsgStringTableEntries>());
        return (MsgStringTableEntries) message;
    }

    private static INetChannel MakeChannel()
    {
        // The table only reads the remote address of the channel (for a log line) and hands the channel to the
        // net manager. INetChannel has many members, so it is mocked instead of hand written.
        var channel = new Mock<INetChannel>();
        channel.SetupGet(c => c.RemoteEndPoint).Returns(new IPEndPoint(IPAddress.Loopback, 1212));
        return channel.Object;
    }

    // ------------------------------------------------------------------------------------------------------------
    // Construction and static data
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Constructor_CreatesEmptyTableWithoutRegisteringAnything()
    {
        // The table is only seeded with id 0 by Initialize (or Reset). Constructing it must not touch the net
        // manager, because the net manager creates its table in its own constructor, before it can register
        // messages.
        var net = new FakeNetManager { IsServer = true };

        var table = new StringTable(net);

        Assert.Multiple(() =>
        {
            Assert.That(table.Strings, Is.Empty);
            Assert.That(net.RegisterCalls, Is.EqualTo(0));
            Assert.That(table.TryGetString(0, out _), Is.False);
        });
    }

    [Test]
    public void Sawmill_IsNullUntilTheOwnerAssignsIt()
    {
        // Sawmill is a public field that the net manager assigns before it initialises the table.
        var table = new StringTable(new FakeNetManager());

        Assert.That(table.Sawmill, Is.Null);
    }

    [Test]
    public void InvalidStringId_IsMinusOne()
    {
        Assert.That(StringTable.InvalidStringId, Is.EqualTo(-1));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Initialize and Reset: server
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Initialize_OnServer_RegistersTableMessageForClientsDuringHandshake()
    {
        // Only clients receive the table, and they must be able to do so before the connection handshake has
        // completed, because the table is what makes every later message understandable.
        var harness = Harness.Server();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Net.RegisterCalls, Is.EqualTo(1));
            Assert.That(harness.Net.RegisteredType, Is.EqualTo(typeof(MsgStringTableEntries)));
            Assert.That(harness.Net.RegisteredAccept, Is.EqualTo(NetMessageAccept.Client | NetMessageAccept.Handshake));
            Assert.That(harness.Net.RegisteredCallback, Is.Not.Null);
        });
    }

    [Test]
    public void Initialize_OnServer_LeavesOnlyTheBootstrapEntry()
    {
        // Id 0 is bound to the table message by convention, so that a client can understand the very first
        // message it receives. Nothing else is in the table until other message types register.
        var harness = Harness.Server();

        Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
    }

    [Test]
    public void Initialize_OnServer_DoesNotRaiseCallbacksOrSendAnything()
    {
        // Both callbacks are client-only. Initialize runs before the server has a network peer, so nothing is sent.
        var harness = Harness.Server();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Calls, Is.Empty);
            Assert.That(harness.Net.SentToAll, Is.Empty);
            Assert.That(harness.Net.SentToChannel, Is.Empty);
        });
    }

    [Test]
    public void Reset_OnServer_ClearsTableAndReseedsBootstrapWithoutRaisingCallbacks()
    {
        var harness = Harness.Server();
        harness.Table.AddString("MsgOne");
        harness.Table.AddString("MsgTwo");

        harness.Table.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
            Assert.That(harness.Calls, Is.Empty, "The update callback is raised by a client only.");
        });
    }

    [Test]
    public void Reset_OnServer_DoesNotRewindTheIdCounter()
    {
        // Ids are never recycled, not even across a reset: the first name added before the reset got id 2, so the
        // first name added after it continues with 3.
        var harness = Harness.Server();
        var before = harness.Table.AddString("MsgOne");

        harness.Table.Reset();
        var after = harness.Table.AddString("MsgTwo");

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(2));
            Assert.That(after, Is.EqualTo(3));
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // AddString: server
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void AddString_FirstCallOnServer_ReturnsTwo_Quirk()
    {
        // PINS CURRENT BEHAVIOUR, KEPT FOR COMPATIBILITY. The first ordinary id is 2, not 1.
        //
        // Why: Initialize registers the table message with the net manager BEFORE it calls Reset. On a server that
        // registration asks the table for an id, which consumes id 1 while the table is still empty. Reset then
        // wipes the table and installs the table message under id 0, but it does not rewind the id counter. Id 1
        // is therefore never used.
        //
        // Nothing on the wire requires this, because ids are always sent explicitly. It is pinned so that tools
        // and tests that compare ids between engine builds keep seeing the same numbers.
        var harness = Harness.Server();

        var id = harness.Table.AddString("MsgFirst");

        Assert.That(id, Is.EqualTo(2));
    }

    [Test]
    public void AddString_FirstCallOnServer_LeavesIdOneUnused_Quirk()
    {
        // Companion of the test above: id 1 really is a permanent gap, not just an unused return value.
        var harness = Harness.Server();

        harness.Table.AddString("MsgFirst");

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(0), Is.EqualTo(TableMessageName));
            Assert.That(harness.Table.GetString(1), Is.Null);
            Assert.That(harness.Table.GetString(2), Is.EqualTo("MsgFirst"));
        });
    }

    [Test]
    public void AddString_LaterCalls_ReturnIncreasingIds()
    {
        // Ids are handed out in registration order.
        var harness = Harness.Server();

        var first = harness.Table.AddString("MsgA");
        var second = harness.Table.AddString("MsgB");
        var third = harness.Table.AddString("MsgC");

        Assert.That(new[] { first, second, third }, Is.EqualTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void AddString_SameStringTwice_ReturnsSameIdAndBroadcastsOnlyOnce()
    {
        // Registering a name is idempotent: the second call finds the existing entry, changes nothing and
        // therefore has nothing to announce to clients.
        var harness = Harness.Server();
        harness.Net.IsRunning = true;

        var first = harness.Table.AddString("MsgA");
        var second = harness.Table.AddString("MsgA");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(harness.Net.SentToAll, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void AddString_ExistingBootstrapName_ReturnsIdZeroWithoutChange()
    {
        // The name of the table message itself is already present under id 0.
        var harness = Harness.Server();
        harness.Net.IsRunning = true;

        var id = harness.Table.AddString(TableMessageName);

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo(0));
            Assert.That(harness.Table.Strings, Has.Count.EqualTo(1));
            Assert.That(harness.Net.SentToAll, Is.Empty);
        });
    }

    [Test]
    public void AddString_NamesDifferingOnlyInCase_GetDifferentIds()
    {
        // Names are compared ordinally and case-sensitively.
        var harness = Harness.Server();

        var lower = harness.Table.AddString("msgfoo");
        var upper = harness.Table.AddString("MsgFoo");

        Assert.That(lower, Is.Not.EqualTo(upper));
    }

    [Test]
    public void AddString_WhenRunning_BroadcastsSingleEntryMessage()
    {
        // Once the server has a network peer, every new registration is announced to the connected clients as a
        // message with exactly one entry: the new id and name.
        var harness = Harness.Server();
        harness.Net.IsRunning = true;

        var id = harness.Table.AddString("MsgA");

        Assert.That(harness.Net.SentToAll, Has.Count.EqualTo(1));
        var message = Sent(harness.Net.SentToAll[0]);
        Assert.Multiple(() =>
        {
            Assert.That(message.Entries, Has.Length.EqualTo(1));
            Assert.That(message.Entries[0].Id, Is.EqualTo(id));
            Assert.That(message.Entries[0].String, Is.EqualTo("MsgA"));
            Assert.That(harness.Net.SentToChannel, Is.Empty, "A registration is broadcast, never sent to one channel.");
        });
    }

    [Test]
    public void AddString_WhenNotRunning_StoresNameButSendsNothing()
    {
        // Registrations that happen before the server has started produce no traffic. Clients get the whole table
        // when they connect instead.
        var harness = Harness.Server();
        harness.Net.IsRunning = false;

        var id = harness.Table.AddString("MsgA");

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(id), Is.EqualTo("MsgA"));
            Assert.That(harness.Net.SentToAll, Is.Empty);
        });
    }

    [Test]
    public void AddString_SkipsIdsOccupiedByAddStringFixed()
    {
        // The counter keeps incrementing until it finds an id that is not in the table, so a pinned id is never
        // handed out a second time.
        var harness = Harness.Server();
        harness.Table.AddStringFixed(2, "MsgPinned");

        var id = harness.Table.AddString("MsgNormal");

        Assert.That(id, Is.EqualTo(3));
    }

    [Test]
    public void AddString_NeverReusesIdsOfRemovedEntries()
    {
        // The counter only moves forward. Removing an entry (here through the live dictionary) does not free its
        // id for reuse.
        var harness = Harness.Server();
        var removed = harness.Table.AddString("MsgA");
        harness.Table.Strings.Remove(removed);

        var next = harness.Table.AddString("MsgB");

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(next, Is.EqualTo(3));
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // AddStringFixed: server
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void AddStringFixed_NewIdAndName_StoresEntryAndBroadcastsIt()
    {
        var harness = Harness.Server();
        harness.Net.IsRunning = true;

        harness.Table.AddStringFixed(10, "MsgPinned");

        Assert.That(harness.Net.SentToAll, Has.Count.EqualTo(1));
        var message = Sent(harness.Net.SentToAll[0]);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(10), Is.EqualTo("MsgPinned"));
            Assert.That(message.Entries, Has.Length.EqualTo(1));
            Assert.That(message.Entries[0].Id, Is.EqualTo(10));
            Assert.That(message.Entries[0].String, Is.EqualTo("MsgPinned"));
        });
    }

    [Test]
    public void AddStringFixed_SameStringUnderSameId_IsNoOp()
    {
        // Repeating an identical registration changes nothing and announces nothing.
        var harness = Harness.Server();
        harness.Net.IsRunning = true;
        harness.Table.AddStringFixed(10, "MsgPinned");

        harness.Table.AddStringFixed(10, "MsgPinned");

        Assert.Multiple(() =>
        {
            Assert.That(harness.Net.SentToAll, Has.Count.EqualTo(1));
            Assert.That(harness.Table.Strings, Has.Count.EqualTo(2), "Id 0 plus the pinned entry.");
        });
    }

    [Test]
    public void AddStringFixed_SameStringUnderOtherId_MovesTheString()
    {
        // The name is unique in the table, so pinning it to a new id removes the old mapping first.
        var harness = Harness.Server();
        harness.Net.IsRunning = true;
        harness.Table.AddStringFixed(10, "MsgPinned");

        harness.Table.AddStringFixed(11, "MsgPinned");

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(10), Is.Null);
            Assert.That(harness.Table.GetString(11), Is.EqualTo("MsgPinned"));
            Assert.That(harness.Table.TryFindStringId("MsgPinned", out var id), Is.True);
            Assert.That(id, Is.EqualTo(11));
            Assert.That(harness.Net.SentToAll, Has.Count.EqualTo(2), "The move is announced as a new entry.");
        });
    }

    [TestCase(0, TestName = "AddStringFixed_IdOccupiedByBootstrapEntry_ThrowsArgumentException")]
    [TestCase(10, TestName = "AddStringFixed_IdOccupiedByOtherString_ThrowsArgumentException")]
    public void AddStringFixed_IdUsedByDifferentString_ThrowsArgumentException(int id)
    {
        // An id cannot name two different messages. Id 0 is just a special case of an occupied id.
        var harness = Harness.Server();
        harness.Table.AddStringFixed(10, "MsgPinned");
        harness.Net.IsRunning = true;

        Assert.That(
            () => harness.Table.AddStringFixed(id, "MsgOther"),
            Throws.TypeOf<ArgumentException>());

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.TryFindStringId("MsgOther", out _), Is.False);
            Assert.That(harness.Net.SentToAll, Is.Empty, "A failed registration must not be announced.");
        });
    }

    [Test]
    public void AddStringFixed_ConflictAfterMove_LeavesTheOldMappingRemoved_Quirk()
    {
        // PINS CURRENT BEHAVIOUR. "MsgA" is pinned to 10 and "MsgB" to 11. Pinning "MsgA" to 11 first removes
        // the old mapping of "MsgA" (10) and only then discovers that 11 is taken, so it throws. The table is left
        // without any entry for "MsgA". Callers are expected not to trigger this; the test documents what happens
        // when they do, so that a change to the order of the checks is a conscious decision.
        var harness = Harness.Server();
        harness.Table.AddStringFixed(10, "MsgA");
        harness.Table.AddStringFixed(11, "MsgB");

        Assert.That(
            () => harness.Table.AddStringFixed(11, "MsgA"),
            Throws.TypeOf<ArgumentException>());

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(10), Is.Null);
            Assert.That(harness.Table.GetString(11), Is.EqualTo("MsgB"));
            Assert.That(harness.Table.TryFindStringId("MsgA", out _), Is.False);
        });
    }

    [Test]
    public void AddStringFixed_IdAboveCounter_DoesNotAdvanceTheCounter()
    {
        // The counter is untouched by pinned ids, so the next ordinary registration is still 2. (The table does not
        // limit ids to the 0..255 range that fits in a packet header; consumers deal with that.)
        var harness = Harness.Server();
        harness.Table.AddStringFixed(1000, "MsgPinned");

        var id = harness.Table.AddString("MsgNormal");

        Assert.That(id, Is.EqualTo(2));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Lookups
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void GetString_UnknownId_ReturnsNull()
    {
        var harness = Harness.Server();

        Assert.That(harness.Table.GetString(123), Is.Null);
    }

    [Test]
    public void GetString_KnownId_ReturnsName()
    {
        var harness = Harness.Server();
        var id = harness.Table.AddString("MsgA");

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(id), Is.EqualTo("MsgA"));
            Assert.That(harness.Table.GetString(0), Is.EqualTo(TableMessageName));
        });
    }

    [Test]
    public void TryGetString_KnownId_ReturnsTrueAndName()
    {
        var harness = Harness.Server();
        var id = harness.Table.AddString("MsgA");

        var found = harness.Table.TryGetString(id, out var name);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(name, Is.EqualTo("MsgA"));
        });
    }

    [Test]
    public void TryGetString_UnknownId_ReturnsFalseAndNull()
    {
        var harness = Harness.Server();

        var found = harness.Table.TryGetString(123, out var name);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(name, Is.Null);
        });
    }

    [Test]
    public void TryFindStringId_KnownName_ReturnsItsId()
    {
        var harness = Harness.Server();
        var expected = harness.Table.AddString("MsgA");

        var found = harness.Table.TryFindStringId("MsgA", out var id);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(id, Is.EqualTo(expected));
            Assert.That(harness.Table.TryFindStringId(TableMessageName, out var bootstrapId), Is.True);
            Assert.That(bootstrapId, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryFindStringId_MissingName_ReturnsFalseAndIdZero()
    {
        // On failure the id is 0, which is a VALID id (the table message), and not InvalidStringId. Callers must
        // look at the return value.
        var harness = Harness.Server();

        var found = harness.Table.TryFindStringId("MsgDoesNotExist", out var id);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(id, Is.EqualTo(0));
        });
    }

    [Test]
    public void TryFindStringId_IsCaseSensitive()
    {
        var harness = Harness.Server();
        harness.Table.AddString("MsgA");

        Assert.That(harness.Table.TryFindStringId("msga", out _), Is.False);
    }

    [Test]
    public void TryFindStringId_WorksOnAClientToo()
    {
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(5, "MsgA")));

        var found = harness.Table.TryFindStringId("MsgA", out var id);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(id, Is.EqualTo(5));
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // SendFullTable
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void SendFullTable_OnServer_SendsOneMessageWithEveryEntryToThatChannel()
    {
        // A connecting client gets the complete table in one un-chunked message addressed to it alone, including id
        // 0. The order of the entries is not part of the contract, so the entries are compared as a set.
        var harness = Harness.Server();
        harness.Table.AddString("MsgA");
        harness.Table.AddString("MsgB");
        var channel = MakeChannel();

        harness.Table.SendFullTable(channel);

        Assert.That(harness.Net.SentToChannel, Has.Count.EqualTo(1));
        var (message, recipient) = harness.Net.SentToChannel[0];
        var entries = Sent(message).Entries.Select(e => (e.Id, e.String)).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(recipient, Is.SameAs(channel));
            Assert.That(entries, Is.EquivalentTo(new[] { (0, TableMessageName), (2, "MsgA"), (3, "MsgB") }));
            Assert.That(harness.Net.SentToAll, Is.Empty, "The full table goes to one channel, not to everybody.");
        });
    }

    [Test]
    public void SendFullTable_OnServer_WithOnlyBootstrapEntry_SendsThatSingleEntry()
    {
        var harness = Harness.Server();

        harness.Table.SendFullTable(MakeChannel());

        var entries = Sent(harness.Net.SentToChannel.Single().Message).Entries;
        Assert.Multiple(() =>
        {
            Assert.That(entries, Has.Length.EqualTo(1));
            Assert.That(entries[0].Id, Is.EqualTo(0));
            Assert.That(entries[0].String, Is.EqualTo(TableMessageName));
        });
    }

    [Test]
    public void SendFullTable_OnClient_SendsNothing()
    {
        var harness = Harness.Client();

        harness.Table.SendFullTable(MakeChannel());

        Assert.Multiple(() =>
        {
            Assert.That(harness.Net.SentToChannel, Is.Empty);
            Assert.That(harness.Net.SentToAll, Is.Empty);
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Client: assigning ids is the server's job
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void AddString_OnClient_ReturnsInvalidIdAndChangesNothing()
    {
        // A client never assigns ids; it only learns them from the server.
        var harness = Harness.Client();

        var id = harness.Table.AddString("MsgA");

        Assert.Multiple(() =>
        {
            Assert.That(id, Is.EqualTo(-1));
            Assert.That(harness.Table.TryFindStringId("MsgA", out _), Is.False);
            Assert.That(harness.Table.Strings, Has.Count.EqualTo(1), "Only the bootstrap entry.");
        });
    }

    [Test]
    public void AddStringFixed_OnClient_DoesNothing()
    {
        var harness = Harness.Client();

        harness.Table.AddStringFixed(10, "MsgPinned");

        Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Client: Initialize and Reset
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Initialize_OnClient_SeedsBootstrapAndRaisesUpdateCallbackWithIt()
    {
        // This is how the client's dispatch cache learns that id 0 is the table message, which it must know to be
        // able to receive the first table message at all.
        var harness = Harness.Client();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
            Assert.That(harness.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.Updates[0], Is.EqualTo(new[] { MakeEntry(0, TableMessageName) }));
            Assert.That(harness.InitCalls, Is.EqualTo(0));
        });
    }

    [Test]
    public void Initialize_OnClient_RegistersTableMessageForClientsDuringHandshake()
    {
        var harness = Harness.Client();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Net.RegisteredType, Is.EqualTo(typeof(MsgStringTableEntries)));
            Assert.That(harness.Net.RegisteredAccept, Is.EqualTo(NetMessageAccept.Client | NetMessageAccept.Handshake));
        });
    }

    [Test]
    public void Initialize_OnClient_DoesNotAddAnythingBesideBootstrap()
    {
        // Registration asks the table for an id, and a client must not assign any (it gets -1).
        var harness = Harness.Client();

        Assert.That(harness.Table.Strings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Reset_OnClient_ClearsTableAndRaisesUpdateCallbackWithBootstrapEntry()
    {
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA"), MakeEntry(3, "MsgB")));
        harness.ClearLog();

        harness.Table.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
            Assert.That(harness.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.Updates[0], Is.EqualTo(new[] { MakeEntry(0, TableMessageName) }));
        });
    }

    [Test]
    public void Reset_OnClient_RaisesUpdateCallbackEvenWithoutInitCallback()
    {
        // The re-seed notification is not subject to the init-callback gating that applies to received messages
        // (see Receive_WithoutInitCallback_DoesNotRaiseUpdateCallback_Quirk).
        var harness = Harness.Client(withInitCallback: false);

        Assert.That(harness.Updates, Has.Count.EqualTo(1));
        harness.Table.Reset();
        Assert.That(harness.Updates, Has.Count.EqualTo(2));
    }

    [Test]
    public void Reset_OnClient_WithoutUpdateCallback_JustReseedsBootstrap()
    {
        var harness = Harness.Client(withUpdateCallback: false);

        Assert.That(() => harness.Table.Reset(), Throws.Nothing);
        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
            Assert.That(harness.Calls, Is.Empty);
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Client: receiving a table message
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Receive_NewEntries_AreAdded()
    {
        var harness = Harness.Client();

        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA"), MakeEntry(3, "MsgB")));

        Assert.That(
            harness.Table.Strings,
            Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName, [2] = "MsgA", [3] = "MsgB" }));
    }

    [Test]
    public void Receive_MergesIntoTableInsteadOfReplacingIt()
    {
        // The server sends single-entry updates later on, so entries that a message does not mention must survive.
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        harness.Net.Deliver(MakeMessage(MakeEntry(3, "MsgB")));

        Assert.That(
            harness.Table.Strings,
            Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName, [2] = "MsgA", [3] = "MsgB" }));
    }

    [TestCase(null, TestName = "Receive_NullString_RemovesTheId")]
    [TestCase("", TestName = "Receive_EmptyString_RemovesTheId")]
    public void Receive_AbsentString_RemovesTheId(string? name)
    {
        // A null or empty name means "this id no longer has a message". On the wire the two cannot be told apart.
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA"), MakeEntry(3, "MsgB")));

        harness.Net.Deliver(MakeMessage(MakeEntry(2, name)));

        Assert.That(
            harness.Table.Strings,
            Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName, [3] = "MsgB" }));
    }

    [Test]
    public void Receive_RemovalOfUnknownId_IsIgnored()
    {
        var harness = Harness.Client();

        Assert.That(() => harness.Net.Deliver(MakeMessage(MakeEntry(77, ""))), Throws.Nothing);

        Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string> { [0] = TableMessageName }));
    }

    [Test]
    public void Receive_StringAlreadyPresentUnderSameId_IsSkipped()
    {
        // Receiving what is already known is harmless, which matters because the full table and incremental
        // updates can overlap.
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        Assert.That(() => harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA"))), Throws.Nothing);

        Assert.That(harness.Table.Strings, Is.EqualTo(new Dictionary<int, string>
        {
            [0] = TableMessageName,
            [2] = "MsgA",
        }));
    }

    [Test]
    public void Receive_StringUnderNewId_MovesTheString()
    {
        // The name is unique, so the old id is dropped when the server reassigns the name to another id.
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        harness.Net.Deliver(MakeMessage(MakeEntry(9, "MsgA")));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(2), Is.Null);
            Assert.That(harness.Table.GetString(9), Is.EqualTo("MsgA"));
        });
    }

    [Test]
    public void Receive_SameIdWithDifferentString_ThrowsArgumentException()
    {
        // The table cannot give one id two names. Replacing a name in place is not supported: the server has to
        // send a removal first.
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        Assert.That(
            () => harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgOther"))),
            Throws.TypeOf<ArgumentException>());

        Assert.That(harness.Table.GetString(2), Is.EqualTo("MsgA"));
    }

    [Test]
    public void Receive_BootstrapIdWithDifferentString_ThrowsArgumentException()
    {
        // Id 0 is occupied from the start. A hostile or buggy server cannot rebind it.
        var harness = Harness.Client();

        Assert.That(
            () => harness.Net.Deliver(MakeMessage(MakeEntry(0, "MsgOther"))),
            Throws.TypeOf<ArgumentException>());

        Assert.That(harness.Table.GetString(0), Is.EqualTo(TableMessageName));
    }

    [Test]
    public void Receive_ConflictInTheMiddle_AbortsRestOfBatchButKeepsEarlierEntries()
    {
        // Entries are applied in message order. The conflict at the second entry throws, so the first entry stays
        // applied, the third is never looked at, and neither callback runs for this message.
        var harness = Harness.Client();

        // Reset first, so that a successful message WOULD raise the init callback (see the receive-after-reset
        // tests). That makes the "no callback" assertion below meaningful for both callbacks.
        harness.Table.Reset();
        harness.Net.Deliver(MakeMessage(MakeEntry(5, "MsgExisting")));
        harness.ClearLog();

        var message = MakeMessage(MakeEntry(6, "MsgBefore"), MakeEntry(5, "MsgConflicting"), MakeEntry(7, "MsgAfter"));

        Assert.That(() => harness.Net.Deliver(message), Throws.TypeOf<ArgumentException>());

        Assert.Multiple(() =>
        {
            Assert.That(harness.Table.GetString(6), Is.EqualTo("MsgBefore"));
            Assert.That(harness.Table.GetString(5), Is.EqualTo("MsgExisting"));
            Assert.That(harness.Table.GetString(7), Is.Null);
            Assert.That(harness.Calls, Is.Empty, "A failed batch raises neither the init nor the update callback.");
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Client: callbacks around a received message
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Receive_RightAfterInitialize_DoesNotRaiseInitCallback_Quirk()
    {
        // PINS CURRENT BEHAVIOUR. Initialize marks the table as initialised, and the init callback is only raised
        // while the table is NOT marked as initialised. So on a first connection the "table loaded" callback never
        // fires; only the update callback does. The init callback of the real net manager just logs a line, which
        // is why nobody noticed.
        var harness = Harness.Client();
        harness.ClearLog();

        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        Assert.Multiple(() =>
        {
            Assert.That(harness.InitCalls, Is.EqualTo(0));
            Assert.That(harness.Updates, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Receive_UpdateCallbackGetsTheMessageEntryArray()
    {
        // The callback receives the array of the message as received, including entries that turned out to be
        // no-ops (already known) and removals (empty name), so the net manager sees everything the server said.
        var harness = Harness.Client();
        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA"), MakeEntry(3, "MsgB")));
        harness.ClearLog();
        var message = MakeMessage(MakeEntry(2, "MsgA"), MakeEntry(3, ""), MakeEntry(4, "MsgC"));

        harness.Net.Deliver(message);

        Assert.Multiple(() =>
        {
            Assert.That(harness.Updates, Has.Count.EqualTo(1));
            Assert.That(harness.Updates[0], Is.SameAs(message.Entries));
            Assert.That(harness.Updates[0], Has.Length.EqualTo(3));
        });
    }

    [Test]
    public void Receive_AfterReset_RaisesInitCallbackBeforeUpdateCallback()
    {
        // Reset clears the "initialised" mark, and nothing sets it again, so from then on the init callback is
        // raised for a received message, always before the update callback.
        var harness = Harness.Client();
        harness.Table.Reset();
        harness.ClearLog();

        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        Assert.That(harness.Calls, Is.EqualTo(new[] { "init", "update" }));
    }

    [Test]
    public void Receive_AfterReset_RaisesInitCallbackForEveryMessageNotJustTheFirst_Quirk()
    {
        // PINS CURRENT BEHAVIOUR. The init callback is not a "first message" notification: after a Reset it is
        // raised again for every table message until Initialize runs again. Its only real consumer logs a line.
        var harness = Harness.Client();
        harness.Table.Reset();
        harness.ClearLog();

        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));
        harness.Net.Deliver(MakeMessage(MakeEntry(3, "MsgB")));
        harness.Net.Deliver(MakeMessage(MakeEntry(4, "MsgC")));

        Assert.That(harness.InitCalls, Is.EqualTo(3));
    }

    [Test]
    public void Receive_WithoutInitCallback_DoesNotRaiseUpdateCallback_Quirk()
    {
        // PINS CURRENT BEHAVIOUR. When no init callback was supplied the receive routine returns right after
        // merging the entries, and the update callback is not raised either: it is gated on the init callback
        // being present. The net manager always passes both, so this only matters for other users of the class.
        // The entries themselves are still applied.
        var harness = Harness.Client(withInitCallback: false);
        harness.ClearLog();

        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        Assert.Multiple(() =>
        {
            Assert.That(harness.Calls, Is.Empty);
            Assert.That(harness.Table.GetString(2), Is.EqualTo("MsgA"));
        });
    }

    [Test]
    public void Receive_WithInitCallbackButNoUpdateCallback_StillRaisesInitCallbackAfterReset()
    {
        var harness = Harness.Client(withUpdateCallback: false);
        harness.Table.Reset();

        harness.Net.Deliver(MakeMessage(MakeEntry(2, "MsgA")));

        Assert.That(harness.Calls, Is.EqualTo(new[] { "init" }));
    }
}
