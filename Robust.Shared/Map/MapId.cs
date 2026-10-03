using System;
using Robust.Shared.Serialization;

namespace Robust.Shared.Map
{
    /// <summary>
    ///     A lightweight, strongly typed handle for a map: a thin wrapper around a signed 32-bit integer.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every map other than <see cref="Nullspace"/> is backed by a map entity, and this identifier is
    ///         the stable integer handle that the map system, map coordinates, physics, console commands and similar
    ///         systems use to refer to it.
    ///     </para>
    ///     <para>
    ///         Id ranges: positive ids are allocated by the server (and by shared code), counting upwards from 1.
    ///         Negative ids are reserved for client-side-only maps, which exist solely on a client and are never
    ///         known to the server; the client allocates those by counting downwards. Zero is
    ///         <see cref="Nullspace"/>. See <see cref="IsClientSide"/>.
    ///     </para>
    ///     <para>
    ///         Because <c>default(MapId)</c> holds zero, it is identical to <see cref="Nullspace"/>. Fields and
    ///         properties that are never assigned therefore mean "no map".
    ///     </para>
    ///     <para>
    ///         The wire format of this type is exactly one 32-bit integer (its single instance field). Do not add
    ///         instance fields, and do not change the type or name of <see cref="Value"/>.
    ///     </para>
    /// </remarks>
    [Serializable, NetSerializable]
    public readonly struct MapId : IEquatable<MapId>
    {
        /// <summary>
        ///     The "no map" identifier, with the value zero.
        /// </summary>
        /// <remarks>
        ///     Nullspace is the map-level equivalent of <c>null</c>. No map entity is associated with it, and
        ///     anything placed in it is a root of the transform hierarchy (it has no transform parent). Map entities
        ///     themselves conceptually live in nullspace and act as the roots of that hierarchy.
        ///     Nullspace is not a client-side map; see <see cref="IsClientSide"/>.
        /// </remarks>
        public static readonly MapId Nullspace = new(0);

        /// <summary>
        ///     The raw integer this identifier wraps, stored exactly as passed to the constructor.
        /// </summary>
        /// <remarks>
        ///     This is only visible to assemblies that Robust.Shared grants internals access to. Code outside of
        ///     those assemblies, such as content, can obtain the number through the explicit conversion to
        ///     <see cref="int"/>.
        /// </remarks>
        internal readonly int Value;

        /// <summary>
        ///     Creates a map identifier from a raw integer.
        /// </summary>
        /// <param name="value">
        ///     The raw id. No validation is performed: any <see cref="int"/> is accepted, including negative values
        ///     (client-side maps) and zero (<see cref="Nullspace"/>). Callers are responsible for allocating ids from
        ///     the correct range.
        /// </param>
        public MapId(int value)
        {
            Value = value;
        }

        /// <summary>
        ///     Whether this id refers to a client-side-only map, which is the case exactly when the raw value is
        ///     negative.
        /// </summary>
        /// <remarks>
        ///     Nullspace (zero) is not client-side. The engine uses this property to refuse creating client-side maps
        ///     on a server, and to refuse creating non-client-side maps on a client that is connected to a server.
        /// </remarks>
        public bool IsClientSide => Value < 0;

        /// <summary>
        ///     Compares this id with another one for equality.
        /// </summary>
        /// <param name="other">The id to compare against.</param>
        /// <returns><see langword="true"/> if both ids wrap the same integer.</returns>
        public bool Equals(MapId other)
        {
            return Value == other.Value;
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Returns <see langword="false"/> for <see langword="null"/> and for any object that is not a boxed
        ///     <see cref="MapId"/>.
        /// </remarks>
        public override bool Equals(object? obj)
        {
            return obj is MapId other && Equals(other);
        }

        /// <summary>
        ///     Returns the wrapped integer itself as the hash code.
        /// </summary>
        /// <remarks>
        ///     This is part of the observable contract: hash-based collections, and some content code that hashes
        ///     map ids, depend on it (the enumeration order of a dictionary or set can too). Equal ids always
        ///     produce equal hashes.
        /// </remarks>
        public override int GetHashCode()
        {
            return Value;
        }

        /// <summary>
        ///     Determines whether two ids are equal.
        /// </summary>
        public static bool operator ==(MapId a, MapId b)
        {
            return a.Value == b.Value;
        }

        /// <summary>
        ///     Determines whether two ids differ. This is always the exact negation of the equality operator.
        /// </summary>
        public static bool operator !=(MapId a, MapId b)
        {
            return !(a == b);
        }

        /// <summary>
        ///     Extracts the raw integer from a map id.
        /// </summary>
        /// <remarks>
        ///     This is the supported way for code that cannot see the internal <see cref="Value"/> field to read the
        ///     number, for example to key a UI list by id or to hash the id together with other data.
        ///     The conversion is deliberately explicit, and there is no conversion in the other direction: use the
        ///     constructor instead. An implicit conversion would make overload resolution ambiguous throughout the
        ///     engine.
        /// </remarks>
        public static explicit operator int(MapId self)
        {
            return self.Value;
        }

        /// <summary>
        ///     Formats the id for logs, console output and debug displays.
        /// </summary>
        /// <returns>
        ///     For client-side ids (negative values), the lowercase letter <c>c</c> directly followed by the
        ///     decimal digits of the negated value, for example <c>c1</c> for -1. For all other values, the plain
        ///     decimal number, for example <c>0</c> or <c>42</c>.
        /// </returns>
        /// <remarks>
        ///     Negation is performed in unchecked arithmetic. <see cref="int.MinValue"/> therefore cannot be
        ///     negated and formats as <c>c-2147483648</c>.
        ///     Caveat: because of the <c>c</c> prefix, the text of a client-side id cannot be parsed back with
        ///     <see cref="int.Parse(string)"/>. This affects console commands that take a map id and their
        ///     auto-completion hints, which display this text.
        /// </remarks>
        public override string ToString()
        {
            if (IsClientSide)
                return "c" + unchecked(-Value).ToString();

            return Value.ToString();
        }
    }
}
