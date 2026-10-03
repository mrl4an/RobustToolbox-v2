using System.Numerics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Robust.Client.Placement.Modes
{
    /// <summary>
    ///     Snaps placement to a tile of a grid, regardless of what currently occupies that tile.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Used for tile-placing tools and for entities that should sit on tile positions. It supports single,
    ///         line and grid-drag placement. No occupancy, density, tile-type or collision check is made.
    ///     </para>
    ///     <para>
    ///         The raw cursor position is first passed through
    ///         <see cref="CoordinatesExtensions.AlignWithClosestGridTile"/>, so the placement can attach to a
    ///         grid that is near the cursor even when the cursor is not over any tile. The search box used for
    ///         that lookup is deliberately larger than the diagonal of a one-meter tile, which keeps line
    ///         placement snapped continuously instead of dropping out of snapping near grid edges.
    ///     </para>
    ///     <para>
    ///         If, after that step, the coordinates do not belong to any grid (free space with no grid nearby),
    ///         alignment stops immediately. <see cref="PlacementMode.MouseCoords"/> then holds the pre-snap
    ///         position, and <see cref="PlacementMode.CurrentTile"/> and the grid spacing keep their previous
    ///         values.
    ///     </para>
    ///     <para>
    ///         Compatibility notes (intentional, content depends on them):
    ///         <list type="bullet">
    ///             <item><description>
    ///                 The final position adds half the tile size to the raw integer tile indices rather than
    ///                 scaling the indices by the tile size. For the standard tile size of 1 this is the tile
    ///                 centre; for other sizes it is not a true centre. The same tile size is used for the
    ///                 half-tile term and for the line/grid spacing.
    ///             </description></item>
    ///             <item><description>
    ///                 The current placement permission is dereferenced without a null check. The placement
    ///                 manager only aligns while a permission exists.
    ///             </description></item>
    ///             <item><description>
    ///                 The manager's placement offset is applied to entity placement only, never to tiles.
    ///             </description></item>
    ///             <item><description>
    ///                 <see cref="IsValidPosition"/> only fails if the base range check fails. Because
    ///                 <see cref="PlacementMode.RangeRequired"/> is not overridden, the result is currently
    ///                 always <see langword="true"/>; the structure is kept so that overriding it later takes
    ///                 effect.
    ///             </description></item>
    ///         </list>
    ///     </para>
    ///     <para>
    ///         The class name is the key used to select this mode (content, prototypes and the wire protocol refer
    ///         to it by the string "AlignTileAny"), and the placement manager instantiates it by reflection using
    ///         the single-argument constructor. Do not rename it or change that constructor signature.
    ///     </para>
    /// </remarks>
    public sealed class AlignTileAny : PlacementMode
    {
        /// <summary>
        ///     Side length, in meters, of the box used to look for a nearby grid when the cursor is not over one.
        /// </summary>
        private const float GridSearchBoxSize = 2f;

        /// <summary>
        ///     Creates the mode. The constructor signature is required for reflection-based instantiation.
        /// </summary>
        /// <param name="pMan">The placement manager that owns this mode.</param>
        public AlignTileAny(PlacementManager pMan) : base(pMan) { }

        /// <inheritdoc />
        public override bool HasLineMode => true;

        /// <inheritdoc />
        public override bool HasGridMode => true;

        /// <summary>
        ///     Snaps the placement position to the centre of the tile under (or next to) the cursor.
        /// </summary>
        /// <remarks>
        ///     Does nothing further when no grid can be found for the snapped coordinates; see the class remarks
        ///     for the resulting state.
        /// </remarks>
        /// <param name="mouseScreen">Cursor position in screen (pixel) coordinates.</param>
        public override void AlignPlacementMode(ScreenCoordinates mouseScreen)
        {
            var entityManager = pManager.EntityManager;

            MouseCoords = ScreenToCursorGrid(mouseScreen)
                .AlignWithClosestGridTile(GridSearchBoxSize, entityManager, pManager.MapManager);

            var gridUid = entityManager.System<SharedTransformSystem>().GetGrid(MouseCoords);

            if (gridUid is not { } gridId || !entityManager.TryGetComponent<MapGridComponent>(gridId, out var mapGrid))
                return;

            CurrentTile = entityManager.System<SharedMapSystem>().GetTileRef(gridId, mapGrid, MouseCoords);

            var tileSize = (float) mapGrid.TileSize;
            GridDistancing = tileSize;

            if (pManager.CurrentPermission!.IsTile)
            {
                MouseCoords = new EntityCoordinates(MouseCoords.EntityId,
                    new Vector2(CurrentTile.X + tileSize / 2, CurrentTile.Y + tileSize / 2));
            }
            else
            {
                MouseCoords = new EntityCoordinates(MouseCoords.EntityId,
                    new Vector2(CurrentTile.X + tileSize / 2 + pManager.PlacementOffset.X,
                        CurrentTile.Y + tileSize / 2 + pManager.PlacementOffset.Y));
            }
        }

        /// <summary>
        ///     A position is valid unless the base range check rejects it.
        /// </summary>
        /// <param name="position">The candidate position.</param>
        /// <returns>The result of <see cref="PlacementMode.RangeCheck"/>.</returns>
        public override bool IsValidPosition(EntityCoordinates position)
        {
            return RangeCheck(position);
        }
    }
}
