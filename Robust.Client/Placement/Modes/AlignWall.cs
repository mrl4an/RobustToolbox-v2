using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Robust.Shared.Map;

namespace Robust.Client.Placement.Modes
{
    public sealed class AlignWall : PlacementMode
    {
        public AlignWall(PlacementManager pMan) : base(pMan) { }

        /// <summary>
        /// Positions the placement ghost for wall-mounted entities. The cursor X is kept as-is,
        /// while Y is snapped to one of the allowed mounting heights inside the hovered tile.
        /// Tile permissions are not snapped at all.
        /// </summary>
        /// <param name="mouseScreen">Current cursor position in screen space.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);
            CurrentTile = GetTileRef(MouseCoords);

            // Tiles are never wall-mounted: leave the raw cursor position untouched.
            if (pManager.CurrentPermission!.IsTile)
                return;

            // Vertical offsets (relative to the tile's Y) at which the entity may sit.
            // Prototype-defined mounting points win; otherwise use the default heights.
            // The 1.5 default intentionally falls inside the tile above.
            var mountingPoints = pManager.CurrentPrototype!.MountingPoints;
            IEnumerable<float> offsets = mountingPoints != null
                ? mountingPoints.Select(point => (float) point)
                : new List<float> { 0.5f, 1.0f, 1.5f };

            var candidates = offsets
                .Select(offset => new Vector2(MouseCoords.X, CurrentTile.Y + offset))
                .ToList();

            // Pick the candidate closest to the cursor; ties resolve to the first in order.
            // An empty candidate list makes First() throw, which is intended behaviour.
            var cursor = MouseCoords.Position;
            var best = candidates
                .OrderBy(candidate => (candidate - cursor).LengthSquared())
                .First();

            MouseCoords = new EntityCoordinates(
                MouseCoords.EntityId,
                best + new Vector2(pManager.PlacementOffset.X, pManager.PlacementOffset.Y));
        }

        /// <summary>
        /// A position is valid only for non-tile permissions that are within placement range.
        /// </summary>
        /// <param name="position">The coordinates to validate.</param>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            return !pManager.CurrentPermission!.IsTile && RangeCheck(position);
        }
    }
}
