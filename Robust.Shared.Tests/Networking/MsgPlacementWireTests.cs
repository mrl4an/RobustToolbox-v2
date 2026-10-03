using System;
using System.Numerics;
using Lidgren.Network;
using NUnit.Framework;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;

namespace Robust.Shared.Tests.Networking;

/// <summary>
///     Pins the exact bit stream that <see cref="MsgPlacement"/> puts on the wire for every placement case.
/// </summary>
/// <remarks>
///     <para>
///         Why this matters: clients and servers are built and released independently, so an old client must keep
///         understanding a new server and the other way round. <see cref="MsgPlacement"/> is the hardest message to
///         keep compatible because the Lidgren buffer is a BIT stream: a bool takes one bit, and nothing re-aligns the
///         stream afterwards, so every field after a bool is written at an unaligned bit offset. A refactor that
///         turns a bool into a byte, moves a field, or pads to a byte boundary still round-trips inside one build,
///         but silently corrupts every field after it for peers running a different build. The golden-bytes tests
///         below fail loudly if that happens; the round-trip tests alone cannot.
///     </para>
///     <para>
///         How the expected bytes were derived. Lidgren writes least-significant-bit first: bit <c>i</c> of a value
///         written at stream bit offset <c>p</c> lands in byte <c>(p + i) / 8</c>, at bit <c>(p + i) % 8</c> of that
///         byte. In other words the whole payload is one big little-endian integer, and a field written at offset
///         <c>p</c> contributes <c>value &lt;&lt; p</c> to it. Fields that start at a byte boundary therefore appear as
///         plain little-endian bytes, and a field that starts <c>s</c> bits past a boundary is the same bytes
///         shifted left by <c>s</c> bits across the byte sequence:
///         <c>out[n] = (previous &gt;&gt; (8 - s)) | (current &lt;&lt; s)</c>, truncated to 8 bits, where the bits
///         already present in the partial byte act as the "previous" value for the first output byte. Each golden
///         array below shows that arithmetic in a comment. The values were additionally cross-checked against an
///         independent bit packer outside of this code base, and each case also has a "mirror" test that
///         rebuilds the same stream with raw Lidgren <see cref="NetOutgoingMessage"/> calls in the order given by
///         the specification and requires identical bytes and an identical bit length.
///     </para>
///     <para>
///         Conventions used by every test in this file:
///         <list type="bullet">
///             <item><description>
///                 Buffers contain only the message payload. The one-byte message id header is added by the net
///                 manager before WriteToBuffer is called and consumed before ReadFromBuffer.
///             </description></item>
///             <item><description>
///                 The exact BIT length is asserted next to the bytes, because the trailing padding bits of the last
///                 byte are not part of the payload and the bytes alone cannot tell where the payload ends.
///             </description></item>
///             <item><description>
///                 The serializer argument is unused by this message, so tests pass null.
///             </description></item>
///         </list>
///     </para>
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
[TestOf(typeof(MsgPlacement))]
internal sealed class MsgPlacementWireTests
{
    // ------------------------------------------------------------------------------------------------------------
    // Golden payloads. Every array below is the payload of one message, written bit by bit as described in the
    // class remarks. "c" lists the bytes that are written after the unaligned prefix bits, "prefix" is the value of
    // the bits that already occupy the low end of the partial byte.
    // ------------------------------------------------------------------------------------------------------------

    // StartPlacement, Range = 0x01020304, IsTile = true, ObjType = "ab", AlignOption = "c".
    //
    //   byte 0      00                  PlaceType StartPlacement = 0
    //   bytes 1..4  04 03 02 01         Range, int32 little-endian (aligned: offset 8)
    //   bit 40      1                   IsTile; from here on the stream is 1 bit past a byte boundary
    //   then, as plain bytes: 02 61 62  ObjType: length 2, 'a' = 0x61, 'b' = 0x62
    //                         01 63     AlignOption: length 1, 'c' = 0x63
    //
    // Shifting those five bytes left by one bit, with the IsTile bit (value 1) as the prefix of byte 5:
    //   byte 5  = 0x01 | (0x02 << 1)                = 0x01 | 0x04          = 0x05
    //   byte 6  = (0x02 >> 7) | (0x61 << 1 & 0xFF)  = 0x00 | 0xC2          = 0xC2
    //   byte 7  = (0x61 >> 7) | (0x62 << 1 & 0xFF)  = 0x00 | 0xC4          = 0xC4
    //   byte 8  = (0x62 >> 7) | (0x01 << 1 & 0xFF)  = 0x00 | 0x02          = 0x02
    //   byte 9  = (0x01 >> 7) | (0x63 << 1 & 0xFF)  = 0x00 | 0xC6          = 0xC6
    //   byte 10 = (0x63 >> 7)                       =                        0x00 (only padding bits left)
    //
    // Total: 40 + 1 + 5 * 8 = 81 bits, i.e. 11 bytes with 7 padding bits.
    private static readonly byte[] StartPlacementTileWire =
        [0x00, 0x04, 0x03, 0x02, 0x01, 0x05, 0xC2, 0xC4, 0x02, 0xC6, 0x00];

    private const int StartPlacementTileBits = 81;

    // StartPlacement, Range = -2, IsTile = false, ObjType = "a", AlignOption = null (written as empty).
    //
    //   byte 0      00                  PlaceType
    //   bytes 1..4  FE FF FF FF         Range = -2 = 0xFFFFFFFE, little-endian
    //   bit 40      0                   IsTile = false, prefix value 0
    //   then, as plain bytes: 01 61     ObjType: length 1, 'a' = 0x61
    //                         00        AlignOption: length 0
    //
    //   byte 5 = 0x00 | (0x01 << 1)                = 0x02
    //   byte 6 = (0x01 >> 7) | (0x61 << 1 & 0xFF)  = 0xC2
    //   byte 7 = (0x61 >> 7) | (0x00 << 1)         = 0x00
    //   byte 8 = (0x00 >> 7)                       = 0x00 (padding only)
    //
    // Total: 40 + 1 + 3 * 8 = 65 bits, i.e. 9 bytes.
    private static readonly byte[] StartPlacementEntityWire =
        [0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x02, 0xC2, 0x00, 0x00];

    private const int StartPlacementEntityBits = 65;

    // StartPlacement, Range = 0, IsTile = true, both strings empty (or null).
    //
    //   byte 0      00                  PlaceType
    //   bytes 1..4  00 00 00 00         Range
    //   bit 40      1                   IsTile
    //   bits 41..48 00000000            ObjType length 0
    //   bits 49..56 00000000            AlignOption length 0
    //
    // Only bit 40 is set, which is bit 0 of byte 5: byte 5 = 0x01. Total 57 bits = 8 bytes.
    private static readonly byte[] StartPlacementEmptyStringsWire =
        [0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00];

    private const int StartPlacementEmptyStringsBits = 57;

    // RequestPlacement for a tile: Align = "a", IsTile = true, Replacement = false, TileType = 0x01020304,
    // NetCoordinates = (entity 7, X = 1.0f, Y = -2.0f), DirRcv = North (4), Mirrored = true.
    //
    //   byte 0   03                      PlaceType RequestPlacement = 3
    //   byte 1   01                      Align length 1
    //   byte 2   61                      Align "a"
    //   bit 24   1                       IsTile
    //   bit 25   0                       Replacement          -> prefix = 0b01 = 0x01, stream is 2 bits past a boundary
    //   then, as plain bytes c0..c16:
    //     c0..c3   04 03 02 01           TileType 0x01020304, little-endian
    //     c4..c7   07 00 00 00           NetCoordinates entity id 7
    //     c8..c11  00 00 80 3F           X = 1.0f = 0x3F800000
    //     c12..c15 00 00 00 C0           Y = -2.0f = 0xC0000000
    //     c16      04                    DirRcv North
    //   and finally the Mirrored bit (1).
    //
    // Shift by 2: byte (3 + i) = (c[i-1] >> 6) | (c[i] << 2 & 0xFF), with the prefix standing in for c[-1] in the low bits.
    //   byte  3 = 0x01 | (0x04 << 2)                 = 0x11
    //   byte  4 = (0x04 >> 6) | (0x03 << 2)          = 0x0C
    //   byte  5 = (0x03 >> 6) | (0x02 << 2)          = 0x08
    //   byte  6 = (0x02 >> 6) | (0x01 << 2)          = 0x04
    //   byte  7 = (0x01 >> 6) | (0x07 << 2)          = 0x1C
    //   bytes 8..13 = 00 x 6                         (c5..c9 are zero, and c10 = 0x80 << 2 truncates to 0;
    //                                                 its bit 7 is carried into the next byte, below)
    //   byte 14 = (c10 >> 6) | (c11 << 2 & 0xFF)   with c10 = 0x80, c11 = 0x3F: 0x02 | 0xFC = 0xFE
    //             (bit 7 of c10 moves into bit 1 of this byte, and 0x3F << 2 = 0xFC)
    //   bytes 15..18 = 00 x 4                        (c12..c14 are zero, 0x3F >> 6 = 0, and c15 = 0xC0 << 2 is
    //                                                 truncated to 0; its two high bits are carried into byte 19)
    //   byte 19 = (c15 >> 6) | (c16 << 2)  = (0xC0 >> 6) | (0x04 << 2) = 0x03 | 0x10 = 0x13
    //   byte 20 = (c16 >> 6) | (Mirrored << 2)       = 0x00 | 0x04 = 0x04
    //
    // Total: 24 + 2 + 32 + 96 + 8 + 1 = 163 bits, i.e. 21 bytes with 5 padding bits.
    private static readonly byte[] RequestPlacementTileWire =
    [
        0x03, 0x01, 0x61,
        0x11, 0x0C, 0x08, 0x04, 0x1C,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFE,
        0x00, 0x00, 0x00, 0x00,
        0x13, 0x04,
    ];

    private const int RequestPlacementTileBits = 163;

    // RequestPlacement for an entity: Align = "a", IsTile = false, Replacement = true,
    // EntityTemplateName = "AB", NetCoordinates = (entity 1, X = 1.5f, Y = 0.25f), DirRcv = West (6),
    // Mirrored = false.
    //
    //   byte 0   03                      PlaceType
    //   byte 1   01, byte 2  61          Align "a"
    //   bit 24   0                       IsTile
    //   bit 25   1                       Replacement          -> prefix = 0b10 = 0x02
    //   then, as plain bytes c0..c15:
    //     c0..c2   02 41 42              EntityTemplateName: length 2, 'A' = 0x41, 'B' = 0x42
    //     c3..c6   01 00 00 00           entity id 1
    //     c7..c10  00 00 C0 3F           X = 1.5f  = 0x3FC00000
    //     c11..c14 00 00 80 3E           Y = 0.25f = 0x3E800000
    //     c15      06                    DirRcv West
    //   and finally the Mirrored bit (0).
    //
    // Shift by 2:
    //   byte  3 = 0x02 | (0x02 << 2)                 = 0x0A
    //   byte  4 = (0x02 >> 6) | (0x41 << 2 & 0xFF)   = 0x04
    //   byte  5 = (0x41 >> 6) | (0x42 << 2 & 0xFF)   = 0x01 | 0x08 = 0x09
    //   byte  6 = (0x42 >> 6) | (0x01 << 2)          = 0x01 | 0x04 = 0x05
    //   bytes 7..12 = 00 x 6                         (c4..c8 are zero and c9 = 0xC0 << 2 truncates to 0)
    //   byte 13 = (0xC0 >> 6) | (0x3F << 2)          = 0x03 | 0xFC = 0xFF
    //   bytes 14..16 = 00 x 3
    //   byte 17 = (0x80 >> 6) | (0x3E << 2 & 0xFF)   with c13 = 0x80, c14 = 0x3E: 0x02 | 0xF8 = 0xFA
    //   byte 18 = (0x3E >> 6) | (0x06 << 2)          = 0x00 | 0x18 = 0x18
    //   byte 19 = (0x06 >> 6) | (Mirrored << 2)      = 0x00
    //
    // Total: 24 + 2 + 24 + 96 + 8 + 1 = 155 bits, i.e. 20 bytes with 5 padding bits.
    private static readonly byte[] RequestPlacementEntityWire =
    [
        0x03, 0x01, 0x61,
        0x0A, 0x04, 0x09, 0x05,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFF,
        0x00, 0x00, 0x00,
        0xFA, 0x18, 0x00,
    ];

    private const int RequestPlacementEntityBits = 155;

    // RequestPlacement for a tile with BOTH flag bits set: Align = "a", IsTile = true, Replacement = true,
    // TileType = -1, NetCoordinates = (entity 7, 1.0f, -2.0f), DirRcv = East (2), Mirrored = false.
    //
    // Same layout as RequestPlacementTileWire, but the prefix is 0b11 = 0x03 and the first four shifted bytes come
    // from TileType = 0xFFFFFFFF (FF FF FF FF):
    //   byte 3 = 0x03 | (0xFF << 2 & 0xFF) = 0x03 | 0xFC = 0xFF
    //   byte 4 = (0xFF >> 6) | 0xFC         = 0x03 | 0xFC = 0xFF
    //   byte 5 = 0xFF, byte 6 = 0xFF
    //   byte 7 = (0xFF >> 6) | (0x07 << 2) = 0x03 | 0x1C = 0x1F
    //   the coordinate bytes shift exactly as in the tile case (00 x 6, FE, 00 x 4),
    //   byte 19 = (0xC0 >> 6) | (0x02 << 2) = 0x03 | 0x08 = 0x0B
    //   byte 20 = Mirrored false = 0x00
    // Total: 163 bits, 21 bytes.
    private static readonly byte[] RequestPlacementTileBothFlagsWire =
    [
        0x03, 0x01, 0x61,
        0xFF, 0xFF, 0xFF, 0xFF, 0x1F,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFE,
        0x00, 0x00, 0x00, 0x00,
        0x0B, 0x00,
    ];

    // RequestPlacement for an entity with everything empty and DirRcv = Invalid (-1, written as 0xFF):
    // Align = "", IsTile = false, Replacement = false, EntityTemplateName = "", NetCoordinates default
    // (entity 0, 0.0f, 0.0f), Mirrored = false.
    //
    //   byte 0   03                      PlaceType
    //   byte 1   00                      Align length 0
    //   bits 16, 17   0 0                IsTile, Replacement -> prefix = 0
    //   then, as plain bytes c0..c13:
    //     c0        00                   EntityTemplateName length 0
    //     c1..c12   00 x 12              NetCoordinates (all zero)
    //     c13       FF                   DirRcv Invalid, as byte 255
    //   and finally the Mirrored bit (0).
    //
    // Shift by 2:
    //   bytes 2..14 = 00 x 13            (prefix and c0..c12 are all zero)
    //   byte 15 = (c12 >> 6) | (0xFF << 2 & 0xFF) = 0x00 | 0xFC = 0xFC
    //   byte 16 = (0xFF >> 6) | (Mirrored << 2)   = 0x03 | 0x00 = 0x03
    //
    // Total: 8 + 8 + 2 + 8 + 96 + 8 + 1 = 131 bits, i.e. 17 bytes.
    private static readonly byte[] RequestPlacementInvalidDirectionWire =
    [
        0x03, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xFC, 0x03,
    ];

    private const int RequestPlacementInvalidDirectionBits = 131;

    // RequestEntRemove, EntityUid = 0x01020304.
    //   byte 0      04                   PlaceType RequestEntRemove = 4
    //   bytes 1..4  04 03 02 01          entity id as int32 little-endian (aligned, no bool in this case)
    // Total: 40 bits = 5 bytes, no padding.
    private static readonly byte[] RequestEntRemoveWire = [0x04, 0x04, 0x03, 0x02, 0x01];

    private const int RequestEntRemoveBits = 40;

    // RequestRectRemove, NetCoordinates = (entity 7, X = 1.0f, Y = -2.0f), RectSize = (2.5f, 4.0f).
    //   byte 0        05                 PlaceType RequestRectRemove = 5
    //   bytes 1..4    07 00 00 00        entity id 7
    //   bytes 5..8    00 00 80 3F        X = 1.0f  = 0x3F800000
    //   bytes 9..12   00 00 00 C0        Y = -2.0f = 0xC0000000
    //   bytes 13..16  00 00 20 40        RectSize.X = 2.5f = 0x40200000
    //   bytes 17..20  00 00 80 40        RectSize.Y = 4.0f = 0x40800000
    // Total: 8 + 96 + 64 = 168 bits = 21 bytes. Everything is byte aligned because the case has no bool.
    private static readonly byte[] RequestRectRemoveWire =
    [
        0x05,
        0x07, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x80, 0x3F,
        0x00, 0x00, 0x00, 0xC0,
        0x00, 0x00, 0x20, 0x40,
        0x00, 0x00, 0x80, 0x40,
    ];

    private const int RequestRectRemoveBits = 168;

    // --------------------------------------------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Creates an empty Lidgren outgoing buffer without needing a connection. <see cref="NetOutgoingMessage"/>
    ///     has an internal constructor, so it is obtained from an unstarted <see cref="NetClient"/>, exactly like
    ///     <see cref="WireTestHelper.Write"/> does. This test needs its own variant because it must also report the
    ///     exact bit length, which <see cref="WireTestHelper.Write"/> does not expose.
    /// </summary>
    private static NetOutgoingMessage CreateBuffer()
    {
        var peer = new NetClient(new NetPeerConfiguration("RobustMsgPlacementWireTests"));
        return peer.CreateMessage();
    }

    private static (byte[] Bytes, int Bits) Extract(NetOutgoingMessage buffer)
    {
        return (buffer.Data.AsSpan(0, buffer.LengthBytes).ToArray(), buffer.LengthBits);
    }

    private static (byte[] Bytes, int Bits) Write(MsgPlacement message)
    {
        var buffer = CreateBuffer();
        message.WriteToBuffer(buffer, null!);
        return Extract(buffer);
    }

    /// <summary>
    ///     Reads <paramref name="payload"/> and checks that the reader stopped at exactly
    ///     <paramref name="expectedBits"/>. <see cref="WireTestHelper.Read{T}"/> cannot be used here because it
    ///     requires the reader to end on a byte boundary, and the unaligned cases end inside the last byte.
    /// </summary>
    private static MsgPlacement Read(byte[] payload, int expectedBits)
    {
        var incoming = WireTestHelper.CreateIncoming(payload);
        var message = new MsgPlacement();

        message.ReadFromBuffer(incoming, null!);

        Assert.That(incoming.Position, Is.EqualTo((long) expectedBits),
            "ReadFromBuffer must stop exactly after the last field of the case.");
        return message;
    }

    private static MsgPlacement RoundTrip(MsgPlacement message)
    {
        var (bytes, bits) = Write(message);
        return Read(bytes, bits);
    }

    private static MsgPlacement TileRequest()
    {
        return new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = "a",
            IsTile = true,
            Replacement = false,
            TileType = 0x01020304,
            NetCoordinates = new NetCoordinates(new NetEntity(7), new Vector2(1.0f, -2.0f)),
            DirRcv = Direction.North,
            Mirrored = true,
        };
    }

    private static MsgPlacement EntityRequest()
    {
        return new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = "a",
            IsTile = false,
            Replacement = true,
            EntityTemplateName = "AB",
            NetCoordinates = new NetCoordinates(new NetEntity(1), new Vector2(1.5f, 0.25f)),
            DirRcv = Direction.West,
            Mirrored = false,
        };
    }

    private static MsgPlacement RectRemove()
    {
        return new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestRectRemove,
            NetCoordinates = new NetCoordinates(new NetEntity(7), new Vector2(1.0f, -2.0f)),
            RectSize = new Vector2(2.5f, 4.0f),
        };
    }

    // --------------------------------------------------------------------------------------------------------
    // Identity
    // --------------------------------------------------------------------------------------------------------

    [Test]
    public void Identity_IsReliableUnorderedCommandMessageOnChannelZero()
    {
        // The type name is the key of the net string table that is synced on connect, so renaming the class breaks
        // every peer built with the old name. The Command group selects the (reliable, unordered) delivery.
        WireTestHelper.AssertIdentity(
            new MsgPlacement(),
            "MsgPlacement",
            MsgGroups.Command,
            NetDeliveryMethod.ReliableUnordered);
    }

    // --------------------------------------------------------------------------------------------------------
    // StartPlacement (server -> client)
    // --------------------------------------------------------------------------------------------------------

    private static MsgPlacement StartTile()
    {
        return new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.StartPlacement,
            Range = 0x01020304,
            IsTile = true,
            ObjType = "ab",
            AlignOption = "c",
        };
    }

    [Test]
    public void StartPlacement_Write_Tile_MatchesGoldenBytes()
    {
        // The IsTile bit pushes both strings one bit off byte alignment, including their varint length bytes.
        // See StartPlacementTileWire for the bit arithmetic.
        var (bytes, bits) = Write(StartTile());

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(StartPlacementTileWire));
            Assert.That(bits, Is.EqualTo(StartPlacementTileBits));
        });
    }

    [Test]
    public void StartPlacement_Write_EntityWithNullAlignOption_MatchesGoldenBytes()
    {
        // A null string is written exactly like the empty string (single varint byte 0), never as an error.
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.StartPlacement,
            Range = -2,
            IsTile = false,
            ObjType = "a",
            AlignOption = null,
        };

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(StartPlacementEntityWire));
            Assert.That(bits, Is.EqualTo(StartPlacementEntityBits));
        });
    }

    [Test]
    public void StartPlacement_Write_NullStrings_AreWrittenAsEmpty()
    {
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.StartPlacement,
            Range = 0,
            IsTile = true,
            ObjType = null,
            AlignOption = null,
        };

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(StartPlacementEmptyStringsWire));
            Assert.That(bits, Is.EqualTo(StartPlacementEmptyStringsBits));
        });
    }

    [Test]
    public void StartPlacement_Write_MirrorsRawLidgrenCallsInSpecOrder()
    {
        // Independent cross-check of the golden array: build the stream with plain Lidgren calls in the order the
        // specification gives (type, Range, IsTile, ObjType, AlignOption) and require identical output.
        var buffer = CreateBuffer();
        buffer.Write((byte) 0);
        buffer.Write(0x01020304);
        buffer.Write(true);
        buffer.Write("ab");
        buffer.Write("c");
        var expected = Extract(buffer);

        var actual = Write(StartTile());

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            Assert.That(actual.Bits, Is.EqualTo(expected.Bits));
        });
    }

    [Test]
    public void StartPlacement_Write_IgnoresPropertiesOfOtherCases()
    {
        // Only Range, IsTile, ObjType and AlignOption travel with StartPlacement. Setting the others must not
        // change the wire bytes, otherwise old receivers would read garbage.
        var message = StartTile();
        message.Align = "zzz";
        message.Replacement = true;
        message.TileType = 99;
        message.EntityTemplateName = "ignored";
        message.NetCoordinates = new NetCoordinates(new NetEntity(5), new Vector2(3, 4));
        message.DirRcv = Direction.East;
        message.EntityUid = new NetEntity(12);
        message.RectSize = new Vector2(8, 9);
        message.Mirrored = true;

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(StartPlacementTileWire));
            Assert.That(bits, Is.EqualTo(StartPlacementTileBits));
        });
    }

    [Test]
    public void StartPlacement_Read_Tile_GoldenBytes()
    {
        var message = Read(StartPlacementTileWire, StartPlacementTileBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.PlaceType, Is.EqualTo(PlacementManagerMessage.StartPlacement));
            Assert.That(message.Range, Is.EqualTo(0x01020304));
            Assert.That(message.IsTile, Is.True);
            Assert.That(message.ObjType, Is.EqualTo("ab"));
            Assert.That(message.AlignOption, Is.EqualTo("c"));
        });
    }

    [Test]
    public void StartPlacement_Read_Entity_GoldenBytes_NullBecomesEmpty()
    {
        var message = Read(StartPlacementEntityWire, StartPlacementEntityBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.Range, Is.EqualTo(-2));
            Assert.That(message.IsTile, Is.False);
            Assert.That(message.ObjType, Is.EqualTo("a"));
            Assert.That(message.AlignOption, Is.EqualTo(string.Empty), "Length 0 must read as empty, never null.");
        });
    }

    [Test]
    public void StartPlacement_Read_LeavesPropertiesOfOtherCasesAtDefaults()
    {
        // A received message is a fresh instance, so everything not on the wire for this case keeps its default.
        var message = Read(StartPlacementTileWire, StartPlacementTileBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.Align, Is.Null);
            Assert.That(message.Replacement, Is.False);
            Assert.That(message.TileType, Is.EqualTo(0));
            Assert.That(message.EntityTemplateName, Is.Null);
            Assert.That(message.NetCoordinates, Is.EqualTo(default(NetCoordinates)));
            Assert.That(message.DirRcv, Is.EqualTo(Direction.South));
            Assert.That(message.EntityUid, Is.EqualTo(NetEntity.Invalid));
            Assert.That(message.RectSize, Is.EqualTo(Vector2.Zero));
            Assert.That(message.Mirrored, Is.False);
        });
    }

    [Test]
    [TestCase(0, false, "", "")]
    [TestCase(5, true, "Wall", "Grid")]
    [TestCase(-1, false, "Floor", "")]
    [TestCase(int.MaxValue, true, "ß日本", "é")]
    [TestCase(int.MinValue, false, "x", "y")]
    public void StartPlacement_RoundTrip_PreservesValues(int range, bool isTile, string objType, string alignOption)
    {
        var original = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.StartPlacement,
            Range = range,
            IsTile = isTile,
            ObjType = objType,
            AlignOption = alignOption,
        };

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.PlaceType, Is.EqualTo(PlacementManagerMessage.StartPlacement));
            Assert.That(result.Range, Is.EqualTo(range));
            Assert.That(result.IsTile, Is.EqualTo(isTile));
            Assert.That(result.ObjType, Is.EqualTo(objType));
            Assert.That(result.AlignOption, Is.EqualTo(alignOption));
        });
    }

    [Test]
    public void StartPlacement_RoundTrip_NullStrings_BecomeEmptyStrings()
    {
        var original = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.StartPlacement,
            ObjType = null,
            AlignOption = null,
        };

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.ObjType, Is.EqualTo(string.Empty));
            Assert.That(result.AlignOption, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void StartPlacement_RoundTrip_LongString_UsesTwoByteVarintAtUnalignedOffset()
    {
        // 200 UTF-8 bytes need the two-byte varint length C8 01, and here it starts one bit past a byte boundary.
        // Bits: 8 type + 32 range + 1 flag + (16 + 1600) ObjType + 8 AlignOption length = 1665.
        var original = StartTile();
        original.ObjType = new string('q', 200);
        original.AlignOption = string.Empty;

        var (bytes, bits) = Write(original);
        var result = Read(bytes, bits);

        Assert.Multiple(() =>
        {
            Assert.That(bits, Is.EqualTo(1665));
            Assert.That(result.ObjType, Is.EqualTo(original.ObjType));
            Assert.That(result.AlignOption, Is.EqualTo(string.Empty));
        });
    }

    // --------------------------------------------------------------------------------------------------------
    // RequestPlacement (client -> server)
    // --------------------------------------------------------------------------------------------------------

    [Test]
    public void RequestPlacement_Write_Tile_MatchesGoldenBytes()
    {
        // IsTile and Replacement are single bits, so TileType, the coordinates and the direction byte all start
        // 2 bits past a byte boundary and the final Mirrored bit follows the direction byte without padding.
        // See RequestPlacementTileWire for the bit arithmetic.
        var (bytes, bits) = Write(TileRequest());

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementTileWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementTileBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_Entity_MatchesGoldenBytes()
    {
        var (bytes, bits) = Write(EntityRequest());

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementEntityWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementEntityBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_TileWithReplacement_MatchesGoldenBytes()
    {
        // Covers the remaining flag combination (both bits set), so that swapping the order of the IsTile and
        // Replacement bits is caught together with the entity case (which has them the other way round).
        var message = TileRequest();
        message.Replacement = true;
        message.TileType = -1;
        message.DirRcv = Direction.East;
        message.Mirrored = false;

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementTileBothFlagsWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementTileBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_Tile_MirrorsRawLidgrenCallsInSpecOrder()
    {
        // Independent cross-check: Align, IsTile, Replacement, TileType, coordinates (id, X, Y), direction byte,
        // Mirrored, using only primitive Lidgren writes.
        var buffer = CreateBuffer();
        buffer.Write((byte) 3);
        buffer.Write("a");
        buffer.Write(true);
        buffer.Write(false);
        buffer.Write(0x01020304);
        buffer.Write(7);
        buffer.Write(1.0f);
        buffer.Write(-2.0f);
        buffer.Write((byte) 4);
        buffer.Write(true);
        var expected = Extract(buffer);

        var actual = Write(TileRequest());

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            Assert.That(actual.Bits, Is.EqualTo(expected.Bits));
        });
    }

    [Test]
    public void RequestPlacement_Write_Entity_MirrorsRawLidgrenCallsInSpecOrder()
    {
        var buffer = CreateBuffer();
        buffer.Write((byte) 3);
        buffer.Write("a");
        buffer.Write(false);
        buffer.Write(true);
        buffer.Write("AB");
        buffer.Write(1);
        buffer.Write(1.5f);
        buffer.Write(0.25f);
        buffer.Write((byte) 6);
        buffer.Write(false);
        var expected = Extract(buffer);

        var actual = Write(EntityRequest());

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            Assert.That(actual.Bits, Is.EqualTo(expected.Bits));
        });
    }

    [Test]
    public void RequestPlacement_Write_Tile_DoesNotSendEntityTemplateName()
    {
        // The branch is chosen by IsTile. A prototype name set on a tile request must not reach the wire.
        var message = TileRequest();
        message.EntityTemplateName = "ShouldNotBeSent";

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementTileWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementTileBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_Entity_DoesNotSendTileType()
    {
        // The mirror image: a tile id set on an entity request must not reach the wire.
        var message = EntityRequest();
        message.TileType = 1234;

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementEntityWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementEntityBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_IgnoresPropertiesOfOtherCases()
    {
        var message = EntityRequest();
        message.Range = 77;
        message.ObjType = "ignored";
        message.AlignOption = "ignored";
        message.EntityUid = new NetEntity(99);
        message.RectSize = new Vector2(5, 6);

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementEntityWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementEntityBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_NullStringsAndInvalidDirection_MatchesGoldenBytes()
    {
        // Align and EntityTemplateName are null here and are written as empty strings. DirRcv = Invalid (-1) is
        // written as 0xFF, and the whole tail is shifted by 2 bits, giving ... FC 03.
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = null,
            IsTile = false,
            Replacement = false,
            EntityTemplateName = null,
            DirRcv = Direction.Invalid,
            Mirrored = false,
        };

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestPlacementInvalidDirectionWire));
            Assert.That(bits, Is.EqualTo(RequestPlacementInvalidDirectionBits));
        });
    }

    [Test]
    public void RequestPlacement_Write_NullStrings_AreIdenticalToEmptyStrings()
    {
        var withNulls = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = null,
            EntityTemplateName = null,
            DirRcv = Direction.North,
        };
        var withEmpties = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = string.Empty,
            EntityTemplateName = string.Empty,
            DirRcv = Direction.North,
        };

        var nullResult = Write(withNulls);
        var emptyResult = Write(withEmpties);

        Assert.Multiple(() =>
        {
            Assert.That(nullResult.Bytes, Is.EqualTo(emptyResult.Bytes));
            Assert.That(nullResult.Bits, Is.EqualTo(emptyResult.Bits));
        });
    }

    [Test]
    public void RequestPlacement_Read_Tile_GoldenBytes()
    {
        var message = Read(RequestPlacementTileWire, RequestPlacementTileBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.PlaceType, Is.EqualTo(PlacementManagerMessage.RequestPlacement));
            Assert.That(message.Align, Is.EqualTo("a"));
            Assert.That(message.IsTile, Is.True);
            Assert.That(message.Replacement, Is.False);
            Assert.That(message.TileType, Is.EqualTo(0x01020304));
            Assert.That(message.NetCoordinates,
                Is.EqualTo(new NetCoordinates(new NetEntity(7), new Vector2(1.0f, -2.0f))));
            Assert.That(message.DirRcv, Is.EqualTo(Direction.North));
            Assert.That(message.Mirrored, Is.True);
        });
    }

    [Test]
    public void RequestPlacement_Read_Tile_LeavesEntityTemplateNameNull()
    {
        // The prototype name is not on the wire for tile requests, so it keeps its default (null), unlike strings
        // that are on the wire and are read back as empty.
        var message = Read(RequestPlacementTileWire, RequestPlacementTileBits);

        Assert.That(message.EntityTemplateName, Is.Null);
    }

    [Test]
    public void RequestPlacement_Read_Entity_GoldenBytes()
    {
        var message = Read(RequestPlacementEntityWire, RequestPlacementEntityBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.PlaceType, Is.EqualTo(PlacementManagerMessage.RequestPlacement));
            Assert.That(message.Align, Is.EqualTo("a"));
            Assert.That(message.IsTile, Is.False);
            Assert.That(message.Replacement, Is.True);
            Assert.That(message.EntityTemplateName, Is.EqualTo("AB"));
            Assert.That(message.NetCoordinates,
                Is.EqualTo(new NetCoordinates(new NetEntity(1), new Vector2(1.5f, 0.25f))));
            Assert.That(message.DirRcv, Is.EqualTo(Direction.West));
            Assert.That(message.Mirrored, Is.False);
        });
    }

    [Test]
    public void RequestPlacement_Read_Entity_LeavesTileTypeAtZero()
    {
        var message = Read(RequestPlacementEntityWire, RequestPlacementEntityBits);

        Assert.That(message.TileType, Is.EqualTo(0));
    }

    [Test]
    public void RequestPlacement_Read_TileWithBothFlags_GoldenBytes()
    {
        var message = Read(RequestPlacementTileBothFlagsWire, RequestPlacementTileBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.IsTile, Is.True);
            Assert.That(message.Replacement, Is.True);
            Assert.That(message.TileType, Is.EqualTo(-1));
            Assert.That(message.DirRcv, Is.EqualTo(Direction.East));
            Assert.That(message.Mirrored, Is.False);
        });
    }

    [Test]
    public void RequestPlacement_Read_LeavesPropertiesOfOtherCasesAtDefaults()
    {
        var message = Read(RequestPlacementEntityWire, RequestPlacementEntityBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.Range, Is.EqualTo(0));
            Assert.That(message.ObjType, Is.Null);
            Assert.That(message.AlignOption, Is.Null);
            Assert.That(message.EntityUid, Is.EqualTo(NetEntity.Invalid));
            Assert.That(message.RectSize, Is.EqualTo(Vector2.Zero));
        });
    }

    [Test]
    public void RequestPlacement_RoundTrip_Tile_PreservesValues()
    {
        var original = TileRequest();
        original.Replacement = true;
        original.Align = "SnapGrid";
        original.TileType = int.MaxValue;
        original.NetCoordinates = new NetCoordinates(new NetEntity(123456), new Vector2(-12.5f, 1024.125f));

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.PlaceType, Is.EqualTo(PlacementManagerMessage.RequestPlacement));
            Assert.That(result.Align, Is.EqualTo("SnapGrid"));
            Assert.That(result.IsTile, Is.True);
            Assert.That(result.Replacement, Is.True);
            Assert.That(result.TileType, Is.EqualTo(int.MaxValue));
            Assert.That(result.NetCoordinates, Is.EqualTo(original.NetCoordinates));
            Assert.That(result.DirRcv, Is.EqualTo(Direction.North));
            Assert.That(result.Mirrored, Is.True);
        });
    }

    [Test]
    public void RequestPlacement_RoundTrip_Entity_PreservesValues()
    {
        var original = EntityRequest();
        original.Align = "Free";
        original.EntityTemplateName = "WallSolid";
        original.NetCoordinates = new NetCoordinates(new NetEntity(42), new Vector2(0.5f, -0.75f));
        original.DirRcv = Direction.SouthWest;
        original.Mirrored = true;

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.Align, Is.EqualTo("Free"));
            Assert.That(result.IsTile, Is.False);
            Assert.That(result.Replacement, Is.True);
            Assert.That(result.EntityTemplateName, Is.EqualTo("WallSolid"));
            Assert.That(result.NetCoordinates, Is.EqualTo(original.NetCoordinates));
            Assert.That(result.DirRcv, Is.EqualTo(Direction.SouthWest));
            Assert.That(result.Mirrored, Is.True, "Mirrored is sent even for entity placements.");
        });
    }

    [Test]
    public void RequestPlacement_RoundTrip_NonAsciiStrings_ArePreserved()
    {
        // The varint counts UTF-8 bytes, not characters, and every byte is written at an unaligned offset.
        var original = EntityRequest();
        original.Align = "ß";
        original.EntityTemplateName = "日本語";

        var (bytes, bits) = Write(original);
        var result = Read(bytes, bits);

        Assert.Multiple(() =>
        {
            // 8 type + (8 + 16) Align + 2 flags + (8 + 72) name + 96 + 8 + 1 = 219 bits.
            Assert.That(bits, Is.EqualTo(219));
            Assert.That(result.Align, Is.EqualTo("ß"));
            Assert.That(result.EntityTemplateName, Is.EqualTo("日本語"));
        });
    }

    [Test]
    public void RequestPlacement_RoundTrip_LongPrototypeName_UsesTwoByteVarint()
    {
        // 200 UTF-8 bytes need the two-byte varint length C8 01, written 2 bits past a byte boundary.
        // Bits: 8 type + 16 Align "a" + 2 flags + (16 + 1600) name + 96 + 8 + 1 = 1747.
        var original = EntityRequest();
        original.EntityTemplateName = new string('p', 200);

        var (bytes, bits) = Write(original);
        var result = Read(bytes, bits);

        Assert.Multiple(() =>
        {
            Assert.That(bits, Is.EqualTo(1747));
            Assert.That(result.EntityTemplateName, Is.EqualTo(original.EntityTemplateName));
        });
    }

    [Test]
    public void RequestPlacement_RoundTrip_NullStrings_BecomeEmptyStrings()
    {
        var original = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = null,
            IsTile = false,
            EntityTemplateName = null,
        };

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.Align, Is.EqualTo(string.Empty));
            Assert.That(result.EntityTemplateName, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RequestPlacement_RoundTrip_AllFlagCombinations(bool isTile, bool replacement)
    {
        // The three single-bit flags (IsTile, Replacement, Mirrored) are in different bit positions of the
        // stream. Mirrored is flipped relative to Replacement so that a mix-up between them is detected.
        var original = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            Align = "x",
            IsTile = isTile,
            Replacement = replacement,
            TileType = 3,
            EntityTemplateName = "E",
            NetCoordinates = new NetCoordinates(new NetEntity(2), new Vector2(1, 2)),
            DirRcv = Direction.SouthEast,
            Mirrored = !replacement,
        };

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsTile, Is.EqualTo(isTile));
            Assert.That(result.Replacement, Is.EqualTo(replacement));
            Assert.That(result.Mirrored, Is.EqualTo(!replacement));
            Assert.That(result.TileType, Is.EqualTo(isTile ? 3 : 0));
            Assert.That(result.EntityTemplateName, Is.EqualTo(isTile ? null : "E"));
            Assert.That(result.DirRcv, Is.EqualTo(Direction.SouthEast));
        });
    }

    // --------------------------------------------------------------------------------------------------------
    // Direction cast
    // --------------------------------------------------------------------------------------------------------

    [Test]
    public void RequestPlacement_Write_InvalidDirection_IsWrittenAsByte255()
    {
        // Direction is a signed-byte enum, and the wire carries it as an unsigned byte through an unchecked cast, so
        // Invalid (-1) is 0xFF on the wire. In the golden array for this case the 0xFF byte appears shifted by 2
        // bits as FC 03 (see RequestPlacementInvalidDirectionWire); here the byte is extracted again and compared
        // directly, which does not depend on the neighbouring bytes.
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            DirRcv = Direction.Invalid,
        };

        var (bytes, _) = Write(message);

        // The direction byte starts at stream bit offset 8 + 8 + 2 + 8 + 96 = 122: byte 15, bit 2. Undo the shift.
        var directionByte = (byte) ((bytes[15] >> 2) | ((bytes[16] & 0x03) << 6));
        Assert.That(directionByte, Is.EqualTo(0xFF));
    }

    [Test]
    public void RequestPlacement_RoundTrip_InvalidDirection_ComesBackAsMinusOne()
    {
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            DirRcv = Direction.Invalid,
        };

        var result = RoundTrip(message);

        Assert.Multiple(() =>
        {
            Assert.That(result.DirRcv, Is.EqualTo(Direction.Invalid));
            Assert.That((int) result.DirRcv, Is.EqualTo(-1));
        });
    }

    [Test]
    public void RequestPlacement_Read_DirectionByte255_IsMinusOne()
    {
        var message = Read(RequestPlacementInvalidDirectionWire, RequestPlacementInvalidDirectionBits);

        Assert.That((int) message.DirRcv, Is.EqualTo(-1));
    }

    [Test]
    [TestCase(Direction.South, 0)]
    [TestCase(Direction.SouthEast, 1)]
    [TestCase(Direction.East, 2)]
    [TestCase(Direction.NorthEast, 3)]
    [TestCase(Direction.North, 4)]
    [TestCase(Direction.NorthWest, 5)]
    [TestCase(Direction.West, 6)]
    [TestCase(Direction.SouthWest, 7)]
    public void RequestPlacement_Write_DirectionValues_AreSentAsTheirNumericValue(Direction direction, int expectedByte)
    {
        // The numeric values of the Direction enum are part of the wire format. With both strings empty the
        // direction byte starts at stream bit offset 8 + 8 + 2 + 8 + 96 = 122, which is byte 15, bit 2. Bits 0..5 of
        // the value are therefore in bits 2..7 of byte 15 (bits 6 and 7 would spill into byte 16, but all values
        // used here are below 8), and bits 0..1 of byte 15 belong to the preceding all-zero coordinate data. Shifting
        // byte 15 right by 2 recovers the value.
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestPlacement,
            DirRcv = direction,
        };

        var (bytes, _) = Write(message);

        Assert.That(bytes[15] >> 2, Is.EqualTo(expectedByte));
    }

    // --------------------------------------------------------------------------------------------------------
    // RequestEntRemove (client -> server)
    // --------------------------------------------------------------------------------------------------------

    [Test]
    public void RequestEntRemove_Write_MatchesGoldenBytes()
    {
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestEntRemove,
            EntityUid = new NetEntity(0x01020304),
        };

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestEntRemoveWire));
            Assert.That(bits, Is.EqualTo(RequestEntRemoveBits));
        });
    }

    [Test]
    public void RequestEntRemove_Write_NegativeId_IsSentUnchanged()
    {
        // Entity ids are signed 32-bit values written as-is (-2 = 0xFFFFFFFE). Ids with the high bits set denote
        // client-side entities and must be transmitted unchanged, not rejected or clamped.
        byte[] expected = [0x04, 0xFE, 0xFF, 0xFF, 0xFF];
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestEntRemove,
            EntityUid = new NetEntity(-2),
        };

        var (bytes, _) = Write(message);

        Assert.That(bytes, Is.EqualTo(expected));
    }

    [Test]
    public void RequestEntRemove_Write_IgnoresPropertiesOfOtherCases()
    {
        var message = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestEntRemove,
            EntityUid = new NetEntity(0x01020304),
            Align = "ignored",
            IsTile = true,
            Replacement = true,
            Mirrored = true,
            Range = 5,
            NetCoordinates = new NetCoordinates(new NetEntity(3), new Vector2(1, 1)),
            RectSize = new Vector2(2, 2),
        };

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestEntRemoveWire));
            Assert.That(bits, Is.EqualTo(RequestEntRemoveBits));
        });
    }

    [Test]
    public void RequestEntRemove_Read_GoldenBytes()
    {
        var message = Read(RequestEntRemoveWire, RequestEntRemoveBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.PlaceType, Is.EqualTo(PlacementManagerMessage.RequestEntRemove));
            Assert.That(message.EntityUid, Is.EqualTo(new NetEntity(0x01020304)));
            Assert.That(message.NetCoordinates, Is.EqualTo(default(NetCoordinates)));
            Assert.That(message.RectSize, Is.EqualTo(Vector2.Zero));
        });
    }

    [Test]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(-1)]
    [TestCase(0x01020304)]
    [TestCase(int.MaxValue)]
    [TestCase(int.MinValue)]
    public void RequestEntRemove_RoundTrip_PreservesEntityId(int id)
    {
        var original = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestEntRemove,
            EntityUid = new NetEntity(id),
        };

        var result = RoundTrip(original);

        Assert.That(result.EntityUid.Id, Is.EqualTo(id));
    }

    // --------------------------------------------------------------------------------------------------------
    // RequestRectRemove (client -> server)
    // --------------------------------------------------------------------------------------------------------

    [Test]
    public void RequestRectRemove_Write_MatchesGoldenBytes()
    {
        var (bytes, bits) = Write(RectRemove());

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestRectRemoveWire));
            Assert.That(bits, Is.EqualTo(RequestRectRemoveBits));
        });
    }

    [Test]
    public void RequestRectRemove_Write_MirrorsRawLidgrenCallsInSpecOrder()
    {
        var buffer = CreateBuffer();
        buffer.Write((byte) 5);
        buffer.Write(7);
        buffer.Write(1.0f);
        buffer.Write(-2.0f);
        buffer.Write(2.5f);
        buffer.Write(4.0f);
        var expected = Extract(buffer);

        var actual = Write(RectRemove());

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            Assert.That(actual.Bits, Is.EqualTo(expected.Bits));
        });
    }

    [Test]
    public void RequestRectRemove_Write_IgnoresPropertiesOfOtherCases()
    {
        var message = RectRemove();
        message.Align = "ignored";
        message.IsTile = true;
        message.Mirrored = true;
        message.EntityUid = new NetEntity(5);
        message.Range = 9;
        message.DirRcv = Direction.East;

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(RequestRectRemoveWire));
            Assert.That(bits, Is.EqualTo(RequestRectRemoveBits));
        });
    }

    [Test]
    public void RequestRectRemove_Read_GoldenBytes()
    {
        var message = Read(RequestRectRemoveWire, RequestRectRemoveBits);

        Assert.Multiple(() =>
        {
            Assert.That(message.PlaceType, Is.EqualTo(PlacementManagerMessage.RequestRectRemove));
            Assert.That(message.NetCoordinates,
                Is.EqualTo(new NetCoordinates(new NetEntity(7), new Vector2(1.0f, -2.0f))));
            Assert.That(message.RectSize, Is.EqualTo(new Vector2(2.5f, 4.0f)));
            Assert.That(message.EntityUid, Is.EqualTo(NetEntity.Invalid));
            Assert.That(message.Mirrored, Is.False);
        });
    }

    [Test]
    public void RequestRectRemove_RoundTrip_PreservesValues()
    {
        var original = new MsgPlacement
        {
            PlaceType = PlacementManagerMessage.RequestRectRemove,
            NetCoordinates = new NetCoordinates(new NetEntity(-5), new Vector2(-100.5f, 200.25f)),
            RectSize = new Vector2(0f, 16f),
        };

        var result = RoundTrip(original);

        Assert.Multiple(() =>
        {
            Assert.That(result.NetCoordinates, Is.EqualTo(original.NetCoordinates));
            Assert.That(result.RectSize, Is.EqualTo(original.RectSize));
        });
    }

    // --------------------------------------------------------------------------------------------------------
    // CancelPlacement / PlacementFailed: deliberately not implemented
    // --------------------------------------------------------------------------------------------------------

    [Test]
    [TestCase(PlacementManagerMessage.CancelPlacement)]
    [TestCase(PlacementManagerMessage.PlacementFailed)]
    public void NotImplementedCases_Write_Throws(PlacementManagerMessage type)
    {
        // PINS CURRENT BEHAVIOUR. These two values have no payload format, so serializing them fails and the send
        // as a whole fails. Quietly inventing a payload would be a protocol change that older peers do not
        // understand, so it must be a deliberate decision, not a side effect of a refactor.
        var message = new MsgPlacement { PlaceType = type };

        Assert.That(() => Write(message), Throws.TypeOf<NotImplementedException>());
    }

    [Test]
    [TestCase(PlacementManagerMessage.CancelPlacement)]
    [TestCase(PlacementManagerMessage.PlacementFailed)]
    public void NotImplementedCases_Read_Throws(PlacementManagerMessage type)
    {
        // The net manager catches exceptions from deserialization and disconnects the sender, so a remote peer that
        // sends byte 1 or 2 is kicked. Changing this would stop that disconnect.
        byte[] payload = [(byte) type];

        Assert.That(() => Read(payload, 8), Throws.TypeOf<NotImplementedException>());
    }

    // --------------------------------------------------------------------------------------------------------
    // Unknown type bytes
    // --------------------------------------------------------------------------------------------------------

    [Test]
    [TestCase((byte) 6)]
    [TestCase((byte) 100)]
    [TestCase((byte) 255)]
    public void UnknownType_Write_WritesOnlyTheTypeByte(byte type)
    {
        // Values outside the enum are tolerated silently: only the type byte is written, however many other
        // properties are filled in, and no exception is thrown.
        var message = new MsgPlacement
        {
            PlaceType = (PlacementManagerMessage) type,
            Align = "ignored",
            IsTile = true,
            Replacement = true,
            Mirrored = true,
            TileType = 5,
            Range = 5,
            ObjType = "ignored",
            EntityUid = new NetEntity(5),
            NetCoordinates = new NetCoordinates(new NetEntity(5), new Vector2(5, 5)),
            RectSize = new Vector2(5, 5),
        };

        var (bytes, bits) = Write(message);

        Assert.Multiple(() =>
        {
            Assert.That(bytes, Is.EqualTo(new[] { type }));
            Assert.That(bits, Is.EqualTo(8));
        });
    }

    [Test]
    [TestCase((byte) 6)]
    [TestCase((byte) 100)]
    [TestCase((byte) 255)]
    public void UnknownType_Read_ConsumesOnlyTheTypeByteAndKeepsDefaults(byte type)
    {
        var message = Read([type], 8);

        Assert.Multiple(() =>
        {
            Assert.That((byte) message.PlaceType, Is.EqualTo(type));
            Assert.That(message.Align, Is.Null);
            Assert.That(message.IsTile, Is.False);
            Assert.That(message.Replacement, Is.False);
            Assert.That(message.TileType, Is.EqualTo(0));
            Assert.That(message.EntityTemplateName, Is.Null);
            Assert.That(message.NetCoordinates, Is.EqualTo(default(NetCoordinates)));
            Assert.That(message.DirRcv, Is.EqualTo(Direction.South));
            Assert.That(message.EntityUid, Is.EqualTo(NetEntity.Invalid));
            Assert.That(message.Range, Is.EqualTo(0));
            Assert.That(message.ObjType, Is.Null);
            Assert.That(message.AlignOption, Is.Null);
            Assert.That(message.RectSize, Is.EqualTo(Vector2.Zero));
            Assert.That(message.Mirrored, Is.False);
        });
    }

    [Test]
    [TestCase((byte) 6)]
    [TestCase((byte) 100)]
    [TestCase((byte) 255)]
    public void UnknownType_Read_IgnoresTrailingBytes(byte type)
    {
        // Nothing after the type byte is interpreted, so a newer peer that appends data to an unknown type does not
        // make an older reader fail or consume it.
        var incoming = WireTestHelper.CreateIncoming([type, 0xAA, 0xBB]);
        var message = new MsgPlacement();

        message.ReadFromBuffer(incoming, null!);

        Assert.That(incoming.Position, Is.EqualTo(8L));
    }

    [Test]
    [TestCase((byte) 6)]
    [TestCase((byte) 100)]
    [TestCase((byte) 255)]
    public void UnknownType_RoundTrip_PreservesTypeByte(byte type)
    {
        var result = RoundTrip(new MsgPlacement { PlaceType = (PlacementManagerMessage) type });

        Assert.That((byte) result.PlaceType, Is.EqualTo(type));
    }

    // --------------------------------------------------------------------------------------------------------
    // Framing and truncation
    // --------------------------------------------------------------------------------------------------------

    [Test]
    public void Write_PayloadStartsWithThePlaceTypeByte_NotWithAMessageId()
    {
        // The one-byte message id is added by the net manager, outside of WriteToBuffer. The first payload byte is
        // therefore the PlaceType for every case.
        Assert.Multiple(() =>
        {
            Assert.That(Write(StartTile()).Bytes[0], Is.EqualTo((byte) PlacementManagerMessage.StartPlacement));
            Assert.That(Write(TileRequest()).Bytes[0], Is.EqualTo((byte) PlacementManagerMessage.RequestPlacement));
            Assert.That(Write(new MsgPlacement { PlaceType = PlacementManagerMessage.RequestEntRemove }).Bytes[0],
                Is.EqualTo((byte) PlacementManagerMessage.RequestEntRemove));
            Assert.That(Write(RectRemove()).Bytes[0], Is.EqualTo((byte) PlacementManagerMessage.RequestRectRemove));
        });
    }

    [Test]
    [TestCase((byte) 0, 0)]
    [TestCase((byte) 3, 3)]
    [TestCase((byte) 4, 4)]
    [TestCase((byte) 5, 5)]
    public void PlaceTypeValues_AreTheNumericWireValues(byte wireValue, int enumValue)
    {
        // The numeric values of PlacementManagerMessage are the wire format. Reordering the enum members would
        // silently change what every old peer thinks a type byte means.
        Assert.Multiple(() =>
        {
            Assert.That((int) (PlacementManagerMessage) wireValue, Is.EqualTo(enumValue));
            Assert.That(Write(new MsgPlacement { PlaceType = (PlacementManagerMessage) enumValue }).Bytes[0],
                Is.EqualTo(wireValue));
        });
    }

    [Test]
    public void Read_EntRemoveTruncatedPayload_Throws([Range(1, 4)] int availableBytes)
    {
        // The case needs 5 bytes (type + int32). A shorter buffer is malformed and must fail with an exception, which
        // makes the net manager disconnect the sender, instead of inventing an entity id from partial data.
        var truncated = RequestEntRemoveWire[..availableBytes];

        Assert.That(() => Read(truncated, RequestEntRemoveBits), Throws.Exception);
    }

    [Test]
    public void Read_EmptyPayload_Throws()
    {
        // Not even the type byte is present.
        Assert.That(() => Read([], 0), Throws.Exception);
    }
}
