using Robust.Shared.Map;

namespace Robust.Client.Placement.Modes
{
    public sealed class PlaceNearby : PlacementMode
    {
        public PlaceNearby(PlacementManager pMan) : base(pMan) { }

        public override bool RangeRequired => true;

        /// <summary>
        /// Uses the raw cursor position with no snapping or offset.
        /// </summary>
        /// <param name="mouseScreen">Current cursor position in screen space.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);
            CurrentTile = GetTileRef(MouseCoords);
        }

        /// <summary>
        /// Entities only (never tiles), and only within placement range.
        /// </summary>
        /// <param name="position">The coordinates to validate.</param>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            if (pManager.CurrentPermission!.IsTile)
                return false;

            return RangeCheck(position);
        }
    }
}
