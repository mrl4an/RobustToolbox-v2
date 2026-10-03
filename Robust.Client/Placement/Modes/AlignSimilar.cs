using System.Linq;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Robust.Client.Placement.Modes
{
    public sealed class AlignSimilar : PlacementMode
    {
        private const uint SnapToRange = 50;

        public AlignSimilar(PlacementManager pMan) : base(pMan) { }

        /// <summary>
        ///     Places the cursor at the raw (unsnapped) grid position under the mouse. When an entity of the
        ///     same prototype is nearby, it must also have a sprite with a base RSI for the placement to be
        ///     considered; in every case the resulting <see cref="PlacementMode.MouseCoords"/> stays equal to
        ///     the raw cursor position.
        /// </summary>
        /// <param name="mouseScreen">The cursor position in screen coordinates.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);
            CurrentTile = GetTileRef(MouseCoords);

            // This mode only applies to entity placement.
            if (pManager.CurrentPermission!.IsTile)
                return;

            if (!RangeCheck(MouseCoords))
                return;

            var entityManager = pManager.EntityManager;
            var transformSystem = entityManager.System<SharedTransformSystem>();
            var lookupSystem = entityManager.System<EntityLookupSystem>();

            var mapId = transformSystem.GetMapId(MouseCoords);
            var cursorMapPos = transformSystem.ToMapCoordinates(MouseCoords).Position;

            // Nearest entity of the prototype being placed, on the same map as the cursor.
            var nearby = lookupSystem.GetEntitiesInRange(MouseCoords, SnapToRange)
                .Where(uid => entityManager.GetComponent<MetaDataComponent>(uid).EntityPrototype == pManager.CurrentPrototype
                              && entityManager.GetComponent<TransformComponent>(uid).MapID == mapId)
                .OrderBy(uid => (transformSystem.GetWorldPosition(uid) - cursorMapPos).LengthSquared())
                .ToList();

            if (nearby.Count == 0)
                return;

            // Without a sprite and a base RSI there is nothing to measure, so bail out.
            if (!entityManager.TryGetComponent<SpriteComponent>(nearby[0], out var sprite) || sprite.BaseRSI == null)
                return;

            // The side-attachment point that would normally be computed here is deliberately not used:
            // the cursor keeps its unsnapped position.
            MouseCoords = new EntityCoordinates(MouseCoords.EntityId, MouseCoords.Position);
        }

        /// <summary>
        ///     A position is valid for this mode when an entity (not a tile) is being placed, the position is
        ///     within placement range, and nothing collides with it.
        /// </summary>
        /// <param name="position">The candidate placement position.</param>
        /// <returns>True if the entity may be placed at <paramref name="position"/>.</returns>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            if (pManager.CurrentPermission!.IsTile)
                return false;

            if (!RangeCheck(position))
                return false;

            return !IsColliding(position);
        }
    }
}
