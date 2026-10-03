using System;
using System.Linq;
using Lidgren.Network;
using NUnit.Framework;
using Robust.Shared.Network;
using Entry = Robust.Shared.Network.MsgStringTableEntries.Entry;

namespace Robust.Shared.Tests.Networking;

/// <summary>
///     Pins the exact bytes that <see cref="MsgStringTableEntries"/> puts on the wire.
/// </summary>
/// <remarks>
///     <para>
///         Why this matters: this message carries the string table, i.e. the mapping from the one-byte message ids
///         to message names, and it is the first thing a client has to understand when it connects. Clients and
///         servers are released independently, so a client of one build must be able to read the table sent by a
///         server of another. A change of the layout still round-trips inside one build but breaks every
///         connection between different builds before anything else can even be negotiated. The golden-byte tests
///         below fail loudly if that happens; the round-trip tests alone cannot.
///     </para>
///     <para>
///         The layout, field by field (the one-byte message id header is added by the net manager and is not part
///         of the payload that these tests look at):
///         <list type="number">
///             <item><description>
///                 Entry count: a fixed-width 32-bit integer, 4 bytes, little-endian.
///             </description></item>
///             <item><description>
///                 Then, for every entry in array order, first the entry id as a ZIGZAG VARINT and then the entry
///                 string as a Lidgren string (an unsigned base-128 varint with the UTF-8 BYTE count, followed by
///                 the UTF-8 bytes).
///             </description></item>
///         </list>
///     </para>
///     <para>
///         Zigzag maps signed numbers to unsigned ones so that small magnitudes stay small:
///         <c>zigzag(v) = (v &lt;&lt; 1) XOR (v &gt;&gt; 31)</c> with an arithmetic shift, i.e. 0 -> 0, -1 -> 1,
///         1 -> 2, -2 -> 3, 2 -> 4 and so on. The result is written as a base-128 varint: 7 data bits per byte,
///         least significant group first, bit 7 set on every byte except the last. The expected bytes in the tests
///         are worked out by hand in the comments next to them. Please do not generate them by calling the code
///         under test.
///     </para>
///     <para>
///         The serializer argument of Read/WriteToBuffer is not used by this message, so tests pass null (done by
///         <see cref="WireTestHelper"/>).
///     </para>
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgStringTableEntries))]
internal sealed class MsgStringTableEntriesWireTests
{
    // The bootstrap entry as the server sends it: id 0, "MsgStringTableEntries".
    //
    //   01 00 00 00   count = 1 (fixed int32, little-endian)
    //   00            id 0: zigzag(0) = 0 -> one varint byte 00
    //   15            string length: 21 UTF-8 bytes = 0x15 (M-s-g + S-t-r-i-n-g + T-a-b-l-e + E-n-t-r-i-e-s = 3+6+5+7)
    //   4D 73 67               "Msg"     M = 4D, s = 73, g = 67
    //   53 74 72 69 6E 67      "String"  S = 53, t = 74, r = 72, i = 69, n = 6E, g = 67
    //   54 61 62 6C 65         "Table"   T = 54, a = 61, b = 62, l = 6C, e = 65
    //   45 6E 74 72 69 65 73   "Entries" E = 45, n = 6E, t = 74, r = 72, i = 69, e = 65, s = 73
    //
    // Total: 4 + 1 + 1 + 21 = 27 bytes.
    private static readonly byte[] BootstrapWire =
    [
        0x01, 0x00, 0x00, 0x00,
        0x00,
        0x15,
        0x4D, 0x73, 0x67,
        0x53, 0x74, 0x72, 0x69, 0x6E, 0x67,
        0x54, 0x61, 0x62, 0x6C, 0x65,
        0x45, 0x6E, 0x74, 0x72, 0x69, 0x65, 0x73,
    ];

    private static Entry MakeEntry(int id, string? name)
    {
        return new Entry { Id = id, String = name! };
    }

    private static MsgStringTableEntries MakeMessage(params Entry[] entries)
    {
        return new MsgStringTableEntries { Entries = entries };
    }

    // ------------------------------------------------------------------------------------------------------------
    // Identity and defaults
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Identity_IsReliableOrderedStringGroupMessageOnChannelZero()
    {
        // The name is the key of the string table, and id 0 of the table is bound to exactly this name. Renaming the
        // class would break the bootstrap with every older peer. The String group makes the delivery reliable and
        // ordered, which matters because table updates must arrive in the order they were made.
        WireTestHelper.AssertIdentity(
            new MsgStringTableEntries(),
            "MsgStringTableEntries",
            MsgGroups.String,
            NetDeliveryMethod.ReliableOrdered);
    }

    [Test]
    public void NewMessage_HasNoEntriesUntilAssignedOrRead()
    {
        Assert.That(new MsgStringTableEntries().Entries, Is.Null);
    }

    [Test]
    public void NewEntry_HasIdZeroAndNoString()
    {
        var entry = new Entry();

        Assert.Multiple(() =>
        {
            Assert.That(entry.Id, Is.EqualTo(0));
            Assert.That(entry.String, Is.Null);
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Writing: whole-message golden bytes
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Write_BootstrapEntry_MatchesGoldenBytes()
    {
        // See BootstrapWire for the byte-by-byte derivation.
        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(0, "MsgStringTableEntries")));

        Assert.That(wire, Is.EqualTo(BootstrapWire));
    }

    [Test]
    public void Write_EmptyEntriesArray_IsJustAZeroCount()
    {
        // Count 0 as a fixed int32 is 00 00 00 00 (a varint would be a single byte). A table message with no
        // entries is what the client debug command "sendgarbage" sends, so it has to stay writable.
        byte[] expected = [0x00, 0x00, 0x00, 0x00];

        var wire = WireTestHelper.Write(MakeMessage());

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_TwoEntries_AreBackToBackInArrayOrder()
    {
        // Entries: (0, "A") and (2, "BC").
        //
        //   02 00 00 00   count = 2
        //   00            entry 1 id:     zigzag(0) = 0
        //   01 41         entry 1 string: length 1, 'A' = 41
        //   04            entry 2 id:     zigzag(2) = 4
        //   02 42 43      entry 2 string: length 2, 'B' = 42, 'C' = 43
        //
        // There are no separators between entries and no trailing data. Total: 4 + 3 + 4 = 11 bytes.
        byte[] expected =
        [
            0x02, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x41,
            0x04, 0x02, 0x42, 0x43,
        ];

        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(0, "A"), MakeEntry(2, "BC")));

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_EntriesAreNotSortedOrDeduplicated()
    {
        // The writer is a plain serialiser: array order is wire order, even when ids descend, and two entries with
        // the same id both go out. Whether that is meaningful is up to the receiver.
        //
        //   count = 2
        //   id 5 -> zigzag 10 = 0A, "x" = 01 78
        //   id 5 -> zigzag 10 = 0A, "y" = 01 79
        byte[] expected =
        [
            0x02, 0x00, 0x00, 0x00,
            0x0A, 0x01, 0x78,
            0x0A, 0x01, 0x79,
        ];

        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(5, "x"), MakeEntry(5, "y")));

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_CountIsFixedWidthLittleEndianInt32_NotAVarint()
    {
        // 300 entries: 300 = 0x0000012C, written as 2C 01 00 00. A varint would be AC 02.
        //
        // Each entry has a one-character name, i.e. 2 string bytes (length + character). Ids are zigzag varints:
        //   ids 0..63    zigzag = 0..126       (< 128)           -> 1 byte each: 64 * 1  =   64
        //   ids 64..299  zigzag = 128..598     (< 16384)         -> 2 bytes each: 236 * 2 = 472
        // Strings: 300 * 2 = 600. Total: 4 + 64 + 472 + 600 = 1140 bytes.
        var entries = Enumerable.Range(0, 300).Select(i => MakeEntry(i, "x")).ToArray();

        var wire = WireTestHelper.Write(MakeMessage(entries));

        Assert.Multiple(() =>
        {
            Assert.That(wire[..4], Is.EqualTo(new byte[] { 0x2C, 0x01, 0x00, 0x00 }));
            Assert.That(wire, Has.Length.EqualTo(1140));
        });
    }

    // ------------------------------------------------------------------------------------------------------------
    // Writing: the entry id is a zigzag varint
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    [TestCase(0, new byte[] { 0x00 })] // zigzag(0) = 0.
    [TestCase(1, new byte[] { 0x02 })] // zigzag(1) = 1 << 1 = 2.
    [TestCase(-1, new byte[] { 0x01 })] // zigzag(-1) = (-2) XOR (-1) = 1. Negative ids are legal on the wire.
    [TestCase(2, new byte[] { 0x04 })] // zigzag(2) = 4.
    [TestCase(-2, new byte[] { 0x03 })] // zigzag(-2) = (-4) XOR (-1) = 3.
    [TestCase(63, new byte[] { 0x7E })] // zigzag(63) = 126 = 0x7E: the largest id that still fits one byte.
    [TestCase(-64, new byte[] { 0x7F })] // zigzag(-64) = (-128) XOR (-1) = 127 = 0x7F: smallest one-byte id.
    [TestCase(64, new byte[] { 0x80, 0x01 })] // zigzag(64) = 128: low 7 bits 0 | continuation 0x80, then 128 >> 7 = 1.
    [TestCase(-65, new byte[] { 0x81, 0x01 })] // zigzag(-65) = (-130) XOR (-1) = 129: low 7 bits 1 | 0x80 = 0x81, then 1.
    [TestCase(255, new byte[] { 0xFE, 0x03 })] // zigzag(255) = 510 = 0b11_1111110: low 7 bits 0x7E | 0x80 = 0xFE, then 510 >> 7 = 3.
    [TestCase(8191, new byte[] { 0xFE, 0x7F })] // zigzag(8191) = 16382 = 0x3FFE: low 7 bits 0x7E | 0x80 = 0xFE, then 0x7F. Last 2-byte id.
    [TestCase(8192, new byte[] { 0x80, 0x80, 0x01 })] // zigzag(8192) = 16384 = 2^14: two zero groups, then 1.
    [TestCase(int.MaxValue, new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0x0F })] // zigzag = 0xFFFFFFFE: groups 7E, 7F, 7F, 7F, 0F.
    [TestCase(int.MinValue, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F })] // zigzag = 0xFFFFFFFF: groups 7F, 7F, 7F, 7F, 0F.
    public void Write_EntryId_IsZigzagVarint(int id, byte[] expectedIdBytes)
    {
        // The entry is (id, "a"): count 1, the id bytes from the case above, then the string 01 61.
        // The id is the only part that changes between cases.
        byte[] expected = [0x01, 0x00, 0x00, 0x00, .. expectedIdBytes, 0x01, 0x61];

        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(id, "a")));

        Assert.That(wire, Is.EqualTo(expected));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Writing: the entry string
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Write_NullString_IsASingleZeroByte()
    {
        // Null is written exactly like the empty string: length 0 and no characters. Id 3 is zigzag 6.
        //   01 00 00 00 | 06 | 00
        byte[] expected = [0x01, 0x00, 0x00, 0x00, 0x06, 0x00];

        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(3, null)));

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_EmptyString_IsASingleZeroByte()
    {
        byte[] expected = [0x01, 0x00, 0x00, 0x00, 0x06, 0x00];

        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(3, "")));

        Assert.That(wire, Is.EqualTo(expected));
    }

    [Test]
    public void Write_NonAsciiString_PrefixesUtf8ByteCountNotCharacterCount()
    {
        // "e acute" (U+00E9) is one character but two UTF-8 bytes (C3 A9), so the length prefix is 02, not 01.
        // "Japanese 'nihon'" (U+65E5 U+672C) is two characters but six UTF-8 bytes: E6 97 A5 | E6 9C AC.
        //   count 1 | id 0 -> 00 | 02 C3 A9
        //   count 1 | id 0 -> 00 | 06 E6 97 A5 E6 9C AC
        byte[] expectedAccent = [0x01, 0x00, 0x00, 0x00, 0x00, 0x02, 0xC3, 0xA9];
        byte[] expectedCjk = [0x01, 0x00, 0x00, 0x00, 0x00, 0x06, 0xE6, 0x97, 0xA5, 0xE6, 0x9C, 0xAC];

        Assert.Multiple(() =>
        {
            Assert.That(WireTestHelper.Write(MakeMessage(MakeEntry(0, "é"))), Is.EqualTo(expectedAccent));
            Assert.That(WireTestHelper.Write(MakeMessage(MakeEntry(0, "日本"))), Is.EqualTo(expectedCjk));
        });
    }

    [Test]
    public void Write_LongString_UsesTwoByteVarintLengthPrefix()
    {
        // 200 = 0b1_1001000: low 7 bits are 0x48, with the continuation bit that is C8, then 200 >> 7 = 01.
        // The string is 'a' (61) repeated 200 times.
        byte[] expected =
        [
            0x01, 0x00, 0x00, 0x00,
            0x00,
            0xC8, 0x01,
            .. Enumerable.Repeat((byte) 0x61, 200),
        ];

        var wire = WireTestHelper.Write(MakeMessage(MakeEntry(0, new string('a', 200))));

        Assert.That(wire, Is.EqualTo(expected));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Writing: invalid use
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Write_NullEntries_ThrowsInvalidOperationException()
    {
        // A message that was never filled in must not silently produce a packet that claims "0 entries".
        var message = new MsgStringTableEntries();

        Assert.That(
            () => WireTestHelper.Write(message),
            Throws.TypeOf<InvalidOperationException>());
    }

    // ------------------------------------------------------------------------------------------------------------
    // Reading
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Read_BootstrapGoldenBytes_YieldsBootstrapEntry()
    {
        var message = WireTestHelper.Read<MsgStringTableEntries>(BootstrapWire);

        Assert.That(message.Entries, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(message.Entries[0].Id, Is.EqualTo(0));
            Assert.That(message.Entries[0].String, Is.EqualTo("MsgStringTableEntries"));
        });
    }

    [Test]
    public void Read_TwoEntries_YieldsEntriesInWireOrder()
    {
        // The golden bytes of Write_TwoEntries_AreBackToBackInArrayOrder.
        byte[] wire =
        [
            0x02, 0x00, 0x00, 0x00,
            0x00, 0x01, 0x41,
            0x04, 0x02, 0x42, 0x43,
        ];

        var message = WireTestHelper.Read<MsgStringTableEntries>(wire);

        Assert.That(message.Entries, Has.Length.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(message.Entries[0].Id, Is.EqualTo(0));
            Assert.That(message.Entries[0].String, Is.EqualTo("A"));
            Assert.That(message.Entries[1].Id, Is.EqualTo(2));
            Assert.That(message.Entries[1].String, Is.EqualTo("BC"));
        });
    }

    [Test]
    public void Read_ZeroCount_YieldsEmptyNonNullArray()
    {
        // The receiving client iterates Entries without a null check, so reading must never leave it null.
        var message = WireTestHelper.Read<MsgStringTableEntries>([0x00, 0x00, 0x00, 0x00]);

        Assert.That(message.Entries, Is.Not.Null);
        Assert.That(message.Entries, Is.Empty);
    }

    [Test]
    [TestCase(new byte[] { 0x00 }, 0)]
    [TestCase(new byte[] { 0x02 }, 1)]
    [TestCase(new byte[] { 0x01 }, -1)]
    [TestCase(new byte[] { 0x7E }, 63)]
    [TestCase(new byte[] { 0x80, 0x01 }, 64)]
    [TestCase(new byte[] { 0x81, 0x01 }, -65)]
    [TestCase(new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0x0F }, int.MaxValue)]
    [TestCase(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }, int.MinValue)]
    public void Read_EntryId_IsDecodedAsZigzagVarint(byte[] idBytes, int expectedId)
    {
        // The mirror image of Write_EntryId_IsZigzagVarint: unzigzag(n) = (n >> 1) XOR -(n & 1), e.g. 1 -> -1,
        // 129 -> -65.
        byte[] wire = [0x01, 0x00, 0x00, 0x00, .. idBytes, 0x01, 0x61];

        var message = WireTestHelper.Read<MsgStringTableEntries>(wire);

        Assert.That(message.Entries, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(message.Entries[0].Id, Is.EqualTo(expectedId));
            Assert.That(message.Entries[0].String, Is.EqualTo("a"));
        });
    }

    [Test]
    public void Read_ZeroLengthString_YieldsEmptyStringNotNull()
    {
        // The wire cannot tell null from empty and reading never produces null. A receiving string table treats
        // an empty name as "remove this id", so this is what a removal looks like after it went over the wire.
        byte[] wire = [0x01, 0x00, 0x00, 0x00, 0x06, 0x00];

        var message = WireTestHelper.Read<MsgStringTableEntries>(wire);

        Assert.Multiple(() =>
        {
            Assert.That(message.Entries[0].Id, Is.EqualTo(3));
            Assert.That(message.Entries[0].String, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void Read_ThenWrite_ReproducesTheOriginalBytes()
    {
        // Canonical input stays canonical: nothing is lost or reordered by a read followed by a write.
        var message = WireTestHelper.Read<MsgStringTableEntries>(BootstrapWire);

        var wire = WireTestHelper.Write(message);

        Assert.That(wire, Is.EqualTo(BootstrapWire));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Round trips
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void RoundTrip_PreservesEntriesAndOrder()
    {
        var original = MakeMessage(
            MakeEntry(0, "MsgStringTableEntries"),
            MakeEntry(2, "MsgPlayerListReq"),
            MakeEntry(255, "é日本"),
            MakeEntry(256, new string('z', 300)),
            MakeEntry(-1, "NegativeId"),
            MakeEntry(int.MaxValue, "Max"),
            MakeEntry(int.MinValue, "Min"));

        var result = WireTestHelper.RoundTrip(original);

        Assert.That(result.Entries, Has.Length.EqualTo(original.Entries.Length));
        Assert.Multiple(() =>
        {
            for (var i = 0; i < original.Entries.Length; i++)
            {
                Assert.That(result.Entries[i].Id, Is.EqualTo(original.Entries[i].Id), $"Id at {i}");
                Assert.That(result.Entries[i].String, Is.EqualTo(original.Entries[i].String), $"String at {i}");
            }
        });
    }

    [Test]
    public void RoundTrip_EmptyEntriesArray_YieldsEmptyArray()
    {
        var result = WireTestHelper.RoundTrip(MakeMessage());

        Assert.That(result.Entries, Is.Not.Null);
        Assert.That(result.Entries, Is.Empty);
    }

    [Test]
    public void RoundTrip_NullString_BecomesEmptyString()
    {
        var result = WireTestHelper.RoundTrip(MakeMessage(MakeEntry(7, null)));

        Assert.That(result.Entries, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(result.Entries[0].Id, Is.EqualTo(7));
            Assert.That(result.Entries[0].String, Is.EqualTo(string.Empty), "A null name is never recovered on read.");
        });
    }

    [Test]
    public void RoundTrip_FullSizedTable_PreservesAllEntries()
    {
        // The largest table the one-byte message id can address: ids 0..255. The table is not chunked, so it all
        // travels in one message.
        var entries = Enumerable.Range(0, 256).Select(i => MakeEntry(i, "Msg" + i)).ToArray();

        var result = WireTestHelper.RoundTrip(MakeMessage(entries));

        Assert.That(
            result.Entries.Select(e => (e.Id, e.String)),
            Is.EqualTo(entries.Select(e => (e.Id, e.String))));
    }

    // ------------------------------------------------------------------------------------------------------------
    // Reading: malformed input
    // ------------------------------------------------------------------------------------------------------------

    [Test]
    public void Read_TruncatedCount_Throws([Range(0, 3)] int availableBytes)
    {
        // The entry count is a fixed 4-byte field, so a shorter buffer cannot be a valid message. The exception
        // makes the net manager drop the sender instead of inventing a count from partial data.
        byte[] full = [0x01, 0x00, 0x00, 0x00];
        var truncated = full[..availableBytes];

        Assert.That(
            () => WireTestHelper.Read<MsgStringTableEntries>(truncated),
            Throws.Exception);
    }

    [Test]
    public void Read_StringLongerThanRemainingData_NeverReturnsPartialString()
    {
        // The entry declares a 5 byte string ("05") but only 2 bytes follow. Lidgren handles this differently
        // depending on how it was built: debug builds throw a NetException, release builds skip to the end of the
        // buffer and return the empty string. Either way the receiver must never see half a name, so both outcomes
        // are accepted and anything else fails the test.
        byte[] wire = [0x01, 0x00, 0x00, 0x00, 0x04, 0x05, 0x61, 0x62];
        var incoming = WireTestHelper.CreateIncoming(wire);
        var message = new MsgStringTableEntries();

        try
        {
            message.ReadFromBuffer(incoming, null!);
        }
        catch (NetException)
        {
            return;
        }

        Assert.That(message.Entries, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(message.Entries[0].Id, Is.EqualTo(2));
            Assert.That(message.Entries[0].String, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void Read_CountThatCannotBeAnArray_Throws()
    {
        // 0xFFFFFFFF entries (FF FF FF FF) cannot be allocated as an array. The array is created before any entry
        // is read, so the failure is immediate and does not depend on the data that follows. Which exception type
        // comes out (overflow or out of memory) is a runtime detail, so only "an exception" is required.
        byte[] wire = [0xFF, 0xFF, 0xFF, 0xFF];

        Assert.That(
            () => WireTestHelper.Read<MsgStringTableEntries>(wire),
            Throws.Exception);
    }
}
