using System;
using System.Numerics;
using Lidgren.Network;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

#nullable disable

namespace Robust.Shared.Network.Messages;

/// <summary>
///     Carries every message of the entity/tile placement protocol between the client and server placement
///     managers. The first body byte (<see cref="PlaceType"/>) selects which of the other properties are
///     transmitted.
/// </summary>
/// <remarks>
/// <para>
///     The Lidgren buffer is a bit stream. Booleans take exactly one bit and nothing re-aligns the stream
///     afterwards, so the fields following a boolean are written at an unaligned bit offset. Read and write are
///     strict mirrors of each other and must be kept that way: this layout is part of the network protocol, and
///     clients and servers built from different revisions have to keep understanding each other.
/// </para>
/// <para>
///     The message identity is the type name <c>MsgPlacement</c>, which is the key in the string table that the
///     net manager syncs on connect; do not rename the type. The message belongs to
///     <see cref="MsgGroups.Command"/>, so it is delivered reliably but unordered.
/// </para>
/// <para>
///     Primitive encodings: byte = 8 bits; bool = 1 bit; int and float = 32 bits little-endian; string = unsigned
///     varint UTF-8 byte count followed by the UTF-8 bytes (null and empty are both written as the single
///     length 0, and read back as the empty string); Vector2 = float X then float Y; NetEntity = its id as an int;
///     NetCoordinates = NetEntity then Vector2 (96 bits).
/// </para>
/// <para>
///     Wire layout of the body (after the message id byte added by the net manager). Every case starts with the
///     <see cref="PlaceType"/> byte.
/// </para>
/// <list type="table">
///     <listheader>
///         <term><see cref="PlaceType"/> (value)</term>
///         <description>Fields after the type byte, in order</description>
///     </listheader>
///     <item>
///         <term><see cref="PlacementManagerMessage.StartPlacement"/> (0), server to client</term>
///         <description>
///             <see cref="Range"/> (int32), <see cref="IsTile"/> (1 bit), <see cref="ObjType"/> (string),
///             <see cref="AlignOption"/> (string).
///         </description>
///     </item>
///     <item>
///         <term><see cref="PlacementManagerMessage.CancelPlacement"/> (1)</term>
///         <description>Not implemented. Reading and writing throw <see cref="NotImplementedException"/>.</description>
///     </item>
///     <item>
///         <term><see cref="PlacementManagerMessage.PlacementFailed"/> (2)</term>
///         <description>Not implemented. Reading and writing throw <see cref="NotImplementedException"/>.</description>
///     </item>
///     <item>
///         <term><see cref="PlacementManagerMessage.RequestPlacement"/> (3), client to server</term>
///         <description>
///             <see cref="Align"/> (string), <see cref="IsTile"/> (1 bit), <see cref="Replacement"/> (1 bit), then
///             either <see cref="TileType"/> (int32) when <see cref="IsTile"/> is true or
///             <see cref="EntityTemplateName"/> (string) when it is false, then <see cref="NetCoordinates"/>
///             (int32, float, float), <see cref="DirRcv"/> (1 byte, the signed direction value cast to byte, so
///             <see cref="Direction.Invalid"/> is 255) and finally <see cref="Mirrored"/> (1 bit).
///         </description>
///     </item>
///     <item>
///         <term><see cref="PlacementManagerMessage.RequestEntRemove"/> (4), client to server</term>
///         <description><see cref="EntityUid"/> (int32).</description>
///     </item>
///     <item>
///         <term><see cref="PlacementManagerMessage.RequestRectRemove"/> (5), client to server</term>
///         <description>
///             <see cref="NetCoordinates"/> (int32, float, float), <see cref="RectSize"/> (float, float).
///         </description>
///     </item>
///     <item>
///         <term>any other value (6 to 255)</term>
///         <description>Nothing beyond the type byte is read or written, and no exception is thrown.</description>
///     </item>
/// </list>
/// <para>
///     Caveats:
/// </para>
/// <list type="bullet">
///     <item><description>
///         Properties that are not part of the case being processed are neither written nor read and keep their
///         default values. A received message is always a fresh instance.
///     </description></item>
///     <item><description>
///         Writing <see cref="PlacementManagerMessage.CancelPlacement"/> or
///         <see cref="PlacementManagerMessage.PlacementFailed"/> throws after the type byte has been appended to
///         the buffer, so the whole send fails. Receiving either value makes the net manager disconnect the
///         sender because deserialization throws. This is deliberate and must be preserved unless the protocol
///         is changed on purpose.
///     </description></item>
///     <item><description>
///         The <see cref="PlaceType"/> byte is cast to the enum without a range check, and no other field is
///         validated here (floats may be NaN, entity ids need not exist). Consumers validate semantics.
///     </description></item>
///     <item><description>
///         The direction is converted between its signed byte enum and a byte with wraparound (unchecked)
///         casts.
///     </description></item>
///     <item><description>
///         Any bytes after the last field of a case are ignored on read.
///     </description></item>
/// </list>
/// </remarks>
public sealed class MsgPlacement : NetMessage
{
    /// <inheritdoc />
    public override MsgGroups MsgGroup => MsgGroups.Command;

    /// <summary>
    ///     Selects which placement operation this message represents and therefore which of the other
    ///     properties are serialized. Always the first byte of the body.
    /// </summary>
    public PlacementManagerMessage PlaceType { get; set; }

    /// <summary>
    ///     Name of the client's placement alignment mode. The server treats it as an opaque identifier.
    ///     Only used by <see cref="PlacementManagerMessage.RequestPlacement"/>.
    /// </summary>
    public string Align { get; set; }

    /// <summary>
    ///     Whether existing entities that share a placement replacement key on the target tile should be
    ///     replaced by the new one. Only used by <see cref="PlacementManagerMessage.RequestPlacement"/>.
    /// </summary>
    public bool Replacement { get; set; }

    /// <summary>
    ///     True when a tile is being placed (or placement of a tile starts), false for an entity. Used by
    ///     <see cref="PlacementManagerMessage.RequestPlacement"/> and
    ///     <see cref="PlacementManagerMessage.StartPlacement"/>. In a placement request it decides whether
    ///     <see cref="TileType"/> or <see cref="EntityTemplateName"/> is serialized.
    /// </summary>
    public bool IsTile { get; set; }

    /// <summary>
    ///     Id of the tile definition to place. Only serialized by
    ///     <see cref="PlacementManagerMessage.RequestPlacement"/> when <see cref="IsTile"/> is true.
    /// </summary>
    public int TileType { get; set; }

    /// <summary>
    ///     Prototype id of the entity to spawn. Only serialized by
    ///     <see cref="PlacementManagerMessage.RequestPlacement"/> when <see cref="IsTile"/> is false.
    /// </summary>
    public string EntityTemplateName { get; set; }

    /// <summary>
    ///     Network-space position of the placement, or of the origin of the rectangle for
    ///     <see cref="PlacementManagerMessage.RequestRectRemove"/>. Used by
    ///     <see cref="PlacementManagerMessage.RequestPlacement"/> and
    ///     <see cref="PlacementManagerMessage.RequestRectRemove"/>.
    /// </summary>
    public NetCoordinates NetCoordinates { get; set; }

    /// <summary>
    ///     Direction to place the object with. Only used by
    ///     <see cref="PlacementManagerMessage.RequestPlacement"/>; transmitted as one byte.
    /// </summary>
    public Direction DirRcv { get; set; }

    /// <summary>
    ///     Network id of the entity to delete. Only used by
    ///     <see cref="PlacementManagerMessage.RequestEntRemove"/>. The value is transmitted as is, including
    ///     ids of client-side entities.
    /// </summary>
    public NetEntity EntityUid { get; set; }

    /// <summary>
    ///     Maximum placement range, in tiles, granted to the client for the placement mode. Only used by
    ///     <see cref="PlacementManagerMessage.StartPlacement"/>.
    /// </summary>
    public int Range { get; set; }

    /// <summary>
    ///     Entity prototype id or tile definition name that the client is allowed to place. Only used by
    ///     <see cref="PlacementManagerMessage.StartPlacement"/>.
    /// </summary>
    public string ObjType { get; set; }

    /// <summary>
    ///     Name of the alignment option for the client's placement mode. Only used by
    ///     <see cref="PlacementManagerMessage.StartPlacement"/>.
    /// </summary>
    public string AlignOption { get; set; }

    /// <summary>
    ///     Width and height of the removal rectangle that extends from <see cref="NetCoordinates"/>. Only used
    ///     by <see cref="PlacementManagerMessage.RequestRectRemove"/>.
    /// </summary>
    public Vector2 RectSize { get; set; }

    /// <summary>
    ///     Whether the placed tile is mirrored. Transmitted at the very end of every
    ///     <see cref="PlacementManagerMessage.RequestPlacement"/>, including entity placements.
    /// </summary>
    public bool Mirrored { get; set; }

    /// <inheritdoc />
    public override void ReadFromBuffer(NetIncomingMessage buffer, IRobustSerializer serializer)
    {
        PlaceType = (PlacementManagerMessage) buffer.ReadByte();

        switch (PlaceType)
        {
            case PlacementManagerMessage.StartPlacement:
                Range = buffer.ReadInt32();
                IsTile = buffer.ReadBoolean();
                ObjType = buffer.ReadString();
                AlignOption = buffer.ReadString();
                break;

            case PlacementManagerMessage.CancelPlacement:
            case PlacementManagerMessage.PlacementFailed:
                throw new NotImplementedException();

            case PlacementManagerMessage.RequestPlacement:
                Align = buffer.ReadString();
                IsTile = buffer.ReadBoolean();
                Replacement = buffer.ReadBoolean();

                if (IsTile)
                    TileType = buffer.ReadInt32();
                else
                    EntityTemplateName = buffer.ReadString();

                NetCoordinates = buffer.ReadNetCoordinates();
                DirRcv = unchecked((Direction) buffer.ReadByte());
                Mirrored = buffer.ReadBoolean();
                break;

            case PlacementManagerMessage.RequestEntRemove:
                EntityUid = buffer.ReadNetEntity();
                break;

            case PlacementManagerMessage.RequestRectRemove:
                NetCoordinates = buffer.ReadNetCoordinates();
                RectSize = buffer.ReadVector2();
                break;
        }
    }

    /// <inheritdoc />
    public override void WriteToBuffer(NetOutgoingMessage buffer, IRobustSerializer serializer)
    {
        buffer.Write((byte) PlaceType);

        switch (PlaceType)
        {
            case PlacementManagerMessage.StartPlacement:
                buffer.Write(Range);
                buffer.Write(IsTile);
                buffer.Write(ObjType ?? string.Empty);
                buffer.Write(AlignOption ?? string.Empty);
                break;

            case PlacementManagerMessage.CancelPlacement:
            case PlacementManagerMessage.PlacementFailed:
                throw new NotImplementedException();

            case PlacementManagerMessage.RequestPlacement:
                buffer.Write(Align ?? string.Empty);
                buffer.Write(IsTile);
                buffer.Write(Replacement);

                if (IsTile)
                    buffer.Write(TileType);
                else
                    buffer.Write(EntityTemplateName ?? string.Empty);

                buffer.Write(NetCoordinates);
                buffer.Write(unchecked((byte) DirRcv));
                buffer.Write(Mirrored);
                break;

            case PlacementManagerMessage.RequestEntRemove:
                buffer.Write(EntityUid);
                break;

            case PlacementManagerMessage.RequestRectRemove:
                buffer.Write(NetCoordinates);
                buffer.Write(RectSize);
                break;
        }
    }
}
