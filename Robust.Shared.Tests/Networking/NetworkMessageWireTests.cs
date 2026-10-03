using System;
using System.Collections.Generic;
using System.Linq;
using Lidgren.Network;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Timing;

namespace Robust.Shared.Tests.Networking;

// These tests pin the exact bytes that a handful of small network messages put on the wire.
//
// Why this matters: clients and servers are built and released independently, so a client from one build
// regularly talks to a server from another. Both sides have to agree byte-for-byte on every message layout.
// A "harmless" refactor (switching a fixed-width integer to a varint, reordering two fields, widening a
// counter, ...) still round-trips inside one build, but silently breaks every peer running another build.
// The golden-byte tests below fail loudly if that happens. The round-trip tests only prove that Write and Read
// agree with each other, so they cannot catch such changes on their own.
//
// Conventions used by every test in this file:
//  - Expected buffers contain only the message payload. The one-byte message id header is added by the net
//    manager before WriteToBuffer is called and consumed before ReadFromBuffer, so it never appears here.
//  - The expected bytes are spelled out by hand next to a comment showing how each byte is derived. Please do
//    not generate them by calling the code under test.
//  - The serializer argument of Read/WriteToBuffer is not used by any of these messages, so tests pass null.

/// <summary>
///     Helpers that build real Lidgren buffers without needing a network connection.
/// </summary>
internal static class WireTestHelper
{
    /// <summary>
    ///     Serializes <paramref name="message"/> into a fresh Lidgren outgoing buffer and returns exactly the
    ///     bytes that were written.
    /// </summary>
    /// <remarks>
    ///     <see cref="NetOutgoingMessage"/> has an internal constructor, so the supported way to obtain one is
    ///     <see cref="NetPeer.CreateMessage()"/>. The peer is never started, so no socket is opened.
    /// </remarks>
    public static byte[] Write(NetMessage message)
    {
        var peer = new NetClient(new NetPeerConfiguration("RobustNetworkMessageWireTests"));
        var buffer = peer.CreateMessage();

        message.WriteToBuffer(buffer, null!);

        // The backing array is over-allocated, so only copy the bytes that were logically written.
        return buffer.Data.AsSpan(0, buffer.LengthBytes).ToArray();
    }

    /// <summary>
    ///     Creates a Lidgren incoming buffer that contains exactly <paramref name="payload"/>, positioned at
    ///     the start, as if the net manager had just received a packet and consumed its message id.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="NetIncomingMessage"/> also has an internal constructor, and the Lidgren assembly only
    ///         exposes its internals to Robust.UnitTesting, not to this test project. The constructor is
    ///         therefore invoked through reflection, after which the public <see cref="NetBuffer.Data"/>,
    ///         <see cref="NetBuffer.LengthBytes"/> and <see cref="NetBuffer.Position"/> members are used to fill
    ///         the buffer.
    ///     </para>
    ///     <para>
    ///         The backing array is deliberately sized to exactly the payload (no spare capacity). Lidgren's
    ///         bounds assertions only exist in debug builds, so with a tight array a read past the end fails
    ///         with an exception in release builds as well, and truncation tests behave the same in both
    ///         configurations.
    ///     </para>
    /// </remarks>
    public static NetIncomingMessage CreateIncoming(byte[] payload)
    {
        var message = (NetIncomingMessage) Activator.CreateInstance(typeof(NetIncomingMessage), nonPublic: true)!;
        message.Data = (byte[]) payload.Clone();
        message.LengthBytes = payload.Length;
        message.Position = 0;
        return message;
    }

    /// <summary>
    ///     Deserializes <paramref name="payload"/> into a new message and checks that the reader consumed every
    ///     byte, no more and no less. A reader that stops early or runs past the end would desynchronise any
    ///     data that follows the message in a real packet.
    /// </summary>
    public static T Read<T>(byte[] payload) where T : NetMessage, new()
    {
        var incoming = CreateIncoming(payload);
        var message = new T();

        message.ReadFromBuffer(incoming, null!);

        Assert.That(incoming.Position, Is.EqualTo(payload.Length * 8L),
            "ReadFromBuffer must consume exactly the bytes of the payload.");
        return message;
    }

    /// <summary>
    ///     Writes <paramref name="message"/> and reads the bytes back into a fresh instance.
    /// </summary>
    public static T RoundTrip<T>(T message) where T : NetMessage, new()
    {
        return Read<T>(Write(message));
    }

    /// <summary>
    ///     Checks the properties the base class derives for a message: the registration name, group, delivery
    ///     method and sequence channel. The name is the key of the net string table and the group decides the
    ///     delivery guarantees, so changing either breaks compatibility with older peers.
    /// </summary>
    public static void AssertIdentity(
        NetMessage message,
        string expectedName,
        MsgGroups expectedGroup,
        NetDeliveryMethod expectedDelivery)
    {
        Assert.Multiple(() =>
        {
            Assert.That(message.MsgName, Is.EqualTo(expectedName));
            Assert.That(message.MsgGroup, Is.EqualTo(expectedGroup));
            Assert.That(message.DeliveryMethod, Is.EqualTo(expectedDelivery));
            Assert.That(message.SequenceChannel, Is.EqualTo(0));
        });
    }
}

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgStateAck))]
internal sealed class MsgStateAckWireTest
{
    [Test]
    public void Identity_IsUnreliableEntityGroupMessageOnChannelZero()
    {
        // The ack is sent very frequently and a lost ack is superseded by the next one, so it is unreliable.
        WireTestHelper.AssertIdentity(
            new MsgStateAck(),
            "MsgStateAck",
            MsgGroups.Entity,
            NetDeliveryMethod.Unreliable);
    }

    [Test]
    public void Write_SampleTick_IsFourBytesLittleEndian()
    {
        // Tick 0x01020304 is written as a fixed-width little-endian uint32, least significant byte first:
        //   0x04, 0x03, 0x02, 0x01
        byte[] expected = [0x04, 0x03, 0x02, 0x01];

        var wire = WireTestHelper.Write(new MsgStateAck { Sequence = new GameTick(0x01020304) });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_DefaultTick_IsFourZeroBytes()
    {
        // A default-constructed message acknowledges tick 0: 00 00 00 00.
        byte[] expected = [0x00, 0x00, 0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgStateAck());

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_SmallTick_IsNotVarintEncoded()
    {
        // A varint would encode 1 as the single byte 01. The wire format is a fixed 4 bytes: 01 00 00 00.
        // Switching to a varint would save bytes but break every peer built with the fixed-width layout.
        byte[] expected = [0x01, 0x00, 0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgStateAck { Sequence = new GameTick(1) });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_MaxTick_IsFourBytesOfFf()
    {
        // uint.MaxValue is 0xFFFFFFFF, i.e. FF FF FF FF. (A varint would need 5 bytes.)
        byte[] expected = [0xFF, 0xFF, 0xFF, 0xFF];

        var wire = WireTestHelper.Write(new MsgStateAck { Sequence = GameTick.MaxValue });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Read_SampleBytes_YieldsSampleTick()
    {
        // The mirror image of the golden write test: 04 03 02 01 -> 0x04 + 0x03*2^8 + 0x02*2^16 + 0x01*2^24
        // = 0x01020304.
        byte[] wire = [0x04, 0x03, 0x02, 0x01];

        var message = WireTestHelper.Read<MsgStateAck>(wire);

        Assert.That(message.Sequence.Value, Is.EqualTo(0x01020304u));
    }

    [Test]
    [TestCase(0u)]
    [TestCase(1u)]
    [TestCase(0x01020304u)]
    [TestCase(0x80000000u)]
    [TestCase(uint.MaxValue)]
    public void RoundTrip_PreservesTick(uint tick)
    {
        // The message does no validation, so every uint32 must survive unchanged, including values with the
        // top bit set.
        var result = WireTestHelper.RoundTrip(new MsgStateAck { Sequence = new GameTick(tick) });

        Assert.That(result.Sequence.Value, Is.EqualTo(tick));
    }

    [Test]
    public void Read_TruncatedPayload_Throws([Range(0, 3)] int availableBytes)
    {
        // The full payload is 4 bytes. A shorter buffer must fail with an exception, which the net manager
        // treats as a failed deserialization, rather than inventing a tick from partial data.
        byte[] full = [0x04, 0x03, 0x02, 0x01];
        var truncated = full[..availableBytes];

        Assert.That(
            () => WireTestHelper.Read<MsgStateAck>(truncated),
            Throws.Exception);
    }
}

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgPlayerListReq))]
internal sealed class MsgPlayerListReqWireTest
{
    [Test]
    public void Identity_IsReliableUnorderedCoreMessageOnChannelZero()
    {
        WireTestHelper.AssertIdentity(
            new MsgPlayerListReq(),
            "MsgPlayerListReq",
            MsgGroups.Core,
            NetDeliveryMethod.ReliableUnordered);
    }

    [Test]
    public void Write_WritesNoPayloadBytes()
    {
        // The request carries no data: only the message id header (added by the net manager, not tested
        // here) appears on the wire. Adding even one payload byte would make new clients send data that old
        // servers do not expect.
        var wire = WireTestHelper.Write(new MsgPlayerListReq());

        Assert.That(wire, Is.Empty);
    }

    [Test]
    public void Read_EmptyPayload_ConsumesNothing()
    {
        // Read (via the helper) asserts that zero bytes were consumed from the zero-byte payload.
        Assert.That(() => WireTestHelper.Read<MsgPlayerListReq>([]), Throws.Nothing);
    }

    [Test]
    public void RoundTrip_Succeeds()
    {
        Assert.That(() => WireTestHelper.RoundTrip(new MsgPlayerListReq()), Throws.Nothing);
    }

    [Test]
    public void Read_TrailingBytes_AreIgnoredAndLeftUnread()
    {
        // The message does not look at what follows it, so a newer peer may append data without this reader
        // failing or swallowing it.
        var incoming = WireTestHelper.CreateIncoming([0xAA, 0xBB, 0xCC]);

        new MsgPlayerListReq().ReadFromBuffer(incoming, null!);

        Assert.That(incoming.Position, Is.EqualTo(0L));
    }
}

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgConCmd))]
internal sealed class MsgConCmdWireTest
{
    [Test]
    public void Identity_IsReliableUnorderedCoreMessageOnChannelZero()
    {
        // Despite its name this message is in MsgGroups.Core, not MsgGroups.Command. The group decides the
        // delivery method, so it is part of the compatibility contract.
        WireTestHelper.AssertIdentity(
            new MsgConCmd(),
            "MsgConCmd",
            MsgGroups.Core,
            NetDeliveryMethod.ReliableUnordered);
    }

    [Test]
    public void Write_ShortAsciiText_IsLengthByteFollowedByUtf8()
    {
        // "hi" is 2 UTF-8 bytes, so the length prefix is the single varint byte 02, followed by
        // 'h' = 0x68 and 'i' = 0x69.
        byte[] expected = [0x02, 0x68, 0x69];

        var wire = WireTestHelper.Write(new MsgConCmd { Text = "hi" });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_EmptyText_IsSingleZeroByte()
    {
        // Length 0 as a varint is the single byte 00 and no characters follow.
        byte[] expected = [0x00];

        var wire = WireTestHelper.Write(new MsgConCmd { Text = "" });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_NullText_IsWrittenLikeEmptyText()
    {
        // The Text property is never initialised, so a message that was never filled in must still produce a
        // valid packet: null is written exactly like the empty string, as one 00 byte.
        byte[] expected = [0x00];

        var wire = WireTestHelper.Write(new MsgConCmd { Text = null });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Read_SingleZeroByte_YieldsEmptyStringNotNull()
    {
        // The receiving side hands Text straight to the console host, so a null here would crash command
        // handling. Length 0 must therefore come back as "".
        var message = WireTestHelper.Read<MsgConCmd>([0x00]);

        Assert.That(message.Text, Is.EqualTo(string.Empty));
    }

    [Test]
    public void RoundTrip_NullText_BecomesEmptyString()
    {
        var result = WireTestHelper.RoundTrip(new MsgConCmd { Text = null });

        Assert.That(result.Text, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Write_NonAsciiText_PrefixesUtf8ByteCountNotCharacterCount()
    {
        // "e acute" (U+00E9) is one character but two UTF-8 bytes: 0xC3 0xA9. The prefix counts bytes, so
        // it is 02, not 01.
        byte[] expected = [0x02, 0xC3, 0xA9];

        var wire = WireTestHelper.Write(new MsgConCmd { Text = "é" });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_CjkText_UsesThreeUtf8BytesPerCharacter()
    {
        // U+65E5 encodes as E6 97 A5 and U+672C as E6 9C AC (three bytes each), so the prefix is 6:
        //   06 | E6 97 A5 | E6 9C AC
        byte[] expected = [0x06, 0xE6, 0x97, 0xA5, 0xE6, 0x9C, 0xAC];

        var wire = WireTestHelper.Write(new MsgConCmd { Text = "日本" });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(127, new byte[] { 0x7F })] // 127 = 0b1111111: largest value that fits one varint byte.
    [TestCase(128, new byte[] { 0x80, 0x01 })] // 128: low 7 bits 0 (+ continuation bit 0x80), then 1.
    [TestCase(200, new byte[] { 0xC8, 0x01 })] // 200 = 0b1_1001000: low 7 bits 0x48 | 0x80 = 0xC8, then 0x01.
    [TestCase(16384, new byte[] { 0x80, 0x80, 0x01 })] // 2^14: low two 7-bit groups are 0, third group is 1.
    public void Write_LongText_UsesBase128VarintLengthPrefix(int length, byte[] expectedPrefix)
    {
        // The length prefix is an unsigned base-128 varint: 7 data bits per byte, least significant group
        // first, high bit set on every byte except the last. The text is 'a' (0x61) repeated, so its UTF-8
        // byte count equals its length.
        var text = new string('a', length);
        byte[] expected = [.. expectedPrefix, .. Enumerable.Repeat((byte) 0x61, length)];

        var wire = WireTestHelper.Write(new MsgConCmd { Text = text });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    [TestCase("hi")]
    [TestCase("")]
    [TestCase("say hello world")]
    [TestCase("é日本")]
    public void RoundTrip_PreservesText(string text)
    {
        var result = WireTestHelper.RoundTrip(new MsgConCmd { Text = text });

        Assert.That(result.Text, Is.EqualTo(text));
    }

    [Test]
    public void RoundTrip_LongText_PreservesText()
    {
        // 300 bytes needs a two-byte varint length prefix on the wire.
        var text = new string('x', 300);

        var result = WireTestHelper.RoundTrip(new MsgConCmd { Text = text });

        Assert.That(result.Text, Is.EqualTo(text));
    }

    [Test]
    public void Read_DeclaredLengthLargerThanData_NeverReturnsPartialText()
    {
        // The prefix announces 5 bytes but only 2 follow. Lidgren handles this differently depending on how it
        // was built: debug builds throw a NetException, release builds return the empty string and skip to the
        // end of the buffer. Either way the receiver must never see half a command line, so both outcomes
        // are accepted here and anything else fails the test.
        byte[] wire = [0x05, 0x68, 0x69];
        var incoming = WireTestHelper.CreateIncoming(wire);
        var message = new MsgConCmd();

        try
        {
            message.ReadFromBuffer(incoming, null!);
        }
        catch (NetException)
        {
            return;
        }

        Assert.That(message.Text, Is.EqualTo(string.Empty));
    }
}

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgPlayerList))]
internal sealed class MsgPlayerListWireTest
{
    // Sample user ids. A .NET Guid is stored as a 32-bit group, two 16-bit groups and 8 plain bytes, and its
    // standard byte-array form writes the first three groups little-endian (see the wire arrays below).
    private static readonly Guid GuidA = new("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid GuidB = new("01020304-0506-0708-090a-0b0c0d0e0f10");

    // GuidA as raw bytes:
    //   00112233 (int32, little-endian) -> 33 22 11 00
    //   4455 (int16, little-endian)     -> 55 44
    //   6677 (int16, little-endian)     -> 77 66
    //   8899aabbccddeeff (8 bytes in order) -> 88 99 AA BB CC DD EE FF
    private static readonly byte[] GuidABytes =
        [0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

    // GuidB as raw bytes:
    //   01020304 -> 04 03 02 01, 0506 -> 06 05, 0708 -> 08 07, then 09 0A 0B 0C 0D 0E 0F 10 in order.
    private static readonly byte[] GuidBBytes =
        [0x04, 0x03, 0x02, 0x01, 0x06, 0x05, 0x08, 0x07, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10];

    private static SessionState Entry(Guid id, string? name, SessionStatus status)
    {
        return new SessionState
        {
            UserId = new NetUserId(id),
            Name = name!,
            Status = status,
        };
    }

    /// <summary>
    ///     The wire form of a single entry: Guid A, the name "Bob" and status InGame, preceded by a count of 1.
    ///     <code>
    ///     01 00 00 00                                     count = 1 (int32, little-endian)
    ///     33 22 11 00 55 44 77 66 88 99 AA BB CC DD EE FF user id (16 raw bytes, see GuidABytes)
    ///     03 42 6F 62                                     name: length 3, 'B' = 0x42, 'o' = 0x6F, 'b' = 0x62
    ///     03                                              status InGame = 3
    ///     </code>
    ///     That is 4 + 16 + 4 + 1 = 25 bytes.
    /// </summary>
    private static readonly byte[] OneEntryWire =
    [
        0x01, 0x00, 0x00, 0x00,
        0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
        0x03, 0x42, 0x6F, 0x62,
        0x03,
    ];

    [Test]
    public void Identity_IsReliableUnorderedCoreMessageOnChannelZero()
    {
        WireTestHelper.AssertIdentity(
            new MsgPlayerList(),
            "MsgPlayerList",
            MsgGroups.Core,
            NetDeliveryMethod.ReliableUnordered);
    }

    [Test]
    public void Write_OneEntry_MatchesGoldenBytes()
    {
        var message = new MsgPlayerList
        {
            Plyrs = [Entry(GuidA, "Bob", SessionStatus.InGame)],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(OneEntryWire));
    }

    [Test]
    public void Write_TwoEntries_AreBackToBackInListOrder()
    {
        // Entry 1 is the same as in the single-entry test: 16 + 4 + 1 = 21 bytes.
        // Entry 2 has an empty name (one 00 byte) and status Disconnected = 4: 16 + 1 + 1 = 18 bytes.
        // With the 4-byte count the total is 4 + 21 + 18 = 43 bytes. There are no separators between entries.
        byte[] expected =
        [
            // count = 2
            0x02, 0x00, 0x00, 0x00,
            // entry 1
            0x33, 0x22, 0x11, 0x00, 0x55, 0x44, 0x77, 0x66, 0x88, 0x99, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF,
            0x03, 0x42, 0x6F, 0x62,
            0x03,
            // entry 2
            0x04, 0x03, 0x02, 0x01, 0x06, 0x05, 0x08, 0x07, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
            0x00,
            0x04,
        ];
        var message = new MsgPlayerList
        {
            Plyrs =
            [
                Entry(GuidA, "Bob", SessionStatus.InGame),
                Entry(GuidB, "", SessionStatus.Disconnected),
            ],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_EmptyList_IsJustAZeroCount()
    {
        // Count 0 as a fixed int32: 00 00 00 00. (A varint would be a single byte.)
        byte[] expected = [0x00, 0x00, 0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgPlayerList { Plyrs = [] });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_UserId_UsesDotNetGuidByteLayoutWithoutLengthPrefix()
    {
        // Only the Guid portion of the one-entry golden bytes (offsets 4..19) is checked here, so that a
        // regression in the Guid layout (for example a big-endian "RFC 4122" order) is easy to spot.
        var message = new MsgPlayerList
        {
            Plyrs = [Entry(GuidB, "x", SessionStatus.Zombie)],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire[4..20], Is.EqualTo(GuidBBytes));
    }

    [Test]
    public void Write_NullName_IsWrittenLikeEmptyName()
    {
        // Count 1, the Guid, then a single 00 for the name, then status Connected = 2.
        byte[] expected =
        [
            0x01, 0x00, 0x00, 0x00,
            .. GuidABytes,
            0x00,
            0x02,
        ];
        var message = new MsgPlayerList
        {
            Plyrs = [Entry(GuidA, null, SessionStatus.Connected)],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_NonAsciiName_PrefixesUtf8ByteCount()
    {
        // "e acute" is 2 UTF-8 bytes (C3 A9), so the name is 02 C3 A9. Status Zombie = 0.
        byte[] expected =
        [
            0x01, 0x00, 0x00, 0x00,
            .. GuidABytes,
            0x02, 0xC3, 0xA9,
            0x00,
        ];
        var message = new MsgPlayerList
        {
            Plyrs = [Entry(GuidA, "é", SessionStatus.Zombie)],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    [TestCase(SessionStatus.Zombie, 0x00)]
    [TestCase(SessionStatus.Connecting, 0x01)]
    [TestCase(SessionStatus.Connected, 0x02)]
    [TestCase(SessionStatus.InGame, 0x03)]
    [TestCase(SessionStatus.Disconnected, 0x04)]
    public void Write_Status_IsOneByteWithTheEnumValue(SessionStatus status, byte expectedByte)
    {
        // The status is the last byte of the entry. Its numeric values are part of the wire format, so
        // reordering the enum would break compatibility even though nothing in this class changes.
        var message = new MsgPlayerList
        {
            Plyrs = [Entry(GuidA, "Bob", status)],
        };

        var wire = WireTestHelper.Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(wire, Has.Length.EqualTo(OneEntryWire.Length));
            Assert.That(wire[^1], Is.EqualTo(expectedByte));
        });
    }

    [Test]
    public void Read_OneEntryGoldenBytes_YieldsExpectedSession()
    {
        var message = WireTestHelper.Read<MsgPlayerList>(OneEntryWire);

        Assert.That(message.Plyrs, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(message.Plyrs[0].UserId, Is.EqualTo(new NetUserId(GuidA)));
            Assert.That(message.Plyrs[0].Name, Is.EqualTo("Bob"));
            Assert.That(message.Plyrs[0].Status, Is.EqualTo(SessionStatus.InGame));
        });
    }

    [Test]
    public void Read_ZeroCount_YieldsEmptyNonNullList()
    {
        // The client applies Plyrs unconditionally, so even an empty snapshot must produce a list.
        var message = WireTestHelper.Read<MsgPlayerList>([0x00, 0x00, 0x00, 0x00]);

        Assert.That(message.Plyrs, Is.Not.Null);
        Assert.That(message.Plyrs, Is.Empty);
    }

    [Test]
    public void RoundTrip_PreservesEntriesAndOrder()
    {
        var original = new MsgPlayerList
        {
            Plyrs =
            [
                Entry(GuidB, "Alice", SessionStatus.Connected),
                Entry(GuidA, "日本", SessionStatus.InGame),
                Entry(Guid.Empty, "", SessionStatus.Zombie),
            ],
        };

        var result = WireTestHelper.RoundTrip(original);

        Assert.That(result.Plyrs, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            for (var i = 0; i < original.Plyrs.Count; i++)
            {
                Assert.That(result.Plyrs[i].UserId, Is.EqualTo(original.Plyrs[i].UserId), $"UserId at {i}");
                Assert.That(result.Plyrs[i].Name, Is.EqualTo(original.Plyrs[i].Name), $"Name at {i}");
                Assert.That(result.Plyrs[i].Status, Is.EqualTo(original.Plyrs[i].Status), $"Status at {i}");
            }
        });
    }

    [Test]
    public void RoundTrip_EmptyList_YieldsEmptyList()
    {
        var result = WireTestHelper.RoundTrip(new MsgPlayerList { Plyrs = [] });

        Assert.That(result.Plyrs, Is.Not.Null);
        Assert.That(result.Plyrs, Is.Empty);
    }

    [Test]
    public void RoundTrip_NullName_BecomesEmptyString()
    {
        var original = new MsgPlayerList
        {
            Plyrs = [Entry(GuidA, null, SessionStatus.Connected)],
        };

        var result = WireTestHelper.RoundTrip(original);

        Assert.That(result.Plyrs[0].Name, Is.EqualTo(string.Empty));
    }

    [Test]
    public void RoundTrip_ControlledEntity_IsNotTransmitted()
    {
        // Only user id, name and status travel with this message. A controlled entity set on the sender must
        // neither change the bytes nor appear on the receiving side.
        var withEntity = Entry(GuidA, "Bob", SessionStatus.InGame);
        withEntity.ControlledEntity = new NetEntity(7);
        var message = new MsgPlayerList { Plyrs = [withEntity] };

        var wire = WireTestHelper.Write(message);
        var result = WireTestHelper.Read<MsgPlayerList>(wire);

        Assert.Multiple(() =>
        {
            Assert.That(wire, Is.EqualTo(OneEntryWire));
            Assert.That(result.Plyrs[0].ControlledEntity, Is.Null);
        });
    }

    [Test]
    public void Read_UndefinedStatusByte_IsPassedThroughUnchanged()
    {
        // PINS CURRENT BEHAVIOUR. The wire format has no range check on the status byte: it is cast straight
        // to SessionStatus, so 0xFF (not a defined member) is accepted. This keeps old receivers working when a
        // newer sender appends a status value. If validation is ever added it will need a compatibility story;
        // update this test deliberately when that happens.
        byte[] wire =
        [
            0x01, 0x00, 0x00, 0x00,
            .. GuidABytes,
            0x00,
            0xFF,
        ];

        var message = WireTestHelper.Read<MsgPlayerList>(wire);

        Assert.That((byte) message.Plyrs[0].Status, Is.EqualTo(0xFF));
    }

    [Test]
    public void RoundTrip_UndefinedStatusValue_IsPreserved()
    {
        // Companion of the pin above for the write direction: the cast to byte does not clamp either.
        var original = new MsgPlayerList
        {
            Plyrs = [Entry(GuidA, "Bob", (SessionStatus) 200)],
        };

        var result = WireTestHelper.RoundTrip(original);

        Assert.That((byte) result.Plyrs[0].Status, Is.EqualTo(200));
    }

    [Test]
    public void Read_NegativeCount_Throws()
    {
        // -1 as a little-endian int32 is FF FF FF FF. A negative count is malformed data and must fail
        // (the net manager then disconnects the sender) instead of being treated as an empty list.
        byte[] wire = [0xFF, 0xFF, 0xFF, 0xFF];

        Assert.That(
            () => WireTestHelper.Read<MsgPlayerList>(wire),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Read_MinimumIntCount_Throws()
    {
        // int32.MinValue = 0x80000000 -> 00 00 00 80 (little-endian).
        byte[] wire = [0x00, 0x00, 0x00, 0x80];

        Assert.That(
            () => WireTestHelper.Read<MsgPlayerList>(wire),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Read_CountLargerThanData_Throws()
    {
        // Count 1_000_000 = 0x000F4240 -> 40 42 0F 00, but no entries follow. A bogus count in a hostile packet
        // must end in an exception, not in an endless or enormous read.
        byte[] wire = [0x40, 0x42, 0x0F, 0x00];

        Assert.That(
            () => WireTestHelper.Read<MsgPlayerList>(wire),
            Throws.Exception);
    }

    [Test]
    public void Read_CountOfTwoButOnlyOneEntry_Throws()
    {
        // The golden one-entry bytes with the count changed from 1 to 2.
        byte[] wire = [.. OneEntryWire];
        wire[0] = 0x02;

        Assert.That(
            () => WireTestHelper.Read<MsgPlayerList>(wire),
            Throws.Exception);
    }

    [Test]
    public void Read_TruncatedPayload_Throws([Range(0, 24)] int availableBytes)
    {
        // The complete single-entry payload is 25 bytes. Cutting it at every shorter length (inside the count,
        // inside the Guid, inside the name, or right before the status byte) must fail.
        var truncated = OneEntryWire[..availableBytes];

        Assert.That(
            () => WireTestHelper.Read<MsgPlayerList>(truncated),
            Throws.Exception);
    }
}

[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgConCmdReg))]
internal sealed class MsgConCmdRegWireTest
{
    [Test]
    public void Identity_IsReliableOrderedStringGroupMessageOnChannelZero()
    {
        WireTestHelper.AssertIdentity(
            new MsgConCmdReg(),
            "MsgConCmdReg",
            MsgGroups.String,
            NetDeliveryMethod.ReliableOrdered);
    }

    [Test]
    public void Write_TwoCommands_MatchesGoldenBytes()
    {
        // count = 2 as a fixed uint16, little-endian: 02 00
        //
        // Command 1: Name "ls", Description "list", Help "ls [path]"
        //   Name:        02 | 6C 73                          'l' = 6C, 's' = 73
        //   Description: 04 | 6C 69 73 74                     "list"
        //   Help:        09 | 6C 73 20 5B 70 61 74 68 5D      "ls [path]" is 9 bytes: 'l' 's' ' ' '[' 'p' 'a' 't' 'h' ']'
        // Command 2: Name "q", Description null, Help ""
        //   Name:        01 | 71
        //   Description: 00                                   null is written like the empty string
        //   Help:        00
        //
        // Total: 2 + (3 + 5 + 10) + (2 + 1 + 1) = 24 bytes.
        byte[] expected =
        [
            0x02, 0x00,
            0x02, 0x6C, 0x73,
            0x04, 0x6C, 0x69, 0x73, 0x74,
            0x09, 0x6C, 0x73, 0x20, 0x5B, 0x70, 0x61, 0x74, 0x68, 0x5D,
            0x01, 0x71,
            0x00,
            0x00,
        ];
        var message = new MsgConCmdReg
        {
            Commands =
            [
                new MsgConCmdReg.Command { Name = "ls", Description = "list", Help = "ls [path]" },
                new MsgConCmdReg.Command { Name = "q", Description = null, Help = "" },
            ],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_Command_WritesNameThenDescriptionThenHelp()
    {
        // Each field is one character so the order is visible in the bytes: Name "N" (4E), Description "D"
        // (44), Help "H" (48). The description comes BEFORE the help text; swapping them would make old
        // clients show descriptions as help and vice versa without any error.
        byte[] expected =
        [
            0x01, 0x00,
            0x01, 0x4E,
            0x01, 0x44,
            0x01, 0x48,
        ];
        var message = new MsgConCmdReg
        {
            Commands = [new MsgConCmdReg.Command { Name = "N", Description = "D", Help = "H" }],
        };

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_NullCommandsList_IsABareZeroCount()
    {
        // This is the request form sent by a client: "send me your command list". It is a zero count and nothing
        // else: 00 00 (uint16, little-endian).
        byte[] expected = [0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgConCmdReg { Commands = null });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_DefaultMessage_IsABareZeroCount()
    {
        // Commands is never initialised, so a freshly constructed message is the request form as well.
        byte[] expected = [0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgConCmdReg());

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_EmptyCommandsList_IsIdenticalToNullList()
    {
        // The server cannot (and does not) tell the two forms apart on the wire.
        byte[] expected = [0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgConCmdReg { Commands = [] });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_CommandWithAllNullStrings_WritesThreeZeroBytes()
    {
        // count = 1 (01 00), then Name, Description and Help are each a single 00.
        byte[] expected = [0x01, 0x00, 0x00, 0x00, 0x00];

        var wire = WireTestHelper.Write(new MsgConCmdReg { Commands = [new MsgConCmdReg.Command()] });

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Read_ZeroCount_YieldsEmptyNonNullList()
    {
        // The client iterates Commands without a null check, so reading must never leave it null.
        var message = WireTestHelper.Read<MsgConCmdReg>([0x00, 0x00]);

        Assert.That(message.Commands, Is.Not.Null);
        Assert.That(message.Commands, Is.Empty);
    }

    [Test]
    public void RoundTrip_NullCommandsList_BecomesEmptyNonNullList()
    {
        var result = WireTestHelper.RoundTrip(new MsgConCmdReg { Commands = null });

        Assert.That(result.Commands, Is.Not.Null);
        Assert.That(result.Commands, Is.Empty);
    }

    [Test]
    public void Read_GoldenBytes_YieldsExpectedCommands()
    {
        // Same layout as Write_TwoCommands_MatchesGoldenBytes; see that test for how the bytes are derived.
        byte[] wire =
        [
            0x02, 0x00,
            0x02, 0x6C, 0x73,
            0x04, 0x6C, 0x69, 0x73, 0x74,
            0x09, 0x6C, 0x73, 0x20, 0x5B, 0x70, 0x61, 0x74, 0x68, 0x5D,
            0x01, 0x71,
            0x00,
            0x00,
        ];

        var message = WireTestHelper.Read<MsgConCmdReg>(wire);

        Assert.That(message.Commands, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(message.Commands[0].Name, Is.EqualTo("ls"));
            Assert.That(message.Commands[0].Description, Is.EqualTo("list"));
            Assert.That(message.Commands[0].Help, Is.EqualTo("ls [path]"));
            Assert.That(message.Commands[1].Name, Is.EqualTo("q"));
            Assert.That(message.Commands[1].Description, Is.EqualTo(string.Empty));
            Assert.That(message.Commands[1].Help, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void RoundTrip_PreservesCommandsAndOrder()
    {
        var original = new MsgConCmdReg
        {
            Commands =
            [
                new MsgConCmdReg.Command { Name = "help", Description = "Shows help", Help = "help [command]" },
                new MsgConCmdReg.Command { Name = "日本", Description = "é", Help = new string('h', 300) },
                new MsgConCmdReg.Command { Name = "quit", Description = "", Help = "" },
            ],
        };

        var result = WireTestHelper.RoundTrip(original);

        Assert.That(result.Commands, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            for (var i = 0; i < original.Commands.Count; i++)
            {
                Assert.That(result.Commands[i].Name, Is.EqualTo(original.Commands[i].Name), $"Name at {i}");
                Assert.That(result.Commands[i].Description, Is.EqualTo(original.Commands[i].Description), $"Description at {i}");
                Assert.That(result.Commands[i].Help, Is.EqualTo(original.Commands[i].Help), $"Help at {i}");
            }
        });
    }

    [Test]
    public void RoundTrip_NullStrings_BecomeEmptyStrings()
    {
        var original = new MsgConCmdReg
        {
            Commands = [new MsgConCmdReg.Command { Name = null, Description = null, Help = null }],
        };

        var result = WireTestHelper.RoundTrip(original);

        Assert.That(result.Commands, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Commands[0].Name, Is.EqualTo(string.Empty));
            Assert.That(result.Commands[0].Description, Is.EqualTo(string.Empty));
            Assert.That(result.Commands[0].Help, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void Write_MaximumRepresentableCount_UsesFullSixteenBitCount()
    {
        // 65535 = 0xFFFF is the largest count the 16-bit field can hold: FF FF. Each command here has three
        // null strings, i.e. 3 bytes, so the whole payload is 2 + 65535 * 3 bytes. The count field must stay
        // 16 bits wide; widening it (even to a varint) would break compatibility.
        var commands = new List<MsgConCmdReg.Command>(ushort.MaxValue);
        for (var i = 0; i < ushort.MaxValue; i++)
        {
            commands.Add(new MsgConCmdReg.Command());
        }

        var wire = WireTestHelper.Write(new MsgConCmdReg { Commands = commands });
        var result = WireTestHelper.Read<MsgConCmdReg>(wire);

        Assert.Multiple(() =>
        {
            Assert.That(wire[0], Is.EqualTo(0xFF));
            Assert.That(wire[1], Is.EqualTo(0xFF));
            Assert.That(wire, Has.Length.EqualTo(2 + (ushort.MaxValue * 3)));
            Assert.That(result.Commands, Has.Count.EqualTo(ushort.MaxValue));
        });
    }

    [Test]
    public void Write_MoreThanMaximumRepresentableCount_ThrowsInsteadOfWrappingTheCount()
    {
        // PINS CURRENT BEHAVIOUR. 65536 commands do not fit in the 16-bit count. Silently truncating the
        // count to 0 would produce a packet whose entries are then parsed as garbage by the receiver, so the
        // writer refuses. The compatibility requirement is only that the field stays 16 bits wide and that the
        // count never wraps silently; how failure is reported may change.
        var commands = new List<MsgConCmdReg.Command>(ushort.MaxValue + 1);
        for (var i = 0; i <= ushort.MaxValue; i++)
        {
            commands.Add(new MsgConCmdReg.Command());
        }

        Assert.That(
            () => WireTestHelper.Write(new MsgConCmdReg { Commands = commands }),
            Throws.Exception);
    }

    [Test]
    public void Read_TruncatedCount_Throws([Range(0, 1)] int availableBytes)
    {
        // The count is a fixed 2-byte field, so an empty buffer or a single byte cannot be a valid message.
        byte[] full = [0x00, 0x00];
        var truncated = full[..availableBytes];

        Assert.That(
            () => WireTestHelper.Read<MsgConCmdReg>(truncated),
            Throws.Exception);
    }
}
