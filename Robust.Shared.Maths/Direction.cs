using System;
using System.Collections.Immutable;
using System.Diagnostics.Contracts;
using System.Numerics;

namespace Robust.Shared.Maths
{
    public enum Direction : sbyte
    {
        Invalid = -1,

        // Values advance counter-clockwise (in world angle) from South, one step per 45 degrees.
        South = 0,
        SouthEast = 1,
        East = 2,
        NorthEast = 3,
        North = 4,
        NorthWest = 5,
        West = 6,
        SouthWest = 7,
    }

    /// <summary>
    ///     Bit-flag representation of a direction, so that several directions can be combined in one value.
    ///     Diagonals are the union of their two adjacent cardinal bits.
    /// </summary>
    [Flags]
    public enum DirectionFlag : sbyte
    {
        None = 0,
        South = 1 << 0,
        East = 1 << 1,
        North = 1 << 2,
        West = 1 << 3,

        SouthEast = South | East,
        NorthEast = North | East,
        NorthWest = North | West,
        SouthWest = South | West,
    }

    /// <summary>
    ///     Extension methods for the <see cref="Direction"/> enum.
    /// </summary>
    public static class DirectionExtensions
    {
        /// <summary>
        ///     Every real direction (the four cardinals and the four diagonals), in enum order.
        ///     <see cref="Direction.Invalid"/> is not included.
        /// </summary>
        public static readonly ImmutableArray<Direction> AllDirections = ImmutableArray.Create(
            Direction.South,
            Direction.SouthEast,
            Direction.East,
            Direction.NorthEast,
            Direction.North,
            Direction.NorthWest,
            Direction.West,
            Direction.SouthWest);

        private const double Segment = 2 * Math.PI / 8.0; // Cut the circle into 8 pieces

        public static Direction AsDir(this DirectionFlag directionFlag)
        {
            switch (directionFlag)
            {
                case DirectionFlag.South:
                    return Direction.South;
                case DirectionFlag.SouthEast:
                    return Direction.SouthEast;
                case DirectionFlag.East:
                    return Direction.East;
                case DirectionFlag.NorthEast:
                    return Direction.NorthEast;
                case DirectionFlag.North:
                    return Direction.North;
                case DirectionFlag.NorthWest:
                    return Direction.NorthWest;
                case DirectionFlag.West:
                    return Direction.West;
                case DirectionFlag.SouthWest:
                    return Direction.SouthWest;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static DirectionFlag AsFlag(this Direction direction)
        {
            switch (direction)
            {
                case Direction.South:
                    return DirectionFlag.South;
                case Direction.SouthEast:
                    return DirectionFlag.SouthEast;
                case Direction.East:
                    return DirectionFlag.East;
                case Direction.NorthEast:
                    return DirectionFlag.NorthEast;
                case Direction.North:
                    return DirectionFlag.North;
                case Direction.NorthWest:
                    return DirectionFlag.NorthWest;
                case Direction.West:
                    return DirectionFlag.West;
                case Direction.SouthWest:
                    return DirectionFlag.SouthWest;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static DirectionFlag AsDirectionFlag(this Vector2i indices)
        {
            switch (indices.X)
            {
                case -1:
                    switch (indices.Y)
                    {
                        case -1:
                            return DirectionFlag.SouthWest;
                        case 0:
                            return DirectionFlag.West;
                        case 1:
                            return DirectionFlag.NorthWest;
                    }
                    break;
                case 0:
                    switch (indices.Y)
                    {
                        case -1:
                            return DirectionFlag.South;
                        case 1:
                            return DirectionFlag.North;
                    }
                    break;
                case 1:
                    switch (indices.Y)
                    {
                        case -1:
                            return DirectionFlag.SouthEast;
                        case 0:
                            return DirectionFlag.East;
                        case 1:
                            return DirectionFlag.NorthEast;
                    }
                    break;
            }

            throw new ArgumentOutOfRangeException(
                $"Tried to use a non-supported Vector2i for conversion to direction flag");
        }

        public static Direction AsDirection(this Vector2i indices)
        {
            switch (indices.X)
            {
                case -1:
                    switch (indices.Y)
                    {
                        case -1:
                            return Direction.SouthWest;
                        case 0:
                            return Direction.West;
                        case 1:
                            return Direction.NorthWest;
                    }
                    break;
                case 0:
                    switch (indices.Y)
                    {
                        case -1:
                            return Direction.South;
                        case 1:
                            return Direction.North;
                    }
                    break;
                case 1:
                    switch (indices.Y)
                    {
                        case -1:
                            return Direction.SouthEast;
                        case 0:
                            return Direction.East;
                        case 1:
                            return Direction.NorthEast;
                    }
                    break;
            }

            throw new ArgumentOutOfRangeException(
                $"Tried to use a non-supported Vector2i for conversion to direction");
        }


        /// <summary>
        ///     Finds the <see cref="Direction"/> closest to a world-space vector.
        ///     The vector is interpreted in world convention (South is (0, -1)); its length does not matter.
        /// </summary>
        /// <param name="vec">World-space direction vector.</param>
        /// <returns>The nearest of the eight directions; never <see cref="Direction.Invalid"/>.</returns>
        public static Direction GetDir(this Vector2 vec)
        {
            return Angle.FromWorldVec(vec).GetDir();
        }

        /// <summary>
        ///     Finds the <see cref="Direction"/> closest to an integer vector.
        ///     Note that, unlike the <see cref="Vector2"/> overload, no world rotation is applied: the plain
        ///     Cartesian angle of the vector is used, so (1, 0) maps to South.
        /// </summary>
        /// <param name="vec">Integer direction vector.</param>
        /// <returns>The nearest of the eight directions; never <see cref="Direction.Invalid"/>.</returns>
        public static Direction GetDir(this Vector2i vec)
        {
            Vector2 asFloat = vec;
            return new Angle(asFloat).GetDir();
        }

        /// <summary>
        ///     Finds the closest cardinal <see cref="Direction"/> (South, East, North or West) for an integer vector.
        ///     Uses the plain Cartesian angle of the vector, without world rotation (see <see cref="GetDir(Vector2i)"/>).
        /// </summary>
        /// <param name="vec">Integer direction vector.</param>
        /// <returns>One of the four cardinal directions.</returns>
        public static Direction GetCardinalDir(this Vector2i vec)
        {
            Vector2 asFloat = vec;
            return new Angle(asFloat).GetCardinalDir();
        }

        public static Direction GetOpposite(this Direction direction)
        {
            return direction switch
            {
                Direction.East => Direction.West,
                Direction.West => Direction.East,
                Direction.North => Direction.South,
                Direction.South => Direction.North,
                Direction.NorthEast => Direction.SouthWest,
                Direction.SouthWest => Direction.NorthEast,
                Direction.NorthWest => Direction.SouthEast,
                Direction.SouthEast => Direction.NorthWest,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
        }

        public static Direction GetClockwise90Degrees(this Direction direction)
        {
            return direction switch
            {
                Direction.East => Direction.South,
                Direction.West => Direction.North,
                Direction.North => Direction.East,
                Direction.South => Direction.West,
                Direction.NorthEast => Direction.SouthEast,
                Direction.SouthWest => Direction.NorthWest,
                Direction.NorthWest => Direction.NorthEast,
                Direction.SouthEast => Direction.SouthWest,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            };
        }

        /// <summary>
        ///     Converts a <see cref="Direction"/> to an angle in the range -PI..+PI (North stays at +PI).
        /// </summary>
        /// <param name="dir">Direction to convert.</param>
        /// <returns>The world angle of the direction. Out-of-range values are not rejected; the formula is applied as is.</returns>
        [Pure]
        public static Angle ToAngle(this Direction dir)
        {
            var theta = Segment * (int) dir;

            // Fold anything strictly beyond PI back into the negative half of the circle.
            if (theta > Math.PI)
                theta -= 2 * Math.PI;

            return new Angle(theta);
        }

        private static readonly Vector2[] DirectionVectors = {
            new (0, -1),
            new Vector2(1, -1).Normalized(),
            new (1, 0),
            new Vector2(1, 1).Normalized(),
            new (0, 1),
            new Vector2(-1, 1).Normalized(),
            new (-1, 0),
            new Vector2(-1, -1).Normalized()
        };

        private static readonly Vector2i[] IntDirectionVectors = {
            new (0, -1),
            new (1, -1),
            new (1, 0),
            new (1, 1),
            new (0, 1),
            new (-1, 1),
            new (-1, 0),
            new (-1, -1)
        };

        /// <summary>
        ///     Converts a <see cref="Direction"/> to a normalized direction vector.
        /// </summary>
        /// <param name="dir">Direction to convert.</param>
        /// <returns>A unit-length vector pointing in that direction.</returns>
        /// <exception cref="IndexOutOfRangeException">If <paramref name="dir"/> is not one of the eight real directions.</exception>
        /// <seealso cref="Vector2"/>
        public static Vector2 ToVec(this Direction dir)
        {
            return DirectionVectors[(int) dir];
        }

        /// <summary>
        /// Converts a Direction to a Vector2i. Useful for getting adjacent tiles.
        /// </summary>
        /// <param name="dir">Direction</param>
        /// <returns>an 2D int Vector</returns>
        /// <exception cref="IndexOutOfRangeException">if invalid Direction is used</exception>
        /// <seealso cref="Vector2i"/>
        public static Vector2i ToIntVec(this Direction dir)
        {
            return IntDirectionVectors[(int) dir];
        }

        /// <summary>
        ///     Offset 2D integer vector by a given direction.
        ///     Convenience for adding <see cref="ToIntVec"/> to <see cref="Vector2i"/>
        /// </summary>
        /// <param name="vec">2D integer vector</param>
        /// <param name="dir">Direction by which we offset</param>
        /// <returns>a newly vector offset by the <param name="dir">dir</param> or exception if the direction is invalid</returns>
        public static Vector2i Offset(this Vector2i vec, Direction dir)
        {
            return vec + dir.ToIntVec();
        }

        /// <summary>
        ///     Converts a direction vector to an angle in the range -PI..+PI.
        ///     This is the plain Cartesian angle (atan2 of Y over X), not the world angle.
        /// </summary>
        /// <param name="vec">The vector whose angle is wanted.</param>
        /// <returns>The angle of that vector.</returns>
        public static Angle ToAngle(this Vector2 vec)
        {
            return new Angle(vec);
        }

        public static Angle ToWorldAngle(this Vector2 vec)
        {
            return Angle.FromWorldVec(vec);
        }
    }
}
