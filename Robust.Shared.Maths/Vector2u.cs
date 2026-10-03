using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace Robust.Shared.Maths
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Vector2u : IEquatable<Vector2u>
    {
        /// <summary>The X component of the vector.</summary>
        [JsonInclude] public uint X;

        /// <summary>The Y component of the vector.</summary>
        [JsonInclude] public uint Y;

        /// <summary>
        ///     Creates a vector from its two components. No validation is performed.
        /// </summary>
        /// <param name="x">The X component.</param>
        /// <param name="y">The Y component.</param>
        public Vector2u(uint x, uint y)
        {
            X = x;
            Y = y;
        }

        public readonly void Deconstruct(out uint x, out uint y)
        {
            x = X;
            y = Y;
        }

        /// <summary>
        ///     True when both components are equal to those of <paramref name="other"/>.
        /// </summary>
        public readonly bool Equals(Vector2u other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <summary>
        ///     True only when <paramref name="obj"/> is a <see cref="Vector2u"/> equal to this one;
        ///     null and any other type yield false.
        /// </summary>
        public override readonly bool Equals(object? obj)
        {
            return obj is Vector2u other && Equals(other);
        }

        /// <summary>
        ///     Hash combining both components. Overflow wraps by design.
        /// </summary>
        public override readonly int GetHashCode()
        {
            unchecked
            {
                return ((int) X * 397) ^ (int) Y;
            }
        }

        public static Vector2u operator /(Vector2u vector, uint divider)
        {
            return new(vector.X / divider, vector.Y / divider);
        }

        public static implicit operator Vector2(Vector2u vector)
        {
            return new(vector.X, vector.Y);
        }

        public static explicit operator Vector2u(Vector2 vector)
        {
            return new((uint) vector.X, (uint) vector.Y);
        }

        public static explicit operator Vector2i(Vector2u vector)
        {
            return new((int) vector.X, (int) vector.Y);
        }
    }
}
