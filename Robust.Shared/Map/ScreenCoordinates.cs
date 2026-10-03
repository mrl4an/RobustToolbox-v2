using Robust.Shared.Maths;
using Robust.Shared.Serialization;
using System;
using System.Numerics;
using JetBrains.Annotations;
using Robust.Shared.Utility;

namespace Robust.Shared.Map
{
    /// <summary>
    ///     Contains the coordinates of a position on the rendering screen.
    /// </summary>
    [PublicAPI]
    [Serializable, NetSerializable]
    public readonly struct ScreenCoordinates : IEquatable<ScreenCoordinates>, ISpanFormattable
    {
        public static readonly ScreenCoordinates Invalid = new(Vector2.Zero, WindowId.Invalid);

        /// <summary>
        ///     The pixel position (x, y) on the window. Assigned only by the constructors.
        /// </summary>
        public readonly Vector2 Position;

        /// <summary>
        ///     Horizontal component of <see cref="Position"/>.
        /// </summary>
        public float X => Position.X;

        /// <summary>
        ///     Vertical component of <see cref="Position"/>.
        /// </summary>
        public float Y => Position.Y;

        /// <summary>
        ///     The window which the coordinates are on.
        /// </summary>
        public readonly WindowId Window;

        /// <summary>
        ///     Creates screen coordinates from a position vector and a window.
        ///     The values are stored as given, without any validation.
        /// </summary>
        /// <param name="position">Pixel position on the window.</param>
        /// <param name="window">The window the position belongs to.</param>
        public ScreenCoordinates(Vector2 position, WindowId window)
        {
            Position = position;
            Window = window;
        }

        /// <summary>
        ///     Creates screen coordinates from separate x and y values and a window.
        ///     The values are stored as given, without any validation.
        /// </summary>
        /// <param name="x">Horizontal pixel position.</param>
        /// <param name="y">Vertical pixel position.</param>
        /// <param name="window">The window the position belongs to.</param>
        public ScreenCoordinates(float x, float y, WindowId window)
        {
            Position = new Vector2(x, y);
            Window = window;
        }

        public bool IsValid => Window != WindowId.Invalid;

        /// <summary>
        ///     Formats as <c>(X, Y, W&lt;window id&gt;)</c>, matching the output of <c>TryFormat</c>.
        /// </summary>
        public override string ToString()
        {
            return $"({Position.X}, {Position.Y}, W{Window.Value})";
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
                $"({Position.X}, {Position.Y}, W{Window.Value})");
        }

        /// <inheritdoc />
        public bool Equals(ScreenCoordinates other)
        {
            return Position.Equals(other.Position) && Window == other.Window;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            return obj is ScreenCoordinates other && Equals(other);
        }

        /// <summary>
        ///     Hash of <see cref="Position"/> and <see cref="Window"/>, consistent with equality.
        /// </summary>
        public override int GetHashCode()
        {
            return HashCode.Combine(Position, Window);
        }

        /// <summary>
        ///     Check for equality by value between two objects.
        /// </summary>
        public static bool operator ==(ScreenCoordinates a, ScreenCoordinates b)
        {
            return a.Equals(b);
        }

        /// <summary>
        ///     Check for inequality by value between two objects.
        /// </summary>
        public static bool operator !=(ScreenCoordinates a, ScreenCoordinates b)
        {
            return !a.Equals(b);
        }

        public void Deconstruct(out Vector2 pos, out WindowId window)
        {
            pos = Position;
            window = Window;
        }
    }
}
