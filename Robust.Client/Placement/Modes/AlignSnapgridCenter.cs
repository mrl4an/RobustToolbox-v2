using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Robust.Client.Placement.Modes
{
    [Virtual]
    public class SnapgridCenter : PlacementMode
    {
        protected MapGridComponent? Grid;
        protected float SnapSize;

        public override bool HasLineMode => true;
        public override bool HasGridMode => true;

        public SnapgridCenter(PlacementManager pMan) : base(pMan) { }

        /// <summary>
        ///     Draws a dark blue guide grid across the whole viewport (when a grid is under the cursor),
        ///     then lets the base class draw the placement ghost on top of it.
        /// </summary>
        public override void Render(in OverlayDrawArgs args)
        {
            if (Grid != null)
            {
                var viewport = (Vector2) pManager.Clyde.ScreenSize;
                var guideColor = new Color(0, 0, 0.3f);

                var transformSys = pManager.EntityManager.System<SharedTransformSystem>();
                var mapSys = pManager.EntityManager.System<SharedMapSystem>();
                var gridUid = transformSys.GetGrid(MouseCoords)!.Value;

                // Grid-local position of the top-left screen corner.
                var topLeftMap = pManager.EyeManager.ScreenToMap(Vector2.Zero);
                var topLeftLocal = mapSys.MapToGrid(gridUid, topLeftMap);

                // Align to whole tile boundaries (floor in local units), then go back to screen space.
                var firstLine = new EntityCoordinates(
                    topLeftLocal.EntityId,
                    new Vector2(MathF.Floor(topLeftLocal.X), MathF.Floor(topLeftLocal.Y)));
                var firstScreen = pManager.EyeManager.CoordinatesToScreen(firstLine);

                var step = SnapSize * EyeManager.PixelsPerMeter;

                for (var x = firstScreen.X; x < viewport.X; x += step)
                {
                    var from = ScreenToWorld(new Vector2(x, 0));
                    var to = ScreenToWorld(new Vector2(x, viewport.Y));
                    args.WorldHandle.DrawLine(from, to, guideColor);
                }

                for (var y = firstScreen.Y; y < viewport.Y; y += step)
                {
                    var from = ScreenToWorld(new Vector2(0, y));
                    var to = ScreenToWorld(new Vector2(viewport.X, y));
                    args.WorldHandle.DrawLine(from, to, guideColor);
                }
            }

            // Always draw the ghost last so it sits above the guide lines.
            base.Render(args);
        }

        /// <summary>
        ///     Moves the cursor position to the centre of the grid tile under the mouse and
        ///     refreshes the cached grid and snap size.
        /// </summary>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);

            var gridId = pManager.EntityManager.System<SharedTransformSystem>().GetGrid(MouseCoords);

            SnapSize = 1;
            if (gridId.HasValue && gridId.Value.IsValid())
            {
                Grid = pManager.EntityManager.GetComponent<MapGridComponent>(gridId.Value);
                SnapSize = Grid.TileSize;
            }
            else
            {
                Grid = null;
            }

            GridDistancing = SnapSize;

            var local = MouseCoords.Position;
            var snapped = new Vector2(
                SnapToCentre(local.X, SnapSize),
                SnapToCentre(local.Y, SnapSize));

            MouseCoords = new EntityCoordinates(
                MouseCoords.EntityId,
                snapped + new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y));
        }

        /// <summary>
        ///     Snaps one coordinate to the middle of the cell of the given size that contains it.
        /// </summary>
        private static float SnapToCentre(float value, float cellSize)
        {
            var cell = MathF.Round(value / cellSize - 0.5f, MidpointRounding.AwayFromZero);
            return (float) (cell + 0.5) * cellSize;
        }

        /// <summary>
        ///     A position is acceptable whenever it passes the range check; no other rules apply.
        /// </summary>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            return RangeCheck(position);
        }
    }
}
