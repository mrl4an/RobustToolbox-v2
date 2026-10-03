using Robust.Shared.Map;

namespace Robust.Client.Placement.Modes
{
    /// <summary>
    ///     Unconstrained placement: the ghost, and the placed object, follow the cursor exactly.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the default mode for entity prototypes and for construction prototypes. The cursor position
    ///         is used as-is: nothing is rounded to a tile, no offset is applied, and no occupancy, collision or
    ///         range test is performed on the client. Only server-side validation can reject the request.
    ///     </para>
    ///     <para>
    ///         If a grid lies under the cursor the coordinates are expressed relative to that grid; otherwise they
    ///         are relative to the map. Placement over empty space is fully supported and never aborts alignment.
    ///     </para>
    ///     <para>
    ///         Compatibility notes (intentional, content depends on them):
    ///         <list type="bullet">
    ///             <item><description>
    ///                 <see cref="IsValidPosition"/> always returns <see langword="true"/>, so the invalid
    ///                 (red) tint is never shown for this mode.
    ///             </description></item>
    ///             <item><description>
    ///                 Over open space <see cref="PlacementMode.CurrentTile"/> is a synthetic tile reference holding
    ///                 an empty tile; consumers must tolerate that.
    ///             </description></item>
    ///             <item><description>
    ///                 <see cref="PlacementMode.RangeRequired"/>, <see cref="PlacementMode.HasLineMode"/> and
    ///                 <see cref="PlacementMode.HasGridMode"/> are left at their base values (all <c>false</c>).
    ///             </description></item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         The class name is the key used to select this mode (content, prototypes and the wire protocol refer
    ///         to it by the string "PlaceFree"), and the placement manager instantiates it by reflection using the
    ///         single-argument constructor. Do not rename it or change that constructor signature.
    ///     </para>
    /// </remarks>
    public sealed class PlaceFree : PlacementMode
    {
        /// <summary>
        ///     Creates the mode. The constructor signature is required for reflection-based instantiation.
        /// </summary>
        /// <param name="pMan">The placement manager that owns this mode.</param>
        public PlaceFree(PlacementManager pMan) : base(pMan) { }

        /// <summary>
        ///     Stores the unmodified cursor position as the placement position.
        /// </summary>
        /// <remarks>
        ///     The order matters: <see cref="PlacementMode.GetTileRef"/> reads
        ///     <see cref="PlacementMode.MouseCoords"/> rather than its argument, so the coordinates must be
        ///     assigned before the tile is resolved. No snapping or offset is applied, and the method never
        ///     returns early when there is no grid under the cursor.
        /// </remarks>
        /// <param name="mouseScreen">Cursor position in screen (pixel) coordinates.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            MouseCoords = ScreenToCursorGrid(mouseScreen);
            CurrentTile = GetTileRef(MouseCoords);
        }

        /// <summary>
        ///     Every position is considered valid in this mode.
        /// </summary>
        /// <param name="position">The candidate position; ignored.</param>
        /// <returns>Always <see langword="true"/>.</returns>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            return true;
        }
    }
}
