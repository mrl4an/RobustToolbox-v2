using System.Numerics;
using Robust.Shared.GameObjects;

namespace Robust.Shared.Physics
{
    public readonly struct RayCastResults
    {

        /// <summary>
        ///     The entity that was hit.
        /// </summary>
        public EntityUid HitEntity { get; }

        /// <summary>
        ///     World-space point at which the ray touched <see cref="HitEntity"/>.
        ///     <see cref="Vector2.Zero"/> if nothing was hit.
        /// </summary>
        public Vector2 HitPos { get; }

        /// <summary>
        ///     Length from the ray's origin to <see cref="HitPos"/>. Zero if nothing was hit.
        /// </summary>
        public float Distance { get; }

        /// <summary>
        ///     Creates a ray cast result. Note that the distance comes first, then the position.
        /// </summary>
        /// <param name="distance">Distance from the ray origin to the contact point.</param>
        /// <param name="hitPos">World-space contact point.</param>
        /// <param name="hitEntity">The entity that was hit.</param>
        public RayCastResults(float distance, Vector2 hitPos, EntityUid hitEntity)
        {
            Distance = distance;
            HitPos = hitPos;
            HitEntity = hitEntity;
        }
    }
}
