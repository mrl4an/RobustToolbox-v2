using System;
using System.Numerics;
using Robust.Shared.Maths;

namespace Robust.Shared.Physics
{
    /// <summary>
    ///     A <see cref="Ray"/> (origin plus direction) bundled with a collision mask that tells
    ///     physics queries which collision layers the ray is allowed to hit.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This type only carries the mask. It never interprets it; filtering by mask is performed by the
    ///         physics queries that accept a <see cref="CollisionRay"/>.
    ///     </para>
    ///     <para>
    ///         Equality and hashing are based on the origin and direction only. The collision mask is
    ///         deliberately ignored, so two rays that differ only by mask compare equal.
    ///     </para>
    ///     <para>
    ///         The type is an immutable value type and can be shared freely between threads.
    ///     </para>
    /// </remarks>
    [Serializable]
    public readonly struct CollisionRay : IEquatable<CollisionRay>
    {
        private readonly Ray _ray;

        /// <summary>
        ///     The origin of the ray, exactly as it was passed to the constructor.
        /// </summary>
        public Vector2 Position => _ray.Position;

        /// <summary>
        ///     The direction of the ray, exactly as it was passed to the constructor.
        ///     It is expected to have unit length.
        /// </summary>
        public Vector2 Direction => _ray.Direction;

        /// <summary>
        ///     Bit set of collision layers this ray should collide with. The value is stored unchanged and is
        ///     only meaningful to the code that runs the physics query; it is typically a
        ///     <c>CollisionGroup</c> combination cast to <see cref="int"/>.
        /// </summary>
        public int CollisionMask { get; }

        /// <summary>
        ///     Creates a new ray with a collision mask.
        /// </summary>
        /// <param name="position">The origin of the ray.</param>
        /// <param name="direction">
        ///     The direction of the ray. It is not normalised here. Debug builds assert (inside the
        ///     <see cref="Ray"/> constructor) that its length is approximately 1; release builds accept any value.
        /// </param>
        /// <param name="collisionMask">
        ///     The collision layers the ray can hit. Any value, including 0 and negative numbers, is accepted.
        /// </param>
        public CollisionRay(Vector2 position, Vector2 direction, int collisionMask)
        {
            _ray = new Ray(position, direction);
            CollisionMask = collisionMask;
        }

        /// <summary>
        ///     Tests the ray against an axis-aligned box.
        /// </summary>
        /// <remarks>
        ///     The collision mask plays no part in this test. The result is exactly what
        ///     <see cref="Ray.Intersects"/> reports for the underlying ray.
        /// </remarks>
        /// <param name="box">The box to test against.</param>
        /// <param name="distance">The distance along the ray to the entry point, or 0 if there is no hit.</param>
        /// <param name="hitPos">The entry point, or <see cref="Vector2.Zero"/> if there is no hit.</param>
        /// <returns>True if the ray hits the box.</returns>
        public bool Intersects(Box2 box, out float distance, out Vector2 hitPos)
        {
            return _ray.Intersects(box, out distance, out hitPos);
        }

        /// <summary>
        ///     Checks whether two rays have the same origin and direction. The collision mask is not compared.
        /// </summary>
        /// <param name="other">The ray to compare with.</param>
        /// <returns>True if origin and direction are equal according to <see cref="Vector2.Equals(Vector2)"/>.</returns>
        public bool Equals(CollisionRay other)
        {
            return Position.Equals(other.Position) && Direction.Equals(other.Direction);
        }

        /// <summary>
        ///     Checks whether <paramref name="obj"/> is a <see cref="CollisionRay"/> with the same origin and
        ///     direction. The collision mask is not compared. Returns false for null and for any other type,
        ///     including a plain <see cref="Ray"/>.
        /// </summary>
        /// <param name="obj">The object to compare with.</param>
        public override bool Equals(object? obj)
        {
            return obj is CollisionRay other && Equals(other);
        }

        /// <summary>
        ///     Hash code derived from origin and direction only, consistent with <see cref="Equals(CollisionRay)"/>.
        ///     Arithmetic overflow wraps and never throws.
        /// </summary>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Position.GetHashCode() * 397) ^ Direction.GetHashCode();
            }
        }

        /// <summary>
        ///     Equality operator; see <see cref="Equals(CollisionRay)"/>. The collision mask is ignored.
        /// </summary>
        public static bool operator ==(CollisionRay a, CollisionRay b)
        {
            return a.Equals(b);
        }

        /// <summary>
        ///     Inequality operator; the negation of the equality operator.
        /// </summary>
        public static bool operator !=(CollisionRay a, CollisionRay b)
        {
            return !a.Equals(b);
        }

        /// <summary>
        ///     Converts to the underlying <see cref="Ray"/>, dropping the collision mask. This allows a
        ///     <see cref="CollisionRay"/> to be passed wherever a <see cref="Ray"/> is expected.
        /// </summary>
        /// <param name="a">The ray to convert.</param>
        public static implicit operator Ray(CollisionRay a)
        {
            return a._ray;
        }
    }
}
