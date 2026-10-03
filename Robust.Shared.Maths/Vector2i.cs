using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Robust.Shared.Utility;

namespace Robust.Shared.Maths
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    // ReSharper disable once InconsistentNaming
    public struct Vector2i :
        IEquatable<Vector2i>,
        ISpanFormattable,
        IAdditionOperators<Vector2i, Vector2i, Vector2i>,
        ISubtractionOperators<Vector2i, Vector2i, Vector2i>,
        IMultiplyOperators<Vector2i, Vector2i, Vector2i>,
        IMultiplyOperators<Vector2i, int, Vector2i>,
        IComparisonOperators<Vector2i, Vector2i, bool>
    {
        /// <summary>The vector (0, 0).</summary>
        public static readonly Vector2i Zero = (0, 0);

        /// <summary>The vector (1, 1).</summary>
        public static readonly Vector2i One = (1, 1);

        /// <summary>Unit step towards positive Y: (0, 1).</summary>
        public static readonly Vector2i Up = (0, 1);

        /// <summary>Unit step towards negative Y: (0, -1).</summary>
        public static readonly Vector2i Down = (0, -1);

        /// <summary>Unit step towards negative X: (-1, 0).</summary>
        public static readonly Vector2i Left = (-1, 0);

        /// <summary>Unit step towards positive X: (1, 0).</summary>
        public static readonly Vector2i Right = (1, 0);

        /// <summary>Diagonal step (-1, -1).</summary>
        public static readonly Vector2i DownLeft = (-1, -1);

        /// <summary>Diagonal step (1, -1).</summary>
        public static readonly Vector2i DownRight = (1, -1);

        /// <summary>Diagonal step (1, 1).</summary>
        public static readonly Vector2i UpRight = (1, 1);

        /// <summary>Diagonal step (-1, 1).</summary>
        public static readonly Vector2i UpLeft = (-1, 1);

        /// <summary>The X component of the vector.</summary>
        [JsonInclude] public int X;

        /// <summary>The Y component of the vector.</summary>
        [JsonInclude] public int Y;

        /// <summary>
        ///     Creates a vector from its two components. No validation is performed.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        public Vector2i(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>
        ///     Builds a vector whose components are the per-axis maximum of the two inputs.
        /// </summary>
        public static Vector2i ComponentMax(Vector2i a, Vector2i b)
        {
            return new Vector2i(a.X > b.X ? a.X : b.X, a.Y > b.Y ? a.Y : b.Y);
        }

        /// <summary>
        ///     Builds a vector whose components are the per-axis minimum of the two inputs.
        /// </summary>
        public static Vector2i ComponentMin(Vector2i a, Vector2i b)
        {
            return new Vector2i(a.X < b.X ? a.X : b.X, a.Y < b.Y ? a.Y : b.Y);
        }

        /// <summary>
        ///     Euclidean length of the vector, derived from <see cref="LengthSquared"/>.
        /// </summary>
        public readonly float Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MathF.Sqrt(LengthSquared);
        }

        /// <summary>
        ///     Squared length. The squares and their sum are computed as <see cref="int"/> (and may wrap)
        ///     before the result is converted to <see cref="float"/>.
        /// </summary>
        public readonly float LengthSquared
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => X * X + Y * Y;
        }

        /// <inheritdoc />
        public readonly bool Equals(Vector2i other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <inheritdoc />
        public readonly override bool Equals(object? obj)
        {
            return obj is Vector2i other && Equals(other);
        }

        /// <inheritdoc />
        public readonly override int GetHashCode()
        {
            // Overflow is intentional here.
            unchecked
            {
                return (X * 397) ^ Y;
            }
        }

        /// <summary>
        ///     Rotates the vector by <paramref name="angle"/>. The rotation is done in floating point and the
        ///     result is truncated toward zero back to integers, so it is not rounded.
        /// </summary>
        /// <param name="angle">The angle to rotate by.</param>
        /// <returns>The truncated rotated vector.</returns>
        public Vector2i Rotate(Angle angle)
        {
            var rotated = angle.RotateVec(this);
            return (Vector2i) rotated;
        }

        /// <summary>Component-wise difference.</summary>
        public static Vector2i operator -(Vector2i a, Vector2i b)
        {
            return new Vector2i(a.X - b.X, a.Y - b.Y);
        }

        /// <summary>Subtracts the scalar from both components.</summary>
        public static Vector2i operator -(Vector2i a, int b)
        {
            return new Vector2i(a.X - b, a.Y - b);
        }

        /// <summary>Negates both components.</summary>
        public static Vector2i operator -(Vector2i a)
        {
            return new Vector2i(-a.X, -a.Y);
        }

        /// <summary>Component-wise sum.</summary>
        public static Vector2i operator +(Vector2i a, Vector2i b)
        {
            return new Vector2i(a.X + b.X, a.Y + b.Y);
        }

        /// <summary>Adds the scalar to both components.</summary>
        public static Vector2i operator +(Vector2i a, int b)
        {
            return new Vector2i(a.X + b, a.Y + b);
        }

        /// <summary>Component-wise (Hadamard) product; this is not a dot product.</summary>
        public static Vector2i operator *(Vector2i a, Vector2i b)
        {
            return new Vector2i(a.X * b.X, a.Y * b.Y);
        }

        /// <summary>Multiplies both components by an integer.</summary>
        public static Vector2i operator *(Vector2i a, int scale)
        {
            return new Vector2i(a.X * scale, a.Y * scale);
        }

        /// <summary>Multiplies both components, converted to float, by <paramref name="scale"/>.</summary>
        public static Vector2 operator *(Vector2i a, float scale)
        {
            return new Vector2(a.X * scale, a.Y * scale);
        }

        /// <summary>Component-wise integer division (truncating; throws on a zero divisor).</summary>
        public static Vector2i operator /(Vector2i a, Vector2i b)
        {
            return new Vector2i(a.X / b.X, a.Y / b.Y);
        }

        /// <summary>Divides both components by an integer (truncating; throws when it is zero).</summary>
        public static Vector2i operator /(Vector2i a, int scale)
        {
            return new Vector2i(a.X / scale, a.Y / scale);
        }

        /// <summary>Divides both components, converted to float, by <paramref name="scale"/> using float division.</summary>
        public static Vector2 operator /(Vector2i a, float scale)
        {
            return new Vector2(a.X / scale, a.Y / scale);
        }

        /// <summary>True when both components are equal.</summary>
        public static bool operator ==(Vector2i a, Vector2i b)
        {
            return a.Equals(b);
        }

        /// <summary>True when at least one component differs.</summary>
        public static bool operator !=(Vector2i a, Vector2i b)
        {
            return !a.Equals(b);
        }

        /// <summary>Splits the vector into its X and Y components.</summary>
        public readonly void Deconstruct(out int x, out int y)
        {
            x = X;
            y = Y;
        }

        /// <summary>Widens the integer components to floats.</summary>
        public static implicit operator Vector2(Vector2i vector)
        {
            return new Vector2(vector.X, vector.Y);
        }

        /// <summary>Converts a float vector to integers by truncating toward zero.</summary>
        public static explicit operator Vector2i(Vector2 vector)
        {
            return new Vector2i((int) vector.X, (int) vector.Y);
        }

        /// <summary>Creates a vector from an (x, y) tuple.</summary>
        public static implicit operator Vector2i((int x, int y) tuple)
        {
            return new Vector2i(tuple.x, tuple.y);
        }

        /// <summary>
        ///     Formats the vector as "(X, Y)".
        /// </summary>
        public override readonly string ToString()
        {
            return $"({X}, {Y})";
        }

        /// <summary>
        ///     Same as <see cref="ToString()"/>; the format and provider arguments are ignored.
        /// </summary>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            return ToString();
        }

        /// <summary>
        ///     Writes the "(X, Y)" text into <paramref name="destination"/> without allocating.
        ///     <paramref name="format"/> and <paramref name="provider"/> are ignored.
        /// </summary>
        /// <returns>False when the text does not fit.</returns>
        public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
        {
            return FormatHelpers.TryFormatInto(destination, out charsWritten, $"({X}, {Y})");
        }

        /// <summary>
        ///     Compares squared lengths (not lexicographic, and not consistent with equality).
        /// </summary>
        public static bool operator >(Vector2i left, Vector2i right)
        {
            return left.LengthSquared > right.LengthSquared;
        }

        /// <inheritdoc cref="op_GreaterThan"/>
        public static bool operator >=(Vector2i left, Vector2i right)
        {
            return left.LengthSquared >= right.LengthSquared;
        }

        /// <inheritdoc cref="op_GreaterThan"/>
        public static bool operator <(Vector2i left, Vector2i right)
        {
            return left.LengthSquared < right.LengthSquared;
        }

        /// <inheritdoc cref="op_GreaterThan"/>
        public static bool operator <=(Vector2i left, Vector2i right)
        {
            return left.LengthSquared <= right.LengthSquared;
        }
    }
}
