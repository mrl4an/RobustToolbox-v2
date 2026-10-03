using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Robust.Client.Placement.Modes
{
    public sealed class AlignTileDense : PlacementMode
    {
        public override bool HasLineMode => true;
        public override bool HasGridMode => true;

        public AlignTileDense(PlacementManager pMan) : base(pMan) { }

        /// <summary>
        /// Snaps the ghost to the centre of the hovered tile. Valid only where something solid already exists (for non-tile placements).
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
                tileSize = (float) entityManager.GetComponent<MapGridComponent>(gridUid.Value).TileSize;
            }

            // Computed from the still-unsnapped cursor coordinates.
            CurrentTile = GetTileRef(MouseCoords);
            GridDistancing = tileSize;

            // Centre of the tile, plus the manager's offset for non-tile placements.
            var centre = new Vector2(CurrentTile.X + tileSize / 2, CurrentTile.Y + tileSize / 2);
            if (!pManager.CurrentPermission!.IsTile)
                centre += new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y);

            MouseCoords = new EntityCoordinates(MouseCoords.EntityId, centre);
        }

        /// <summary>
        /// Valid when in range and, for entities, when something dense is already present.
        /// </summary>
        /// <param name="position">The coordinates to validate.</param>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            if (!RangeCheck(position))
                return false;

            // Tiles need no support; entities need something dense already at the spot.
            return pManager.CurrentPermission!.IsTile || IsColliding(position);
        }
    }
}
