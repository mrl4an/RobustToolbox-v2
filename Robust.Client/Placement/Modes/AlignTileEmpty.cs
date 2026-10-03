using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Robust.Client.Placement.Modes
{
    public sealed class AlignTileEmpty : PlacementMode
    {
        public override bool HasLineMode => true;
        public override bool HasGridMode => true;

        public AlignTileEmpty(PlacementManager pMan) : base(pMan) { }

        /// <summary>
        /// Snaps the ghost to the centre of the hovered tile. CurrentTile is only refreshed when a grid is under the cursor.
        /// </summary>
        /// <param name="mouseScreen">Current cursor position in screen space.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);

            var entityManager = pManager.EntityManager;

            // Default tile size when the cursor is not over any grid.
            var tileSize = 1f;
            var gridUid = entityManager.System<SharedTransformSystem>().GetGrid(MouseCoords);
            if (gridUid != null && gridUid.Value.IsValid())
            {
                var mapSystem = entityManager.System<SharedMapSystem>();
                var gridComponent = entityManager.GetComponent<MapGridComponent>(gridUid.Value);
                CurrentTile = mapSystem.GetTileRef(gridUid.Value, gridComponent, MouseCoords);
                tileSize = (float) gridComponent.TileSize;
            }

            GridDistancing = tileSize;

            // Centre of the tile, plus the manager's offset for non-tile placements.
            var centre = new Vector2(CurrentTile.X + tileSize / 2, CurrentTile.Y + tileSize / 2);
            if (!pManager.CurrentPermission!.IsTile)
                centre += new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y);

            MouseCoords = new EntityCoordinates(MouseCoords.EntityId, centre);
        }

        /// <summary>
        /// Valid when in range and no entity of any kind overlaps the current tile.
        /// </summary>
        /// <param name="position">The coordinates to validate.</param>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            if (!RangeCheck(position))
                return false;

            var entityManager = pManager.EntityManager;
            var mapId = entityManager.System<SharedTransformSystem>().GetMapId(MouseCoords);

            // Slightly under one tile so entities only touching a neighbouring tile's edge are ignored.
            var tileBox = new Box2(
                new Vector2(CurrentTile.X, CurrentTile.Y),
                new Vector2(CurrentTile.X + 0.99f, CurrentTile.Y + 0.99f));

            return !entityManager.System<EntityLookupSystem>().AnyEntitiesIntersecting(mapId, tileBox);
        }
    }
}
