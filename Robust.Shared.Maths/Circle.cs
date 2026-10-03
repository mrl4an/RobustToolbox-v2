using System;
using System.Numerics;
using Robust.Shared.Utility;

namespace Robust.Shared.Maths
{
    /// <summary>
    ///     A filled 2D disc described by a centre and a radius. The type performs no validation
    ///     of the radius and has no notion of coordinate space.
    /// </summary>
    [Serializable]
    public struct Circle : IEquatable<Circle>, ISpanFormattable
    {
        /// <summary>
        ///     Centre of the circle.
        /// </summary>
        public Vector2 Position;

        /// <summary>
        ///     Distance from the center to the edge of the circle.
        /// </summary>
        public float Radius;

        /// <summary>
        ///     Creates a circle from a centre and a radius. Values are stored as given.
        /// </summary>
        public Circle(Vector2 position, float radius)
        {
            Position = position;
            Radius = radius;
        }

        /// <summary>
        ///     Whether <paramref name="point"/> lies inside or (approximately) on the circle.
        ///     The comparison is made on squared distances with a small relative/absolute tolerance.
        /// </summary>
        public readonly bool Contains(Vector2 point)
        {
            return ContainsPoint(point.X, point.Y);
        }

        /// <summary>
        ///     Strict overlap test between two circles, without tolerance. The sum of the radii is
        ///     squared as-is, so negative radii are not treated as their absolute value.
        /// </summary>
        public readonly bool Intersects(Circle circle)
        {
            var dx = Position.X - circle.Position.X;
            var dy = Position.Y - circle.Position.Y;
            var sum = Radius + circle.Radius;

            return dx * dx + dy * dy < sum * sum;
        }

        /// <summary>
        ///     Whether this circle overlaps a box. Edges of the box may be given in either order.
        /// </summary>
        public readonly bool Intersects(Box2 box)
        {
            var nearestX = MathHelper.Median(box.Left, box.Right, Position.X);
            var nearestY = MathHelper.Median(box.Bottom, box.Top, Position.Y);

            return ContainsPoint(nearestX, nearestY);
        }

        private readonly bool ContainsPoint(float x, float y)
        {
            var dx = Position.X - x;
            var dy = Position.Y - y;
            var distSquared = dx * dx + dy * dy;
            var radiusSquared = Radius * Radius;

            return distSquared < radiusSquared || MathHelper.CloseToPercent(distSquared, radiusSquared);
        }

        /// <summary>
        ///     Exact equality of centre and radius.
        /// </summary>
        public readonly bool Equals(Circle other)
        {
            return Position.Equals(other.Position) && Radius.Equals(other.Radius);
        }

        /// <inheritdoc />
        public override readonly bool Equals(object? obj)
        {
            return obj is Circle other && Equals(other);
        }

        /// <inheritdoc />
        public override readonly int GetHashCode()
        {
            return unchecked((Position.GetHashCode() * 397) ^ Radius.GetHashCode());
        }

        /// <summary>
        ///     Returns whether two circles are exactly equal.
        /// </summary>
        public static bool operator ==(Circle a, Circle b)
        {
            return a.Equals(b);
        }

        /// <summary>
        ///     Returns whether two circles differ.
        /// </summary>
        public static bool operator !=(Circle a, Circle b)
        {
            return !(a == b);
        }

        /// <inheritdoc />
        public override readonly string ToString()
        {
            return $"Circle ({Position.X}, {Position.Y}), {Radius} r";
        }

        /// <summary>
        ///     Returns the same text as <see cref="ToString()"/>; both arguments are ignored.
        /// </summary>
        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            return ToString();
        }

        /// <summary>
        ///     Writes the same text as <see cref="ToString()"/> into <paramref name="destination"/>;
        ///     <paramref name="format"/> and <paramref name="provider"/> are ignored.
        /// </summary>
        public readonly bool TryFormat(
            Span<char> destination,
            out int charsWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider)
        {
            return FormatHelpers.TryFormatInto(
                destination,
                out charsWritten,
                $"Circle ({Position.X}, {Position.Y}), {Radius} r");
        }
    }
}
