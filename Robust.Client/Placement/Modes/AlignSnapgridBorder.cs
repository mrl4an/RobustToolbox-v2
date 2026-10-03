using System;
using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Robust.Client.Placement.Modes
{
    public sealed class SnapgridBorder : SnapgridCenter
    {
        /// <summary>Line placement is supported by this mode.</summary>
        public override bool HasLineMode => true;
        /// <summary>Grid placement is supported by this mode.</summary>
        public override bool HasGridMode => true;

        public SnapgridBorder(PlacementManager pMan) : base(pMan)
        {
        }


        /// <summary>
        ///     Aligns the placement position to the nearest tile corner (grid line intersection) under the cursor,
        ///     using the tile size of the grid below the cursor, or 1 when there is no grid.
        /// </summary>
        /// <param name="mouseScreen">Cursor position in screen coordinates.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);

            var transformSys = pManager.EntityManager.System<SharedTransformSystem>();
            var gridId = transformSys.GetGrid(MouseCoords);

            SnapSize = 1f;
            if (gridId is { } id && id.IsValid())
            {
                Grid = pManager.EntityManager.GetComponent<MapGridComponent>(id);
                SnapSize = Grid.TileSize;
            }
            else
            {
                Grid = null;
            }

            GridDistancing = SnapSize;

            // Round each axis to the nearest multiple of the snap size (ties away from zero), no half-tile shift.
            var snapped = new Vector2(
                MathF.Round(MouseCoords.X / SnapSize, MidpointRounding.AwayFromZero) * SnapSize,
                MathF.Round(MouseCoords.Y / SnapSize, MidpointRounding.AwayFromZero) * SnapSize);

            MouseCoords = new EntityCoordinates(
                MouseCoords.EntityId,
                snapped + new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y));
        }
    }
}
