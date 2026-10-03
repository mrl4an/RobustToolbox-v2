using System;
using System.Numerics;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Robust.Shared.Physics
{
    /// <summary>
    ///     An infinite 2D ray: an origin plus a direction vector. Points on the ray are
    ///     <c>Position + t * Direction</c> for <c>t &gt;= 0</c>. The type carries no notion of
    ///     coordinate space; results are expressed in whatever space the caller supplies.
    /// </summary>
    [Serializable]
    public struct Ray : IEquatable<Ray>
    {
        /// <summary>
        ///     Origin of the ray.
        /// </summary>
        public Vector2 Position;

        /// <summary>
        ///     Direction of the ray. Expected to have unit length.
        /// </summary>
        public Vector2 Direction;

        /// <summary>
        ///     Creates a ray from an origin and a direction.
        /// </summary>
        /// <param name="position">The origin of the ray.</param>
        /// <param name="direction">
        ///     The direction of the ray. It is stored as given and is not normalised;
        ///     debug builds assert that it is of unit length.
        /// </param>
        public Ray(Vector2 position, Vector2 direction)
        {
            DebugTools.Assert(MathHelper.CloseToPercent(direction.LengthSquared(), 1f));

            Position = position;
            Direction = direction;
        }

        /// <summary>
        ///     Tests this ray against an axis-aligned box using the slab method.
        /// </summary>
        /// <param name="box">The box to test. Inverted edges are tolerated.</param>
        /// <param name="distance">
        ///     The ray parameter of the entry point (a multiple of the length of <see cref="Direction"/>),
        ///     or 0 when there is no hit.
        /// </param>
        /// <param name="hitPos">The entry point, or <see cref="Vector2.Zero"/> when there is no hit.</param>
        /// <returns>True if the ray hits the box.</returns>
        public readonly bool Intersects(Box2 box, out float distance, out Vector2 hitPos)
        {
            const float eps = 1.0e-7f;

            var tmin = 0.0f;
            var tmax = float.MaxValue;

            var loX = MathF.Min(box.Left, box.Right);
            var hiX = MathF.Max(box.Left, box.Right);
            var loY = MathF.Min(box.Top, box.Bottom);
            var hiY = MathF.Max(box.Top, box.Bottom);

            // X slab. A near-parallel ray outside the slab is rejected; otherwise the division
            // below is still performed (infinities / NaN are intentionally left to IEEE rules).
            if (MathF.Abs(Direction.X) < eps && (Position.X < loX || Position.X > hiX))
            {
                distance = 0f;
                hitPos = Vector2.Zero;
                return false;
            }

            {
                var inv = 1.0f / Direction.X;
                var t1 = (loX - Position.X) * inv;
                var t2 = (hiX - Position.X) * inv;

                if (t1 > t2)
                    (t1, t2) = (t2, t1);

                tmin = MathF.Max(t1, tmin);
                tmax = MathF.Min(t2, tmax);

                if (tmin > tmax)
                {
                    distance = 0f;
                    hitPos = Vector2.Zero;
                    return false;
                }
            }

            // Y slab.
            if (MathF.Abs(Direction.Y) < eps && (Position.Y < loY || Position.Y > hiY))
            {
                distance = 0f;
                hitPos = Vector2.Zero;
                return false;
            }

            {
                var inv = 1.0f / Direction.Y;
                var t1 = (loY - Position.Y) * inv;
                var t2 = (hiY - Position.Y) * inv;

                if (t1 > t2)
                    (t1, t2) = (t2, t1);

                tmin = MathF.Max(t1, tmin);
                tmax = MathF.Min(t2, tmax);

                if (tmin > tmax)
                {
                    distance = 0f;
                    hitPos = Vector2.Zero;
                    return false;
                }
            }

            distance = tmin;
            hitPos = Position + Direction * tmin;
            return true;
        }

        /// <summary>
        ///     Exact component-wise equality.
        /// </summary>
        public readonly bool Equals(Ray other)
        {
            return Position.Equals(other.Position) && Direction.Equals(other.Direction);
        }

        /// <inheritdoc />
        public override readonly bool Equals(object? obj)
        {
            return obj is Ray other && Equals(other);
        }

        /// <inheritdoc />
        public override readonly int GetHashCode()
        {
            return unchecked((Position.GetHashCode() * 397) ^ Direction.GetHashCode());
        }

        /// <summary>
        ///     Returns whether two rays are exactly equal.
        /// </summary>
        public static bool operator ==(Ray a, Ray b)
        {
            return a.Equals(b);
        }

        /// <summary>
        ///     Returns whether two rays differ.
        /// </summary>
        public static bool operator !=(Ray a, Ray b)
        {
            return !(a == b);
        }
    }
}
