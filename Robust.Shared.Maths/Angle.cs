using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Robust.Shared.Maths
{
    /// <summary>
    ///     A representation of an angle, in radians.
    /// </summary>
    [Serializable]
    public readonly struct Angle : IApproxEquatable<Angle>, IEquatable<Angle>, ISpanFormattable
    {
        /// <summary>
        ///     The zero angle (0 radians).
        /// </summary>
        public static Angle Zero { get; } = new();

        /// <summary>
        ///     The angle in radians, stored exactly as supplied (never wrapped or normalised).
        /// </summary>
        public readonly double Theta;

        /// <summary>
        ///     The stored angle converted to degrees, without any reduction to a single turn.
        /// </summary>
        public double Degrees => MathHelper.RadiansToDegrees(Theta);

        /// <summary>
        ///     Creates an angle from a value in radians. The value is kept as is.
        /// </summary>
        /// <param name="theta">The angle in radians.</param>
        public Angle(double theta)
        {
            Theta = theta;
        }

        /// <summary>
        ///     Creates an angle from the direction of a vector, measured counter-clockwise from the positive X axis.
        ///     The vector does not have to be normalised.
        /// </summary>
        /// <param name="dir">The direction vector.</param>
        public Angle(Vector2 dir)
        {
            Theta = Math.Atan2((double) dir.Y, (double) dir.X);
        }

        public static Angle FromWorldVec(Vector2 dir)
        {
            return new Angle(dir) + Math.PI / 2;
        }

        /// <summary>
        ///     Converts this angle to a unit vector (cos, sin), where 0 points along +X and pi/2 along +Y.
        ///     For world-space use, where 0 means "south", use <see cref="ToWorldVec"/> instead.
        /// </summary>
        /// <returns>The unit vector for this angle.</returns>
        public readonly Vector2 ToVec()
        {
            var x = Math.Cos(Theta);
            var y = Math.Sin(Theta);
            return new Vector2((float) x, (float) y);
        }

        public readonly Vector2 ToWorldVec()
        {
            return (this - MathHelper.PiOver2).ToVec();
        }

        private const double Segment = 2 * Math.PI / 8.0; // Cut the circle into 8 pieces
        private const double Offset = Segment / 2.0; // offset the pieces by 1/2 their size

        /// <summary>
        ///     Maps this angle onto the nearest of the eight compass directions.
        /// </summary>
        /// <returns>The matching <see cref="Direction"/>.</returns>
        public readonly Direction GetDir()
        {
            var wrapped = Theta % Math.Tau;
            if (wrapped < 0)
                wrapped += Math.Tau;

            return (Direction) (Math.Floor((wrapped + Offset) / Segment) % 8);
        }

        /// <summary>
        ///     Rotates a direction by this angle and returns the resulting direction, snapped to eight-way.
        /// </summary>
        /// <param name="dir">The direction to rotate.</param>
        /// <returns>The rotated direction.</returns>
        public Direction RotateDir(Direction dir)
        {
            var total = Theta + Segment * (int) dir;
            var wrapped = total % Math.Tau;
            if (wrapped < 0)
                wrapped += Math.Tau;

            return (Direction) (Math.Floor((wrapped + Offset) / Segment) % 8);
        }

        private const double CardinalSegment = 2 * Math.PI / 4.0; // Cut the circle into 4 pieces
        private const double CardinalOffset = CardinalSegment / 2.0; // offset the pieces by 1/2 their size

        /// <summary>
        ///     Maps this angle onto the nearest of the four cardinal directions (South, East, North, West).
        /// </summary>
        /// <returns>The matching cardinal <see cref="Direction"/>.</returns>
        [Pure]
        public readonly Direction GetCardinalDir()
        {
            var wrapped = Theta % Math.Tau;
            if (wrapped < 0)
                wrapped += Math.Tau;

            var quarter = Math.Floor((wrapped + CardinalOffset) / CardinalSegment);
            return (Direction) (quarter * 2 % 8);
        }

        /// <summary>
        /// Rounds the angle to the nearest cardinal direction. This behaves similarly to a combination of
        /// <see cref="GetCardinalDir"/> and Direction.ToAngle(), however this may return an angle outside of the range
        /// returned by those methods (-pi to pi).
        /// </summary>
        public Angle RoundToCardinalAngle()
        {
            return new Angle(CardinalSegment * Math.Floor((Theta + CardinalOffset) / CardinalSegment));
        }

        /// <summary>
        ///     Rotates the vector counter-clockwise around its origin by the value of Theta.
        /// </summary>
        /// <param name="vec">Vector to rotate.</param>
        /// <returns>New rotated vector.</returns>
        [Pure]
        public readonly Vector2 RotateVec(in Vector2 vec)
        {
            // No calculation necessery when theta is zero
            if (Theta == 0) return vec;

            var cos = Math.Cos(Theta);
            var sin = Math.Sin(Theta);
            var dx = cos * vec.X - sin * vec.Y;
            var dy = sin * vec.X + cos * vec.Y;

            return new Vector2((float) dx, (float) dy);
        }

        public bool EqualsApprox(Angle other, double tolerance)
        {
            return EqualsApprox(this, other, tolerance);
        }

        public bool EqualsApprox(Angle other)
        {
            return EqualsApprox(this, other);
        }

        /// <summary>
        ///     Compares two angles approximately, ignoring whole turns, using the default tolerance.
        /// </summary>
        private static bool EqualsApprox(Angle a, Angle b)
        {
            var posA = FlipPositive(Reduce(a.Theta));
            var posB = FlipPositive(Reduce(b.Theta));

            // The extra checks cover one angle sitting just above 0 while the other is just below a full turn.
            return MathHelper.CloseToPercent(posA, posB)
                   || MathHelper.CloseToPercent(posA + MathHelper.TwoPi, posB)
                   || MathHelper.CloseToPercent(posA, posB + MathHelper.TwoPi);
        }

        /// <summary>
        ///     Compares two angles approximately, ignoring whole turns, using a caller-supplied tolerance.
        /// </summary>
        private static bool EqualsApprox(Angle a, Angle b, double tolerance)
        {
            var posA = FlipPositive(Reduce(a.Theta));
            var posB = FlipPositive(Reduce(b.Theta));

            // The extra checks cover one angle sitting just above 0 while the other is just below a full turn.
            return MathHelper.CloseToPercent(posA, posB, tolerance)
                   || MathHelper.CloseToPercent(posA + MathHelper.TwoPi, posB, tolerance)
                   || MathHelper.CloseToPercent(posA, posB + MathHelper.TwoPi, tolerance);
        }

        /// <summary>
        ///     Returns this angle with all whole turns removed (rounded toward zero), keeping its sign.
        /// </summary>
        [Pure]
        public readonly Angle Reduced()
        {
            return new Angle(Reduce(Theta));
        }

        /// <summary>
        ///     Removes whole turns from a radian value, truncating toward zero so the sign is preserved.
        /// </summary>
        private static double Reduce(double theta)
        {
            var turns = (int) (theta / Math.Tau);
            return theta - turns * Math.Tau;
        }

        /// <inheritdoc />
        public readonly bool Equals(Angle other)
        {
            return Theta.Equals(other.Theta);
        }

        /// <inheritdoc />
        public readonly override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Angle angle && Equals(angle);
        }

        /// <inheritdoc />
        public readonly override int GetHashCode()
        {

            return Theta.GetHashCode();

        }

        public static bool operator ==(Angle a, Angle b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Angle a, Angle b)
        {
            return !(a == b);
        }

        [Pure]
        public readonly Angle Opposite()
        {
            return new Angle(FlipPositive(Theta-Math.PI));
        }

        [Pure]
        public readonly Angle FlipPositive()
        {
            return new(FlipPositive(Theta));
        }

        /// <summary>
        ///     Adds one full turn to a negative radian value; non-negative values are returned unchanged.
        /// </summary>
        private static double FlipPositive(double theta)
        {
            if (theta >= 0)
                return theta;

            return theta + Math.Tau;
        }

        /// <summary>
        ///     Similar to Lerp but, but defaults to making sure that lerping from 1 to 359 degrees doesn't wrap around
        ///     the whole circle.
        /// </summary>
        public static Angle Lerp(in Angle a, in Angle b, float factor)
        {
            return a + ShortestDistance(a, b) * factor;
        }

        /// <summary>
        ///     Returns the shortest distance between two angles.
        /// </summary>
        public static Angle ShortestDistance(in Angle a, in Angle b)
        {
            var delta = (b - a) % Math.Tau;
            return 2 * delta % Math.Tau - delta;
        }

        /// <summary>
        ///     Constructs a new angle, from degrees instead of radians.
        /// </summary>
        /// <param name="degrees">The angle in degrees.</param>
        public static Angle FromDegrees(double degrees)
        {
            // Avoid rounding issues with common use cases.
            switch (degrees)
            {
                case -270:
                    return new Angle(Math.PI * -1.5);
                case 90:
                    return new Angle(Math.PI / 2);
                case -180:
                    return new Angle(-Math.PI);
                case 180:
                    return new Angle(Math.PI);
                case 270.0:
                    return new Angle(Math.PI * 1.5);
                case -90:
                    return new Angle(Math.PI / -2);
                default:
                    return new(MathHelper.DegreesToRadians(degrees));
            }
        }

        /// <summary>
        ///     Implicitly converts an angle to its radian value.
        /// </summary>
        public static implicit operator double(Angle angle)
        {
            return angle.Theta;
        }

        /// <summary>
        ///     Implicitly converts a radian value to an angle, without normalisation.
        /// </summary>
        public static implicit operator Angle(double theta)
        {
            return new Angle(theta);
        }

        /// <summary>
        ///     Implicitly converts a single-precision radian value to an angle (exact widening to double).
        /// </summary>
        public static implicit operator Angle(float theta)
        {
            return new Angle((double) theta);
        }

        public static Angle operator +(Angle a, Angle b)
            => new(a.Theta + b.Theta);

        public static Angle operator -(Angle a, Angle b)
            => new(a.Theta - b.Theta);

        public static Angle operator -(Angle orig)
            => new(-orig.Theta);

        public override string ToString()
        {
            return $"{Theta} rad";
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
                $"{Theta} rad");
        }
    }
}
