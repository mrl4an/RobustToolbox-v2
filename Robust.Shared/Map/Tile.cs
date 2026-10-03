using System;
using System.ComponentModel;
using JetBrains.Annotations;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Shared.Map;

/// <summary>
///     This structure contains the data for an individual Tile in a <c>MapGrid</c>.
/// </summary>
[PublicAPI, Serializable]
public readonly struct Tile : IEquatable<Tile>, ISpanFormattable
{
    /// <summary>
    ///     The internal type ID of the tile definition used by this cell. 0 is reserved for empty space.
    /// </summary>
    public readonly int TypeId;

    /// <summary>
    ///     Custom flags for additional tile-data.
    /// </summary>
    public readonly byte Flags;

    /// <summary>
    ///     Variant of this tile to render.
    /// </summary>
    public readonly byte Variant;

    /// <summary>
    ///     Rotation and mirroring of this tile to render. 0-3 is normal, 4-7 is mirrored.
    /// </summary>
    public readonly byte RotationMirroring;

    /// <summary>
    ///     An empty tile that can be compared against.
    /// </summary>
    public static readonly Tile Empty = new(0);

    /// <summary>
    ///     Whether this tile is space, i.e. its <see cref="TypeId"/> is 0.
    /// </summary>
    public bool IsEmpty => TypeId == 0;

    /// <summary>
    ///     Creates a new tile.
    /// </summary>
    /// <param name="typeId">The type ID of the tile definition. 0 means empty space.</param>
    /// <param name="flags">Custom flags for additional tile data.</param>
    /// <param name="variant">The variant of the tile to render.</param>
    /// <param name="rotationMirroring">The rotation and mirroring to render; 0-3 is normal, 4-7 is mirrored.</param>
    public Tile(int typeId, byte flags = 0, byte variant = 0, byte rotationMirroring = 0)
    {
        TypeId = typeId;
        Flags = flags;
        Variant = variant;
        RotationMirroring = rotationMirroring;
    }

    public static byte DirectionToByte(Direction direction, bool throwIfDiagonal = false)
    {
        switch (direction)
        {
            case Direction.South:
                return 0;
            case Direction.East:
                return 1;
            case Direction.North:
                return 2;
            case Direction.West:
                return 3;
            default:
                if (throwIfDiagonal)
                    throw new InvalidEnumArgumentException("direction", (int)direction, typeof(Direction));
                else
                    return 0;
        }
    }

    /// <summary>
    ///     Checks whether two tiles are equal in all four fields.
    /// </summary>
    public static bool operator ==(Tile a, Tile b)
    {
        return a.Equals(b);
    }

    /// <summary>
    ///     Checks whether two tiles differ in at least one field.
    /// </summary>
    public static bool operator !=(Tile a, Tile b)
    {
        return !a.Equals(b);
    }

    /// <summary>
    ///     Formats the tile as <c>Tile TypeId, Flags, Variant, RotationMirroring</c>.
    /// </summary>
    public override string ToString()
    {
        return $"Tile {TypeId}, {Flags}, {Variant}, {RotationMirroring}";
    }

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return ToString();
    }

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        return FormatHelpers.TryFormatInto(
            destination,
            out charsWritten,
            $"Tile {TypeId}, {Flags}, {Variant}");
    }

    /// <inheritdoc />
    public bool Equals(Tile other)
    {
        return TypeId == other.TypeId && Flags == other.Flags && Variant == other.Variant && RotationMirroring == other.RotationMirroring;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(null, obj))
            return false;
        return obj is Tile other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            var hash = TypeId.GetHashCode() * 397;
            hash ^= Flags.GetHashCode();
            hash ^= Variant.GetHashCode();
            hash ^= RotationMirroring.GetHashCode();
            return hash;
        }
    }

    [Pure]
    public Tile WithVariant(byte variant)
    {
        return new Tile(TypeId, Flags, variant);
    }

    [Pure]
    public Tile WithFlag(byte flag)
    {
        return new Tile(TypeId, flags: flag, variant: Variant);
    }
}

public sealed class TileFlagLayer {}

